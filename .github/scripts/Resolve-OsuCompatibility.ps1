[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Repository,
    [string] $GitHubToken,
    [bool] $Force = $false,
    [string] $ReleaseTag,
    [Parameter(Mandatory = $true)]
    [string] $GitHubOutput
)

$ErrorActionPreference = "Stop"

$headers = @{
    Accept = "application/vnd.github+json"
    "X-GitHub-Api-Version" = "2022-11-28"
}

if (-not [string]::IsNullOrWhiteSpace($GitHubToken)) {
    $headers.Authorization = "Bearer $GitHubToken"
}

function Get-RulesetApiVersion([string] $tag) {
    $encodedRef = [Uri]::EscapeDataString($tag)
    $response = Invoke-RestMethod -Headers $headers -Uri "https://api.github.com/repos/ppy/osu/contents/osu.Game/Rulesets/Ruleset.cs?ref=$encodedRef"
    $encodedContent = $response.content -replace '\s', ''
    $source = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($encodedContent))
    $match = [regex]::Match($source, 'CURRENT_RULESET_API_VERSION\s*=\s*"(?<version>[^"]+)"')

    if (-not $match.Success) {
        throw "Could not read CURRENT_RULESET_API_VERSION from $tag."
    }

    return [Version]::Parse($match.Groups['version'].Value)
}

function Get-NextSuffix([string] $suffix) {
    if ([string]::IsNullOrEmpty($suffix)) {
        return "a"
    }

    $characters = $suffix.ToCharArray()

    for ($i = $characters.Length - 1; $i -ge 0; $i--) {
        if ($characters[$i] -ne 'z') {
            $characters[$i] = [char]([int]$characters[$i] + 1)
            return -join $characters
        }

        $characters[$i] = 'a'
    }

    return "a$(-join $characters)"
}

$osuReleases = Invoke-RestMethod -Headers $headers -Uri "https://api.github.com/repos/ppy/osu/releases?per_page=40"
$lazerRelease = $osuReleases | Where-Object { $_.tag_name.EndsWith("-lazer") } | Select-Object -First 1
$tachyonRelease = $osuReleases | Where-Object { $_.tag_name.EndsWith("-tachyon") } | Select-Object -First 1

if ($null -eq $lazerRelease -or $null -eq $tachyonRelease) {
    throw "Could not find both official osu! lazer and Tachyon releases."
}

$lazerApi = Get-RulesetApiVersion $lazerRelease.tag_name
$tachyonApi = Get-RulesetApiVersion $tachyonRelease.tag_name
$sticksRelease = Invoke-RestMethod -Headers $headers -Uri "https://api.github.com/repos/$Repository/releases/latest"
$isTagRelease = -not [string]::IsNullOrWhiteSpace($ReleaseTag)
$assetNames = @($sticksRelease.assets | ForEach-Object { $_.name })
$lazerAsset = "osu.Game.Rulesets.Sticks-$($lazerRelease.tag_name).dll"
$tachyonAsset = "osu.Game.Rulesets.Sticks-$($tachyonRelease.tag_name).dll"
$targets = [Collections.Generic.List[object]]::new()

if ($Force -or $assetNames -notcontains $lazerAsset -or $assetNames -notcontains "$lazerAsset.sha256") {
    $targets.Add([ordered]@{
        name = "Latest stable lazer"
        channel = "lazer"
        tag = $lazerRelease.tag_name
        client_version = ($lazerRelease.tag_name -replace '-lazer$', '')
        api_version = $lazerApi.ToString()
        asset_name = $lazerAsset
    })
}

if ($Force -or $assetNames -notcontains $tachyonAsset -or $assetNames -notcontains "$tachyonAsset.sha256") {
    $targets.Add([ordered]@{
        name = "Latest Tachyon canary"
        channel = "tachyon"
        tag = $tachyonRelease.tag_name
        client_version = ($tachyonRelease.tag_name -replace '-tachyon$', '')
        api_version = $tachyonApi.ToString()
        asset_name = $tachyonAsset
    })
}

$versionTag = if ($isTagRelease) { $ReleaseTag } else { $sticksRelease.tag_name }
$tagMatch = [regex]::Match($versionTag, '^v(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?<suffix>[a-z]*)$')

if (-not $tagMatch.Success) {
    throw "Sticks release tag does not contain a supported version: $versionTag"
}

$baseVersion = "$($tagMatch.Groups['major'].Value).$($tagMatch.Groups['minor'].Value).$($tagMatch.Groups['patch'].Value)"
$releaseTag = if ($isTagRelease) { $ReleaseTag } else { "v$baseVersion$(Get-NextSuffix $tagMatch.Groups['suffix'].Value)" }
$rulesetVersion = $releaseTag -replace '^v', ''
$assemblyVersion = "$baseVersion.0"
$matrix = @{ include = $targets.ToArray() } | ConvertTo-Json -Depth 5 -Compress
$hasChanges = ($targets.Count -gt 0).ToString().ToLowerInvariant()

"has_changes=$hasChanges" | Out-File -FilePath $GitHubOutput -Encoding utf8 -Append
"matrix=$matrix" | Out-File -FilePath $GitHubOutput -Encoding utf8 -Append
"previous_release_tag=$($sticksRelease.tag_name)" | Out-File -FilePath $GitHubOutput -Encoding utf8 -Append
"release_tag=$releaseTag" | Out-File -FilePath $GitHubOutput -Encoding utf8 -Append
"ruleset_version=$rulesetVersion" | Out-File -FilePath $GitHubOutput -Encoding utf8 -Append
"assembly_version=$assemblyVersion" | Out-File -FilePath $GitHubOutput -Encoding utf8 -Append
"lazer_tag=$($lazerRelease.tag_name)" | Out-File -FilePath $GitHubOutput -Encoding utf8 -Append
"tachyon_tag=$($tachyonRelease.tag_name)" | Out-File -FilePath $GitHubOutput -Encoding utf8 -Append
"lazer_api=$lazerApi" | Out-File -FilePath $GitHubOutput -Encoding utf8 -Append
"tachyon_api=$tachyonApi" | Out-File -FilePath $GitHubOutput -Encoding utf8 -Append
"lazer_asset=$lazerAsset" | Out-File -FilePath $GitHubOutput -Encoding utf8 -Append
"tachyon_asset=$tachyonAsset" | Out-File -FilePath $GitHubOutput -Encoding utf8 -Append
"source_ref=$ReleaseTag" | Out-File -FilePath $GitHubOutput -Encoding utf8 -Append

if ($targets.Count -eq 0) {
    Write-Host "Latest release $($sticksRelease.tag_name) already contains $lazerAsset and $tachyonAsset. No compatibility build is needed."
}
elseif ($isTagRelease) {
    Write-Host "Compatibility build required for new tagged release $releaseTag: $($targets.tag -join ', ')"
}
else {
    Write-Host "Compatibility build required for: $($targets.tag -join ', '). Next automatic release: $releaseTag"
}

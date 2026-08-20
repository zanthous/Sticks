[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [Version] $LazerApi,
    [Parameter(Mandatory = $true)]
    [Version] $TachyonApi,
    [string] $ProjectPath = ".\osu.Game.Rulesets.Sticks"
)

$boundaries = @(
    @{
        Symbol = "STICKS_RULESET_API_2026_818"
        MinimumApi = [Version]::Parse("2026.818.0")
    }
)
$sourceFiles = Get-ChildItem -LiteralPath $ProjectPath -Recurse -File |
    Where-Object { $_.Extension -in ".cs", ".csproj", ".props", ".targets" }

foreach ($boundary in $boundaries) {
    if ($LazerApi -lt $boundary.MinimumApi -or $TachyonApi -lt $boundary.MinimumApi) {
        continue
    }

    $matches = @($sourceFiles | Select-String -SimpleMatch $boundary.Symbol)

    if ($matches.Count -gt 0) {
        $locations = $matches | ForEach-Object { "$($_.Path):$($_.LineNumber)" }
        throw "Compatibility conditional $($boundary.Symbol) is obsolete for lazer $LazerApi and Tachyon $TachyonApi. Remove both branches and the associated build property. Found at: $($locations -join ', ')"
    }
}

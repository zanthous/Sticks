[CmdletBinding()]
param(
    [string] $ProjectPath = ".\osu.Game.Rulesets.Sticks"
)

$ErrorActionPreference = "Stop"

# Both supported channels use the same ruleset API. No compatibility symbols remain.
$sourceFiles = Get-ChildItem -LiteralPath $ProjectPath -Recurse -File |
    Where-Object { $_.Extension -in ".cs", ".csproj", ".props", ".targets" }

$matches = @($sourceFiles | Select-String -SimpleMatch "STICKS_RULESET_API_")

if ($matches.Count -gt 0) {
    $locations = $matches | ForEach-Object { "$($_.Path):$($_.LineNumber)" }
    throw "Unexpected compatibility symbol. Remove obsolete branches and build properties, or add an API boundary guard if a new upstream API requires them. Found at: $($locations -join ', ')"
}

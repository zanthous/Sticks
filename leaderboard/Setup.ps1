param(
    [Parameter(Mandatory)][string]$PublicOrigin,
    [Parameter(Mandatory)][int]$OsuClientId,
    [Parameter(Mandatory)][string]$DatabaseId
)

# Owner setup only. Nothing runs from the game or the release workflow.
# 1. Create a Cloudflare D1 database named zankai-osu and use its ID here.
# 2. Register an osu! OAuth app with callback: https://YOUR-WORKER/auth/callback
# 3. Run this script with the three public values. It prints the remaining commands.
# The client secret is entered through `wrangler secret put`, never passed on a command line.
$ErrorActionPreference = 'Stop'
if ($PublicOrigin -notmatch '^https://[a-zA-Z0-9.-]+$') { throw 'Use an HTTPS origin without a path or trailing slash.' }
if ($OsuClientId -le 0) { throw 'Invalid osu! client ID.' }
if ($DatabaseId -notmatch '^[a-fA-F0-9-]{36}$') { throw 'Invalid D1 database ID.' }

$path = Join-Path $PSScriptRoot 'wrangler.jsonc'
$config = Get-Content $path -Raw
$config = $config -replace '"PUBLIC_ORIGIN": "[^"]*"', ('"PUBLIC_ORIGIN": "' + $PublicOrigin + '"')
$config = $config -replace '"OSU_CLIENT_ID": "[^"]*"', ('"OSU_CLIENT_ID": "' + $OsuClientId + '"')
$config = $config -replace '"database_id": "[^"]*"', ('"database_id": "' + $DatabaseId + '"')
[IO.File]::WriteAllText($path, $config)
Write-Output 'Configuration written. From the leaderboard directory, run:'
Write-Output 'npx wrangler d1 migrations apply zankai-osu --remote'
Write-Output 'npx wrangler r2 bucket create zankai-osu-replays'
Write-Output 'Keep the replay bucket private (no public r2.dev URL). Enable R2 billing in Cloudflare first; Workers Paid is not required.'
Write-Output 'npx wrangler secret put OSU_CLIENT_SECRET'
Write-Output 'npx wrangler deploy'
Write-Output ('Build the ruleset with -p:SticksLeaderboardUrl=' + $PublicOrigin)
Write-Output ('For future releases, set the GitHub Actions repository variable STICKS_LEADERBOARD_URL to ' + $PublicOrigin)
Write-Output "In the D1 console, approve a Sticks set with: INSERT OR IGNORE INTO whitelisted_sets(ruleset,set_id) VALUES ('sticks',YOUR_SET_ID);"
Write-Output "Remove one with: DELETE FROM whitelisted_sets WHERE ruleset='sticks' AND set_id=YOUR_SET_ID;"
Write-Output 'Ban an osu! user ID with: INSERT OR IGNORE INTO banned_users(user_id) VALUES (USER_ID);'
Write-Output 'Unban with: DELETE FROM banned_users WHERE user_id = USER_ID;'

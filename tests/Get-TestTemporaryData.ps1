param([int]$ReviewAfterDays = 14)
$ErrorActionPreference = 'Stop'
if ($ReviewAfterDays -lt 1) { throw 'ReviewAfterDays must be positive' }
# Inventory only: legacy, failed and linked directories are never deleted by this tool.
Get-ChildItem -LiteralPath ([IO.Path]::GetTempPath()) -Directory -Force |
    Where-Object Name -Match '^LaunchDesk-(tests|log-benchmark|scale)-[a-f0-9]{32}$' |
    ForEach-Object {
        $state = 'legacy-no-owner'
        $ownerAlive = $null
        $markerPath = Join-Path $_.FullName '.launchdesk-test-workspace.json'
        if ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) { $state = 'linked-refused' }
        elseif (Test-Path -LiteralPath $markerPath -PathType Leaf) {
            try {
                if ((Get-Item -LiteralPath $markerPath).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked marker' }
                $owner = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
                if ($owner.key -notmatch '^[a-f0-9]{32}$' -or -not $_.Name.EndsWith('-' + $owner.key)) { throw 'Invalid owner marker' }
                $ownerAlive = $false
                $process = Get-Process -Id $owner.ownerPid -ErrorAction SilentlyContinue
                if ($process) {
                    try { $ownerAlive = $process.StartTime.ToUniversalTime().Ticks -eq $owner.ownerStartedUtcTicks }
                    finally { $process.Dispose() }
                }
                $state = if ($ownerAlive) { 'active' } else { 'retained-or-interrupted' }
            } catch { $state = 'unverified-marker' }
        }
        [pscustomobject]@{path=$_.FullName;state=$state;ownerAlive=$ownerAlive;
            ageDays=([DateTime]::UtcNow - $_.LastWriteTimeUtc).TotalDays;
            reviewSuggested=([DateTime]::UtcNow - $_.LastWriteTimeUtc).TotalDays -ge $ReviewAfterDays;
            automaticDeletion=$false}
    }

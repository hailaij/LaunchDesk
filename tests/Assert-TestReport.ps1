param([Parameter(Mandatory)][string]$Path, [switch]$RequireNoSkips, [int]$ExpectedCount = 107)
$ErrorActionPreference = 'Stop'
$metadata = Get-Content -LiteralPath ($Path + '.metadata.json') -Raw | ConvertFrom-Json
if ($metadata.schemaVersion -ne 1 -or $metadata.commit -notmatch '^[a-f0-9]{40}$' -or -not $metadata.sdk -or -not $metadata.os -or -not $metadata.runner -or @($metadata.sourceHashes).Count -eq 0) { throw 'Missing test provenance' }
if ([DateTimeOffset]::Parse($metadata.finishedAt) -lt [DateTimeOffset]::Parse($metadata.startedAt)) { throw 'Invalid evidence timestamps' }
if ((Get-FileHash -LiteralPath $Path).Hash -ne $metadata.reportSha256) { throw 'Test report hash mismatch' }
if ($metadata.workflowCommit -and $metadata.workflowCommit -ne $metadata.commit) { throw 'Runner/source commit mismatch' }
foreach ($source in $metadata.sourceHashes) { if (-not $source.path -or $source.sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid source hash inventory' } }
$results = @(Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json)
if ($results.Count -ne $ExpectedCount -or @($results.name | Sort-Object -Unique).Count -ne $results.Count) { throw "Unexpected/duplicate test inventory: $($results.Count), expected $ExpectedCount" }
foreach ($test in $results) {
    if ($test.status -notin @('passed', 'failed', 'skipped')) { throw "Missing/invalid status: $($test.name)" }
    if ($test.status -eq 'passed' -and $test.passed -ne $true) { throw 'Invalid passed result' }
    if ($test.status -eq 'failed' -and $test.passed -ne $false) { throw 'Invalid failed result' }
    if ($test.status -eq 'skipped' -and ($null -ne $test.passed -or -not $test.skipped -or -not $test.reason)) { throw 'Skip represented as pass/fail or has no reason' }
}
$failed = @($results | Where-Object status -eq 'failed').Count
$skipped = @($results | Where-Object status -eq 'skipped').Count
if ($failed -or ($RequireNoSkips -and $skipped)) { throw "Verification incomplete: $failed failed, $skipped skipped" }
Write-Output "Verified report: $($results.Count - $failed - $skipped) passed, $failed failed, $skipped skipped"

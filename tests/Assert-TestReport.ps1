param([Parameter(Mandatory)][string]$Path, [switch]$RequireNoSkips, [int]$ExpectedCount = 104)
$ErrorActionPreference = 'Stop'
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

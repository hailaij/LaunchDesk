param([Parameter(Mandatory)][string]$Path)
$ErrorActionPreference = 'Stop'
$report = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
$metadata = Get-Content -LiteralPath ($Path + '.metadata.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $Path).Hash -ne $metadata.reportSha256) { throw 'Benchmark evidence hash mismatch' }
if ($report.integrity -ne '10001 records exported without loss') { throw 'Missing log integrity evidence' }
$expected = @('decode-utf-8', 'decode-auto', 'writer-flush-True', 'writer-flush-False', 'persist-record', 'read-tail-1', 'read-idle')
if (@($report.metrics).Count -ne $expected.Count) { throw 'Incomplete benchmark inventory' }
foreach ($name in $expected) {
    $metric = @($report.metrics | Where-Object name -eq $name)
    if ($metric.Count -ne 1 -or $metric[0].count -ne 10000 -or $null -eq $metric[0].elapsedMs -or $null -eq $metric[0].allocatedBytes -or $metric[0].elapsedMs -le 0 -or $metric[0].allocatedBytes -lt 0) { throw "Invalid benchmark: $name" }
    $allocationLimitBytesPerOperation = switch ($name) { 'decode-utf-8' { 10000 }; 'decode-auto' { 10000 }; 'read-tail-1' { 128 }; 'read-idle' { 64 }; default { [double]::PositiveInfinity } }
    if ($metric[0].allocatedBytes / $metric[0].count -gt $allocationLimitBytesPerOperation) { throw "Allocation regression (bytes/operation): $name" }
    # Total milliseconds for all 10000 operations; keep the original shared-runner budgets.
    $elapsedLimitMsTotal = if ($name -eq 'persist-record') { 30000 } else { 5000 }
    if ($metric[0].elapsedMs -gt $elapsedLimitMsTotal) { throw "Latency regression (ms total): $name" }
}
Write-Output 'Log backend performance and persisted-record integrity gates passed'

param([Parameter(Mandatory)][string]$ScreenshotPath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$absolute = [IO.Path]::GetFullPath($ScreenshotPath)
$prefix = Join-Path ([IO.Path]::GetDirectoryName($absolute)) ([IO.Path]::GetFileNameWithoutExtension($absolute))
function Series($Values, [int]$Count) {
    $samples = @($Values)
    if ($samples.Count -ne $Count) { throw "Incomplete performance samples: $($samples.Count), expected $Count" }
    foreach ($value in $samples) {
        if ($null -eq $value -or $value -isnot [ValueType] -or [double]::IsNaN([double]$value) -or
            [double]::IsInfinity([double]$value) -or $value -lt 0) { throw 'Invalid performance sample' }
    }
    $sorted = @($samples | Sort-Object)
    return @{ p95 = $sorted[[Math]::Ceiling($Count * 0.95) - 1]; max = $sorted[-1] }
}
$live = Get-Content -LiteralPath "$prefix-live-log-performance.json" -Raw | ConvertFrom-Json
$incremental = Get-Content -LiteralPath "$prefix-incremental-performance.json" -Raw | ConvertFrom-Json
$virtual = Get-Content -LiteralPath "$prefix-virtualization.json" -Raw | ConvertFrom-Json
$liveTimes = Series $live.refreshAndLayoutMs 10
$incrementalTimes = Series $incremental.refreshAndLayoutMs 40
$allocated = Series $incremental.refreshAllocatedBytes 40
$virtualTimes = Series $virtual.updateAndLayoutMs 2
$limits = @{ incrementalP95Ms = 250; incrementalMaxMs = 1500; synchronousAllocatedBytes = 1048576;
    liveP95Ms = 750; liveMaxMs = 1500; virtualMaxMs = 1500; renderedRows = 100 }
if ($live.batches -ne 10 -or $live.linesPerBatch -ne 1000 -or $live.displayedCharacters -gt 500000 -or
    $live.renderedRows -le 0 -or $live.renderedRows -gt $limits.renderedRows -or
    $incremental.batches -ne 40 -or $incremental.retainedRows -ne 3000 -or $incremental.rowsPerBatch -ne 20 -or
    $incremental.resets -ne 0 -or $incremental.selectionAndAnchorPreserved -ne $true -or
    $incremental.maxRenderedRows -le 0 -or $incremental.maxRenderedRows -gt $limits.renderedRows -or
    $virtual.totalShortLines -ne 30000 -or $virtual.virtualCopyAllPassed -ne $true -or $virtual.selectionAndReadingPositionPreserved -ne $true) {
    throw 'Log performance structural gate failed'
}
if ($incrementalTimes.p95 -gt $limits.incrementalP95Ms -or $incrementalTimes.max -gt $limits.incrementalMaxMs -or
    $allocated.max -gt $limits.synchronousAllocatedBytes -or $liveTimes.p95 -gt $limits.liveP95Ms -or
    $liveTimes.max -gt $limits.liveMaxMs -or $virtualTimes.max -gt $limits.virtualMaxMs) {
    throw 'Log performance time/allocation budget exceeded'
}
@{ status = 'passed'; limits = $limits; incrementalMs = $incrementalTimes; liveMs = $liveTimes;
    synchronousAllocatedBytes = $allocated; virtualMs = $virtualTimes; resets = $incremental.resets } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$prefix-performance-gate.json" -Encoding utf8
Write-Output "Log performance gate passed: incremental P95=$($incrementalTimes.p95) ms, live P95=$($liveTimes.p95) ms"

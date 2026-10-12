param([Parameter(Mandatory)][string]$ScreenshotPath, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$inputPath = [IO.Path]::GetFullPath($ScreenshotPath)
$prefix = Join-Path ([IO.Path]::GetDirectoryName($inputPath)) ([IO.Path]::GetFileNameWithoutExtension($inputPath))
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$results = [Collections.Generic.List[object]]::new()
foreach ($case in @('slow', 'allocation', 'reset', 'incomplete', 'invalid-sample')) {
    $target = Join-Path $output $case
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    foreach ($suffix in @('live-log-performance', 'incremental-performance', 'virtualization')) {
        Copy-Item -LiteralPath "$prefix-$suffix.json" -Destination (Join-Path $target "probe-$suffix.json") -Force
    }
    $path = Join-Path $target 'probe-incremental-performance.json'
    $metrics = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    switch ($case) {
        'slow' { $metrics.refreshAndLayoutMs[0] = 1501 }
        'allocation' { $metrics.refreshAllocatedBytes[0] = 1048577 }
        'reset' { $metrics.resets = 1 }
        'incomplete' { $metrics.refreshAndLayoutMs = @() }
        'invalid-sample' { $metrics.refreshAndLayoutMs[0] = 'NaN' }
    }
    $metrics | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $path -Encoding utf8
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Assert-LogPerformance.ps1') -ScreenshotPath (Join-Path $target 'probe.png') | Out-Null }
    catch { $rejected = $true }
    if (-not $rejected) { throw "Performance gate accepted negative fixture: $case" }
    $results.Add(@{ name = $case; status = 'passed'; expectedRejection = $true })
}
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'results.json') -Encoding utf8

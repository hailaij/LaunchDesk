param([string]$OutputDirectory = 'artifacts/verification/ui', [string]$AppPath = 'src/LaunchDesk.App/bin/Release/net10.0-windows/LaunchDesk.exe')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$exe = (Resolve-Path -LiteralPath $AppPath).Path
function Run-App([string]$Mode, [string]$Report) {
    $process = Start-Process -FilePath $exe -ArgumentList @($Mode, ('"' + $Report + '"')) -PassThru
    try {
        if (-not $process.WaitForExit(120000)) { throw "UI smoke timed out: $Mode" }
        if ($process.ExitCode -ne 0) { throw "UI smoke failed: $Mode, exit $($process.ExitCode)" }
    } finally { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }; $process.Dispose() }
}
foreach ($mode in @('--ui-smoke', '--ui-smoke-en-min')) {
    $path = Join-Path $output ($mode.TrimStart('-') + '.png')
    Run-App $mode $path
    $bitmap = [Drawing.Bitmap]::new($path)
    try {
        if ($bitmap.Width -lt 800 -or $bitmap.Height -lt 500) { throw 'Screenshot dimensions too small' }
        $colors = [Collections.Generic.HashSet[int]]::new()
        for ($y = 0; $y -lt $bitmap.Height; $y += 13) {
            for ($x = 0; $x -lt $bitmap.Width; $x += 13) { [void]$colors.Add($bitmap.GetPixel($x, $y).ToArgb()) }
        }
        if ($colors.Count -lt 12) { throw 'Blank/near-blank UI screenshot' }
    } finally { $bitmap.Dispose() }
    $metricsPath = Join-Path ([IO.Path]::GetDirectoryName($path)) ([IO.Path]::GetFileNameWithoutExtension($path) + '-virtualization.json')
    $metrics = Get-Content -LiteralPath $metricsPath -Raw | ConvertFrom-Json
    if ($metrics.totalShortLines -ne 30000 -or -not $metrics.virtualCopyAllPassed -or -not $metrics.selectionAndReadingPositionPreserved) { throw 'Virtualization evidence incomplete' }
    & (Join-Path $PSScriptRoot 'Assert-LogPerformance.ps1') -ScreenshotPath $path
}
& (Join-Path $PSScriptRoot 'Verify-LogPerformanceContracts.ps1') -ScreenshotPath (Join-Path $output 'ui-smoke.png') -OutputDirectory (Join-Path $output 'performance-contracts')
foreach ($mode in @('--timer', '--loaded')) {
    dotnet run --project tests/LaunchDesk.UiChecks -c Release --no-build -- $mode (Join-Path $output ($mode.TrimStart('-') + '.json'))
    if ($LASTEXITCODE -ne 0) { throw "UI host failed: $mode" }
    $result = Get-Content -LiteralPath (Join-Path $output ($mode.TrimStart('-') + '.json')) -Raw | ConvertFrom-Json
    if (-not $result.diagnosticsWritten) { throw "Missing UI diagnostics: $mode" }
    if ($mode -eq '--timer' -and -not $result.timerResumedAfterError) { throw 'UI timer did not resume' }
    if ($mode -eq '--loaded' -and $result.loadedExceptionEscaped) { throw 'Loaded exception escaped' }
}
& (Join-Path $PSScriptRoot 'Verify-UiFailures.ps1') -AppPath $exe -OutputDirectory (Join-Path $output 'fatal-cleanup')
@{ status = 'passed'; screenshotPixelChecks = $true; timerRecovery = $true; fatalCleanup = $true; logPerformanceGate = $true } | ConvertTo-Json |
    Set-Content -LiteralPath (Join-Path $output 'summary.json') -Encoding utf8

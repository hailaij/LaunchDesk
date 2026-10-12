param(
    [string]$AppPath = 'src/LaunchDesk.App/bin/Release/net10.0-windows/LaunchDesk.exe',
    [string]$HostPath = 'tests/LaunchDesk.UiChecks/bin/Release/net10.0-windows/LaunchDesk.UiChecks.exe',
    [string]$OutputDirectory = 'artifacts/verification/correctness/fatal-cleanup'
)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $HostPath).Path
$production = (Resolve-Path -LiteralPath $AppPath).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
foreach ($mode in @('--ui-fault-loaded','--ui-fault-dispatcher','--ui-smoke','--ui-smoke-min','--ui-smoke-en','--ui-smoke-en-min')) {
    $marker = Join-Path $output ('rejected-' + [guid]::NewGuid().ToString('N'))
    $process = Start-Process -FilePath $production -ArgumentList @($mode, ('"' + $marker + '"')) -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit(5000) -or $process.ExitCode -ne 2 -or (Test-Path -LiteralPath $marker)) { throw "Production accepted test argument: $mode" }
    } finally { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }; $process.Dispose() }
}
$results = foreach ($mode in @('loaded', 'dispatcher')) {
    $marker = Join-Path $output ($mode + '-' + [guid]::NewGuid().ToString('N'))
    $process = Start-Process -FilePath $exe -ArgumentList @('--ui-fault-' + $mode, ('"' + $marker + '"')) -WindowStyle Hidden -PassThru
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(10)
        while (-not (Test-Path -LiteralPath ($marker + '.pid'))) {
            if ($process.HasExited -or [DateTime]::UtcNow -gt $deadline) { throw "Fault fixture failed to initialize: $mode" }
            Start-Sleep -Milliseconds 20
        }
        $rootId = [int](Get-Content -LiteralPath ($marker + '.pid'))
        $children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$rootId")
        if ($children.Count -eq 0) { throw "No isolated child observed: $mode" }
        [IO.File]::WriteAllText($marker + '.observed', 'observed')
        if (-not $process.WaitForExit(10000)) { throw "Fault cleanup timed out: $mode" }
        $process.Refresh()
        $alive = @(@($rootId) + @($children.ProcessId) | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
        $diagnostics = Get-Content -LiteralPath ($marker + '.diagnostics-path')
        if ($process.ExitCode -ne 1 -or $alive.Count -ne 0 -or -not (Test-Path -LiteralPath $diagnostics)) { throw "Fault cleanup failed: $mode" }
        [pscustomobject]@{ mode = $mode; exitCode = $process.ExitCode; rootId = $rootId; childIds = @($children.ProcessId); survivingIds = $alive; diagnostics = $diagnostics; passed = $true }
    }
    finally {
        # Only the owned test application is eligible for emergency cleanup; its Job owns the fixture tree.
        if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
}
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'results.json') -Encoding utf8
$results

param([string]$OutputDirectory = 'artifacts/verification/data-lifecycle-repro/verified')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = [IO.Path]::GetFullPath($OutputDirectory, $root)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$source = (Get-Content -LiteralPath (Join-Path $root 'src/LaunchDesk.App/MainWindow.xaml.cs') -Raw).Replace("`r`n", "`n")
$startMarker = "            try`n            {`n                store.Save(config);"
$endMarker = "            try`n            {`n                using var run"
$start = $source.IndexOf($startMarker, $source.IndexOf('private async void SettingsClick'))
$end = $source.IndexOf($endMarker, $start)
if ($start -lt 0 -or $end -lt 0) { throw 'Settings block boundaries changed; review reproduction harness.' }
$block = $source.Substring($start, $end - $start).
    Replace('manager = new(config, store.Root, Program.SignalAsync)', 'manager = NewManager()').
    Replace('AppContext.BaseDirectory', 'BasePath').Replace('DataLocation.Save(destinationRoot)', 'SaveLocator(destinationRoot)')
$harness = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'LaunchDesk.DataRepros/SettingsHarness.cs') -Raw).Replace("`r`n", "`n")
if (-not $harness.Contains($block)) { throw 'Settings harness drifted from production control flow.' }
$paths = @('src/LaunchDesk.App/MainWindow.xaml.cs', 'src/LaunchDesk.Core/ProcessManager.cs',
    'src/LaunchDesk.Core/Configuration.cs', 'src/LaunchDesk.Core/DataLocation.cs', 'tests/LaunchDesk.DataRepros/SettingsHarness.cs',
    'tests/LaunchDesk.DataRepros/Program.cs')
$hashes = @($paths | ForEach-Object { @{ path = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $root $_)).Hash } })
Push-Location $root
try {
    @{ generatedAt = [DateTimeOffset]::UtcNow.ToString('o'); commit = (git rev-parse HEAD); dirtyFiles = @(git status --short);
       sdk = (dotnet --version); os = [Environment]::OSVersion.VersionString; sourceHashes = $hashes;
       limitation = 'Extracted settings transaction; no real dialog, startup registry or user locator invoked.' } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'environment.json') -Encoding utf8
    dotnet restore tests/LaunchDesk.DataRepros --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Repro locked restore failed.' }
    dotnet build tests/LaunchDesk.DataRepros -c Release --no-restore -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Repro build failed.' }
    dotnet run --project tests/LaunchDesk.DataRepros -c Release --no-build -- $output
    if ($LASTEXITCODE -ne 0) { throw 'Reproduction verification failed; inspect results.json.' }
    $results = (Get-Content -LiteralPath (Join-Path $output 'results.json') -Raw | ConvertFrom-Json).results
    if ($results.Count -ne 6 -or @($results | Where-Object verification -ne 'passed').Count) { throw 'Incomplete reproduction evidence.' }
} finally { Pop-Location }

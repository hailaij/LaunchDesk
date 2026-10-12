param([Parameter(Mandatory)][string]$Directory, [Parameter(Mandatory)][string]$Pin, [ValidateSet('App','Uninstaller','Installer')][string]$Mode = 'App')
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$checker = Join-Path $project 'tools/LaunchDesk.ReleaseChecks/bin/Release/net10.0-windows/LaunchDesk.ReleaseChecks.exe'
if (-not (Test-Path -LiteralPath $checker)) { throw 'Build ReleaseChecks first' }
switch ($Mode) {
    App { $files = @('LaunchDesk.exe', 'LaunchDesk.dll', 'LaunchDesk.Core.dll', 'LaunchDesk.Cli.exe', 'LaunchDesk.Cli.dll', 'LaunchDesk.Host.exe', 'LaunchDesk.Host.dll') | ForEach-Object { Join-Path $Directory $_ } }
    Uninstaller {
        $files = @(Get-ChildItem -LiteralPath $Directory -Filter 'uninst-*.e32' -File | Select-Object -ExpandProperty FullName)
        if ($files.Count -ne 1) { throw 'Expected exactly one signed uninstaller' }
    }
    Installer {
        $version = ([xml](Get-Content -LiteralPath (Join-Path $project 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
        $files = @(Join-Path $Directory "LaunchDesk-v$version-win-x64-setup.exe")
    }
}
foreach ($file in $files) {
    & $checker --verify-file $file $Pin
    if ($LASTEXITCODE -ne 0) { throw "Publisher/native trust check failed: $file" }
    $signature = Get-AuthenticodeSignature -LiteralPath $file
    if ($signature.Status -ne 'Valid' -or $null -eq $signature.TimeStamperCertificate) { throw "Missing valid timestamp: $file" }
}
if ($Mode -eq 'App') {
    & $checker --embedded-pin (Join-Path $Directory 'LaunchDesk.Core.dll') $Pin
    if ($LASTEXITCODE -ne 0) { throw 'Embedded production pin mismatch' }
}

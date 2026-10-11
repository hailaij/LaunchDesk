param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Destination, [ValidateSet('App','Uninstaller','Installer')][string]$Mode)
$ErrorActionPreference = 'Stop'
$config = & (Join-Path $PSScriptRoot 'Get-ReleaseConfiguration.ps1')
& (Join-Path $PSScriptRoot 'Verify-ReleaseFiles.ps1') -Directory $Source -Pin $config.certificateSha256 -Mode $Mode
$files = @(Get-ChildItem -LiteralPath $Source -File)
switch ($Mode) {
    App { $allowed = @('LaunchDesk.exe','LaunchDesk.dll','LaunchDesk.Core.dll') }
    Uninstaller { $allowed = @(Get-ChildItem -LiteralPath $Destination -Filter 'uninst-*.e32' -File | Select-Object -ExpandProperty Name) }
    Installer { $allowed = @(Get-ChildItem -LiteralPath $Destination -Filter '*-setup.exe' -File | Select-Object -ExpandProperty Name) }
}
if ($files.Count -ne $allowed.Count -or @($files | Where-Object Name -notin $allowed).Count -or @(Get-ChildItem -LiteralPath $Source -Directory).Count) { throw 'Signing return inventory differs from expected inputs' }
foreach ($file in $files) { Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $Destination $file.Name) -Force }

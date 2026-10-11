param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$SigningDirectory,
    [string]$OutputDirectory = 'artifacts/signing/installer',
    [string]$CompilerPath,
    [switch]$PrepareUninstaller
)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$signing = [IO.Path]::GetFullPath($SigningDirectory)
$output = [IO.Path]::GetFullPath($OutputDirectory)
$version = ([xml](Get-Content -LiteralPath (Join-Path $project 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
if (-not $CompilerPath) {
    $CompilerPath = @((Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe'), (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'), (Join-Path $env:ProgramFiles 'Inno Setup 6/ISCC.exe')) |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not (Test-Path -LiteralPath $CompilerPath)) { throw 'Inno compiler missing' }
if ($PrepareUninstaller) {
    if ((Test-Path -LiteralPath $signing) -and @(Get-ChildItem -LiteralPath $signing -Force).Count) { throw 'Use a fresh, empty uninstaller signing directory' }
    New-Item -ItemType Directory -Path $signing -Force | Out-Null
} else {
    $config = & (Join-Path $PSScriptRoot 'Get-ReleaseConfiguration.ps1')
    & (Join-Path $PSScriptRoot 'Verify-ReleaseFiles.ps1') -Directory $publish -Pin $config.certificateSha256 -Mode App
    & (Join-Path $PSScriptRoot 'Verify-ReleaseFiles.ps1') -Directory $signing -Pin $config.certificateSha256 -Mode Uninstaller
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
# Inno's documented manual signed-uninstaller flow first emits a cache PE and rejects unsigned input.
$log = & $CompilerPath "/DAppVersion=$version" "/DPublishDir=$publish" "/DOutputDir=$output" /DSignedBuild "/DSigningDir=$signing" (Join-Path $project 'installer/LaunchDesk.iss') 2>&1
$code = $LASTEXITCODE
$log | Out-File -LiteralPath (Join-Path $output 'compiler.log') -Encoding utf8
$files = @(Get-ChildItem -LiteralPath $signing -Filter 'uninst-*.e32' -File)
if ($PrepareUninstaller) {
    if ($code -eq 0 -or $files.Count -ne 1 -or (Get-AuthenticodeSignature -LiteralPath $files[0].FullName).Status -ne 'NotSigned') { throw 'Expected first-pass unsigned uninstaller cache was not emitted/rejected' }
    Write-Output $files[0].FullName
} else {
    if ($code -ne 0) { throw "Signed uninstaller compilation failed: $code. See compiler.log" }
    Write-Output (Join-Path $output "LaunchDesk-v$version-win-x64-setup.exe")
}

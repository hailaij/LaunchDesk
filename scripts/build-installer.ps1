param(
    [string]$CompilerPath,
    [string]$OutputDirectory = 'artifacts/installers',
    [string]$UpdateSignerSha256 = ''
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$CompilerPath = & (Join-Path $PSScriptRoot 'Resolve-InnoCompiler.ps1') -CompilerPath $CompilerPath
$version = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Installer requires a numeric major.minor.patch version.' }
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory, $projectRoot)
$buildRoot = Join-Path $outputRoot ('build-' + [guid]::NewGuid().ToString('N'))
# Reuse the release packager so runtime licenses and documentation are included.
& (Join-Path $PSScriptRoot 'package.ps1') -OutputDirectory $buildRoot -UpdateSignerSha256 $UpdateSignerSha256
$publishRoot = Join-Path $buildRoot 'LaunchDesk-win-x64'
& $CompilerPath ("/DAppVersion=$version") ("/DPublishDir=$publishRoot") ("/DOutputDir=$outputRoot") (Join-Path $projectRoot 'installer\LaunchDesk.iss')
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed: $LASTEXITCODE" }
$setupPath = Join-Path $outputRoot ("LaunchDesk-v$version-win-x64-setup.exe")
if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) { throw 'Compiler did not create the expected installer.' }
$hash = Get-FileHash -LiteralPath $setupPath -Algorithm SHA256
($hash.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($setupPath)) | Out-File -LiteralPath ($setupPath + '.sha256') -Encoding ascii
Write-Output $setupPath

param(
    [string]$CompilerPath,
    [string]$OutputDirectory = 'artifacts/installers',
    [string]$UpdateSignerSha256 = ''
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not $CompilerPath) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    $CompilerPath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (-not $CompilerPath) {
        $command = Get-Command ISCC.exe -CommandType Application -ErrorAction SilentlyContinue
        if ($command) { $CompilerPath = $command.Source }
    }
}
if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
    throw 'Inno Setup 6 is required. Install it from https://jrsoftware.org/isdl.php or supply -CompilerPath.'
}
$CompilerPath = (Resolve-Path -LiteralPath $CompilerPath).Path
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

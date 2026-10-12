param(
    [Parameter(Mandatory)][string]$SetupPath,
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$PreviousPublishDirectory,
    [string]$CompilerPath = 'D:\tool\Inno Setup 6\ISCC.exe',
    [string]$OutputDirectory = 'artifacts/verification/installer',
    [ValidateSet('english','chinesesimplified')][string]$Language = 'english'
)
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
$sandbox = Join-Path $output ('sandbox-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $sandbox -Force | Out-Null
$setup = (Resolve-Path -LiteralPath $SetupPath).Path
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$previous = (Resolve-Path -LiteralPath $PreviousPublishDirectory).Path
$version = ([xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot '../Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$identity = [guid]::NewGuid().ToString().ToUpperInvariant()
$registry = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{$identity}_is1"
$productionRegistry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{9A2D379B-6292-4F1F-98D1-68F47AB28483}_is1'
function RegistrationSnapshot {
    if (Test-Path -LiteralPath $productionRegistry) { return (Get-ItemProperty -LiteralPath $productionRegistry | ConvertTo-Json -Depth 5 -Compress) }
    return 'absent'
}
$productionBefore = RegistrationSnapshot
if (Test-Path -LiteralPath $registry) { throw 'Test identity already registered' }
if ((Get-AuthenticodeSignature -LiteralPath $setup).Status -ne 'NotSigned') { throw 'Expected explicitly unsigned candidate' }
function Scoped([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($sandbox + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Unscoped installer operation: $resolved" }
    return $resolved
}
function Run([string]$Executable, [string[]]$Arguments) {
    $process = Start-Process -FilePath (Scoped $Executable) -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit(180000)) { throw 'Installer timed out' }
        if ($process.ExitCode -ne 0) { throw "Installer failed: $($process.ExitCode)" }
    } finally { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }; $process.Dispose() }
}
function Compile([string]$Source, [string]$Version, [string]$Name) {
    $directory = Join-Path $sandbox $Name
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    & $CompilerPath "/DIsolationId=$identity" "/DAppVersion=$Version" "/DPublishDir=$Source" "/DOutputDir=$directory" (Join-Path $PSScriptRoot '../installer/LaunchDesk.iss') |
        Out-File -LiteralPath (Join-Path $directory 'compile.log') -Encoding utf8
    if ($LASTEXITCODE -ne 0) { throw 'Isolated installer compilation failed' }
    return (Join-Path $directory "LaunchDesk-v$Version-win-x64-setup.exe")
}
function Install([string]$Installer, [string]$Destination, [string]$Name) {
    Run $Installer @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS', '/TASKS=""', ('/LANG=' + $Language), ('/DIR="' + (Scoped $Destination) + '"'), ('/LOG="' + (Join-Path $sandbox ($Name + '.log')) + '"'))
    if (-not (Test-Path -LiteralPath $registry)) { throw 'Isolated installation not registered' }
    if ((Get-ItemProperty -LiteralPath $registry).InstallLocation.TrimEnd('\') -ne $Destination) { throw 'Wrong isolated install path' }
    if ((Get-ItemProperty -LiteralPath $registry).'Inno Setup: Language' -ne $Language) { throw 'Wrong installer language' }
}
function Uninstall([string]$Destination, [string]$Name) {
    Run (Join-Path $Destination 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="' + (Join-Path $sandbox ($Name + '.log')) + '"'))
    if (Test-Path -LiteralPath $registry) { throw 'Isolated uninstall registration remained' }
}
function VerifyPayload([string]$Destination) {
    foreach ($file in Get-ChildItem -LiteralPath $publish -File -Recurse) {
        $relative = [IO.Path]::GetRelativePath($publish, $file.FullName)
        $installed = Join-Path $Destination $relative
        if (-not (Test-Path -LiteralPath $installed) -or (Get-FileHash -LiteralPath $installed).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) {
            throw "Installed payload mismatch: $relative"
        }
    }
}
$activeInstallation = $null
$result = [ordered]@{ status = 'running'; version = $version; language = $Language; candidateSetup = $setup; candidateSha256 = (Get-FileHash -LiteralPath $setup).Hash;
    isolatedIdentity = $identity; sandbox = $sandbox; scope = 'Same installer script and release payload; isolated AppId/mutex, no shortcuts. Not a production-identity or Windows 10 acceptance.' }
try {
    $newInstaller = Compile $publish $version 'new-compiler'
    $previousVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $previous 'LaunchDesk.exe')).FileVersion -replace '\.0$', ''
    if ($previousVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Previous payload version is invalid' }
    $result.previousVersion = $previousVersion
    $oldInstaller = Compile $previous $previousVersion 'old-compiler'
    $fresh = Join-Path $sandbox 'fresh-install'; $activeInstallation = $fresh
    Install $newInstaller $fresh 'fresh'
    VerifyPayload $fresh
    foreach ($name in @('data', 'portable.flag', 'config.json', 'logs', 'backups')) {
        if (Test-Path -LiteralPath (Join-Path $fresh $name)) { throw "First install contains user data: $name" }
    }
    if (@(Get-ChildItem -LiteralPath $fresh -Filter '*.jsonl' -Recurse -File).Count) { throw 'First install contains log samples' }
    dotnet run --project tests/LaunchDesk.Tests -c Release --no-build -- --install-data-probe (Join-Path $fresh 'data') 0
    if ($LASTEXITCODE -ne 0) { throw 'First-use configuration is not empty' }
    $result.freshEmpty = $true
    Uninstall $fresh 'fresh-uninstall'; $activeInstallation = $null
    $upgrade = Join-Path $sandbox 'upgrade-install'; $activeInstallation = $upgrade
    Install $oldInstaller $upgrade 'old-install'
    $data = Join-Path $upgrade 'data'; $logs = Join-Path $data 'logs'; $backups = Join-Path $data 'backups/manual'
    New-Item -ItemType Directory -Path $logs,$backups -Force | Out-Null
    $configuration = @{SchemaVersion=1;Preferences=@{Language='en-US';Theme='Dark';CheckUpdatesOnStartup=$false};Folders=@();Items=@(@{Id=[guid]::NewGuid().ToString();Name='preserved';Target='not-executed';AutoStart=$false})} | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText((Join-Path $data 'config.json'), $configuration)
    [IO.File]::WriteAllText((Join-Path $logs 'sentinel.jsonl'), '{"text":"preserved log"}')
    [IO.File]::WriteAllText((Join-Path $backups 'config-sentinel.json'), $configuration)
    [IO.File]::WriteAllText((Join-Path $upgrade 'portable.flag'), 'isolated acceptance')
    $tracked = @((Join-Path $data 'config.json'), (Join-Path $logs 'sentinel.jsonl'), (Join-Path $backups 'config-sentinel.json'), (Join-Path $upgrade 'portable.flag'))
    $hashes = @{}; foreach ($path in $tracked) { $hashes[$path] = (Get-FileHash -LiteralPath $path).Hash }
    Install $newInstaller $upgrade 'upgrade'
    VerifyPayload $upgrade
    foreach ($path in $tracked) { if ((Get-FileHash -LiteralPath $path).Hash -ne $hashes[$path]) { throw "Upgrade changed data: $path" } }
    dotnet run --project tests/LaunchDesk.Tests -c Release --no-build -- --install-data-probe $data 1
    if ($LASTEXITCODE -ne 0) { throw 'Preserved upgrade configuration cannot load' }
    $result.upgradeDataPreserved = $true
    $result.installedVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $upgrade 'LaunchDesk.exe')).FileVersion
    if ($result.installedVersion -ne "$version.0") { throw 'Installed binary version mismatch' }
    Uninstall $upgrade 'upgrade-uninstall'; $activeInstallation = $null
    foreach ($path in $tracked) { if ((Get-FileHash -LiteralPath $path).Hash -ne $hashes[$path]) { throw "Uninstall changed data: $path" } }
    $result.uninstallDataPreserved = $true
    $result.preservedHashes = $hashes
    $result.productionRegistrationUnchanged = (RegistrationSnapshot) -eq $productionBefore
    if (-not $result.productionRegistrationUnchanged) { throw 'Production registration changed' }
    $result.status = 'passed'
} catch { $result.status = 'failed'; $result.error = $_.ToString(); throw }
finally {
    if ($activeInstallation -and (Test-Path -LiteralPath (Join-Path $activeInstallation 'unins000.exe'))) { Uninstall $activeInstallation 'cleanup-uninstall' }
    $result.isolatedRegistrationRemoved = -not (Test-Path -LiteralPath $registry)
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'results.json') -Encoding utf8
}

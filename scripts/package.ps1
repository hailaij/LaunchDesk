param([string]$OutputDirectory = 'artifacts', [string]$UpdateSignerSha256 = '')
$ErrorActionPreference = 'Stop'
if ($UpdateSignerSha256 -and $UpdateSignerSha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'UpdateSignerSha256 must be the publisher certificate DER SHA-256 (64 hex characters).' }
$projectRoot = Split-Path $PSScriptRoot -Parent
$versionText = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory, $projectRoot)
$publishRoot = Join-Path $outputRoot 'LaunchDesk-win-x64'
if (Test-Path -LiteralPath $publishRoot) {
    if ((Get-Item -LiteralPath $publishRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked publish directory refused.' }
    if (@(Get-ChildItem -LiteralPath $publishRoot -Force).Count) { throw 'Publish directory is not empty. Use a new output directory; stale files must not enter a release.' }
}
$stageRoot = Join-Path $outputRoot ('.package-' + [guid]::NewGuid().ToString('N'))
dotnet publish (Join-Path $projectRoot 'src/LaunchDesk.App/LaunchDesk.App.csproj') -c Release -r win-x64 --self-contained true -o $stageRoot "-p:UpdateSignerSha256=$UpdateSignerSha256"
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
dotnet publish (Join-Path $projectRoot 'src/LaunchDesk.Cli/LaunchDesk.Cli.csproj') -c Release -r win-x64 --self-contained true -o $stageRoot "-p:UpdateSignerSha256=$UpdateSignerSha256"
if ($LASTEXITCODE -ne 0) { throw 'CLI publish failed.' }
dotnet publish (Join-Path $projectRoot 'src/LaunchDesk.Host/LaunchDesk.Host.csproj') -c Release -r win-x64 --self-contained true -o $stageRoot "-p:UpdateSignerSha256=$UpdateSignerSha256"
if ($LASTEXITCODE -ne 0) { throw 'Service/elevation host publish failed.' }
dotnet build (Join-Path $projectRoot 'tools/LaunchDesk.ReleaseChecks/LaunchDesk.ReleaseChecks.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Release verification tool build failed.' }
& (Join-Path $projectRoot 'tools/LaunchDesk.ReleaseChecks/bin/Release/net10.0-windows/LaunchDesk.ReleaseChecks.exe') --embedded-pin (Join-Path $stageRoot 'LaunchDesk.Core.dll') $UpdateSignerSha256
if ($LASTEXITCODE -ne 0) { throw 'Published assembly publisher pin mismatch.' }
foreach ($file in 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'PRIVACY.md') {
    Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination $stageRoot
}
foreach ($guide in @(@('user-guide.md', 'README.md'), @('user-guide.en.md', 'README.en.md'))) {
    $text = [IO.File]::ReadAllText((Join-Path $projectRoot ('docs/' + $guide[0]))).Replace('](../', '](')
    foreach ($detail in 'cli-and-recovery.md','health-monitoring.md','service-accessibility-installer.md') { $text = $text.Replace(('](' + $detail + ')'), ('](docs/' + $detail + ')')) }
    [IO.File]::WriteAllText((Join-Path $stageRoot $guide[1]), $text, [Text.UTF8Encoding]::new($false))
}
$docsRoot = Join-Path $stageRoot 'docs'
New-Item -ItemType Directory -Path $docsRoot -Force | Out-Null
foreach ($guide in 'cli-and-recovery.md','health-monitoring.md','service-accessibility-installer.md','service-accessibility-evidence.md') {
    $text = [IO.File]::ReadAllText((Join-Path $projectRoot ('docs/' + $guide))).Replace(
        "](release-v$versionText.md)", "](https://github.com/hailaij/LaunchDesk/blob/v$versionText/docs/release-v$versionText.md)")
    [IO.File]::WriteAllText((Join-Path $docsRoot $guide), $text, [Text.UTF8Encoding]::new($false))
}
# Debug symbols and development evidence are not part of a user distribution.
foreach ($symbol in (Get-ChildItem -LiteralPath $stageRoot -Filter '*.pdb' -File)) {
    Remove-Item -LiteralPath $symbol.FullName -Force -ErrorAction Stop
}
$noticesRoot = Join-Path $stageRoot 'runtime-notices'
New-Item -ItemType Directory -Path $noticesRoot -Force | Out-Null
$assets = Get-Content -LiteralPath (Join-Path $projectRoot 'src/LaunchDesk.App/obj/project.assets.json') -Raw | ConvertFrom-Json
$packagesRoot = $assets.project.restore.packagesPath
$runtime = Get-Content -LiteralPath (Join-Path $stageRoot 'LaunchDesk.runtimeconfig.json') -Raw | ConvertFrom-Json
foreach ($framework in $runtime.runtimeOptions.includedFrameworks) {
    $package = $framework.name.ToLowerInvariant() + '.runtime.win-x64'
    $packageDirectory = Join-Path (Join-Path $packagesRoot $package) $framework.version
    $licenseFiles = @(Get-ChildItem -LiteralPath $packageDirectory -File | Where-Object { $_.Name -match 'LICENSE|NOTICE' })
    if ($licenseFiles.Count -eq 0) { throw ('Runtime license missing: ' + $package) }
    foreach ($file in $licenseFiles) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $noticesRoot ($package + '-' + $framework.version + '-' + $file.Name))
    }
}
if (@(Get-ChildItem -LiteralPath $noticesRoot -File).Count -eq 0) { throw 'Runtime license files were not found.' }
$hostAssets = Get-Content -LiteralPath (Join-Path $projectRoot 'src/LaunchDesk.Host/obj/project.assets.json') -Raw | ConvertFrom-Json
foreach ($library in $hostAssets.libraries.PSObject.Properties) {
    if ($library.Value.type -ne 'package' -or $library.Name -notmatch '^System\.(ServiceProcess.ServiceController|Diagnostics.EventLog)/') { continue }
    $directory = Join-Path $hostAssets.project.restore.packagesPath $library.Value.path
    $licenses = @(Get-ChildItem -LiteralPath $directory -File | Where-Object Name -Match 'LICENSE|NOTICE')
    if ($licenses.Count -eq 0) { throw ('Host dependency license missing: ' + $library.Name) }
    foreach ($file in $licenses) { Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $noticesRoot ($library.Name.Replace('/', '-') + '-' + $file.Name)) }
}
$zipPath = Join-Path $outputRoot ('LaunchDesk-v' + $versionText + '-win-x64.zip')
Compress-Archive -Path (Join-Path $stageRoot '*') -DestinationPath $zipPath -Force
New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
Copy-Item -Path (Join-Path $stageRoot '*') -Destination $publishRoot -Recurse -Force
$hash = Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
($hash.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($zipPath)) | Out-File -LiteralPath ($zipPath + '.sha256') -Encoding ascii
Write-Output $zipPath
$resolvedStage = [IO.Path]::GetFullPath($stageRoot)
if (-not $resolvedStage.StartsWith($outputRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Unexpected staging path; cleanup refused.'
}
Remove-Item -LiteralPath $resolvedStage -Recurse -Force

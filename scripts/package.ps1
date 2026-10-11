param([string]$OutputDirectory = 'artifacts')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory, $projectRoot)
$publishRoot = Join-Path $outputRoot 'LaunchDesk-win-x64'
$stageRoot = Join-Path $outputRoot ('.package-' + [guid]::NewGuid().ToString('N'))
dotnet publish (Join-Path $projectRoot 'src/LaunchDesk.App/LaunchDesk.App.csproj') -c Release -r win-x64 --self-contained true -o $stageRoot
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
foreach ($file in 'README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md') {
    Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination $stageRoot
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $stageRoot -Recurse
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
$versionText = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
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

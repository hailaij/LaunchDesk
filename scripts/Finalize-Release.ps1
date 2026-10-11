param([Parameter(Mandatory)][string]$PublishDirectory, [Parameter(Mandatory)][string]$InstallerDirectory, [string]$OutputDirectory = 'artifacts/release')
$ErrorActionPreference = 'Stop'
$config = & (Join-Path $PSScriptRoot 'Get-ReleaseConfiguration.ps1')
& (Join-Path $PSScriptRoot 'Verify-ReleaseFiles.ps1') -Directory $PublishDirectory -Pin $config.certificateSha256 -Mode App
& (Join-Path $PSScriptRoot 'Verify-ReleaseFiles.ps1') -Directory $InstallerDirectory -Pin $config.certificateSha256 -Mode Installer
$project = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content -LiteralPath (Join-Path $project 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $output) -and @(Get-ChildItem -LiteralPath $output -Force).Count) { throw 'Final release output must be empty; stale/unsigned files are not publishable' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$installerName = "LaunchDesk-v$version-win-x64-setup.exe"
Copy-Item -LiteralPath (Join-Path $InstallerDirectory $installerName) -Destination $output
$zipName = "LaunchDesk-v$version-win-x64.zip"
Compress-Archive -Path (Join-Path $PublishDirectory '*') -DestinationPath (Join-Path $output $zipName)
$assets = foreach ($name in @($installerName, $zipName)) {
    $path = Join-Path $output $name
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name" | Out-File -LiteralPath ($path + '.sha256') -Encoding ascii
    @{ name = $name; sha256 = $hash; size = (Get-Item -LiteralPath $path).Length }
}
@{ version = $version; certificateSha256 = $config.certificateSha256; verifiedAt = [DateTimeOffset]::UtcNow.ToString('O'); assets = @($assets) } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'release-manifest.json') -Encoding utf8

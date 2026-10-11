param([string]$OutputDirectory = 'artifacts/verification/signing-fixture', [string]$SignToolPath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# This imports temporary trust roots. Never run on a developer/user workstation or self-hosted runner.
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'Signing fixtures require a disposable GitHub-hosted Windows runner. No certificates were imported.'
}
Write-Host 'Signing fixture: locating Windows SDK signtool'
if (-not $SignToolPath) {
    $SignToolPath = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -File |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $SignToolPath -or -not (Test-Path -LiteralPath $SignToolPath)) { throw 'Windows SDK signtool.exe is required.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$source = Join-Path (Split-Path $PSScriptRoot -Parent) 'tests/LaunchDesk.Tests/bin/Release/net10.0-windows/LaunchDesk.Tests.exe'
if (-not (Test-Path -LiteralPath $source)) { throw 'Build the test apphost before generating fixtures.' }
Write-Host 'Signing fixture: creating temporary code-signing key and certificate'
$certificate = New-SelfSignedCertificate -Subject ('CN=LaunchDesk CI Fixture ' + [guid]::NewGuid().ToString('N')) -Type CodeSigningCert -CertStoreLocation 'Cert:\CurrentUser\My' -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddDays(2)
$thumbprint = $certificate.Thumbprint
try {
    $certificatePath = Join-Path $output 'fixture.cer'
    Export-Certificate -Cert $certificate -FilePath $certificatePath | Out-Null
    Write-Host 'Signing fixture: importing disposable runner trust root'
    # CurrentUser Root import can display an interactive Windows consent dialog on hosted runners.
    & "$env:SystemRoot/System32/certutil.exe" -user -f -addstore Root $certificatePath
    if ($LASTEXITCODE -ne 0) { throw 'Temporary fixture root import failed.' }
    $signedPath = Join-Path $output 'signed-fixture.exe'
    Copy-Item -LiteralPath $source -Destination $signedPath
    Write-Host 'Signing fixture: signing isolated apphost'
    & $SignToolPath sign /fd SHA256 /sha1 $thumbprint /s My $signedPath
    if ($LASTEXITCODE -ne 0) { throw 'Fixture signing failed.' }
    Write-Host 'Signing fixture: checking native trust'
    if ((Get-AuthenticodeSignature -LiteralPath $signedPath).Status -ne 'Valid') { throw 'Fixture native trust is not valid.' }
    $manifest = Join-Path $output 'fixture.json'
    @{ signedPath = $signedPath; certificatePath = $certificatePath; certificateSha256 = $certificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256); thumbprint = $thumbprint } |
        ConvertTo-Json | Set-Content -LiteralPath $manifest -Encoding utf8
    $manifest
}
catch {
    Remove-Item -LiteralPath "Cert:\CurrentUser\Root\$thumbprint" -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "Cert:\CurrentUser\My\$thumbprint" -Force -DeleteKey -ErrorAction SilentlyContinue
    throw
}
finally { $certificate.Dispose() }

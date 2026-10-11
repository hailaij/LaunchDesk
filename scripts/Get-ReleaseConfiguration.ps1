param([string]$ConfigPath = 'build/signing.json', [switch]$GitHubOutputs)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$config = Get-Content -LiteralPath ([IO.Path]::GetFullPath($ConfigPath, $project)) -Raw | ConvertFrom-Json
if ($config.certificateSha256 -notmatch '^[a-fA-F0-9]{64}$' -or -not $config.certificatePath) {
    throw 'Production publisher not configured. Trusted update release is blocked; unsigned previews remain separate.'
}
$certificatePath = [IO.Path]::GetFullPath($config.certificatePath, $project)
if (-not $certificatePath.StartsWith($project + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Public release certificate must be inside the reviewed repository.' }
$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new([IO.File]::ReadAllBytes($certificatePath))
try {
    if ($certificate.HasPrivateKey -or $certificate.Subject -eq $certificate.Issuer -or $certificate.Subject -match 'LaunchDesk CI Fixture') { throw 'Private/self-signed/test certificates cannot configure production releases.' }
    $pin = $certificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256)
    if ($pin -ne $config.certificateSha256) { throw 'Reviewed public certificate and publisher pin disagree.' }
} finally { $certificate.Dispose() }
foreach ($field in 'signPathOrganizationId', 'signPathProjectSlug', 'signPathPolicySlug', 'signPathAppConfigurationSlug', 'signPathUninstallerConfigurationSlug', 'signPathInstallerConfigurationSlug') {
    if ($config.$field -notmatch '^[a-zA-Z0-9_-]+$') { throw "Missing/invalid production signing field: $field" }
}
if ($GitHubOutputs) {
    if (-not $env:GITHUB_OUTPUT) { throw 'GITHUB_OUTPUT unavailable' }
    foreach ($field in 'certificateSha256', 'signPathOrganizationId', 'signPathProjectSlug', 'signPathPolicySlug', 'signPathAppConfigurationSlug', 'signPathUninstallerConfigurationSlug', 'signPathInstallerConfigurationSlug') {
        "$field=$($config.$field)" | Out-File -LiteralPath $env:GITHUB_OUTPUT -Append -Encoding utf8
    }
} else { $config }

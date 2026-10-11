param([string]$OutputDirectory = 'artifacts/verification')
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
$previous = $env:LAUNCHDESK_SIGNED_FIXTURE
$manifest = $null
New-Item -ItemType Directory -Path $output -Force | Out-Null
Write-Host 'CI verification: recording runner environment'
@{ commit = $env:GITHUB_SHA; image = $env:ImageOS; imageVersion = $env:ImageVersion; sdk = (dotnet --version); powershell = $PSVersionTable.PSVersion.ToString(); runner = $env:RUNNER_ENVIRONMENT } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'environment.json') -Encoding utf8
try {
    Write-Host 'CI verification: preparing isolated signing fixture'
    $fixtureOutput = & (Join-Path $PSScriptRoot 'New-SigningFixture.ps1') -OutputDirectory (Join-Path $output 'signing-fixture')
    $manifest = $fixtureOutput | Where-Object { $_ -is [string] -and $_.EndsWith('fixture.json') } | Select-Object -Last 1
    if (-not $manifest) { throw 'Missing fixture manifest' }
    $env:LAUNCHDESK_SIGNED_FIXTURE = $manifest
    Write-Host 'CI verification: running core suite'
    dotnet run --project tests/LaunchDesk.Tests -c Release --no-build -- --report (Join-Path $output 'test-results.json') --fail-on-skip
    if ($LASTEXITCODE -ne 0) { throw 'Core integration tests failed or skipped' }
    & (Join-Path $PSScriptRoot 'Assert-TestReport.ps1') -Path (Join-Path $output 'test-results.json') -RequireNoSkips
    Write-Host 'CI verification: running engineering contracts'
    & (Join-Path $PSScriptRoot 'Verify-Engineering.ps1') -OutputDirectory (Join-Path $output 'engineering-contracts')
    Write-Host 'CI verification: running UI and fatal-cleanup checks'
    & (Join-Path $PSScriptRoot 'Verify-Ui.ps1') -OutputDirectory (Join-Path $output 'ui')
}
finally {
    Write-Host 'CI verification: cleaning temporary certificate and key'
    $env:LAUNCHDESK_SIGNED_FIXTURE = $previous
    if ($manifest -and (Test-Path -LiteralPath $manifest)) {
        $thumbprint = (Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json).thumbprint
        if ($thumbprint -notmatch '^[A-Fa-f0-9]{40}$') { throw 'Invalid test certificate cleanup identity' }
        Remove-Item -LiteralPath "Cert:\CurrentUser\Root\$thumbprint" -Force -ErrorAction Stop
        Remove-Item -LiteralPath "Cert:\CurrentUser\My\$thumbprint" -Force -DeleteKey -ErrorAction Stop
    }
}

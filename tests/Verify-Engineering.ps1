param([string]$OutputDirectory = 'artifacts/verification/engineering/contracts')
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$project = Split-Path $PSScriptRoot -Parent
$checker = Join-Path $project 'tools/LaunchDesk.ReleaseChecks/bin/Release/net10.0-windows/LaunchDesk.ReleaseChecks.exe'
$previous = $env:LAUNCHDESK_SIGNED_FIXTURE
$results = [Collections.Generic.List[object]]::new()
function Check([string]$Name, [scriptblock]$Action) {
    & $Action
    $results.Add(@{ name = $Name; status = 'passed' })
}
function Reject([scriptblock]$Action) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Expected rejection did not happen' }
}
try {
    Check 'Production assembly excludes fault probes and UI smoke members; test fixtures are rejected' {
        & $checker --production-boundary (Join-Path $project 'src/LaunchDesk.App/bin/Release/net10.0-windows/LaunchDesk.dll') ''
        if ($LASTEXITCODE -ne 0) { throw 'Production fault probe check failed' }
        & $checker --production-boundary (Join-Path $project 'tests/LaunchDesk.UiChecks/bin/Release/net10.0-windows/LaunchDesk.UiChecks.dll') '' 2>$null
        if ($LASTEXITCODE -ne 1) { throw 'Fault probe fixture was accepted as production' }
        & $checker --production-boundary (Join-Path $project 'tests/LaunchDesk.Tests/bin/Release/net10.0-windows/LaunchDesk.Tests.dll') '' 2>$null
        if ($LASTEXITCODE -ne 1) { throw 'UI smoke method fixture was accepted as production' }
    }
    Check 'Packaging refuses stale payload without modifying it' {
        $directory = Join-Path $output ('stale-package-' + [guid]::NewGuid().ToString('N'))
        $payload = Join-Path $directory 'LaunchDesk-win-x64'
        New-Item -ItemType Directory -Path $payload -Force | Out-Null
        $sentinel = Join-Path $payload 'config.json'
        [IO.File]::WriteAllText($sentinel, 'private sentinel: must never be packaged')
        $hash = (Get-FileHash -LiteralPath $sentinel).Hash
        $reason = ''
        try { & (Join-Path $project 'scripts/package.ps1') -OutputDirectory $directory } catch { $reason = $_.Exception.Message }
        if (-not $reason.Contains('Publish directory is not empty')) { throw 'Stale payload was not rejected at preflight' }
        if ((Get-FileHash -LiteralPath $sentinel).Hash -ne $hash -or @(Get-ChildItem -LiteralPath $directory -Filter '*.zip').Count) { throw 'Stale payload changed or packaged' }
    }
    Check 'Bilingual installer translation is pinned and service host is included in signing' {
        $translation = Join-Path $project 'installer/Languages/ChineseSimplified.isl'
        if ((Get-FileHash -LiteralPath $translation).Hash -ne 'E0B0B350E2245F3C5E65586DFE43D574F6E7F06F2261149ABA284954B3FC9A8D') { throw 'Unreviewed installer translation' }
        $installer = Get-Content -LiteralPath (Join-Path $project 'installer/LaunchDesk.iss') -Raw
        foreach ($name in 'english','chinesesimplified') {
            if (-not $installer.Contains('Name: "' + $name + '"; MessagesFile:')) { throw 'Installer language missing' }
            foreach ($message in 'DesktopIcon','Shortcuts','LaunchApp','ExitBeforeUninstall') {
                if (-not $installer.Contains($name + '.' + $message + '=')) { throw 'Localized installer message missing' }
            }
        }
        foreach ($script in 'scripts/Import-SignedFiles.ps1','scripts/Verify-ReleaseFiles.ps1','.github/workflows/release.yml') {
            $text = Get-Content -LiteralPath (Join-Path $project $script) -Raw
            if (-not $text.Contains('LaunchDesk.Host.exe') -or -not $text.Contains('LaunchDesk.Host.dll')) { throw 'Service/elevation host missing from signing inventory' }
        }
    }
    Check 'Missing production publisher blocks release before building' {
        $config = Join-Path $output 'empty-signing.json'
        @{certificateSha256='';certificatePath=''} | ConvertTo-Json | Set-Content -LiteralPath $config -Encoding utf8
        Reject { & (Join-Path $project 'scripts/Get-ReleaseConfiguration.ps1') -ConfigPath $config }
    }
    Check 'Missing fixture is explicitly skipped and strict mode fails' {
        $env:LAUNCHDESK_SIGNED_FIXTURE = $null
        $report = Join-Path $output 'skipped.json'
        dotnet run --project tests/LaunchDesk.Tests -c Release --no-build -- --report $report --filter 'Signing:' --fail-on-skip
        if ($LASTEXITCODE -ne 1) { throw 'Skipped strict suite did not fail' }
        $tests = @(Get-Content -LiteralPath $report -Raw | ConvertFrom-Json)
        if ($tests.Count -ne 4 -or @($tests | Where-Object status -eq 'skipped').Count -ne 3 -or @($tests | Where-Object status -eq 'passed').Count -ne 1) { throw 'Signing result inventory incorrect' }
        & (Join-Path $PSScriptRoot 'Assert-TestReport.ps1') -Path $report -ExpectedCount 4
        Check 'Test provenance rejects missing metadata and tampered reports' {
            $copied = Join-Path $output 'tampered-report.json'
            Copy-Item -LiteralPath $report -Destination $copied
            Reject { & (Join-Path $PSScriptRoot 'Assert-TestReport.ps1') -Path $copied -ExpectedCount 4 }
            Copy-Item -LiteralPath ($report + '.metadata.json') -Destination ($copied + '.metadata.json')
            [IO.File]::AppendAllText($copied, ' ')
            Reject { & (Join-Path $PSScriptRoot 'Assert-TestReport.ps1') -Path $copied -ExpectedCount 4 }
        }
        Reject { & (Join-Path $PSScriptRoot 'Assert-TestReport.ps1') -Path $report -ExpectedCount 4 -RequireNoSkips }
    }
    Check 'Broken fixture is failed, not skipped or passed' {
        $manifest = Join-Path $output 'broken-fixture.json'
        @{ signedPath='missing.exe';certificatePath='missing.cer';certificateSha256=('0' * 64) } | ConvertTo-Json | Set-Content -LiteralPath $manifest -Encoding utf8
        $env:LAUNCHDESK_SIGNED_FIXTURE = $manifest
        $report = Join-Path $output 'failed.json'
        dotnet run --project tests/LaunchDesk.Tests -c Release --no-build -- --report $report --filter 'Signing: valid'
        if ($LASTEXITCODE -ne 1) { throw 'Failed test exit incorrect' }
        $test = @(Get-Content -LiteralPath $report -Raw | ConvertFrom-Json)
        if ($test.Count -ne 1 -or $test[0].status -ne 'failed' -or $test[0].passed -ne $false -or -not $test[0].error) { throw 'Failed result schema incorrect' }
        Reject { & (Join-Path $PSScriptRoot 'Assert-TestReport.ps1') -Path $report -ExpectedCount 1 }
    }
    Check 'Mismatched embedded pin is rejected' {
        & $checker --embedded-pin (Join-Path $project 'src/LaunchDesk.Core/bin/Release/net10.0-windows/LaunchDesk.Core.dll') ('f' * 64) 2>$null
        if ($LASTEXITCODE -ne 1) { throw 'Embedded pin mismatch accepted' }
    }
    Check 'Unsigned executable cannot become a release' {
        & $checker --verify-file (Join-Path $project 'tests/LaunchDesk.Tests/bin/Release/net10.0-windows/LaunchDesk.Tests.exe') ('0' * 64) 2>$null
        if ($LASTEXITCODE -ne 1) { throw 'Unsigned release accepted' }
    }
    if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
        Check 'Developer workstation cannot import CI test trust roots' {
            Reject { & (Join-Path $PSScriptRoot 'New-SigningFixture.ps1') -OutputDirectory (Join-Path $output 'forbidden-fixture') }
        }
    }
    $results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'results.json') -Encoding utf8
}
finally { $env:LAUNCHDESK_SIGNED_FIXTURE = $previous }

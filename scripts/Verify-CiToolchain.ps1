param([string]$CompilerPath)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$tool = (Get-Content -LiteralPath (Join-Path $project 'build/toolchain.json') -Raw | ConvertFrom-Json).innoSetup
if (-not $CompilerPath) {
    $CompilerPath = @((Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe'), (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'), (Join-Path $env:ProgramFiles 'Inno Setup 6/ISCC.exe')) |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $CompilerPath) { throw 'Pinned Inno compiler missing' }
if ((Get-FileHash -LiteralPath $CompilerPath -Algorithm SHA256).Hash -ne $tool.compilerSha256) { throw 'Compiler hash differs from reviewed toolchain' }
if ((Get-AuthenticodeSignature -LiteralPath $CompilerPath).Status -ne 'Valid') { throw 'Compiler native trust failed' }
Write-Output "Verified Inno Setup $($tool.version) compiler"

param([string]$CompilerPath)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$tool = (Get-Content -LiteralPath (Join-Path $project 'build/toolchain.json') -Raw | ConvertFrom-Json).innoSetup
$CompilerPath = & (Join-Path $PSScriptRoot 'Resolve-InnoCompiler.ps1') -CompilerPath $CompilerPath
if ((Get-FileHash -LiteralPath $CompilerPath -Algorithm SHA256).Hash -ne $tool.compilerSha256) { throw 'Compiler hash differs from reviewed toolchain' }
if ((Get-AuthenticodeSignature -LiteralPath $CompilerPath).Status -ne 'Valid') { throw 'Compiler native trust failed' }
Write-Output "Verified Inno Setup $($tool.version) compiler"

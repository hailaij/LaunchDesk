param([string]$OutputDirectory = 'artifacts/verification/compiler-path')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $root -Force | Out-Null
$resolver = Join-Path (Split-Path $PSScriptRoot -Parent) 'scripts/Resolve-InnoCompiler.ps1'
$fixture = Join-Path $root ('fixture with spaces-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$file = Join-Path $fixture 'ISCC.exe'
Set-Content -LiteralPath $file -Value 'not executable; resolution fixture only'
Copy-Item -LiteralPath $file -Destination (Join-Path $fixture 'other.exe')
$results = @()
if ((& $resolver -CompilerPath $file) -ne $file) { throw 'Explicit compiler file not resolved' }
$results += @{ name = 'Explicit compiler path with spaces and file validation'; status = 'passed' }
foreach ($invalid in @($fixture, (Join-Path $fixture 'missing/ISCC.exe'), (Join-Path $fixture 'other.exe'))) {
    $rejected = $false
    try { & $resolver -CompilerPath $invalid | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw "Invalid compiler accepted: $invalid" }
}
$results += @{ name = 'Directory and missing paths rejected without fallback'; status = 'passed' }
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'results.json') -Encoding utf8

param([string]$CompilerPath)
$ErrorActionPreference = 'Stop'
if (-not $CompilerPath) {
    $candidates = @(
        foreach ($root in @($env:LOCALAPPDATA, ${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
            if ($root) {
                $suffix = if ($root -eq $env:LOCALAPPDATA) { 'Programs/Inno Setup 6/ISCC.exe' } else { 'Inno Setup 6/ISCC.exe' }
                Join-Path $root $suffix
            }
        }
        foreach ($key in @('HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/Inno Setup 6_is1',
            'HKLM:/Software/Microsoft/Windows/CurrentVersion/Uninstall/Inno Setup 6_is1',
            'HKLM:/Software/WOW6432Node/Microsoft/Windows/CurrentVersion/Uninstall/Inno Setup 6_is1')) {
            $location = (Get-ItemProperty -LiteralPath $key -Name InstallLocation -ErrorAction SilentlyContinue).InstallLocation
            if ($location) { Join-Path $location 'ISCC.exe' }
        }
        $command = Get-Command ISCC.exe -CommandType Application -ErrorAction SilentlyContinue
        if ($command) { $command.Source }
    )
    $CompilerPath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
    throw 'Inno Setup 6 compiler missing. Supply -CompilerPath with the full path to ISCC.exe.'
}
if ([IO.Path]::GetFileName($CompilerPath) -ine 'ISCC.exe') { throw 'CompilerPath must identify ISCC.exe, not its directory or installer.' }
(Resolve-Path -LiteralPath $CompilerPath).Path

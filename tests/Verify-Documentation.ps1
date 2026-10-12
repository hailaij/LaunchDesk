param([string]$OutputDirectory = 'artifacts/verification/documentation')
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content -LiteralPath (Join-Path $project 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$imagesRoot = Join-Path $project 'docs/images'
$manifest = Get-Content -LiteralPath (Join-Path $imagesRoot 'manifest.json') -Raw | ConvertFrom-Json
if ($manifest.version -ne $version -or -not $manifest.fixtureData) { throw 'Documentation screenshots must be regenerated for the current version with isolated data.' }
$inventory = @{}
foreach ($image in $manifest.images) {
    if ($image.name -notmatch '^[a-z-]+\.png$' -or $inventory.ContainsKey($image.name)) { throw 'Invalid screenshot inventory' }
    $path = Join-Path $imagesRoot $image.name
    if ((Get-FileHash -LiteralPath $path).Hash -ne $image.sha256) { throw "Documentation screenshot changed without verification: $($image.name)" }
    $inventory[$image.name] = $path
}
$used = [Collections.Generic.HashSet[string]]::new()
$relativeFiles = & git -C $project ls-files --cached --others --exclude-standard -- '*.md'
if ($LASTEXITCODE -ne 0) { throw 'Markdown inventory failed' }
$files = @($relativeFiles | Sort-Object -Unique | ForEach-Object { Join-Path $project $_ })
foreach ($file in $files) {
    $text = Get-Content -LiteralPath $file -Raw
    foreach ($match in [regex]::Matches($text, '!\[[^\]]*\]\(([^)]+)\)')) {
        $target = $match.Groups[1].Value
        if ($target -match '^https?://') { continue }
        $path = [IO.Path]::GetFullPath((Join-Path (Split-Path $file -Parent) $target))
        $name = [IO.Path]::GetFileName($path)
        if (-not $inventory.ContainsKey($name) -or $path -ne $inventory[$name]) { throw "Missing or unverified Markdown image: $file -> $target" }
        [void]$used.Add($name)
    }
}
if ($used.Count -ne $inventory.Count -or $used.Count -eq 0) { throw 'Screenshot manifest contains unused or missing images' }
$output = [IO.Path]::GetFullPath($OutputDirectory, $project)
New-Item -ItemType Directory -Path $output -Force | Out-Null
@{status='passed';version=$version;images=$used.Count;markdownFiles=@($files).Count;manifest='docs/images/manifest.json'} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'results.json') -Encoding utf8
Write-Output "Documentation images verified: $($used.Count), version $version"

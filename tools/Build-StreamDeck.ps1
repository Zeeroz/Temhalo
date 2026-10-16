[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$node = (Get-Command node -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$pnpm = Get-Command pnpm -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $pnpm) {
    # Some portable developer runtimes keep their command shims beside node/.
    $portableShim = Join-Path (Split-Path (Split-Path (Split-Path $node))) 'bin/fallback/pnpm.cmd'
    if (Test-Path -LiteralPath $portableShim -PathType Leaf) { $pnpm = Get-Item -LiteralPath $portableShim }
}
if (-not $pnpm) { throw 'pnpm 11.25.0 est requis : npm install -g pnpm@11.25.0' }
$pnpmPath = if ($pnpm -is [IO.FileInfo]) { $pnpm.FullName } else { $pnpm.Source }
function Invoke-PluginCommand([string]$Executable, [string[]]$Arguments) {
    $PSNativeCommandUseErrorActionPreference = $false
    & $Executable @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Construction du plugin interrompue (code $LASTEXITCODE)." }
}
$nodeVersion = (& $node --version).TrimStart('v')
if ($LASTEXITCODE -ne 0 -or ([version]$nodeVersion).Major -lt 24) { throw 'Node.js 24 ou plus récent est requis pour construire le plugin.' }
$pnpmVersion = (& $pnpmPath --version).Trim()
if ($LASTEXITCODE -ne 0 -or $pnpmVersion -ne '11.25.0') { throw 'Utilisez la version pnpm 11.25.0 verrouillée par le projet.' }
Push-Location -LiteralPath (Join-Path $repoRoot 'plugins/streamdeck')
try {
    Invoke-PluginCommand $pnpmPath @('install','--frozen-lockfile','--ignore-scripts')
    Invoke-PluginCommand $node @('tools/build.mjs')
    Invoke-PluginCommand $pnpmPath @('test')
    Invoke-PluginCommand $pnpmPath @('run','validate')
    $packageDirectory = Join-Path $repoRoot 'artifacts/streamdeck/packages'
    if (Test-Path -LiteralPath $packageDirectory) {
        $previous = Join-Path $repoRoot ('artifacts/streamdeck/previous-package-' + [guid]::NewGuid().ToString('N'))
        $boundary = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts/streamdeck')) + [IO.Path]::DirectorySeparatorChar
        if (-not [IO.Path]::GetFullPath($packageDirectory).StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase) -or
            -not [IO.Path]::GetFullPath($previous).StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Chemin de conservation hors du projet.' }
        # Keep all previous builds; this never deletes an installed plugin or a profile.
        Move-Item -LiteralPath $packageDirectory -Destination $previous
    }
    [void][IO.Directory]::CreateDirectory($packageDirectory)
    Invoke-PluginCommand $pnpmPath @('run','pack')
    $produced = @(Get-ChildItem -LiteralPath $packageDirectory -File -Filter '*.streamDeckPlugin')
    if ($produced.Count -ne 1) { throw 'Un seul paquet Stream Deck était attendu.' }
    $destination = Join-Path $packageDirectory 'Temhalo.streamDeckPlugin'
    if ($produced[0].FullName -ne $destination) { Move-Item -LiteralPath $produced[0].FullName -Destination $destination }
    $sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    "$sha256  Temhalo.streamDeckPlugin" | Set-Content -LiteralPath ($destination + '.sha256') -Encoding ASCII
    Write-Output $destination
}
finally { Pop-Location }

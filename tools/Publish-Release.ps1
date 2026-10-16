[CmdletBinding()]
param([string]$Tag = $env:GITHUB_REF_NAME)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
[xml]$props = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
$version = [string]$props.Project.PropertyGroup.Version
if ($Tag -cne "v$version") { throw 'Le tag doit correspondre exactement à Directory.Build.props.' }
$packageDirectory = Join-Path $repoRoot 'artifacts/packages'
$filenames = @("Temhalo-$version-Setup-x64.exe", "Temhalo-$version-win-x64.zip")
$assets = @()
foreach ($name in $filenames) {
    $path = Join-Path $packageDirectory $name
    $checksum = $path + '.sha256'
    $expected = ((Get-Content -LiteralPath $checksum -Raw).Trim() -split '\s+')[0]
    if ($expected -notmatch '\A[a-fA-F0-9]{64}\z' -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $expected) { throw "Empreinte incorrecte : $name" }
    $assets += $path, $checksum
}
$notes = Join-Path $repoRoot 'artifacts/release-notes.md'
@"
Temhalo $version pour Windows 10/11 x64.

Téléchargez **Temhalo-$version-Setup-x64.exe** pour l’installation et les mises à jour intégrées. Le ZIP est une version portable autonome.

Les réglages et les comptes chiffrés sont conservés lors d’une mise à jour. Arrêtez le relais avant d’installer. Les exécutables ne sont pas signés Authenticode ; les empreintes SHA-256 accompagnent les fichiers.

Guide : https://github.com/Zeeroz/Temhalo/blob/main/docs/MISES-A-JOUR.md
"@ | Set-Content -LiteralPath $notes -Encoding UTF8
$arguments = @('release','create',$Tag,'--repo','Zeeroz/Temhalo','--verify-tag','--draft','--title',"Temhalo $version",'--notes-file',$notes)
if ($version -like '*-preview.*') { $arguments += '--prerelease' }
& gh @arguments @assets
if ($LASTEXITCODE) { throw 'Création impossible. Une release existante ne sera jamais remplacée automatiquement.' }
# All four assets exist before the release becomes visible to the in-app updater.
$json = & gh api "repos/Zeeroz/Temhalo/releases/tags/$Tag"
if ($LASTEXITCODE) { throw 'Lecture des actifs impossible : la release reste en brouillon.' }
$release = ($json -join "`n") | ConvertFrom-Json
foreach ($asset in @($release.assets)) {
    $local = Join-Path $packageDirectory $asset.name
    if ($asset.name -notin ($assets | ForEach-Object { [IO.Path]::GetFileName($_) }) -or
        $asset.digest -cne ('sha256:' + (Get-FileHash -LiteralPath $local -Algorithm SHA256).Hash.ToLowerInvariant())) { throw 'Actif GitHub non conforme. La release reste en brouillon.' }
}
if (@($release.assets).Count -ne 4) { throw 'Release incomplète : quatre fichiers attendus.' }
& gh release edit $Tag --repo Zeeroz/Temhalo --draft=false
if ($LASTEXITCODE) { throw 'Publication impossible ; le brouillon est conservé.' }

[CmdletBinding()]
param([string]$Tag = $env:GITHUB_REF_NAME, [switch]$ResumeDraft)
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
function Find-Release {
    # Draft releases can have an untagged URL and return 404 from /releases/tags/.
    # List authenticated releases, resolve the numeric ID, then use it throughout.
    $json = & gh api 'repos/Zeeroz/Temhalo/releases?per_page=100'
    if ($LASTEXITCODE) { throw 'Lecture des releases impossible.' }
    $matchingReleases = @(($json -join "`n" | ConvertFrom-Json) | Where-Object tag_name -CEQ $Tag)
    if ($matchingReleases.Count -gt 1) { throw 'Plusieurs releases portent ce tag. Vérification manuelle nécessaire.' }
    if ($matchingReleases.Count -eq 1) { return $matchingReleases[0] }
    return $null
}
$release = Find-Release
if ($null -ne $release) {
    if (-not $ResumeDraft) { throw 'Une release existe déjà. Utilisez la reprise après vérification des actifs ; aucun fichier ne sera écrasé.' }
}
else {
    if ($ResumeDraft) { throw 'Aucun brouillon à reprendre.' }
    $arguments = @('release','create',$Tag,'--repo','Zeeroz/Temhalo','--verify-tag','--draft','--title',"Temhalo $version",'--notes-file',$notes)
    if ($version -like '*-preview.*') { $arguments += '--prerelease' }
    & gh @arguments @assets
    if ($LASTEXITCODE) { throw 'Création impossible. Une release existante ne sera jamais remplacée automatiquement.' }
    $release = Find-Release
}
if ($null -eq $release -or $release.id -le 0) { throw 'Identifiant de release introuvable ; le brouillon est conservé.' }
$releaseId = [long]$release.id
$json = & gh api "repos/Zeeroz/Temhalo/releases/$releaseId"
if ($LASTEXITCODE) { throw 'Lecture des actifs impossible : le brouillon est conservé.' }
$release = ($json -join "`n") | ConvertFrom-Json
if ($release.tag_name -cne $Tag -or $release.prerelease -ne ($version -like '*-preview.*')) { throw 'Tag ou canal de release non conforme.' }
# All four assets match the exact validated CI files before anything becomes public.
foreach ($asset in @($release.assets)) {
    $local = Join-Path $packageDirectory $asset.name
    if ($asset.name -cnotin ($assets | ForEach-Object { [IO.Path]::GetFileName($_) }) -or $asset.state -cne 'uploaded' -or
        $asset.size -ne (Get-Item -LiteralPath $local).Length -or
        $asset.digest -cne ('sha256:' + (Get-FileHash -LiteralPath $local -Algorithm SHA256).Hash.ToLowerInvariant())) { throw 'Actif GitHub non conforme. La release reste en brouillon.' }
}
if (@($release.assets).Count -ne 4) { throw 'Release incomplète : quatre fichiers attendus.' }
if ($release.draft) {
    & gh api --method PATCH "repos/Zeeroz/Temhalo/releases/$releaseId" -F draft=false --jq '.html_url'
    if ($LASTEXITCODE) { throw 'Publication impossible ; le brouillon est conservé.' }
}
else { Write-Host "Release déjà publiée et vérifiée : $($release.html_url)" }

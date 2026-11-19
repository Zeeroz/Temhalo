#Requires -Version 7.0
<#
.SYNOPSIS
Copie chaque archive native verrouillée dans la release permanente « native-tools » de Temhalo.
.DESCRIPTION
Les fournisseurs amont (notamment les autobuilds FFmpeg) suppriment ou déplacent leurs archives.
Avant chaque publication, ce script garantit que la première URL du manifeste — le miroir Temhalo —
sert exactement l'archive attendue. Une archive absente est récupérée depuis les autres sources du
manifeste, vérifiée par SHA-256, puis ajoutée. Un fichier existant différent n'est jamais remplacé.
#>
[CmdletBinding()]
param([string]$Repository = 'Zeeroz/Temhalo', [string]$Tag = 'native-tools')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$lock = Get-Content -LiteralPath (Join-Path $repoRoot 'config/native-tools.lock.json') -Raw | ConvertFrom-Json
if ($lock.SchemaVersion -ne 2) { throw 'Le manifeste natif doit lister ses sources dans Urls (schéma 2).' }
$mirrorPrefix = "https://github.com/$Repository/releases/download/$Tag/"

function Get-MirrorRelease {
    $json = & gh api "repos/$Repository/releases/tags/$Tag" 2>$null
    if ($LASTEXITCODE) { return $null }
    return ($json -join "`n") | ConvertFrom-Json
}

$release = Get-MirrorRelease
if ($null -eq $release) {
    $notes = @'
Copies exactes des archives MediaMTX et FFmpeg utilisées par Temhalo, conservées ici car les fournisseurs amont suppriment leurs anciennes builds.

Chaque archive est vérifiée par l'application avec l'empreinte SHA-256 de `config/native-tools.lock.json` avant extraction. Sources et licences : voir ce manifeste et `packaging/THIRD-PARTY-NOTICES.md`.

Cette release n'est pas une version de Temhalo : ne la supprimez pas.
'@
    $target = if ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { 'main' }
    & gh release create $Tag --repo $Repository --target $target --prerelease --title 'Outils natifs (miroir permanent)' --notes $notes
    if ($LASTEXITCODE) { throw 'Création de la release miroir impossible.' }
    $release = Get-MirrorRelease
    if ($null -eq $release) { throw 'Release miroir introuvable après création.' }
}
if ($release.draft) { throw 'La release miroir est un brouillon : ses URL ne seraient pas publiques.' }

$work = Join-Path ([IO.Path]::GetTempPath()) ('temhalo-mirror-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($work)
try {
    foreach ($tool in $lock.Tools) {
        $name = [string]$tool.ArchiveName
        $expected = ([string]$tool.Sha256).ToLowerInvariant()
        if ([IO.Path]::GetFileName($name) -cne $name -or $expected -notmatch '\A[a-f0-9]{64}\z') { throw "Entrée de manifeste invalide : $($tool.Name)" }
        $urls = @($tool.Urls)
        if ($urls[0] -cne $mirrorPrefix + $name) { throw "La première source de $($tool.Name) doit être le miroir $mirrorPrefix$name" }
        $asset = @($release.assets | Where-Object name -CEQ $name)
        if ($asset.Count -eq 1) {
            if ($asset[0].state -cne 'uploaded' -or $asset[0].digest -cne "sha256:$expected") {
                throw "Le miroir contient un $name différent du manifeste. Vérification manuelle nécessaire ; rien n'est remplacé."
            }
            Write-Host "$($tool.Name) déjà présent dans le miroir."
            continue
        }
        $file = Join-Path $work $name
        $found = $false
        foreach ($url in $urls | Select-Object -Skip 1) {
            try {
                Write-Host "Récupération de $name depuis $url"
                Invoke-WebRequest -Uri $url -OutFile $file -MaximumRedirection 5 -UseBasicParsing
                if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $expected) { $found = $true; break }
                Write-Warning "Empreinte différente pour $url"
            }
            catch { Write-Warning "Source indisponible : $url ($($_.Exception.Message))" }
            Remove-Item -LiteralPath $file -ErrorAction SilentlyContinue
        }
        if (-not $found) { throw "Aucune source amont ne fournit plus $name. Mettez à jour config/native-tools.lock.json vers une build disponible." }
        & gh release upload $Tag $file --repo $Repository
        if ($LASTEXITCODE) { throw "Envoi de $name vers le miroir impossible." }
        Write-Host "$($tool.Name) ajouté au miroir."
    }
    # Re-read and prove every locked archive is public with the expected digest.
    $release = Get-MirrorRelease
    foreach ($tool in $lock.Tools) {
        $asset = @($release.assets | Where-Object name -CEQ $tool.ArchiveName)
        if ($asset.Count -ne 1 -or $asset[0].state -cne 'uploaded' -or $asset[0].digest -cne ('sha256:' + ([string]$tool.Sha256).ToLowerInvariant())) {
            throw "Miroir incomplet pour $($tool.Name)."
        }
    }
    Write-Host 'Miroir des outils natifs vérifié.'
}
finally { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }

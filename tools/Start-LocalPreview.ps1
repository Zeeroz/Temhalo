[CmdletBinding()]
param([ValidateRange(1024,65535)][int]$Port = 1936)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($Port -eq 1935) { throw "Le port 1935 est réservé à l’entrée OBS de Temhalo." }
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$manifestPath = Join-Path $repoRoot 'artifacts\native-tools.resolved.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw 'Lancez d''abord tools\native\Get-NativeTools.ps1 pour installer MediaMTX.'
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$tool = @($manifest.Tools | Where-Object Name -eq 'MediaMTX')
if ($tool.Count -ne 1 -or -not (Test-Path -LiteralPath $tool[0].Executable -PathType Leaf)) {
    throw 'MediaMTX introuvable. Relancez tools\native\Get-NativeTools.ps1.'
}
$config = Join-Path $repoRoot 'config\mediamtx-preview.yml'
if ($Port -ne 1936) {
    $generatedDirectory = Join-Path $repoRoot 'artifacts\preview-config'
    [void][IO.Directory]::CreateDirectory($generatedDirectory)
    $generatedConfig = Join-Path $generatedDirectory ("mediamtx-preview-$Port.yml")
    $template = [IO.File]::ReadAllText($config).Replace('127.0.0.1:1936', "127.0.0.1:$Port")
    [IO.File]::WriteAllText($generatedConfig, $template)
    $config = $generatedConfig
}

# Ignore external MTX_* overrides so this optional server stays on loopback.
# Restore the calling PowerShell environment when the foreground command ends.
$overrides = @{}
foreach ($entry in @(Get-ChildItem Env: | Where-Object Name -like 'MTX_*')) {
    $overrides[$entry.Name] = $entry.Value
    [Environment]::SetEnvironmentVariable($entry.Name, $null, 'Process')
}
try {
    Write-Host "Apercu local : rtmp://127.0.0.1:$Port/live/preview"
    Write-Host "Apres Emission confirmee, ouvrez VLC dans un autre terminal : & .\tools\Open-LocalPreview.ps1 -Port $Port"
    Write-Host 'Gardez ce terminal ouvert. Ctrl+C pour arreter le serveur apres Temhalo.'
    & $tool[0].Executable $config
    if ($LASTEXITCODE -ne 0) {
        throw "MediaMTX s'est arrete (code $LASTEXITCODE). Verifiez notamment que le port $Port est libre."
    }
}
finally {
    foreach ($name in $overrides.Keys) {
        [Environment]::SetEnvironmentVariable($name, $overrides[$name], 'Process')
    }
}

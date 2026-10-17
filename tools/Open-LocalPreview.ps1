[CmdletBinding()]
param(
    [ValidateRange(1024,65535)][int]$Port = 1936,
    [ValidateRange(50,1000)][int]$CacheMilliseconds = 150,
    [string]$VlcPath = '',
    [switch]$PrintCommand
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($Port -eq 1935) { throw 'Le port 1935 contient le flux OBS. Choisissez le port de la sortie locale, normalement 1936.' }
if ($VlcPath) {
    if (-not (Test-Path -LiteralPath $VlcPath -PathType Leaf)) { throw 'Exécutable VLC introuvable.' }
    $vlcExecutable = (Resolve-Path -LiteralPath $VlcPath).Path
}
else {
    $candidates = @(
        (Join-Path $env:ProgramFiles 'VideoLAN\VLC\vlc.exe')
        if (${env:ProgramFiles(x86)}) { Join-Path ${env:ProgramFiles(x86)} 'VideoLAN\VLC\vlc.exe' }
        $command = Get-Command vlc.exe -CommandType Application -ErrorAction SilentlyContinue
        if ($command) { $command.Source }
    )
    $vlcExecutable = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (-not $vlcExecutable) { throw 'Installez VLC ou indiquez son exécutable avec -VlcPath.' }
}
if ([IO.Path]::GetFileName($vlcExecutable) -ine 'vlc.exe') { throw 'Le chemin doit désigner vlc.exe.' }

# A delay increase deliberately stops packet delivery. VLC must not compensate
# that pause by growing its jitter buffer. Keep its A/V clock synchronization on.
# avio-options targets the RTMP protocol buffer, not the demuxer's codec options.
$arguments = @(
    '--no-one-instance'
    '--no-video-title-show'
    "--network-caching=$CacheMilliseconds"
    '--clock-jitter=0'
    '--avio-options={rtmp_buffer=0}'
    "rtmp://127.0.0.1:$Port/live/preview"
)
if ($PrintCommand) {
    # Display only; never evaluate a constructed command string.
    $quoted = $arguments | ForEach-Object { "'" + $_.Replace("'", "''") + "'" }
    Write-Output ('& "' + $vlcExecutable + '" ' + ($quoted -join ' '))
    return
}

Write-Host 'Ouvrez cet aperçu après « Émission confirmée » dans Temhalo.'
Write-Host "Lecture locale VLC : cache $CacheMilliseconds ms, tampon adaptatif désactivé pour cette instance."
# A separate visible instance receives these options even when VLC is already open.
# No saved VLC preferences are changed and no remote-control port is opened.
Start-Process -FilePath $vlcExecutable -ArgumentList $arguments -WindowStyle Normal

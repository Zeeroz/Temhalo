[CmdletBinding()]
param([ValidateRange(10, 3600)][int]$Seconds = 120)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$manifest = Get-Content -LiteralPath (Join-Path $repoRoot 'artifacts\native-tools.resolved.json') -Raw | ConvertFrom-Json
$ffmpeg = ($manifest.Tools | Where-Object Name -eq 'FFmpeg').Executable
$fixtureDir = Join-Path $repoRoot 'artifacts\ui-test-source'
[void][IO.Directory]::CreateDirectory($fixtureDir)
$source = Join-Path $fixtureDir ('pattern-' + [Guid]::NewGuid().ToString('N') + '.flv')
# Encoding only creates the synthetic source. The running Temhalo chain stays in streamcopy.
& $ffmpeg -hide_banner -v error -f lavfi -i 'testsrc2=size=640x360:rate=30' -f lavfi `
    -i 'sine=frequency=880:sample_rate=48000' -t 8 -c:v h264_mf -hw_encoding 0 `
    -b:v 1500k -g 30 -bf 0 -c:a aac -b:a 160k -f flv -n $source
if ($LASTEXITCODE -ne 0) { throw "La mire H.264/AAC n'a pas pu être créée." }
Write-Host "Mire locale pendant $Seconds s. Démarrez le relais Temhalo avant ce script."
& $ffmpeg -hide_banner -loglevel warning -re -stream_loop -1 -i $source -t $Seconds `
    -map 0:v:0 -map 0:a:0 -c copy -f flv -flvflags no_duration_filesize 'rtmp://127.0.0.1:1935/live/stream'
if ($LASTEXITCODE -ne 0) { throw 'La publication locale a été interrompue. Vérifiez que le relais est actif.' }

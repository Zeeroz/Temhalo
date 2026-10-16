[CmdletBinding()]
param([ValidateRange(1, 15)][int]$DelaySeconds = 5)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
& (Join-Path $PSScriptRoot 'native\Get-NativeTools.ps1') -VerifyOnly
$native = Get-Content -LiteralPath (Join-Path $repoRoot 'artifacts\native-tools.resolved.json') -Raw | ConvertFrom-Json
$ffmpeg = ($native.Tools | Where-Object Name -eq 'FFmpeg').Executable
$ffprobe = Join-Path (Split-Path -Parent $ffmpeg) 'ffprobe.exe'
$hostDll = Join-Path $repoRoot 'src\Temhalo.RelayHost\bin\Debug\net8.0-windows10.0.26100.0\Temhalo.RelayHost.dll'
$testDir = Join-Path $repoRoot ('artifacts\validation\stage2-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($testDir)
$source = Join-Path $testDir 'source.flv'
$owned = [Collections.Generic.List[Diagnostics.Process]]::new()
$hostProcess = $null
$publisher = $null
$stopFile = Join-Path $testDir 'stop.request'

function Start-Owned([string]$Executable, [string[]]$ArgumentValues, [string]$Label) {
    $quoted = @($ArgumentValues | ForEach-Object {
        if ($_.Contains('"') -or $_.Contains("`n")) { throw 'Unexpected quote in controlled argument.' }
        '"' + [regex]::Replace($_, '(\\+)$', '$1$1') + '"'
    }) -join ' '
    $process = Start-Process -FilePath $Executable -ArgumentList $quoted -WorkingDirectory $repoRoot `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $testDir "$Label.stdout.txt") `
        -RedirectStandardError (Join-Path $testDir "$Label.stderr.txt")
    $owned.Add($process)
    return $process
}

function Stop-Owned([Diagnostics.Process]$Process) {
    if ($Process -and -not $Process.HasExited) { $Process.Kill(); [void]$Process.WaitForExit(5000) }
}

function Read-Status([string]$Directory) {
    $files = @(Get-ChildItem -LiteralPath $Directory -Filter status.json -Recurse -ErrorAction SilentlyContinue)
    if ($files.Count -eq 0) { return $null }
    try { return Get-Content -LiteralPath $files[0].FullName -Raw | ConvertFrom-Json }
    catch [IO.IOException] { return $null }
}

function Wait-Until([scriptblock]$Condition, [string]$Description, [int]$TimeoutSeconds = 40) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        if (& $Condition) { return }
        if ($script:hostProcess -and $script:hostProcess.HasExited) { throw "Host exited during $Description. See $testDir" }
        Start-Sleep -Milliseconds 50
    }
    throw "Timeout: $Description. See $testDir"
}

function Native-Text([string]$Executable, [string[]]$ArgumentValues) {
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $lines = @(& $Executable @ArgumentValues 2>&1)
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previousPreference }
    if ($code -ne 0) { throw "Native command failed: $($lines -join [Environment]::NewLine)" }
    return ($lines | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
}

function Start-Publisher([string]$Label) {
    return Start-Owned $ffmpeg @('-hide_banner', '-loglevel', 'warning', '-nostdin', '-re', '-stream_loop', '-1',
        '-i', $source, '-map', '0:v:0', '-map', '0:a:0', '-c', 'copy', '-f', 'flv',
        'rtmp://127.0.0.1:1935/live/stream') $Label
}

try {
    Native-Text $ffmpeg @('-hide_banner', '-loglevel', 'error', '-f', 'lavfi', '-i', 'testsrc2=size=640x360:rate=30',
        '-f', 'lavfi', '-i', 'sine=frequency=880:sample_rate=48000', '-t', '8', '-c:v', 'h264_mf', '-hw_encoding', '0',
        '-b:v', '1500k', '-g', '30', '-bf', '0', '-c:a', 'aac', '-b:a', '160k', '-f', 'flv', '-n', $source) | Out-Null
    $relayDir = Join-Path $testDir 'relay'
    $hostProcess = Start-Owned (Get-Command dotnet).Source @($hostDll, '--repo-root', $repoRoot,
        '--output-directory', $relayDir, '--stop-file', $stopFile, '--delay-seconds', "$DelaySeconds", '--buffer-mib', '32') 'host'
    Wait-Until { $s = Read-Status $relayDir; $s -and $s.Relay.MediaMtxPid } 'ingest startup'
    $publisher = Start-Publisher 'publisher'
    Wait-Until { $s = Read-Status $relayDir; $s -and $s.Relay.Delay -and $s.Relay.Delay.ReadTags -gt 40 -and $s.Relay.Delay.EmittedMediaTags -eq 0 } 'initial buffering'
    $buffering = Read-Status $relayDir
    if ($buffering.Relay.BytesTransferred -ne 0 -or $buffering.Relay.Delay.BufferBytes -le 0) { throw 'Output started before initial delay.' }
    Wait-Until { $s = Read-Status $relayDir; $s -and $s.Relay.Delay.EmittedMediaTags -gt 160 } 'delayed output'
    $during = Read-Status $relayDir
    if ($during.Relay.Delay.MinimumMediaAgeMilliseconds -lt $DelaySeconds * 1000 -or
        $during.Relay.Delay.LastMediaAgeMilliseconds -gt ($DelaySeconds + 2) * 1000) { throw 'Measured local delay is outside tolerance.' }

    # Stop the source and verify that its already-buffered tail is still emitted at its due times.
    Stop-Owned $publisher
    Wait-Until { $s = Read-Status $relayDir; $s -and $s.Relay.State -eq 'Draining' -and
        $s.Relay.Delay.InputCompleted -and $s.Relay.Delay.BufferedTags -gt 0 } 'source disconnected with buffered tail'
    $tail = Read-Status $relayDir
    $file = $tail.Relay.OutputFile
    [IO.File]::WriteAllText($stopFile, 'stop')
    if (-not $hostProcess.WaitForExit(($DelaySeconds + 15) * 1000) -or $hostProcess.ExitCode -ne 0) { throw 'Delayed tail did not finish cleanly.' }
    $finished = Read-Status $relayDir
    if ($finished.Relay.State -ne 'Stopped' -or $finished.Relay.Delay.BufferBytes -ne 0 -or
        $finished.Relay.Delay.EmittedMediaTags -le $tail.Relay.Delay.EmittedMediaTags) { throw 'Tail was dropped or buffer was not released.' }
    if ($finished.Relay.Delay.MaximumMediaAgeMilliseconds -gt ($DelaySeconds + 2) * 1000) { throw 'Maximum delay exceeded tolerance.' }
    $probeArgs = @('-v', 'error', '-show_packets', '-show_streams', '-show_data_hash', 'sha256', '-show_entries',
        'packet=codec_type,flags,dts_time,pts_time,data_hash:stream=codec_type,codec_name', '-of', 'json')
    $sourceProbe = (Native-Text $ffprobe ($probeArgs + @($source))) | ConvertFrom-Json
    $probe = (Native-Text $ffprobe ($probeArgs + @($file))) | ConvertFrom-Json
    if (@($probe.streams | Where-Object codec_name -eq 'h264').Count -ne 1 -or
        @($probe.streams | Where-Object codec_name -eq 'aac').Count -ne 1) { throw 'Missing H.264/AAC tracks.' }
    $firstVideo = @($probe.packets | Where-Object codec_type -eq 'video')[0]
    if ($firstVideo.flags -notmatch 'K') { throw 'Output did not resume on a keyframe.' }
    $audioHashes = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($packet in $sourceProbe.packets | Where-Object codec_type -eq 'audio') { [void]$audioHashes.Add($packet.data_hash) }
    $lastDts = @{}
    foreach ($packet in $probe.packets) {
        $dts = [double]::Parse($packet.dts_time, [Globalization.CultureInfo]::InvariantCulture)
        if ($lastDts.ContainsKey($packet.codec_type) -and $dts -lt $lastDts[$packet.codec_type]) { throw 'Non-monotonic DTS.' }
        $lastDts[$packet.codec_type] = $dts
        if ($packet.codec_type -eq 'audio' -and -not $audioHashes.Contains($packet.data_hash)) { throw 'AAC payload changed.' }
    }
    $avDifference = [Math]::Abs($lastDts.video - $lastDts.audio)
    if ($avDifference -gt 0.25) { throw 'A/V end timestamps diverged.' }
    $hashArgs = @('-v', 'error', '-i', $source, '-map', '0:v:0', '-c:v', 'copy', '-bsf:v',
        'filter_units=pass_types=1-5', '-f', 'framehash', '-hash', 'sha256', '-')
    $sourceHashes = @((Native-Text $ffmpeg $hashArgs) -split '\r?\n' | Where-Object { $_ -and $_ -notmatch '^#' } |
        ForEach-Object { ($_ -split ',')[-1].Trim() })
    $hashArgs[3] = $file
    $outputHashes = @((Native-Text $ffmpeg $hashArgs) -split '\r?\n' | Where-Object { $_ -and $_ -notmatch '^#' } |
        ForEach-Object { ($_ -split ',')[-1].Trim() })
    $index = [Array]::IndexOf($sourceHashes, $outputHashes[0])
    foreach ($hash in $outputHashes) {
        if ($index -lt 0 -or $hash -ne $sourceHashes[$index]) { throw 'H.264 slices changed or frames skipped.' }
        $index = ($index + 1) % $sourceHashes.Count
    }
    Native-Text $ffmpeg @('-v', 'error', '-xerror', '-i', $file, '-f', 'null', '-') | Out-Null

    # Deliberately insufficient budget: no early release, no silent shortening, explicit failure.
    $smallDir = Join-Path $testDir 'small-buffer'
    $hostProcess = Start-Owned (Get-Command dotnet).Source @($hostDll, '--repo-root', $repoRoot,
        '--output-directory', $smallDir, '--delay-seconds', '300', '--buffer-mib', '1') 'small-host'
    Wait-Until { $s = Read-Status $smallDir; $s -and $s.Relay.MediaMtxPid } 'small-buffer startup'
    $publisher = Start-Publisher 'small-publisher'
    if (-not $hostProcess.WaitForExit(30000) -or $hostProcess.ExitCode -ne 1) { throw 'Overflow did not stop the host with exit code 1.' }
    Stop-Owned $publisher
    $overflow = Read-Status $smallDir
    if ($overflow.Relay.State -ne 'Faulted' -or $overflow.Relay.Delay.EmittedMediaTags -ne 0 -or
        $overflow.Relay.Delay.BufferBytes -ne 0 -or $overflow.Relay.Delay.PeakBufferBytes -gt 1048576) { throw 'Overflow violated buffer policy.' }
    $report = [PSCustomObject]@{
        Stage = 2; Status = 'passed'; CheckedAtUtc = [DateTime]::UtcNow.ToString('o'); DelaySeconds = $DelaySeconds
        MinimumMediaAgeMilliseconds = $finished.Relay.Delay.MinimumMediaAgeMilliseconds
        LastMediaAgeMilliseconds = $finished.Relay.Delay.LastMediaAgeMilliseconds
        MaximumMediaAgeMilliseconds = $finished.Relay.Delay.MaximumMediaAgeMilliseconds
        PeakBufferBytes = $finished.Relay.Delay.PeakBufferBytes; FinalBufferBytes = $finished.Relay.Delay.BufferBytes
        TailMediaTagsAfterSourceDisconnect = $finished.Relay.Delay.EmittedMediaTags - $tail.Relay.Delay.EmittedMediaTags
        PacketsVerified = $probe.packets.Count; VideoSliceHashesVerified = $outputHashes.Count
        AvEndDifferenceSeconds = $avDifference; FirstVideoIsKeyframe = $true; DecodedWithoutError = $true
        OverflowFailedClosed = $true; OverflowPeakBytes = $overflow.Relay.Delay.PeakBufferBytes
        File = $file; OutputDirectory = $testDir
    }
    $report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $testDir 'result.json') -Encoding UTF8
    $report | ConvertTo-Json
}
finally {
    if ($hostProcess -and -not $hostProcess.HasExited) {
        [IO.File]::WriteAllText($stopFile, 'stop')
        [void]$hostProcess.WaitForExit(($DelaySeconds + 10) * 1000)
    }
    foreach ($process in $owned) { Stop-Owned $process; $process.Dispose() }
}

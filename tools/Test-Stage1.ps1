[CmdletBinding()]
param([switch]$StartupOnly)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
& (Join-Path $PSScriptRoot 'native\Get-NativeTools.ps1') -VerifyOnly
$native = Get-Content -LiteralPath (Join-Path $repoRoot 'artifacts\native-tools.resolved.json') -Raw | ConvertFrom-Json
$ffmpeg = ($native.Tools | Where-Object Name -eq 'FFmpeg').Executable
$ffprobe = Join-Path (Split-Path -Parent $ffmpeg) 'ffprobe.exe'
$hostDll = Join-Path $repoRoot 'src\Temhalo.RelayHost\bin\Debug\net8.0-windows10.0.26100.0\Temhalo.RelayHost.dll'
if (-not (Test-Path -LiteralPath $hostDll)) { throw 'Build Debug x64 before running this test.' }
$testDir = Join-Path $repoRoot ('artifacts\validation\stage1-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($testDir)
$source = Join-Path $testDir 'source.flv'
$stopFile = Join-Path $testDir 'stop.request'
$owned = [Collections.Generic.List[Diagnostics.Process]]::new()
$publisher = $null
$relayHost = $null
$statusFile = $null
$events = [Collections.Generic.List[string]]::new()

function Start-Owned([string]$Executable, [string[]]$ArgumentValues, [string]$Label) {
    # Start-Process joins its argument list; explicitly quote each Windows argument.
    $quoted = @($ArgumentValues | ForEach-Object {
        if ($_.Contains('"') -or $_.Contains("`n")) { throw 'Unexpected quote in controlled test argument.' }
        '"' + [regex]::Replace($_, '(\\+)$', '$1$1') + '"'
    }) -join ' '
    $process = Start-Process -FilePath $Executable -ArgumentList $quoted -WorkingDirectory $repoRoot `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $testDir "$Label.stdout.txt") `
        -RedirectStandardError (Join-Path $testDir "$Label.stderr.txt")
    $owned.Add($process)
    return $process
}

function Read-Status {
    if (-not $script:statusFile) {
        $found = @(Get-ChildItem -LiteralPath (Join-Path $testDir 'relay') -Filter status.json -Recurse -ErrorAction SilentlyContinue)
        if ($found.Count -eq 0) { return $null }
        $script:statusFile = $found[0].FullName
    }
    try { return Get-Content -LiteralPath $script:statusFile -Raw | ConvertFrom-Json }
    catch [IO.IOException] { return $null }
}

function Wait-Until([scriptblock]$Condition, [string]$Description, [int]$TimeoutSeconds = 30) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        if (& $Condition) { return }
        if ($script:relayHost -and $script:relayHost.HasExited) { throw "Relay exited during: $Description. See $testDir" }
        Start-Sleep -Milliseconds 100
    }
    throw "Timeout: $Description. See $testDir"
}

function Publish-Source([string]$Label) {
    return Start-Owned $ffmpeg @('-hide_banner', '-loglevel', 'warning', '-nostdin', '-re', '-stream_loop', '-1',
        '-i', $source, '-map', '0:v:0', '-map', '0:a:0', '-c', 'copy', '-f', 'flv',
        'rtmp://127.0.0.1:1935/live/stream') $Label
}

function Stop-Owned([Diagnostics.Process]$Process) {
    if ($Process -and -not $Process.HasExited) { $Process.Kill(); $Process.WaitForExit(5000) | Out-Null }
}

function Native-Text([string]$Executable, [string[]]$ArgumentValues) {
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $lines = @(& $Executable @ArgumentValues 2>&1)
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previousPreference }
    if ($code -ne 0) { throw "Native test command failed: $($lines -join [Environment]::NewLine)" }
    return ($lines | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
}

if ($StartupOnly) {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 1935)
    $savedEnvironment = @{}
    $reportName = 'temhalo-test-' + [Guid]::NewGuid().ToString('N') + '.log'
    $nativeReport = Join-Path (Split-Path -Parent $ffmpeg) $reportName
    try {
        $listener.Server.ExclusiveAddressUse = $true
        $listener.Start()
        $conflict = Start-Owned (Get-Command dotnet).Source @($hostDll, '--repo-root', $repoRoot,
            '--output-directory', (Join-Path $testDir 'conflict'), '--run-seconds', '1') 'occupied-port'
        if (-not $conflict.WaitForExit(10000) -or $conflict.ExitCode -ne 1 -or -not $listener.Server.IsBound) {
            throw 'An occupied port was not rejected without touching its owner.'
        }
        $listener.Stop()
        $overrides = @{ MTX_MOQ = 'true'; MTX_RTSP = 'true'; MTX_RTMPADDRESS = '0.0.0.0:1935';
            FFREPORT = ('file=' + $reportName + ':level=48') }
        foreach ($name in $overrides.Keys) {
            $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
            [Environment]::SetEnvironmentVariable($name, $overrides[$name], 'Process')
        }
        $relayHost = Start-Owned (Get-Command dotnet).Source @($hostDll, '--repo-root', $repoRoot,
            '--output-directory', (Join-Path $testDir 'relay'), '--stop-file', $stopFile) 'isolated-host'
        Wait-Until { $s = Read-Status; $s -and $s.Relay.MediaMtxPid -and $s.Relay.State -ne 'StartingIngest' } 'isolated startup'
        $serverId = (Read-Status).Relay.MediaMtxPid
        $tcp = @(Get-NetTCPConnection -State Listen -OwningProcess $serverId -ErrorAction Stop)
        $udp = @(Get-NetUDPEndpoint -OwningProcess $serverId -ErrorAction SilentlyContinue)
        if ($tcp.Count -ne 1 -or $tcp[0].LocalAddress -ne '127.0.0.1' -or $tcp[0].LocalPort -ne 1935 -or $udp.Count -ne 0) {
            throw 'Unexpected native listening socket.'
        }
        if (Test-Path -LiteralPath $nativeReport) { throw 'FFREPORT escaped the controlled logger.' }
        [IO.File]::WriteAllText($stopFile, 'stop')
        if (-not $relayHost.WaitForExit(20000) -or $relayHost.ExitCode -ne 0) { throw 'Idle startup did not stop cleanly.' }
        $report = [PSCustomObject]@{ Stage = 1; Status = 'passed'; CheckedAtUtc = [DateTime]::UtcNow.ToString('o');
            OccupiedPortRejected = $true; InheritedOverridesBlocked = $true; OnlyLoopbackRtmpListener = $true;
            UnfilteredReportBlocked = $true; IdleShutdown = $true; OutputDirectory = $testDir }
        $report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $testDir 'startup-result.json') -Encoding UTF8
        $report | ConvertTo-Json
    }
    finally {
        $listener.Stop()
        foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
        if ($relayHost -and -not $relayHost.HasExited) {
            [IO.File]::WriteAllText($stopFile, 'stop'); [void]$relayHost.WaitForExit(15000)
        }
        foreach ($process in $owned) { Stop-Owned $process; $process.Dispose() }
    }
    return
}

try {
    # Only the synthetic source is encoded, using the Windows software H.264 MFT.
    # The publisher and both FFmpeg relay processes use -c copy.
    Native-Text $ffmpeg @('-hide_banner', '-loglevel', 'error', '-f', 'lavfi', '-i',
        'testsrc2=size=640x360:rate=30', '-f', 'lavfi', '-i', 'sine=frequency=880:sample_rate=48000',
        '-t', '8', '-c:v', 'h264_mf', '-hw_encoding', '0', '-b:v', '1500k', '-g', '30', '-bf', '0',
        '-c:a', 'aac', '-b:a', '160k', '-f', 'flv', '-n', $source) | Out-Null
    $relayHost = Start-Owned (Get-Command dotnet).Source @($hostDll, '--repo-root', $repoRoot,
        '--output-directory', (Join-Path $testDir 'relay'), '--stop-file', $stopFile) 'host'
    Wait-Until { $s = Read-Status; $s -and $s.Relay.MediaMtxPid } 'ingest ready'
    # Exercise a cold start without any publisher before connecting the source.
    Start-Sleep -Seconds 3
    $publisher = Publish-Source 'publisher-1'
    Wait-Until { $s = Read-Status; $s -and $s.Relay.BytesTransferred -gt 600000 } 'baseline media'
    $baseline = (Read-Status).Relay.OutputFile
    $old = Read-Status
    Stop-Owned $publisher
    Wait-Until { $s = Read-Status; $s -and $s.Relay.Attempt -gt $old.Relay.Attempt } 'baseline finalization'
    $publisher = Publish-Source 'publisher-baseline-resume'
    Wait-Until { $s = Read-Status; $s -and $s.Relay.BytesTransferred -gt ($old.Relay.BytesTransferred + 300000) } 'media before crash tests'
    $old = Read-Status
    Stop-Process -Id $old.Relay.EmitterPid -Force
    Wait-Until { $s = Read-Status; $s -and $s.Relay.Attempt -gt $old.Relay.Attempt -and
        $s.Relay.BytesTransferred -gt ($old.Relay.BytesTransferred + 300000) } 'emitter restart'
    $events.Add('EmitterCrashRecovered')

    $old = Read-Status
    Stop-Process -Id $old.Relay.ReaderPid -Force
    Wait-Until { $s = Read-Status; $s -and $s.Relay.Attempt -gt $old.Relay.Attempt -and
        $s.Relay.BytesTransferred -gt ($old.Relay.BytesTransferred + 300000) } 'reader restart'
    $events.Add('ReaderCrashRecovered')

    Stop-Owned $publisher
    $old = Read-Status
    Wait-Until { $s = Read-Status; $s -and $s.Relay.Attempt -gt $old.Relay.Attempt } 'publisher disconnect'
    $publisher = Publish-Source 'publisher-2'
    Wait-Until { $s = Read-Status; $s -and $s.Relay.BytesTransferred -gt ($old.Relay.BytesTransferred + 300000) } 'publisher reconnect'
    $events.Add('PublisherReconnectRecovered')

    $old = Read-Status
    Stop-Process -Id $old.Relay.MediaMtxPid -Force
    Stop-Owned $publisher
    Wait-Until { $s = Read-Status; $s -and $s.Relay.MediaMtxPid -and
        $s.Relay.MediaMtxPid -ne $old.Relay.MediaMtxPid -and $s.Relay.State -ne 'StartingIngest' } 'MediaMTX restart'
    $publisher = Publish-Source 'publisher-3'
    Wait-Until { $s = Read-Status; $s -and $s.Relay.BytesTransferred -gt ($old.Relay.BytesTransferred + 300000) } 'media after ingest restart'
    $events.Add('MediaMtxCrashRecovered')

    $finalChildren = Read-Status
    [IO.File]::WriteAllText($stopFile, 'stop')
    if (-not $relayHost.WaitForExit(20000)) { throw 'Host did not stop within 20 seconds.' }
    if ($relayHost.ExitCode -ne 0) { throw "Relay host exit code: $($relayHost.ExitCode)" }
    Stop-Owned $publisher
    $final = Read-Status
    if ($final.Relay.State -ne 'Stopped') { throw 'Final status was not Stopped.' }
    foreach ($childId in @($finalChildren.Relay.MediaMtxPid, $finalChildren.Relay.ReaderPid, $finalChildren.Relay.EmitterPid)) {
        if ($childId -and (Get-Process -Id $childId -ErrorAction SilentlyContinue)) { throw "Child still running: $childId" }
    }
    $events.Add('GracefulStopNoOrphans')

    $probeArgs = @('-v', 'error', '-show_packets', '-show_streams', '-show_data_hash', 'sha256',
        '-show_entries', 'packet=codec_type,stream_index,dts_time,data_hash:stream=index,codec_name,codec_type', '-of', 'json')
    $inputProbe = (Native-Text $ffprobe ($probeArgs + @($source))) | ConvertFrom-Json
    $outputProbe = (Native-Text $ffprobe ($probeArgs + @($baseline))) | ConvertFrom-Json
    $video = @($outputProbe.streams | Where-Object codec_type -eq 'video')
    $audio = @($outputProbe.streams | Where-Object codec_type -eq 'audio')
    if ($video.Count -ne 1 -or $video[0].codec_name -ne 'h264' -or $audio.Count -ne 1 -or $audio[0].codec_name -ne 'aac') {
        throw 'Expected exactly one H.264 and one AAC stream.'
    }
    $known = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($packet in $inputProbe.packets | Where-Object codec_type -eq 'audio') { [void]$known.Add($packet.data_hash) }
    $lastDts = @{}
    foreach ($packet in $outputProbe.packets) {
        if ($packet.codec_type -eq 'audio' -and -not $known.Contains($packet.data_hash)) { throw 'AAC packet payload changed.' }
        $dts = [double]::Parse($packet.dts_time, [Globalization.CultureInfo]::InvariantCulture)
        $track = $packet.codec_type
        if ($lastDts.ContainsKey($track) -and $dts -lt $lastDts[$track]) { throw 'Non-monotonic DTS in baseline file.' }
        $lastDts[$track] = $dts
    }
    if ($outputProbe.packets.Count -lt 100) { throw 'Insufficient media captured.' }
    if ([Math]::Abs($lastDts.video - $lastDts.audio) -gt 0.25) { throw 'A/V end timestamps diverged by more than 250 ms.' }
    # MediaMTX may normalize non-VCL H.264 units. Compare the actual encoded slices,
    # after selecting NAL types 1..5 in the verifier only. No filter runs in the relay.
    $vclArgs = @('-v', 'error', '-i', $source, '-map', '0:v:0', '-c:v', 'copy', '-bsf:v',
        'filter_units=pass_types=1-5', '-f', 'framehash', '-hash', 'sha256', '-')
    $sourceHashes = @( (Native-Text $ffmpeg $vclArgs) -split '\r?\n' | Where-Object { $_ -and $_ -notmatch '^#' } |
        ForEach-Object { ($_ -split ',')[-1].Trim() } )
    $vclArgs[3] = $baseline
    $outputHashes = @( (Native-Text $ffmpeg $vclArgs) -split '\r?\n' | Where-Object { $_ -and $_ -notmatch '^#' } |
        ForEach-Object { ($_ -split ',')[-1].Trim() } )
    if ($outputHashes.Count -lt 30) { throw 'Insufficient video slices captured.' }
    $sourceIndex = [Array]::IndexOf($sourceHashes, $outputHashes[0])
    foreach ($hash in $outputHashes) {
        if ($sourceIndex -lt 0 -or $hash -ne $sourceHashes[$sourceIndex]) { throw 'H.264 slices changed or frames skipped.' }
        $sourceIndex = ($sourceIndex + 1) % $sourceHashes.Count
    }
    Native-Text $ffmpeg @('-hide_banner', '-v', 'error', '-xerror', '-i', $baseline, '-f', 'null', '-') | Out-Null
    $events.Add('PacketPayloadsPreservedAndDecodable')

    # A hard host crash must also release all supervised children via the job object.
    $crashHost = Start-Owned (Get-Command dotnet).Source @($hostDll, '--repo-root', $repoRoot,
        '--output-directory', (Join-Path $testDir 'crash-relay')) 'crash-host'
    $crashStatus = $null
    $crashPublisher = $null
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.Elapsed.TotalSeconds -lt 15) {
        $files = @(Get-ChildItem -LiteralPath (Join-Path $testDir 'crash-relay') -Filter status.json -Recurse -ErrorAction SilentlyContinue)
        if ($files.Count -gt 0) {
            $crashStatus = Get-Content -LiteralPath $files[0].FullName -Raw | ConvertFrom-Json
            if ($crashStatus.Relay.MediaMtxPid -and -not $crashPublisher) { $crashPublisher = Publish-Source 'publisher-crash' }
            if ($crashStatus.Relay.ReaderPid -and $crashStatus.Relay.BytesTransferred -gt 10000) { break }
        }
        Start-Sleep -Milliseconds 100
    }
    if (-not $crashStatus -or -not $crashStatus.Relay.ReaderPid) { throw 'Hard-crash setup failed.' }
    Stop-Owned $crashHost
    Stop-Owned $crashPublisher
    Start-Sleep -Milliseconds 750
    foreach ($childId in @($crashStatus.Relay.MediaMtxPid, $crashStatus.Relay.ReaderPid, $crashStatus.Relay.EmitterPid)) {
        if ($childId -and (Get-Process -Id $childId -ErrorAction SilentlyContinue)) { throw "Orphan after host crash: $childId" }
    }
    $events.Add('HardHostCrashNoOrphans')
    $report = [PSCustomObject]@{
        Stage = 1; Status = 'passed'; CheckedAtUtc = [DateTime]::UtcNow.ToString('o'); Events = @($events.ToArray())
        BaselineFile = $baseline; PacketsVerified = $outputProbe.packets.Count
        VideoSliceHashesVerified = $outputHashes.Count
        AvEndDifferenceSeconds = [Math]::Abs($lastDts.video - $lastDts.audio)
        RelayedBytes = $final.Relay.BytesTransferred; OutputDirectory = $testDir
    }
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $testDir 'result.json') -Encoding UTF8
    $report | ConvertTo-Json -Depth 5
}
finally {
    if ($relayHost -and -not $relayHost.HasExited) {
        [IO.File]::WriteAllText($stopFile, 'stop')
        [void]$relayHost.WaitForExit(15000)
    }
    foreach ($process in $owned) { Stop-Owned $process; $process.Dispose() }
}

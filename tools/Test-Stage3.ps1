[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
& (Join-Path $PSScriptRoot 'native\Get-NativeTools.ps1') -VerifyOnly
$native = Get-Content -LiteralPath (Join-Path $repoRoot 'artifacts\native-tools.resolved.json') -Raw | ConvertFrom-Json
$ffmpeg = ($native.Tools | Where-Object Name -eq 'FFmpeg').Executable
$ffprobe = Join-Path (Split-Path -Parent $ffmpeg) 'ffprobe.exe'
$hostDll = Join-Path $repoRoot 'src\Temhalo.RelayHost\bin\Debug\net8.0-windows10.0.26100.0\Temhalo.RelayHost.dll'
$testDir = Join-Path $repoRoot ('artifacts\validation\stage3-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($testDir)
$source = Join-Path $testDir 'source.flv'
$relayDir = Join-Path $testDir 'relay'
$stopFile = Join-Path $testDir 'stop.request'
$owned = [Collections.Generic.List[Diagnostics.Process]]::new()
$history = [Collections.Generic.List[object]]::new()
$hostProcess = $null
$sessionDirectory = $null

function Start-Owned([string]$Executable, [string[]]$ArgumentValues, [string]$Label) {
    $quoted = @($ArgumentValues | ForEach-Object {
        if ($_.Contains('"') -or $_.Contains("`n")) { throw 'Unexpected test argument.' }
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
function Native-Text([string]$Executable, [string[]]$ArgumentValues) {
    $previousPreference = $ErrorActionPreference
    try { $ErrorActionPreference = 'Continue'; $lines = @(& $Executable @ArgumentValues 2>&1); $code = $LASTEXITCODE }
    finally { $ErrorActionPreference = $previousPreference }
    if ($code -ne 0) { throw "Native command failed: $($lines -join [Environment]::NewLine)" }
    return ($lines | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
}
function Read-Status {
    $files = @(Get-ChildItem -LiteralPath $relayDir -Filter status.json -Recurse -ErrorAction SilentlyContinue)
    if ($files.Count -eq 0) { return $null }
    $script:sessionDirectory = Split-Path -Parent $files[0].FullName
    try { return Get-Content -LiteralPath $files[0].FullName -Raw | ConvertFrom-Json }
    catch [IO.IOException] { return $null }
}
function Wait-Until([scriptblock]$Condition, [string]$Description, [int]$TimeoutSeconds = 40) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        $s = Read-Status
        if ($s -and $s.Relay.Delay) {
            $history.Add([PSCustomObject]@{ Utc = $s.UpdatedUtc; Attempt = $s.Relay.Attempt; Delay = $s.Relay.Delay })
        }
        if (& $Condition $s) { return }
        if ($script:hostProcess -and $script:hostProcess.HasExited) { throw "Host exited during $Description. See $testDir" }
        Start-Sleep -Milliseconds 50
    }
    throw "Timeout: $Description. See $testDir"
}
function Set-Target([int]$Seconds) {
    return & (Join-Path $PSScriptRoot 'Set-LocalDelay.ps1') -Seconds $Seconds -SessionDirectory $sessionDirectory
}
function Wait-Target([int]$Seconds, [long]$Version) {
    Wait-Until { param($s) $s -and $s.Relay.Delay -and $s.Relay.Delay.Control.AppliedVersion -ge $Version -and
        $s.Relay.Delay.Control.TargetReached -and $s.Relay.Delay.LastMediaAgeMilliseconds -ge $Seconds * 1000 -and
        $s.Relay.Delay.LastMediaAgeMilliseconds -le ($Seconds * 1000 + 1200) } "target $Seconds s"
}

try {
    Native-Text $ffmpeg @('-v', 'error', '-f', 'lavfi', '-i', 'testsrc2=size=640x360:rate=30', '-f', 'lavfi',
        '-i', 'sine=frequency=880:sample_rate=48000', '-t', '8', '-c:v', 'h264_mf', '-hw_encoding', '0',
        '-b:v', '1500k', '-g', '30', '-bf', '0', '-c:a', 'aac', '-b:a', '160k', '-f', 'flv', '-n', $source) | Out-Null
    $hostProcess = Start-Owned (Get-Command dotnet).Source @($hostDll, '--repo-root', $repoRoot,
        '--output-directory', $relayDir, '--stop-file', $stopFile, '--delay-seconds', '0', '--buffer-mib', '32',
        '--pause-step-seconds', '2', '--resume-seconds', '1') 'host'
    Wait-Until { param($s) $s -and $s.Relay.MediaMtxPid } 'ingest startup'
    $publisher = Start-Owned $ffmpeg @('-v', 'warning', '-nostdin', '-re', '-stream_loop', '-1', '-i', $source,
        '-map', '0:v:0', '-map', '0:a:0', '-c', 'copy', '-f', 'flv', 'rtmp://127.0.0.1:1935/live/stream') 'publisher'
    Wait-Until { param($s) $s -and $s.Relay.Delay -and $s.Relay.Delay.EmittedMediaTags -gt 100 } 'baseline output'
    $baseline = Read-Status
    $pidSet = @($baseline.Relay.MediaMtxPid, $baseline.Relay.ReaderPid, $baseline.Relay.EmitterPid)
    $request = Set-Target 6
    Wait-Target 6 $request.AcceptedVersion
    $increased = Read-Status
    if ($increased.Relay.Delay.Control.CompletedPauseSteps -ne 3 -or
        $increased.Relay.Delay.Control.MaximumPauseMilliseconds -gt 2500) { throw 'Pause step policy failed.' }
    $request = Set-Target 3
    Wait-Target 3 $request.AcceptedVersion
    $reduced = Read-Status
    if ($reduced.Relay.Delay.Control.CompletedSeeks -lt 1) { throw 'Reduction did not seek.' }
    $request = Set-Target 0
    Wait-Target 0 $request.AcceptedVersion
    $zero = Read-Status
    if ($zero.Relay.Delay.BufferBytes -ge $increased.Relay.Delay.BufferBytes) { throw 'Zero did not release the delayed backlog.' }

    # Emergency zero while a pause is actually in progress.
    $request = Set-Target 10
    Wait-Until { param($s) $s -and $s.Relay.Delay.Control.IsPaused } 'emergency pause'
    $request = Set-Target 0
    Wait-Target 0 $request.AcceptedVersion
    for ($cycle = 0; $cycle -lt 2; $cycle++) {
        $request = Set-Target 2
        Wait-Target 2 $request.AcceptedVersion
        $request = Set-Target 0
        Wait-Target 0 $request.AcceptedVersion
    }
    # Malformed user input must not terminate the stream or apply an accidental zero/default.
    $invalid = @{ Id = [Guid]::NewGuid().ToString('D'); Seconds = -1 } | ConvertTo-Json -Compress
    [IO.File]::WriteAllText((Join-Path $sessionDirectory 'delay-request.json'), $invalid)
    Start-Sleep -Milliseconds 500
    $last = Read-Status
    if ($last.Relay.Delay.TargetSeconds -ne 0 -or $hostProcess.HasExited) { throw 'Invalid command affected the session.' }
    $newPidSet = @($last.Relay.MediaMtxPid, $last.Relay.ReaderPid, $last.Relay.EmitterPid)
    if (@(Compare-Object $pidSet $newPidSet).Count -ne 0 -or $last.Relay.Attempt -ne $baseline.Relay.Attempt) {
        throw 'A delay change restarted a media process.'
    }
    Stop-Owned $publisher
    [IO.File]::WriteAllText($stopFile, 'stop')
    if (-not $hostProcess.WaitForExit(15000) -or $hostProcess.ExitCode -ne 0) { throw 'Dynamic relay did not stop cleanly.' }
    $final = Read-Status
    if ($final.Relay.Delay.BufferBytes -ne 0) { throw 'Buffer leak after shutdown.' }
    $file = $baseline.Relay.OutputFile
    $probeArgs = @('-v', 'error', '-show_packets', '-show_streams', '-show_data_hash', 'sha256', '-show_entries',
        'packet=codec_type,flags,dts_time,pts_time,data_hash:stream=codec_type,codec_name', '-of', 'json')
    $inputProbe = (Native-Text $ffprobe ($probeArgs + @($source))) | ConvertFrom-Json
    $probe = (Native-Text $ffprobe ($probeArgs + @($file))) | ConvertFrom-Json
    $knownAudio = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($packet in $inputProbe.packets | Where-Object codec_type -eq 'audio') { [void]$knownAudio.Add($packet.data_hash) }
    $lastDts = @{}
    foreach ($packet in $probe.packets) {
        $dts = [double]::Parse($packet.dts_time, [Globalization.CultureInfo]::InvariantCulture)
        if ($lastDts.ContainsKey($packet.codec_type) -and $dts -lt $lastDts[$packet.codec_type]) { throw 'DTS regressed across a cut.' }
        $lastDts[$packet.codec_type] = $dts
        if ($packet.codec_type -eq 'audio' -and -not $knownAudio.Contains($packet.data_hash)) { throw 'AAC data changed.' }
    }
    $avDifference = [Math]::Abs($lastDts.video - $lastDts.audio)
    if ($avDifference -gt 0.25) { throw 'A/V end timestamps diverged.' }
    $hashArgs = @('-v', 'error', '-i', $source, '-map', '0:v:0', '-c:v', 'copy', '-bsf:v',
        'filter_units=pass_types=1-5', '-f', 'framehash', '-hash', 'sha256', '-')
    $inputHashes = @((Native-Text $ffmpeg $hashArgs) -split '\r?\n' | Where-Object { $_ -and $_ -notmatch '^#' } |
        ForEach-Object { ($_ -split ',')[-1].Trim() })
    $hashArgs[3] = $file
    $outputHashes = @((Native-Text $ffmpeg $hashArgs) -split '\r?\n' | Where-Object { $_ -and $_ -notmatch '^#' } |
        ForEach-Object { ($_ -split ',')[-1].Trim() })
    $knownVideo = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($hash in $inputHashes) { [void]$knownVideo.Add($hash) }
    foreach ($hash in $outputHashes) { if (-not $knownVideo.Contains($hash)) { throw 'Encoded H.264 slices changed.' } }
    Native-Text $ffmpeg @('-v', 'error', '-xerror', '-i', $file, '-f', 'null', '-') | Out-Null
    $report = [PSCustomObject]@{
        Stage = 3; Status = 'passed'; CheckedAtUtc = [DateTime]::UtcNow.ToString('o')
        SameNativePidsThroughout = $true; SameOutputFile = $true; InvalidCommandRejected = $true
        SixSecondAgeMs = $increased.Relay.Delay.LastMediaAgeMilliseconds
        ThreeSecondAgeMs = $reduced.Relay.Delay.LastMediaAgeMilliseconds; ZeroAgeMs = $zero.Relay.Delay.LastMediaAgeMilliseconds
        SixSecondPauseSteps = $increased.Relay.Delay.Control.CompletedPauseSteps
        CompletedSeeks = $final.Relay.Delay.Control.CompletedSeeks
        MaximumPauseMilliseconds = $final.Relay.Delay.Control.MaximumPauseMilliseconds
        PeakBufferBytes = $final.Relay.Delay.PeakBufferBytes; FinalBufferBytes = $final.Relay.Delay.BufferBytes
        PacketsVerified = $probe.packets.Count; VideoSliceHashesVerified = $outputHashes.Count
        AvEndDifferenceSeconds = $avDifference; DecodedWithoutError = $true; OutputDirectory = $testDir; File = $file
    }
    $history.ToArray() | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $testDir 'history.json') -Encoding UTF8
    $report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $testDir 'result.json') -Encoding UTF8
    $report | ConvertTo-Json
}
finally {
    if ($hostProcess -and -not $hostProcess.HasExited) {
        [IO.File]::WriteAllText($stopFile, 'stop'); [void]$hostProcess.WaitForExit(15000)
    }
    foreach ($process in $owned) { Stop-Owned $process; $process.Dispose() }
}

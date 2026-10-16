[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or -not [Environment]::Is64BitOperatingSystem) {
    throw 'Stage 0 native validation requires Windows x64.'
}

# Verification only: this cannot download or start a media relay.
& (Join-Path $PSScriptRoot 'native\Get-NativeTools.ps1') -VerifyOnly
$resolved = Get-Content -LiteralPath (Join-Path $repoRoot 'artifacts\native-tools.resolved.json') -Raw | ConvertFrom-Json
$settings = Get-Content -LiteralPath (Join-Path $repoRoot 'config\temhalo.defaults.json') -Raw | ConvertFrom-Json
$ingest = [Uri]$settings.Ingest.Url
if ($ingest.Scheme -ne 'rtmp' -or $ingest.Host -ne '127.0.0.1' -or $ingest.Port -ne 1935 -or $ingest.AbsolutePath -ne '/live/stream') {
    throw 'Unexpected local ingest URI.'
}
if ($settings.Delay.InitialSeconds -lt 0 -or $settings.Delay.InitialSeconds -gt $settings.Delay.MaximumSeconds -or $settings.Delay.MaximumSeconds -gt 300) {
    throw 'Invalid delay range.'
}
if ($settings.Buffer.MaximumBytes -lt 16777216 -or $settings.Buffer.WarningUsageRatio -le 0 -or $settings.Buffer.WarningUsageRatio -ge 1) {
    throw 'Invalid buffer budget or warning threshold.'
}
if ($settings.Destination.PSObject.Properties.Name -contains 'StreamKey') {
    throw 'Stream keys must not be stored in the default JSON configuration.'
}

function Read-NativeMetadata([string]$Executable, [string[]]$Arguments) {
    # Windows PowerShell 5.1 wraps native stderr lines in ErrorRecord objects.
    # Metadata commands may legitimately use stderr; their exit code is decisive.
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $lines = @(& $Executable @Arguments 2>&1)
        $nativeExitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousPreference }
    if ($nativeExitCode -ne 0) { throw "Native metadata command failed: $($lines -join [Environment]::NewLine)" }
    return @($lines | ForEach-Object { $_.ToString() })
}

$mtx = ($resolved.Tools | Where-Object Name -eq 'MediaMTX').Executable
$ffmpeg = ($resolved.Tools | Where-Object Name -eq 'FFmpeg').Executable
$mtxVersion = Read-NativeMetadata $mtx @('--version')
$configPath = Join-Path $repoRoot 'config\mediamtx.yml'
$mtxValidation = Read-NativeMetadata $mtx @("--validate-conf=$configPath")
if (($mtxVersion -join '') -notmatch '^v1\.21\.2') { throw 'Unexpected MediaMTX version.' }

$ffmpegVersion = Read-NativeMetadata $ffmpeg @('-hide_banner', '-version')
$ffmpegLicense = Read-NativeMetadata $ffmpeg @('-hide_banner', '-L')
if (($ffmpegVersion -join '') -notmatch 'n8\.1\.3-18-g46d899d7a0') { throw 'Unexpected FFmpeg version.' }
if (($ffmpegVersion -join '') -match '--enable-gpl|--enable-nonfree') { throw 'The pinned LGPL build was expected.' }
if (($ffmpegLicense -join ' ') -notmatch 'Lesser General Public License') { throw 'FFmpeg did not identify itself as LGPL.' }

$protocols = @(Read-NativeMetadata $ffmpeg @('-hide_banner', '-protocols') | ForEach-Object { $_.Trim() })
foreach ($required in @('rtmp', 'rtmps', 'file', 'pipe')) {
    if ($protocols -notcontains $required) { throw "FFmpeg protocol missing: $required" }
}
$muxers = Read-NativeMetadata $ffmpeg @('-hide_banner', '-muxers')
$demuxers = Read-NativeMetadata $ffmpeg @('-hide_banner', '-demuxers')
if (-not ($muxers | Where-Object { $_ -match '^\s*E\s+flv\s' })) { throw 'FLV muxer missing.' }
if (-not ($demuxers | Where-Object { $_ -match '^\s*D\s+flv\s' })) { throw 'FLV demuxer missing.' }

$reportDir = Join-Path $repoRoot 'artifacts\validation'
[void][IO.Directory]::CreateDirectory($reportDir)
$ffmpegLicense | Set-Content -LiteralPath (Join-Path $reportDir 'ffmpeg-license.txt') -Encoding UTF8
$ffmpegVersion | Set-Content -LiteralPath (Join-Path $reportDir 'ffmpeg-version.txt') -Encoding UTF8
$report = [PSCustomObject]@{
    Stage = 0
    CheckedAtUtc = [DateTime]::UtcNow.ToString('o')
    Status = 'passed'
    Ingest = $settings.Ingest.Url
    MaximumDelaySeconds = $settings.Delay.MaximumSeconds
    BufferBudgetBytes = $settings.Buffer.MaximumBytes
    MediaMtxVersion = ($mtxVersion -join '')
    MediaMtxConfiguration = ($mtxValidation -join [Environment]::NewLine)
    FFmpegVersion = $ffmpegVersion[0]
    FFmpegLicense = 'LGPL-3.0-or-later'
    RequiredProtocolsPresent = @('rtmp', 'rtmps', 'file', 'pipe')
    FlvMuxerAndDemuxerPresent = $true
    NativeHashesVerified = $true
    RelayStarted = $false
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $reportDir 'stage0.json') -Encoding UTF8
Write-Host 'Stage 0 validation passed. No ingest server or media relay was started.'
$report

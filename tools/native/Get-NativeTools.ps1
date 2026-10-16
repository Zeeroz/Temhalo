[CmdletBinding()]
param(
    [ValidateSet('All', 'MediaMTX', 'FFmpeg')]
    [string]$Tool = 'All',
    [switch]$VerifyOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$lockPath = Join-Path $repoRoot 'config\native-tools.lock.json'
$lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
if ($lock.SchemaVersion -ne 1 -or $lock.Architecture -ne 'win-x64') {
    throw 'Unsupported native tool manifest.'
}
$cacheRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\downloads'))
$nativeRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\native\win-x64'))

function Assert-ChildPath([string]$Root, [string]$Candidate) {
    $rootPrefix = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $candidatePath = [IO.Path]::GetFullPath($Candidate)
    if (-not $candidatePath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path escapes its intended directory: $candidatePath"
    }
    return $candidatePath
}

function Assert-Sha256([string]$Path, [string]$Expected) {
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if (-not $actual.Equals($Expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw "SHA-256 mismatch for $([IO.Path]::GetFileName($Path)); no extraction or execution permitted."
    }
}

function Save-Archive([Uri]$Uri, [string]$Destination) {
    if ($Uri.Scheme -ne 'https' -or $Uri.Host -ne 'github.com') {
        throw 'Only pinned HTTPS GitHub release archives are accepted.'
    }
    $partial = Assert-ChildPath $cacheRoot ($Destination + '.partial')
    $client = [Net.Http.HttpClient]::new()
    $client.Timeout = [TimeSpan]::FromMinutes(10)
    $client.DefaultRequestHeaders.UserAgent.ParseAdd('Temhalo-Native-Setup/0.0')
    $response = $null
    $inputStream = $null
    $outputStream = $null
    try {
        $response = $client.GetAsync($Uri, [Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        [void]$response.EnsureSuccessStatusCode()
        $inputStream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        $outputStream = [IO.File]::Create($partial)
        [void]$inputStream.CopyToAsync($outputStream).GetAwaiter().GetResult()
        $outputStream.Dispose()
        $outputStream = $null
        # Both paths were resolved inside the download cache before this move.
        $checkedDestination = Assert-ChildPath $cacheRoot $Destination
        Move-Item -LiteralPath $partial -Destination $checkedDestination
    }
    finally {
        if ($null -ne $outputStream) { $outputStream.Dispose() }
        if ($null -ne $inputStream) { $inputStream.Dispose() }
        if ($null -ne $response) { $response.Dispose() }
        $client.Dispose()
    }
}

function Expand-CheckedArchive([string]$ArchivePath, [string]$Target, [string]$ArchiveRoot) {
    $checkedTarget = Assert-ChildPath $nativeRoot $Target
    if (Test-Path -LiteralPath $checkedTarget) {
        if (@(Get-ChildItem -LiteralPath $checkedTarget -Force).Count -eq 0) {
            # Remove only this verified empty directory after an interrupted setup.
            [IO.Directory]::Delete($checkedTarget)
        }
        else { throw "An incomplete or unverified native directory already exists: $checkedTarget" }
    }
    [void][IO.Directory]::CreateDirectory($checkedTarget)
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        foreach ($entry in $archive.Entries) {
            $entryName = $entry.FullName.Replace('\', '/')
            if ($entryName.StartsWith('/') -or $entryName.Contains(':')) {
                throw 'An unsafe archive path was rejected.'
            }
            # Never create symlinks or follow an archive-supplied filesystem link.
            $unixType = ($entry.ExternalAttributes -shr 16) -band 0xF000
            if ($unixType -eq 0xA000) { throw 'Archive symlinks are not accepted.' }
            if ($ArchiveRoot) {
                if ($entryName -eq ($ArchiveRoot + '/')) { continue }
                if (-not $entryName.StartsWith($ArchiveRoot + '/', [StringComparison]::Ordinal)) {
                    throw 'Unexpected archive root.'
                }
                $entryName = $entryName.Substring($ArchiveRoot.Length + 1)
            }
            if (-not $entryName) { continue }
            $destination = Assert-ChildPath $checkedTarget (Join-Path $checkedTarget $entryName)
            if ($entryName.EndsWith('/')) {
                [void][IO.Directory]::CreateDirectory($destination)
                continue
            }
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
            $source = $entry.Open()
            $targetStream = [IO.File]::Create($destination)
            try { $source.CopyTo($targetStream) }
            finally { $source.Dispose(); $targetStream.Dispose() }
        }
    }
    finally { $archive.Dispose() }
}

$selected = @($lock.Tools | Where-Object { $Tool -eq 'All' -or $_.Name -eq $Tool })
$resolved = @()
foreach ($item in $selected) {
    $archivePath = Assert-ChildPath $cacheRoot (Join-Path $cacheRoot $item.ArchiveName)
    $targetRoot = Assert-ChildPath $nativeRoot (Join-Path $nativeRoot $item.DirectoryName)
    $provenancePath = Join-Path $targetRoot '.temhalo-provenance.json'
    $exePath = Assert-ChildPath $targetRoot (Join-Path $targetRoot $item.Executable)
    if (-not (Test-Path -LiteralPath $archivePath)) {
        if ($VerifyOnly) { throw "Archive missing: $archivePath" }
        [void][IO.Directory]::CreateDirectory($cacheRoot)
        Write-Host "Downloading $($item.Name) $($item.Version)..."
        Save-Archive ([Uri]$item.Url) $archivePath
    }
    Assert-Sha256 $archivePath $item.Sha256
    if (-not (Test-Path -LiteralPath $provenancePath)) {
        if ($VerifyOnly) { throw "Verified extraction missing: $targetRoot" }
        Expand-CheckedArchive $archivePath $targetRoot $item.ArchiveRoot
        if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) { throw "Executable missing: $exePath" }
        $fileRecords = @(Get-ChildItem -LiteralPath $targetRoot -Recurse -File | Sort-Object FullName | ForEach-Object {
            [PSCustomObject]@{
                Path = $_.FullName.Substring($targetRoot.Length).TrimStart('\', '/').Replace('\', '/')
                Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        })
        [PSCustomObject]@{ ArchiveSha256 = $item.Sha256; Files = $fileRecords } |
            ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $provenancePath -Encoding UTF8
    }
    $provenance = Get-Content -LiteralPath $provenancePath -Raw | ConvertFrom-Json
    if ($provenance.ArchiveSha256 -ne $item.Sha256) { throw 'Installed native archive version differs from the lock.' }
    foreach ($fileRecord in $provenance.Files) {
        $filePath = Assert-ChildPath $targetRoot (Join-Path $targetRoot $fileRecord.Path)
        Assert-Sha256 $filePath $fileRecord.Sha256
    }
    if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) { throw "Executable missing: $exePath" }
    $resolved += [PSCustomObject]@{ Name = $item.Name; Version = $item.Version; Executable = $exePath; Directory = $targetRoot; ArchiveSha256 = $item.Sha256 }
    Write-Host "$($item.Name) verified: $($item.Version)"
}

$reportPath = Join-Path $repoRoot 'artifacts\native-tools.resolved.json'
# Preserve the other entry if only one dependency was requested.
if (Test-Path -LiteralPath $reportPath) {
    $previous = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    $resolved += @($previous.Tools | Where-Object { $resolved.Name -notcontains $_.Name })
}
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($reportPath))
[PSCustomObject]@{ SchemaVersion = 1; Tools = @($resolved | Sort-Object Name) } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportPath -Encoding UTF8
Write-Host "Resolved tool paths: $reportPath"

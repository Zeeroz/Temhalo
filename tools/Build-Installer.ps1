[CmdletBinding()]
param(
    [string]$ArchivePath,
    [string]$OutputPath,
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$NumericVersion,
    [string]$TestRoot,
    [ValidatePattern('^[a-f0-9]{32}$')][string]$TestId
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
[xml]$properties = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
$version = [string]$properties.Project.PropertyGroup.Version
if ($version -notmatch '^(\d+)\.(\d+)\.(\d+)(?:-preview\.(\d+))?$') { throw 'Version non prise en charge : fournir une version stable ou -preview.N.' }
if (-not $NumericVersion) {
    $revision = if ($Matches.ContainsKey(4)) { $Matches[4] } else { '65535' }
    $NumericVersion = "$($Matches[1]).$($Matches[2]).$($Matches[3]).$revision"
}
if (@($NumericVersion.Split('.') | Where-Object { [int]$_ -gt 65535 }).Count) { throw 'Composante de version supérieure à 65535.' }
if ([bool]$TestRoot -ne [bool]$TestId) { throw 'TestRoot et TestId doivent être fournis ensemble.' }
if ($TestRoot) {
    $TestRoot = [IO.Path]::GetFullPath($TestRoot)
    $testPrefix = Join-Path $repoRoot 'artifacts\validation\installer\'
    if (-not $TestRoot.StartsWith($testPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Le mode de test doit rester sous artifacts\validation\installer.' }
}
if (-not $ArchivePath) { $ArchivePath = Join-Path $repoRoot "artifacts\packages\Temhalo-$version-win-x64.zip" }
$archive = (Resolve-Path -LiteralPath $ArchivePath).Path
$expectedHash = ((Get-Content -LiteralPath ($archive + '.sha256') -Raw).Trim() -split '\s+')[0]
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Empreinte du package source invalide.' }
$buildDirectory = Join-Path $repoRoot ('artifacts\installer\build-' + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $buildDirectory 'payload\Temhalo'
$generated = Join-Path $buildDirectory 'generated'
[void][IO.Directory]::CreateDirectory($generated)
if (-not $OutputPath) {
    $OutputPath = if ($TestRoot) { Join-Path $TestRoot 'Temhalo-Test-Setup.exe' } else { Join-Path $repoRoot "artifacts\packages\Temhalo-$version-Setup-x64.exe" }
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $OutputPath) { throw "Installeur déjà présent : $OutputPath. Choisir un nouveau nom pour conserver la livraison précédente." }
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($OutputPath))

# Acquire a portable official compiler, without installing it into Windows.
$lock = Get-Content -LiteralPath (Join-Path $repoRoot 'packaging\windows\nsis.lock.json') -Raw | ConvertFrom-Json
$cache = Join-Path $repoRoot 'artifacts\toolchains'
[void][IO.Directory]::CreateDirectory($cache)
$compilerArchive = Join-Path $cache "nsis-$($lock.version).zip"
if (-not (Test-Path -LiteralPath $compilerArchive) -or (Get-FileHash -LiteralPath $compilerArchive -Algorithm SHA256).Hash -ne $lock.sha256) {
    $partial = $compilerArchive + '.' + [Guid]::NewGuid().ToString('N') + '.partial'
    try {
        $response = Invoke-WebRequest -Uri $lock.url -OutFile $partial -PassThru -TimeoutSec 60
        # SourceForge sometimes returns an HTML landing page instead of the ZIP.
        if ($response.Headers['Content-Type'] -match 'text/html') {
            $html = Get-Content -LiteralPath $partial -Raw
            $match = [regex]::Match($html, [regex]::Escape($lock.url) + '\?ts=[^"<> ]+')
            if (-not $match.Success) { throw 'SourceForge ne fournit pas le ZIP attendu.' }
            Invoke-WebRequest -Uri ([Net.WebUtility]::HtmlDecode($match.Value)) -OutFile $partial -TimeoutSec 60
        }
        if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash -ne $lock.sha256 -or (Get-Item -LiteralPath $partial).Length -ne $lock.bytes) { throw 'SHA-256/taille du compilateur NSIS non conforme.' }
        Move-Item -LiteralPath $partial -Destination $compilerArchive -Force
    }
    finally { if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial } }
}
$compilerDirectory = Join-Path $buildDirectory 'compiler'
[IO.Compression.ZipFile]::ExtractToDirectory($compilerArchive, $compilerDirectory)
$nsis = Join-Path $compilerDirectory "nsis-$($lock.version)"
$compiler = Join-Path $nsis 'makensis.exe'
if ((& $compiler /VERSION) -ne "v$($lock.version)") { throw 'Version du compilateur inattendue.' }

# Validate every payload byte and every relative path before generating NSIS code.
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    foreach ($entry in $zip.Entries) {
        $destination = [IO.Path]::GetFullPath((Join-Path (Join-Path $buildDirectory 'payload') $entry.FullName))
        if (-not $destination.StartsWith($payload + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Chemin ZIP hors du dossier Temhalo.' }
    }
}
finally { $zip.Dispose() }
[IO.Compression.ZipFile]::ExtractToDirectory($archive, (Join-Path $buildDirectory 'payload'))
$manifest = Get-Content -LiteralPath (Join-Path $payload 'package-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.Version -ne $version) { throw 'La version du ZIP ne correspond pas au projet.' }
foreach ($entry in $manifest.Files) {
    $path = Join-Path $payload $entry.Path
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.Sha256 -or (Get-Item -LiteralPath $path).Length -ne $entry.Bytes) { throw "Fichier source non conforme : $($entry.Path)" }
}
if (@(Get-ChildItem -LiteralPath $payload -Recurse -File).Count -ne @($manifest.Files).Count + 1) { throw 'Fichiers supplémentaires dans le ZIP source.' }
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\INSTALLATION-WINDOWS.md') -Destination $payload
Copy-Item -LiteralPath (Join-Path $nsis 'COPYING') -Destination (Join-Path $payload 'Licenses\NSIS-COPYING.txt')
Copy-Item -LiteralPath (Join-Path $repoRoot 'packaging\windows\INSTALLER-NOTICES.txt') -Destination $payload

$files = @(Get-ChildItem -LiteralPath $payload -Recurse -File | Sort-Object FullName)
$currentPaths = @($files | ForEach-Object { [IO.Path]::GetRelativePath($payload, $_.FullName).Replace('\','/') })
$historyPath = Join-Path $repoRoot 'packaging\windows\uninstall-inventory.json'
$history = if (Test-Path -LiteralPath $historyPath) { @(Get-Content -LiteralPath $historyPath -Raw | ConvertFrom-Json) } else { @() }
if ($TestRoot) { $history += 'obsolete-validation.dll' }
$allPaths = @(@($history) + $currentPaths | Sort-Object -Unique)
foreach ($relative in $allPaths) {
    if ([IO.Path]::IsPathRooted($relative) -or $relative -match '(^|/)\.\.?(/|$)|[\\:*?"<>|$\r\n]' -or $relative -eq 'Uninstall.exe') { throw "Chemin d'inventaire non sûr : $relative" }
}
# Keep the union across releases: cleanup never reads a mutable deletion manifest.
if (-not $TestRoot) { ConvertTo-Json -InputObject $allPaths | Set-Content -LiteralPath $historyPath -Encoding UTF8 }
function Escape-Nsis([string]$Value) { return $Value.Replace('$','$$').Replace('"','$\"') }
function Save-Include([string]$Name, [object[]]$Lines) {
    $Lines | Set-Content -LiteralPath (Join-Path $generated $Name) -Encoding UTF8
}
$directories = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($relative in $allPaths) {
    $parent = [IO.Path]::GetDirectoryName($relative.Replace('/','\'))
    while ($parent) { [void]$directories.Add($parent); $parent = [IO.Path]::GetDirectoryName($parent) }
}
$directoryPaths = @($directories | Sort-Object { $_.Length } -Descending)
$preflight = [Collections.Generic.List[string]]::new()
$preflight.Add('!macro Preflight PREFIX')
foreach ($relative in $directoryPaths) {
    $preflight.Add('StrCpy $GuardedPath "$INSTDIR\' + $relative + '"')
    $preflight.Add('Call @{PREFIX}GuardDirectory'.Replace('@{', [string][char]36 + '{'))
}
foreach ($relative in @($allPaths) + @('Uninstall.exe')) {
    $preflight.Add('StrCpy $GuardedPath "$INSTDIR\' + $relative.Replace('/','\') + '"')
    $preflight.Add('Call @{PREFIX}ProbeOwnedFile'.Replace('@{', [string][char]36 + '{'))
}
$preflight.Add('!macroend')
Save-Include 'preflight.nsh' $preflight.ToArray()
$fileCommands = [Collections.Generic.List[string]]::new()
foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($payload, $file.FullName)
    $parent = [IO.Path]::GetDirectoryName($relative)
    $fileCommands.Add('SetOutPath "$INSTDIR' + $(if ($parent) { '\' + $parent } else { '' }) + '"')
    $fileCommands.Add('File "' + (Escape-Nsis $file.FullName) + '"')
}
Save-Include 'payload.nsh' $fileCommands.ToArray()
Save-Include 'delete-files.nsh' @($allPaths | ForEach-Object { 'Delete "$INSTDIR\' + $_.Replace('/','\') + '"' })
Save-Include 'delete-directories.nsh' @($directoryPaths | ForEach-Object { 'RMDir "$INSTDIR\' + $_ + '"' })
$obsolete = @($allPaths | Where-Object { $currentPaths -notcontains $_ })
Save-Include 'obsolete.nsh' @('; Known obsolete files only.'; $obsolete | ForEach-Object { 'Delete "$INSTDIR\' + $_.Replace('/','\') + '"' })
& (Join-Path $PSScriptRoot 'New-InstallerArtwork.ps1') -OutputDirectory (Join-Path $buildDirectory 'artwork')
$arguments = @('/V2', '/WX', '/INPUTCHARSET', 'UTF8',
    "/DPRODUCT_VERSION=$version", "/DNUMERIC_VERSION=$NumericVersion",
    "/DOUTPUT_FILE=$(Escape-Nsis $OutputPath)", "/DGENERATED_DIR=$(Escape-Nsis $generated)",
    "/DBRAND_ICON=$(Escape-Nsis (Join-Path $repoRoot 'assets\brand\temhalo.ico'))",
    "/DARTWORK_DIR=$(Escape-Nsis (Join-Path $buildDirectory 'artwork'))",
    "/DMAX_RELATIVE_PATH=$(($allPaths | Measure-Object -Property Length -Maximum).Maximum)",
    "/DINSTALLED_KIB=$([int][Math]::Ceiling(($files | Measure-Object Length -Sum).Sum / 1KB))")
if ($TestRoot) { $arguments += "/DTEST_ROOT=$(Escape-Nsis $TestRoot)", "/DTEST_ID=$TestId" }
$installerSourceHash = (Get-FileHash -LiteralPath (Join-Path $repoRoot 'packaging\windows\Temhalo.nsi') -Algorithm SHA256).Hash.ToLowerInvariant()
$arguments += (Join-Path $repoRoot 'packaging\windows\Temhalo.nsi')
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw "Compilation NSIS échouée ($LASTEXITCODE)." }
$sha256 = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$sha256  $([IO.Path]::GetFileName($OutputPath))" | Set-Content -LiteralPath ($OutputPath + '.sha256') -Encoding ASCII
$report = [PSCustomObject]@{
    Installer = $OutputPath; Version = $version; NumericVersion = $NumericVersion
    Bytes = (Get-Item -LiteralPath $OutputPath).Length; SHA256 = $sha256
    Compiler = "NSIS $($lock.version)"; CompilerSHA256 = $lock.sha256
    SourceArchive = $archive; SourceArchiveSHA256 = $expectedHash.ToLowerInvariant()
    PayloadFiles = $currentPaths.Count; HistoricalFiles = $allPaths.Count
    ObsoleteFiles = $obsolete.Count; TestRoot = $TestRoot; TestId = $TestId
    UninstallRegistrySubKey = if ($TestRoot) { "Software\Microsoft\Windows\CurrentVersion\Uninstall\Temhalo.Installer.Test.$TestId" } else { 'Software\Microsoft\Windows\CurrentVersion\Uninstall\Temhalo' }
    BuildDirectory = $buildDirectory; Signed = $false
    InstallerSourceSHA256 = $installerSourceHash
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath ($OutputPath + '.build.json') -Encoding UTF8
Write-Output $report

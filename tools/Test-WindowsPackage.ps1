[CmdletBinding()]
param([Parameter(Mandatory)][string]$ArchivePath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$archive = (Resolve-Path -LiteralPath $ArchivePath).Path
$expected = ((Get-Content -LiteralPath ($archive + '.sha256') -Raw).Trim() -split '\s+')[0]
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) { throw 'SHA-256 du ZIP invalide.' }
$destination = Join-Path $repoRoot ('artifacts\validation\stage6\extracted with spaces-' + [Guid]::NewGuid().ToString('N'))
[IO.Compression.ZipFile]::ExtractToDirectory($archive, $destination)
$package = Join-Path $destination 'Temhalo'
$manifest = Get-Content -LiteralPath (Join-Path $package 'package-manifest.json') -Raw | ConvertFrom-Json
$prefix = [IO.Path]::GetFullPath($package).TrimEnd('\') + '\'
foreach ($entry in $manifest.Files) {
    $path = [IO.Path]::GetFullPath((Join-Path $package $entry.Path))
    if (-not $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Chemin hors du package.' }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.Sha256 -or (Get-Item -LiteralPath $path).Length -ne $entry.Bytes) { throw "Fichier invalide : $($entry.Path)" }
}
$actualFiles = @(Get-ChildItem -LiteralPath $package -Recurse -File)
if ($actualFiles.Count -ne @($manifest.Files).Count + 1) { throw 'Fichiers supplémentaires non annoncés dans le package.' }
foreach ($file in 'Temhalo.App.exe','coreclr.dll','hostfxr.dll','Microsoft.UI.Xaml.dll','Microsoft.AspNetCore.Server.Kestrel.Core.dll','temhalo.portable.json','Licenses\nuget-inventory.json','Assets\Brand\temhalo.ico','Assets\Brand\splash\temhalo-splash-dark.png','Temhalo.App.pri','App.xbf','MainWindow.xbf','Views\DashboardPage.xbf','Styles\Colors.xbf','Styles\Controls.xbf') {
    if (-not (Test-Path -LiteralPath (Join-Path $package $file) -PathType Leaf)) { throw "Fichier essentiel absent : $file" }
}
if (@($actualFiles | Where-Object { $_.Extension -in '.dpapi','.log','.pfx','.p12','.cs' -or $_.Name -eq 'native-tools.resolved.json' }).Count) { throw 'Fichier privé/source inattendu dans le package.' }
$icon = [IO.File]::ReadAllBytes((Join-Path $package 'Assets\Brand\temhalo.ico'))
if ([BitConverter]::ToUInt16($icon, 2) -ne 1) { throw 'ICO invalide.' }
$sizes = @(); for ($i = 0; $i -lt [BitConverter]::ToUInt16($icon, 4); $i++) { $width = [int]$icon[6 + 16 * $i]; if ($width -eq 0) { $width = 256 }; $sizes += $width }
foreach ($size in 16,32,48,256) { if ($sizes -notcontains $size) { throw "Taille ICO absente : $size" } }
$runtime = Get-Content -LiteralPath (Join-Path $package 'Temhalo.App.runtimeconfig.json') -Raw | ConvertFrom-Json
if (@($runtime.runtimeOptions.includedFrameworks).Count -ne 2) { throw 'Runtimes autonomes incomplets.' }
$report = [PSCustomObject]@{ Status = 'passed'; Archive = $archive; ExtractedDirectory = $package; FilesVerified = @($manifest.Files).Count; IconSizes = $sizes; Runtime = $runtime.runtimeOptions.includedFrameworks; GuiStarted = $false }
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destination 'package-test.json') -Encoding UTF8
Write-Output $report

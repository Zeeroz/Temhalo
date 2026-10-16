[CmdletBinding()]
param([switch]$SkipPublish)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
[xml]$properties = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
$version = [string]$properties.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(-[a-z0-9.]+)?$') { throw 'Version non valide pour le packaging.' }
$releaseRoot = Join-Path $repoRoot 'artifacts\packages'
$publishRoot = Join-Path $repoRoot 'artifacts\publish\win-x64'
$packageName = "Temhalo-$version-win-x64"
$stage = Join-Path $releaseRoot ($packageName + '-' + [Guid]::NewGuid().ToString('N'))
$package = Join-Path $stage 'Temhalo'
Push-Location -LiteralPath $repoRoot
try {
    if (-not $SkipPublish) {
        & dotnet restore src/Temhalo.App -p:Platform=x64 -p:PublishProfile=Portable -p:SelfContained=true --locked-mode
        if ($LASTEXITCODE -ne 0) { throw 'Restauration du profil portable échouée.' }
        # A unique output avoids stale files from any previous publish.
        $publishRoot = Join-Path $repoRoot ('artifacts\publish\' + [Guid]::NewGuid().ToString('N'))
        & dotnet publish src/Temhalo.App -c Release -p:Platform=x64 -p:PublishProfile=Portable -p:SelfContained=true --no-restore -o $publishRoot
        if ($LASTEXITCODE -ne 0) { throw 'Publication autonome échouée.' }
        [void][IO.Directory]::CreateDirectory((Join-Path $repoRoot 'artifacts\publish'))
        $publishRoot | Set-Content -LiteralPath (Join-Path $repoRoot 'artifacts\publish\last-output.txt') -Encoding UTF8
    }
    else { $publishRoot = (Get-Content -LiteralPath (Join-Path $repoRoot 'artifacts\publish\last-output.txt') -Raw).Trim() }
    if (-not (Test-Path -LiteralPath (Join-Path $publishRoot 'Temhalo.App.exe'))) { throw 'Exécutable publié absent.' }
    $runtime = Get-Content -LiteralPath (Join-Path $publishRoot 'Temhalo.App.runtimeconfig.json') -Raw | ConvertFrom-Json
    if (-not $runtime.runtimeOptions.includedFrameworks -or $runtime.runtimeOptions.PSObject.Properties.Name -contains 'frameworks') { throw 'Le runtime .NET doit être inclus.' }
    foreach ($required in 'coreclr.dll','hostfxr.dll','Microsoft.UI.Xaml.dll','Microsoft.AspNetCore.Server.Kestrel.Core.dll','Assets\Brand\temhalo.ico','Temhalo.App.pri','MainWindow.xbf','Views\DashboardPage.xbf','Styles\Colors.xbf','Styles\Controls.xbf') {
        if (-not (Test-Path -LiteralPath (Join-Path $publishRoot $required))) { throw "Dépendance autonome absente : $required" }
    }
    [void][IO.Directory]::CreateDirectory($package)
    foreach ($item in Get-ChildItem -LiteralPath $publishRoot) { Copy-Item -LiteralPath $item.FullName -Destination $package -Recurse }
    $configDirectory = Join-Path $package 'config'
    [void][IO.Directory]::CreateDirectory($configDirectory)
    foreach ($file in 'mediamtx.yml','native-tools.lock.json') { Copy-Item -LiteralPath (Join-Path $repoRoot "config\$file") -Destination $configDirectory }
    @{ Version = $version; Architecture = 'win-x64'; NativeTools = 'verified-upstream-download-on-first-relay-start' } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $package 'temhalo.portable.json') -Encoding UTF8
    foreach ($directory in 'icons','splash') {
        Copy-Item -LiteralPath (Join-Path $repoRoot "assets\brand\$directory") -Destination (Join-Path $package 'Assets\Brand') -Recurse
    }
    Copy-Item -LiteralPath (Join-Path $repoRoot 'BRAND.md') -Destination $package
    Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $package
    Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\MISES-A-JOUR.md') -Destination $package
    Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\ETAPE-6.md') -Destination (Join-Path $package 'GUIDE-DE-TEST.md')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\ETAPE-5BIS-SERVICES.md') -Destination (Join-Path $package 'GUIDE-OBS.md')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'packaging\LISEZ-MOI.txt') -Destination $package
    Copy-Item -LiteralPath (Join-Path $repoRoot 'packaging\THIRD-PARTY-NOTICES.md') -Destination $package
    $noticeDirectory = Join-Path $package 'Licenses'
    [void][IO.Directory]::CreateDirectory($noticeDirectory)
    $assets = Get-Content -LiteralPath (Join-Path $repoRoot 'src\Temhalo.App\obj\project.assets.json') -Raw | ConvertFrom-Json
    $inventory = @()
    $packageFolders = @($assets.packageFolders.PSObject.Properties.Name)
    $libraries = @($assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object { $_.Name })
    foreach ($framework in $runtime.runtimeOptions.includedFrameworks) {
        $runtimePackage = if ($framework.name -eq 'Microsoft.NETCore.App') { 'microsoft.netcore.app.runtime.win-x64' } else { 'microsoft.aspnetcore.app.runtime.win-x64' }
        $libraries += "$runtimePackage/$($framework.version)"
    }
    foreach ($identity in @($libraries | Sort-Object -Unique)) {
        $parts = $identity.Split('/')
        $relative = $identity.ToLowerInvariant().Replace('/', '\')
        $source = $null
        foreach ($folder in $packageFolders) {
            $candidate = Join-Path $folder $relative
            if (Test-Path -LiteralPath $candidate -PathType Container) { $source = $candidate; break }
        }
        if (-not $source) { throw "Package NuGet absent pour les notices : $identity" }
        $destination = Join-Path $noticeDirectory ($parts -join '.')
        [void][IO.Directory]::CreateDirectory($destination)
        $noticeFiles = @(Get-ChildItem -LiteralPath $source -File | Where-Object { $_.Name -match '(?i)license|notice|copyright|\.nuspec$' })
        foreach ($file in $noticeFiles) { Copy-Item -LiteralPath $file.FullName -Destination $destination }
        $inventory += [PSCustomObject]@{ Package = $parts[0]; Version = $parts[1]; NuGet = "https://www.nuget.org/packages/$($parts[0])/$($parts[1])"; NoticeFiles = @($noticeFiles.Name) }
    }
    $inventory | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $noticeDirectory 'nuget-inventory.json') -Encoding UTF8
    $privateFiles = @(Get-ChildItem -LiteralPath $package -Recurse -File | Where-Object { $_.Extension -in '.dpapi','.log','.pfx','.p12','.user','.cs' -or $_.Name -eq 'native-tools.resolved.json' })
    if ($privateFiles.Count -ne 0) { throw 'Le package contient des fichiers privés ou des sources inattendues.' }
    $records = @(Get-ChildItem -LiteralPath $package -Recurse -File | Sort-Object FullName | ForEach-Object {
        [PSCustomObject]@{ Path = $_.FullName.Substring($package.Length + 1).Replace('\','/'); Bytes = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    @{ Version = $version; Runtime = $runtime.runtimeOptions.includedFrameworks; Files = $records } |
        ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $package 'package-manifest.json') -Encoding UTF8
    $zipPath = Join-Path $releaseRoot ($packageName + '.zip')
    if (Test-Path -LiteralPath $zipPath) { throw 'Une archive de cette version existe déjà. Déplacez-la ou choisissez une nouvelle version.' }
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $false)
    $digest = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$digest  $([IO.Path]::GetFileName($zipPath))" | Set-Content -LiteralPath ($zipPath + '.sha256') -Encoding ASCII
    Write-Host "Package : $zipPath"
    Write-Host "SHA-256 : $digest"
    Write-Output $zipPath
}
finally { Pop-Location }

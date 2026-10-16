[CmdletBinding()]
param([string]$ArchivePath, [switch]$KeepInstalledForUi)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$testId = [Guid]::NewGuid().ToString('N')
$testRoot = Join-Path $repoRoot ("artifacts\validation\installer\isolated with spaces-é-$testId")
[void][IO.Directory]::CreateDirectory($testRoot)
$buildParameters = @{ TestRoot = $testRoot; TestId = $testId }
if ($ArchivePath) { $buildParameters.ArchivePath = $ArchivePath }
$build = & (Join-Path $PSScriptRoot 'Build-Installer.ps1') @buildParameters
$installPath = Join-Path $testRoot 'Programs\Temhalo'
$registryPath = 'HKCU:\' + $build.UninstallRegistrySubKey
$profileData = Join-Path $testRoot 'profile data'
[void][IO.Directory]::CreateDirectory($profileData)
$profileSentinel = Join-Path $profileData 'preserve-settings-and-recordings.bin'
[IO.File]::WriteAllBytes($profileSentinel, [Security.Cryptography.RandomNumberGenerator]::GetBytes(128))
$sentinelHash = (Get-FileHash -LiteralPath $profileSentinel).Hash
$checks = [Collections.Generic.List[object]]::new()
$reportPath = Join-Path $testRoot 'installer-test.json'
$installedFiles = @(Get-ChildItem -LiteralPath (Join-Path $build.BuildDirectory 'payload\Temhalo') -Recurse -File)

function Assert-Check([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "Échec : $Name" }
    $checks.Add([PSCustomObject]@{ Name = $Name; Passed = $true })
    Write-Host "OK : $Name"
}
function Invoke-TestExecutable([string]$Executable, [string]$Arguments, [int]$ExpectedExit = 0) {
    $process = Start-Process -FilePath $Executable -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(60000)) { throw "Processus non terminé après 60 secondes : $Executable" }
    if ($process.ExitCode -ne $ExpectedExit) { throw "Code $($process.ExitCode), attendu $ExpectedExit : $Executable" }
}
function Invoke-Setup([int]$ExpectedExit = 0) {
    # NSIS /D= consumes the rest of the command line, including spaces.
    Invoke-TestExecutable $build.Installer "/S /D=$installPath" $ExpectedExit
}
function Invoke-Uninstall([int]$ExpectedExit = 0) {
    # Run a separate copy so ExecWait waits for the actual uninstaller, and the
    # installed Uninstall.exe can be removed. _?= must be last, without quotes.
    $runner = Join-Path $testRoot ('Uninstall-runner-' + [Guid]::NewGuid().ToString('N') + '.exe')
    Copy-Item -LiteralPath (Join-Path $installPath 'Uninstall.exe') -Destination $runner
    Invoke-TestExecutable $runner "/S _?=$installPath" $ExpectedExit
}
function Assert-Payload {
    foreach ($file in $installedFiles) {
        $relative = [IO.Path]::GetRelativePath((Join-Path $build.BuildDirectory 'payload\Temhalo'), $file.FullName)
        $installed = Join-Path $installPath $relative
        if (-not (Test-Path -LiteralPath $installed -PathType Leaf) -or (Get-FileHash -LiteralPath $installed).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) { throw "Fichier différent/absent : $relative" }
    }
    Assert-Check $true "$($installedFiles.Count) fichiers installés conformes par SHA-256"
}
function Assert-Profile {
    Assert-Check ((Get-FileHash -LiteralPath $profileSentinel).Hash -eq $sentinelHash) 'Données du profil conservées'
}

try {
    Assert-Check (-not (Test-Path -LiteralPath $registryPath)) 'Identité de test absente avant installation'
    Invoke-Setup
    Assert-Payload
    $registration = Get-ItemProperty -LiteralPath $registryPath
    Assert-Check ($registration.InstallLocation -eq $installPath -and $registration.DisplayVersion -eq $build.Version) 'Enregistrement HKCU et version corrects'
    Assert-Check ($registration.UninstallString -eq ('"' + $installPath + '\Uninstall.exe"')) 'Commande de désinstallation citée correctement'
    $shell = New-Object -ComObject WScript.Shell
    foreach ($shortcut in @((Join-Path $testRoot 'Desktop\Temhalo.lnk'), (Join-Path $testRoot 'Start Menu\Temhalo\Temhalo.lnk'))) {
        $link = $shell.CreateShortcut($shortcut)
        Assert-Check ($link.TargetPath -eq (Join-Path $installPath 'Temhalo.App.exe') -and $link.WorkingDirectory -eq $installPath) 'Raccourci cible et dossier de travail corrects'
        Assert-Check ($link.IconLocation -eq ((Join-Path $installPath 'Assets\Brand\temhalo.ico') + ',0')) 'Icône Temhalo du raccourci'
        Assert-Check ($link.Arguments -eq ('--data-directory "' + $profileData + '"')) 'Profil du raccourci isolé du profil personnel'
    }
    Assert-Check (Test-Path -LiteralPath (Join-Path $testRoot 'Start Menu\Temhalo\Désinstaller Temhalo.lnk')) 'Entrée de désinstallation dans le menu Démarrer'
    Assert-Profile

    # The updater's parent is still exiting when the installer starts. Wait for
    # that process before replacement, using an isolated helper, never the live app.
    $waitParent = Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList '-NoProfile -Command "Start-Sleep -Seconds 4"' -WindowStyle Hidden -PassThru
    $waitTimer = [Diagnostics.Stopwatch]::StartNew()
    Invoke-TestExecutable $build.Installer "/S /WAITPID=$($waitParent.Id) /D=$installPath"
    $waitTimer.Stop()
    Assert-Check ($waitParent.HasExited -and $waitTimer.Elapsed.TotalSeconds -ge 3) 'Mise à jour : attente de la fermeture du processus parent'
    Assert-Payload
    Assert-Profile

    $foreignFile = Join-Path $installPath 'document-personnel-conserve.txt'
    'Donnée de test ajoutée par un utilisateur.' | Set-Content -LiteralPath $foreignFile -Encoding UTF8
    $foreignHash = (Get-FileHash -LiteralPath $foreignFile).Hash
    $obsoleteFile = Join-Path $installPath 'obsolete-validation.dll'
    'Known retired payload fixture.' | Set-Content -LiteralPath $obsoleteFile
    $binary = Join-Path $installPath 'Temhalo.App.exe'
    $beforeLockHash = (Get-FileHash -LiteralPath $binary).Hash
    $handle = [IO.File]::Open($binary, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        Invoke-Setup 32
        Assert-Check ((Get-FileHash -LiteralPath $binary).Hash -eq $beforeLockHash) 'Mise à jour refusée sans modification lorsque le fichier est utilisé'
        Invoke-Uninstall 32
        Assert-Check (Test-Path -LiteralPath $registryPath) 'Désinstallation refusée et enregistrement conservé lorsque le fichier est utilisé'
    }
    finally { $handle.Dispose() }
    Assert-Payload

    # A same-version update repairs a missing XAML resource without touching data.
    $missingResource = [IO.Path]::GetFullPath((Join-Path $installPath 'MainWindow.xbf'))
    if (-not $missingResource.StartsWith($installPath + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Chemin de test hors installation.' }
    Remove-Item -LiteralPath $missingResource
    Invoke-Setup
    Assert-Payload
    Assert-Check (-not (Test-Path -LiteralPath $obsoleteFile)) 'Fichier connu devenu obsolète retiré à la mise à jour'
    Assert-Check ((Get-FileHash -LiteralPath $foreignFile).Hash -eq $foreignHash) 'Fichier personnel conservé lors de la réparation'
    Assert-Profile

    # Compare numeric versions using the real installer without a second payload.
    Set-ItemProperty -LiteralPath $registryPath -Name NumericVersion -Value '99.0.0.0'
    Invoke-Setup 1638
    Assert-Payload
    Set-ItemProperty -LiteralPath $registryPath -Name NumericVersion -Value $build.NumericVersion
    Assert-Check $true "Installation d'une version antérieure refusée"

    Invoke-Uninstall
    Assert-Check (-not (Test-Path -LiteralPath $registryPath)) 'Entrée des applications installées retirée'
    Assert-Check (-not (Test-Path -LiteralPath (Join-Path $testRoot 'Desktop\Temhalo.lnk'))) 'Raccourci Bureau retiré'
    Assert-Check (-not (Test-Path -LiteralPath (Join-Path $testRoot 'Start Menu\Temhalo'))) 'Dossier Temhalo du menu Démarrer retiré'
    $remaining = @(Get-ChildItem -LiteralPath $installPath -Recurse -File)
    Assert-Check ($remaining.Count -eq 1 -and $remaining[0].FullName -eq $foreignFile) 'Seul le fichier personnel ajouté reste dans le dossier'
    Assert-Profile
    Assert-Check ((Get-FileHash -LiteralPath $foreignFile).Hash -eq $foreignHash) 'Fichier personnel conservé lors de la désinstallation'
    # Remove only our sentinel, after validating the absolute test boundary.
    if (-not $foreignFile.StartsWith($testRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Chemin de test hors dossier autorisé.' }
    Remove-Item -LiteralPath $foreignFile
    [IO.Directory]::Delete($installPath, $false)
    Invoke-Setup
    Assert-Payload
    Assert-Profile
    if (-not $KeepInstalledForUi) {
        Invoke-Uninstall
        Assert-Check (-not (Test-Path -LiteralPath $installPath)) "Dossier d'installation vide entièrement retiré"
        Assert-Profile
    }
    $status = 'passed'
}
catch { $status = 'failed'; $failure = $_.Exception.Message; throw }
finally {
    [PSCustomObject]@{
        Status = $status; At = [DateTimeOffset]::Now.ToString('o'); Checks = $checks.ToArray()
        Build = $build; InstallDirectory = $installPath; ProfileData = $profileData
        KeptInstalledForUi = [bool]$KeepInstalledForUi; CleanWindowsVmTested = $false
        Failure = if (Get-Variable failure -ErrorAction SilentlyContinue) { $failure } else { $null }
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    Write-Host "Rapport : $reportPath"
}

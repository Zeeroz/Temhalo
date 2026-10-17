[CmdletBinding()]
param(
    [ValidatePattern('^[0-9]{3,4}x[0-9]{3,4}$')][string]$WindowSize = '1280x820',
    [string]$SettingsDirectory = '',
    [switch]$ValidationSession,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$dimensions = $WindowSize.Split('x')
if ([int]$dimensions[0] -lt 320 -or [int]$dimensions[0] -gt 3840 -or [int]$dimensions[1] -lt 480 -or [int]$dimensions[1] -gt 2160) {
    throw 'Dimensions attendues : largeur de 320 à 3840, hauteur de 480 à 2160.'
}
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
# A local development profile pins the physical directory when the launching host
# virtualizes LocalAppData. An explicit -SettingsDirectory always takes precedence.
$launchProfilePath = Join-Path $repoRoot 'config\temhalo.launch.local.json'
if (-not $SettingsDirectory -and (Test-Path -LiteralPath $launchProfilePath -PathType Leaf)) {
    $launchProfile = Get-Content -LiteralPath $launchProfilePath -Raw | ConvertFrom-Json
    if ($launchProfile.Version -ne 1 -or -not ($launchProfile.SettingsDirectory -is [string]) -or
        -not [IO.Path]::IsPathFullyQualified($launchProfile.SettingsDirectory) -or
        -not (Test-Path -LiteralPath $launchProfile.SettingsDirectory -PathType Container)) {
        throw 'Le dossier du profil local de lancement est invalide.'
    }
    $SettingsDirectory = $launchProfile.SettingsDirectory
}
$exe = Join-Path $repoRoot "src\Temhalo.App\bin\x64\$Configuration\net8.0-windows10.0.26100.0\win-x64\Temhalo.App.exe"
if (-not (Test-Path -LiteralPath $exe)) { throw "Compilez la solution $Configuration x64 avant de lancer Temhalo." }
if ($ValidationSession) {
    if (-not $SettingsDirectory) { throw 'Une validation exige un dossier de reglages explicite.' }
    # The desktop inspector resolves installed applications by executable name.
    # A test-owned apphost avoids selecting the user's installed Temhalo window.
    $validationExe = Join-Path (Split-Path $exe) ('Temhalo.Validation-' + [guid]::NewGuid().ToString('N') + '.exe')
    Copy-Item -LiteralPath $exe -Destination $validationExe
    Copy-Item -LiteralPath ([IO.Path]::ChangeExtension($exe, '.pri')) -Destination ([IO.Path]::ChangeExtension($validationExe, '.pri'))
    $exe = $validationExe
}
$appArguments = @('--window-size', $WindowSize)
if ($ValidationSession) { $appArguments += '--validation-session' }
if ($SettingsDirectory) { $appArguments += @('--settings-directory', [IO.Path]::GetFullPath($SettingsDirectory), '--data-directory', [IO.Path]::GetFullPath($SettingsDirectory)) }
& $exe @appArguments

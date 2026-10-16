[CmdletBinding()]
param([ValidateRange(600,7200)][int]$DurationSeconds = 1800, [switch]$SkipSoak, [switch]$SkipBuild)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Push-Location -LiteralPath $repoRoot
try {
    if (-not $SkipBuild) {
        & dotnet restore Temhalo.sln -p:Platform=x64 --locked-mode
        if ($LASTEXITCODE -ne 0) { throw 'Restauration verrouillée échouée.' }
        & dotnet build Temhalo.sln -c Release -p:Platform=x64 --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Compilation Release échouée.' }
    }
    & dotnet test Temhalo.sln -c Release -p:Platform=x64 --no-build --no-restore --logger 'trx;LogFilePrefix=stage6' --results-directory artifacts/validation/stage6
    if ($LASTEXITCODE -ne 0) { throw 'Tests unitaires/HTTP échoués.' }
    if (-not $SkipSoak) {
        Write-Host "Test natif réel de $DurationSeconds secondes, quatre sorties RTMP locales, ports 1935/1936 libres. Ctrl+C pour annuler."
        & dotnet run --project tests/Temhalo.NativeValidation -c Release --no-build -- $repoRoot "--robustness=$DurationSeconds"
        if ($LASTEXITCODE -ne 0) { throw 'Test de robustesse échoué. Consultez artifacts/validation/stage5-*/native.log.' }
    }
}
finally { Pop-Location }

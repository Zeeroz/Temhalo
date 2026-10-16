[CmdletBinding()]
param([switch]$SkipBuild)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Push-Location -LiteralPath $repoRoot
try {
    if (-not $SkipBuild) {
        & dotnet restore Temhalo.sln -p:Platform=x64 --locked-mode
        if ($LASTEXITCODE -ne 0) { throw 'Restauration des comptes échouée.' }
        & dotnet build Temhalo.sln -c Release -p:Platform=x64 --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Compilation des comptes échouée.' }
    }
    & dotnet test Temhalo.sln -c Release -p:Platform=x64 --no-build --no-restore --logger 'trx;LogFilePrefix=accounts' --results-directory artifacts/validation/accounts
    if ($LASTEXITCODE -ne 0) { throw 'Tests des comptes échoués.' }
}
finally { Pop-Location }

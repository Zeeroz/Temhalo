[CmdletBinding()]
param([switch]$SkipBuild, [switch]$SkipNative)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Push-Location -LiteralPath $repoRoot
try {
    if (-not $SkipBuild) {
        & dotnet restore Temhalo.sln -p:Platform=x64 --locked-mode
        if ($LASTEXITCODE -ne 0) { throw 'Restauration verrouillée échouée.' }
        & dotnet build Temhalo.sln -c Debug -p:Platform=x64 --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Compilation échouée. Fermez Temhalo avant de recompiler.' }
    }
    & dotnet test Temhalo.sln -c Debug -p:Platform=x64 --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Un test unitaire ou HTTP a échoué.' }
    if (-not $SkipNative) {
        Write-Host 'Test natif : ports 1935 et 1936 libres, aucune diffusion vers une plateforme publique.'
        & dotnet run --project tests/Temhalo.NativeValidation/Temhalo.NativeValidation.csproj -c Debug --no-build -- $repoRoot --multistream
        if ($LASTEXITCODE -ne 0) { throw 'Le test multistream a échoué ; consulter artifacts/validation/stage5-*/native.log.' }
    }
}
finally { Pop-Location }

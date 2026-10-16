[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
& (Join-Path $PSScriptRoot 'native\Get-NativeTools.ps1') -VerifyOnly
& dotnet run --project (Join-Path $repoRoot 'tests\Temhalo.NativeValidation\Temhalo.NativeValidation.csproj') -c Debug -- $repoRoot
if ($LASTEXITCODE -ne 0) { throw 'La validation native de l''etape 5 a echoue. Consultez artifacts\validation\stage5-*\native.log.' }

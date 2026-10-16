[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('\A[1-9][0-9]{0,18}\z')][string]$RunId,
    [Parameter(Mandatory)][ValidatePattern('\Av[0-9]+\.[0-9]+\.[0-9]+(?:-preview\.[0-9]+)?\z')][string]$Tag
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Get-GitHubJson([string]$Endpoint) {
    $text = & gh api $Endpoint
    if ($LASTEXITCODE) { throw "Lecture GitHub impossible : $Endpoint" }
    return ($text -join "`n") | ConvertFrom-Json
}
$repository = 'repos/Zeeroz/Temhalo'
$run = Get-GitHubJson "$repository/actions/runs/$RunId"
$commit = Get-GitHubJson "$repository/commits/$Tag"
$jobs = Get-GitHubJson "$repository/actions/runs/$RunId/jobs?per_page=100"
$builds = @($jobs.jobs | Where-Object { $_.name -ceq 'build' -and $_.conclusion -ceq 'success' })
if ($run.status -cne 'completed' -or $run.event -cne 'push' -or $run.path -cne '.github/workflows/windows.yml' -or
    $run.repository.full_name -cne 'Zeeroz/Temhalo' -or $run.head_branch -cne $Tag -or
    $run.head_sha -cne $commit.sha -or $builds.Count -ne 1) { throw 'Reprise refusée : la CI et le tag ne correspondent pas à un build validé de Temhalo.' }
$artifacts = Get-GitHubJson "$repository/actions/runs/$RunId/artifacts?per_page=100"
$packages = @($artifacts.artifacts | Where-Object { $_.name -ceq 'Temhalo-windows-x64' -and -not $_.expired })
if ($packages.Count -ne 1) { throw 'Archive de livraison absente, ambiguë ou expirée.' }
Write-Host "Reprise autorisée : $Tag, commit $($commit.sha), build validé, archive $($packages[0].id). Aucun fichier ne sera reconstruit ou écrasé."

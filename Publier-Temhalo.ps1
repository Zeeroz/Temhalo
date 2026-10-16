#Requires -Version 7.0
<#
.SYNOPSIS
Teste et publie une nouvelle version de Temhalo depuis ce depot.
.EXAMPLE
./Publier-Temhalo.ps1 -Version 0.7.0-preview.3
.EXAMPLE
./Publier-Temhalo.ps1 -Version 0.7.0-preview.3 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
param([string]$Version)
Import-Module (Join-Path $PSScriptRoot 'tools/Publishing/Temhalo.Release.psm1') -Force
Invoke-TemhaloPublication @PSBoundParameters

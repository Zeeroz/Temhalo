[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$validationRoot = Join-Path $repoRoot 'artifacts\validation\clean-windows'
$labRoot = Join-Path $validationRoot ('lab-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
$inputDirectory = Join-Path $labRoot 'Input'
$resultsDirectory = Join-Path $labRoot 'Results'
[void][IO.Directory]::CreateDirectory($inputDirectory)
[void][IO.Directory]::CreateDirectory($resultsDirectory)
$baselinePath = Join-Path $repoRoot 'artifacts\packages\Temhalo-0.6.0-preview.1-Setup-x64.exe'
$baseline = Get-Content -LiteralPath ($baselinePath + '.build.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $baselinePath).Hash -ne $baseline.SHA256) { throw 'Installeur de référence altéré.' }
Copy-Item -LiteralPath $baselinePath -Destination $inputDirectory
$upgradeName = 'Temhalo-Validation-Upgrade.exe'
$upgrade = & (Join-Path $PSScriptRoot 'Build-Installer.ps1') -NumericVersion '0.6.0.2' -OutputPath (Join-Path $inputDirectory $upgradeName)
if ([version]$upgrade.NumericVersion -le [version]$baseline.NumericVersion) { throw 'La révision de mise à jour doit être supérieure.' }

Push-Location -LiteralPath $repoRoot
try {
    & dotnet restore tests/Temhalo.CleanWindowsValidation -p:Platform=x64 --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restauration agent echouee.' }
    & dotnet publish tests/Temhalo.CleanWindowsValidation -c Release -p:Platform=x64 --no-restore -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $inputDirectory 'agent')
    if ($LASTEXITCODE -ne 0) { throw 'Publication agent echouee.' }
}
finally { Pop-Location }
$agent = Join-Path $inputDirectory 'agent\Temhalo.CleanWindowsValidation.exe'
Copy-Item -LiteralPath (Join-Path $baseline.BuildDirectory 'payload\Temhalo\Licenses') -Destination (Join-Path $inputDirectory 'agent\Licenses') -Recurse
$selfTest = Start-Process -FilePath $agent -ArgumentList '--self-test' -WindowStyle Hidden -PassThru
if (-not $selfTest.WaitForExit(60000) -or $selfTest.ExitCode -ne 0) { throw 'Autocontrole agent echoue.' }
# Prove that this guest-only agent refuses to run installations on this host.
$guardTest = Start-Process -FilePath $agent -ArgumentList @('--run','invalid-input','invalid-results') -WindowStyle Hidden -PassThru
if (-not $guardTest.WaitForExit(60000) -or $guardTest.ExitCode -ne 90) { throw 'Protection de refus sur le poste de developpement echouee.' }

$lock = Get-Content -LiteralPath (Join-Path $repoRoot 'packaging\windows\clean-validation.lock.json') -Raw | ConvertFrom-Json
$obs = $lock.obs
$cache = Join-Path $repoRoot 'artifacts\toolchains'
[void][IO.Directory]::CreateDirectory($cache)
$obsPath = Join-Path $cache $obs.fileName
if (-not (Test-Path -LiteralPath $obsPath) -or (Get-FileHash -LiteralPath $obsPath).Hash -ne $obs.sha256) {
    $partial = $obsPath + '.partial-' + [Guid]::NewGuid().ToString('N')
    try {
        Invoke-WebRequest -Uri $obs.url -OutFile $partial -TimeoutSec 120
        if ((Get-FileHash -LiteralPath $partial).Hash -ne $obs.sha256 -or (Get-Item -LiteralPath $partial).Length -ne $obs.bytes) { throw 'OBS téléchargé ne correspond pas à la version verrouillée.' }
        Move-Item -LiteralPath $partial -Destination $obsPath -Force
    }
    finally { if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial } }
}
Copy-Item -LiteralPath $obsPath -Destination $inputDirectory

$nativeManifest = Get-Content -LiteralPath (Join-Path $repoRoot 'artifacts\native-tools.resolved.json') -Raw | ConvertFrom-Json
$ffmpeg = ($nativeManifest.Tools | Where-Object Name -eq 'FFmpeg').Executable
$pattern = Join-Path $inputDirectory 'test-pattern.mp4'
Push-Location -LiteralPath (Join-Path $repoRoot 'assets\brand\fonts')
try {
    $filter = "drawtext=fontfile=JetBrainsMono-Variable.ttf:text='TEMHALO %{pts} s':fontsize=42:fontcolor=white:box=1:boxcolor=black@0.8:x=24:y=24"
    # Only this synthetic test source is encoded. Temhalo remains passthrough.
    & $ffmpeg -hide_banner -v error -f lavfi -i 'testsrc2=size=1280x720:rate=30' -f lavfi -i 'sine=frequency=880:sample_rate=48000' -t 60 -vf $filter -c:v h264_mf -hw_encoding 0 -b:v 2500k -g 30 -bf 0 -c:a aac -b:a 128k -movflags +faststart -n $pattern
    if ($LASTEXITCODE -ne 0) { throw 'Création de la mire audio/vidéo échouée.' }
}
finally { Pop-Location }

function Get-PayloadInventory([string]$BuildDirectory) {
    $payload = Join-Path $BuildDirectory 'payload\Temhalo'
    return @(Get-ChildItem -LiteralPath $payload -File -Recurse | Sort-Object FullName | ForEach-Object {
        [PSCustomObject]@{ Path=[IO.Path]::GetRelativePath($payload,$_.FullName); Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant() }
    })
}
$artifacts = @(Get-ChildItem -LiteralPath $inputDirectory -File | Where-Object { $_.Extension -in '.exe','.mp4' } | ForEach-Object {
    [PSCustomObject]@{Name=$_.Name; Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}
})
$initialFiles = @(Get-PayloadInventory $baseline.BuildDirectory)
$upgradeFiles = @(Get-PayloadInventory $upgrade.BuildDirectory)
if ($initialFiles.Count -ne $upgradeFiles.Count) { throw 'Les deux révisions doivent contenir le même payload applicatif.' }
foreach ($file in $initialFiles) {
    $updated = @($upgradeFiles | Where-Object Path -eq $file.Path)
    if ($updated.Count -ne 1 -or $updated[0].Sha256 -ne $file.Sha256) { throw 'Le payload de validation de mise à jour diffère du payload approuvé.' }
}
$plan = [PSCustomObject]@{
    SourceHost=[Environment]::MachineName; Setup=[IO.Path]::GetFileName($baselinePath); UpgradeSetup=$upgradeName
    ObsSetup=$obs.fileName; InitialNumericVersion=$baseline.NumericVersion; UpgradeNumericVersion=$upgrade.NumericVersion
    Artifacts=$artifacts; InitialFiles=$initialFiles; UpgradeFiles=$upgradeFiles
}
$plan | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $inputDirectory 'plan.json') -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\VALIDATION-WINDOWS-VIERGE.md') -Destination (Join-Path $inputDirectory 'LISEZ-MOI.md')
# Copyable test value inside the guest; host clipboard redirection stays disabled.
[IO.File]::WriteAllText((Join-Path $inputDirectory 'OBS-local.url'), "[InternetShortcut]`r`nURL=rtmp://127.0.0.1:1935/live`r`n")
$escapedInput = [Security.SecurityElement]::Escape($inputDirectory)
$escapedResults = [Security.SecurityElement]::Escape($resultsDirectory)
$configuration = @"
<Configuration>
  <VGpu>Enable</VGpu>
  <Networking>Enable</Networking>
  <AudioInput>Disable</AudioInput>
  <VideoInput>Disable</VideoInput>
  <ClipboardRedirection>Disable</ClipboardRedirection>
  <MemoryInMB>8192</MemoryInMB>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>$escapedInput</HostFolder>
      <SandboxFolder>C:\TemhaloValidation\Input</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$escapedResults</HostFolder>
      <SandboxFolder>C:\TemhaloValidation\Results</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>C:\TemhaloValidation\Input\agent\Temhalo.CleanWindowsValidation.exe --run C:\TemhaloValidation\Input C:\TemhaloValidation\Results</Command>
  </LogonCommand>
</Configuration>
"@
$sandboxPath = Join-Path $labRoot 'Temhalo-Windows-Vierge.wsb'
$configuration | Set-Content -LiteralPath $sandboxPath -Encoding UTF8
# Parse the generated configuration; this does not start or alter the sandbox.
[xml]$checkedConfiguration = Get-Content -LiteralPath $sandboxPath -Raw
if ($checkedConfiguration.Configuration.MappedFolders.MappedFolder.Count -ne 2) { throw 'Configuration Sandbox incomplète.' }
$report = [PSCustomObject]@{
    Status='prepared-awaiting-clean-guest'; LabRoot=$labRoot; SandboxFile=$sandboxPath
    InputDirectory=$inputDirectory; ResultsDirectory=$resultsDirectory
    Created=[DateTimeOffset]::Now.ToString('o'); HostRestartPerformed=$false
    GuestTestsExecuted=$false; AgentSelfTestPassed=$true; HostExecutionRefusalVerified=$true
    OriginalInstallerSHA256=$baseline.SHA256; UpgradeInstallerSHA256=$upgrade.SHA256
    OriginalNumericVersion=$baseline.NumericVersion; UpgradeNumericVersion=$upgrade.NumericVersion
    UpgradeAppPayloadUnchanged=$true; ObsVersion=$obs.version; ObsSHA256=$obs.sha256
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $labRoot 'preparation.json') -Encoding UTF8
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $validationRoot 'latest-lab.json') -Encoding UTF8
Write-Output $report

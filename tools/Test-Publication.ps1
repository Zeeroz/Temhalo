#Requires -Version 7.0
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$modulePath = Join-Path $PSScriptRoot 'Publishing/Temhalo.Release.psm1'
$testRoot = Join-Path $repoRoot ('artifacts/validation/publication-tests/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($testRoot)
$checks = [Collections.Generic.List[object]]::new()
function Assert-Check([bool]$Value, [string]$Name) {
    if (-not $Value) { throw "Echec du test : $Name" }
    $checks.Add([PSCustomObject]@{ Name = $Name; Passed = $true })
    Write-Host "OK : $Name"
}

$module = Import-Module $modulePath -Force -PassThru
$numeric = & $module { ConvertTo-ReleaseVersion '0.7.0-preview.10' }
Assert-Check ($numeric -eq [version]'0.7.0.10') 'Conversion numerique de la version'
$stable = & $module { ConvertTo-ReleaseVersion '0.7.0' }
Assert-Check ($stable -gt $numeric) 'Une stable suit les previews de meme version'
foreach ($invalid in @('01.2.3','v1.2.3','1.2.3-beta.1','1.2.3-preview.65535','65536.0.0','1.2.3; git push',"1.2.3`n")) {
    $rejected = $false
    try { & $module { param($value) ConvertTo-ReleaseVersion $value } $invalid | Out-Null } catch { $rejected = $true }
    Assert-Check $rejected "Version invalide refusee : $($invalid.Replace("`n",'LF'))"
}

# The full orchestration uses in-memory CLI doubles and a temporary project.
# No test can run a real build, Git commit/push, or GitHub publication.
function Invoke-Scenario([hashtable]$Options = @{}, [switch]$WhatIfMode) {
    $fixture = Join-Path $testRoot ([guid]::NewGuid().ToString('N') + ' avec espaces')
    [void][IO.Directory]::CreateDirectory($fixture)
    $initialVersion = if ($Options.ContainsKey('InitialVersion')) { $Options.InitialVersion } else { '0.7.0-preview.1' }
    [IO.File]::WriteAllText((Join-Path $fixture 'Directory.Build.props'), "<Project><PropertyGroup><Version>$initialVersion</Version></PropertyGroup></Project>")
    $isolated = Import-Module $modulePath -Force -PassThru
    return & $isolated {
        param($fixture, $options, $whatIfMode)
        $script:Scenario = @{
            Root=$fixture; Branch='main'; Origin='https://github.com/Zeeroz/Temhalo.git'; Head=('a'*40)
            TagExists=$false; TagCommit=('a'*40); RemoteTag=$false; Dirty=$true; Staged=$false
            Committed=$false; Pushed=$false; Dispatched=$false; Changes=$true; FailNative=''; FailScript=''
            RemoteAhead=$false; CiBuildFailure=$false; CiReleaseFailure=$false; PublicInvalid=$false
            Commands=[Collections.Generic.List[string]]::new(); Scripts=[Collections.Generic.List[string]]::new()
        }
        foreach ($entry in $options.GetEnumerator()) { $script:Scenario[$entry.Key] = $entry.Value }
        function script:Get-ReleaseRoot { return $script:Scenario.Root }
        function script:Resolve-ReleaseTool([string]$Name, [string]$Root) { return $Name }
        function script:Read-Host { return '0.7.0-preview.2' }
        function script:Start-Sleep { param([int]$Seconds) }
        function script:Invoke-ReleaseScript([string]$Root,[string]$Name,[hashtable]$Parameters=@{}) {
            $script:Scenario.Scripts.Add($Name)
            if ($script:Scenario.FailScript -ceq $Name) { throw 'Erreur de script simulee.' }
            if ($Name -ceq 'Test-Installer.ps1' -and $script:Scenario.ContainsKey('BranchAfterTests')) { $script:Scenario.Branch=$script:Scenario.BranchAfterTests }
            if ($Name -ceq 'Test-Installer.ps1' -and $script:Scenario.ContainsKey('OriginAfterTests')) { $script:Scenario.Origin=$script:Scenario.OriginAfterTests }
        }
        function script:New-FakeRelease([string]$Version) {
            $files = @("Temhalo-$Version-Setup-x64.exe", "Temhalo-$Version-win-x64.zip")
            $files += @($files | ForEach-Object { $_+'.sha256' })
            return @{tag_name="v$Version";draft=$false;prerelease=$Version.Contains('-preview.');html_url="https://github.com/Zeeroz/Temhalo/releases/tag/v$Version";
                assets=@($files | ForEach-Object { @{name=$_;state='uploaded';size=1024;digest=('sha256:'+('a'*64));browser_download_url="https://github.com/Zeeroz/Temhalo/releases/download/v$Version/$_"} })}
        }
        function script:Invoke-ReleaseNative {
            param([string]$File,[string[]]$Arguments,[switch]$Capture,[switch]$AllowFailure)
            $key = "$File $($Arguments -join ' ')"
            $state = $script:Scenario
            $state.Commands.Add($key)
            if ($state.FailNative -and $key.StartsWith($state.FailNative,[StringComparison]::Ordinal)) { throw 'Erreur de commande simulee.' }
            $output = ''; $code = 0
            if ($File -ceq 'git') {
                switch -Regex ($Arguments -join ' ') {
                    '^rev-parse --show-toplevel$' { $output=$state.Root; break }
                    '^branch --show-current$' { $output=$state.Branch; break }
                    '^remote get-url origin$' { $output=$state.Origin; break }
                    '^rev-parse HEAD$' { $output=$state.Head; break }
                    '^status ' { if ($state.Dirty) { $output=' M src/example.cs' }; break }
                    '^ls-remote --tags origin$' { if($state.RemoteTag) { $output=$state.TagCommit+"`trefs/tags/v0.7.0-preview.2" }; break }
                    '^show-ref ' { if(-not $state.TagExists) { $code=1 }; break }
                    '^rev-list ' { $output=$state.TagCommit; break }
                    '^merge-base ' { if($state.RemoteAhead) { $code=1 }; break }
                    '^add ' { $state.Staged=$true; break }
                    '^diff --cached --quiet$' { if($state.Changes) { $code=1 }; break }
                    '^commit ' { $state.Committed=$true; $state.Head='b'*40; $state.Dirty=$false; break }
                    '^tag ' { $state.TagExists=$true; $state.TagCommit=$state.Head; break }
                    '^push ' { $state.Pushed=$true; $state.RemoteTag=$true; break }
                }
            }
            elseif ($File -ceq 'gh') {
                switch -Regex ($Arguments -join ' ') {
                    '^api repos/Zeeroz/Temhalo$' { $output=@{permissions=@{push=$true}} | ConvertTo-Json -Compress; break }
                    '^api .*actions/workflows/windows.yml$' { $output=@{state='active'} | ConvertTo-Json -Compress; break }
                    '^api .*releases\?per_page=100$' { $output=ConvertTo-Json -InputObject @((New-FakeRelease '0.7.0-preview.1')) -Depth 5 -Compress; break }
                    '^api .*commits/main$' { $output=@{sha=$state.Head} | ConvertTo-Json -Compress; break }
                    '^run list ' {
                        $isRecovery = $key.Contains('workflow_dispatch')
                        $id = if($isRecovery) { 456 } else { 123 }
                        $output=ConvertTo-Json -InputObject @(@{databaseId=$id;headSha=$state.Head;headBranch='v0.7.0-preview.2';createdAt=[DateTimeOffset]::UtcNow.ToString('o')}) -Compress
                        break
                    }
                    '^run view ' {
                        $recovery = $Arguments[2] -ceq '456'
                        $failed = -not $recovery -and ($state.CiBuildFailure -or $state.CiReleaseFailure)
                        $buildResult=if($state.CiBuildFailure) {'failure'} else {'success'}
                        $releaseResult=if($state.CiReleaseFailure) {'failure'} else {'success'}
                        $output=@{conclusion=$(if($failed){'failure'}else{'success'});status='completed';url='https://github.com/Zeeroz/Temhalo/actions/runs/123';
                            jobs=@(@{name='build';conclusion=$buildResult},@{name='release';conclusion=$releaseResult})} | ConvertTo-Json -Depth 5 -Compress
                        break
                    }
                    '^workflow run ' { $state.Dispatched=$true; break }
                    '^api .*releases/tags/' {
                        $release=New-FakeRelease '0.7.0-preview.2'
                        if($state.PublicInvalid) { $release.assets[0].digest='incorrect' }
                        $output=$release | ConvertTo-Json -Depth 5 -Compress
                        break
                    }
                }
            }
            return [PSCustomObject]@{ ExitCode=$code; Output=$output }
        }
        $failure=$null
        try {
            if($whatIfMode) { Invoke-TemhaloPublication -Version '0.7.0-preview.2' -WhatIf 6>$null }
            else { Invoke-TemhaloPublication -Version '0.7.0-preview.2' 6>$null }
        }
        catch { $failure=$_.Exception.Message }
        return [PSCustomObject]@{ State=$script:Scenario; Failure=$failure; Props=[IO.File]::ReadAllText((Join-Path $fixture 'Directory.Build.props')) }
    } $fixture $Options ([bool]$WhatIfMode)
}

$result=Invoke-Scenario
Assert-Check ($null -eq $result.Failure -and $result.State.Pushed -and $result.State.Committed) 'Parcours complet : tests puis commit, tag, push et verification'
Assert-Check ($result.Props.Contains('<Version>0.7.0-preview.2</Version>')) 'Version mise a jour automatiquement'
Assert-Check ($result.State.Commands.Contains('git push --atomic origin HEAD:refs/heads/main refs/tags/v0.7.0-preview.2:refs/tags/v0.7.0-preview.2')) 'Push atomique sans force'
Assert-Check ($result.State.Scripts.IndexOf('Test-Installer.ps1') -lt $result.State.Scripts.LastIndexOf('Test-PublicSource.ps1')) 'Controle de installation avant audit final et publication'

$result=Invoke-Scenario -WhatIfMode
Assert-Check ($null -eq $result.Failure -and -not $result.State.Pushed -and -not $result.State.Staged -and
    $result.Props.Contains('0.7.0-preview.1') -and -not @($result.State.Commands | Where-Object { $_ -match '^git (fetch|commit|tag|push|add) ' }).Count) 'WhatIf ne modifie ni version, index, tags ou depot'

foreach ($prefix in @('dotnet restore','dotnet build','dotnet test','git push')) {
    $result=Invoke-Scenario @{FailNative=$prefix}
    Assert-Check ($null -ne $result.Failure -and -not $result.State.Pushed) "Echec bloquant : $prefix"
    if($prefix -like 'dotnet*') { Assert-Check (-not $result.State.Committed -and -not $result.State.TagExists) 'Aucun commit ou tag apres un echec des tests' }
}
foreach ($scriptName in @('Package-Windows.ps1','Test-WindowsPackage.ps1','Build-Installer.ps1','Test-Installer.ps1')) {
    $result=Invoke-Scenario @{FailScript=$scriptName}
    Assert-Check ($null -ne $result.Failure -and -not $result.State.Committed -and -not $result.State.Pushed) "Echec bloquant : $scriptName"
}
foreach($options in @(@{Origin='https://github.com/another/repo.git'},@{Branch='feature'},@{RemoteAhead=$true},@{TagExists=$true;TagCommit=('c'*40)})) {
    $result=Invoke-Scenario $options
    Assert-Check ($null -ne $result.Failure -and -not $result.State.Pushed) 'Depot, branche, avance distante ou conflit de tag refuses'
}
foreach($options in @(@{BranchAfterTests='another-branch'},@{OriginAfterTests='https://github.com/another/repo.git'})) {
    $result=Invoke-Scenario $options
    Assert-Check ($null -ne $result.Failure -and -not $result.State.Committed -and -not $result.State.Pushed) 'Changement de branche ou de remote pendant les tests bloque avant le commit'
}
$result=Invoke-Scenario @{RemoteTag=$true;TagExists=$true;Dirty=$false;InitialVersion='0.7.0-preview.2'}
Assert-Check ($null -eq $result.Failure -and -not $result.State.Committed -and -not $result.State.Scripts.Contains('Package-Windows.ps1')) 'Reprise du tag distant sans reconstruire ou deplacer les fichiers'
$result=Invoke-Scenario @{CiBuildFailure=$true}
Assert-Check ($null -ne $result.Failure -and -not $result.State.Dispatched) 'Un build GitHub en echec ne declenche pas une reprise de publication'
$result=Invoke-Scenario @{CiReleaseFailure=$true}
Assert-Check ($null -eq $result.Failure -and $result.State.Dispatched) 'Une publication interrompue utilise la reprise verifiee'
$result=Invoke-Scenario @{PublicInvalid=$true}
Assert-Check ($null -ne $result.Failure) 'Une release sans empreinte valide ne peut pas etre annoncee comme publiee'

$module=Import-Module $modulePath -Force -PassThru
$backupRoot=Join-Path $testRoot 'backup-files'
[void][IO.Directory]::CreateDirectory((Join-Path $backupRoot 'artifacts/packages'))
$oldFile=Join-Path $backupRoot 'artifacts/packages/Temhalo-0.7.0-preview.2-Setup-x64.exe'
[IO.File]::WriteAllText($oldFile,'fake-preserved-binary')
& $module { param($root) Save-PreviousReleaseFiles $root '0.7.0-preview.2' } $backupRoot 6>$null
$saved=@(Get-ChildItem -LiteralPath (Join-Path $backupRoot 'artifacts/publications') -Recurse -File)
Assert-Check ($saved.Count -eq 1 -and [IO.File]::ReadAllText($saved[0].FullName) -ceq 'fake-preserved-binary') 'Les anciens livrables locaux sont conserves exactement'

$nativeFailure=$false
try { & $module { param($pwsh) Invoke-ReleaseNative $pwsh @('-NoProfile','-NonInteractive','-Command','exit 9') -Capture } (Join-Path $PSHOME 'pwsh.exe') | Out-Null }
catch { $nativeFailure=$true }
Assert-Check $nativeFailure 'Un vrai processus avec code non nul interrompt le script'
$allowed=& $module { param($pwsh) Invoke-ReleaseNative $pwsh @('-NoProfile','-NonInteractive','-Command','exit 9') -Capture -AllowFailure } (Join-Path $PSHOME 'pwsh.exe')
Assert-Check ($allowed.ExitCode -eq 9) 'Les codes attendus peuvent etre inspectes sans etre masques'

# Exercise the actual audit against a separate local Git index, including files
# that have not been staged yet. The canary is generated only in the ignored fixture.
$auditRoot=Join-Path $testRoot 'audit-fixture'
[void][IO.Directory]::CreateDirectory((Join-Path $auditRoot 'tools'))
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Test-PublicSource.ps1') -Destination (Join-Path $auditRoot 'tools/Test-PublicSource.ps1')
& git -C $auditRoot init -q
if ($LASTEXITCODE) { throw 'Initialisation Git de test impossible.' }
$trackedPath=Join-Path $auditRoot 'tracked.txt'
[IO.File]::WriteAllText($trackedPath,'public fixture')
& git -C $auditRoot add -- tracked.txt
if ($LASTEXITCODE) { throw 'Index Git de test impossible.' }
$canaryPath=Join-Path $auditRoot 'untracked.txt'
[IO.File]::WriteAllText($canaryPath,('ghp_'+('x'*40)))
$secretBlocked=$false
try { & (Join-Path $auditRoot 'tools/Test-PublicSource.ps1') -IncludeUntracked 6>$null }
catch { $secretBlocked=$_.Exception.Message -like 'Identifiant prive*' -or $_.Exception.Message -like 'Identifiant privé*' }
Assert-Check $secretBlocked 'Un identifiant prive dans un fichier non indexe est bloque avant les tests'
[IO.File]::Delete($canaryPath)
[IO.File]::Delete($trackedPath)
& (Join-Path $auditRoot 'tools/Test-PublicSource.ps1') -IncludeUntracked 6>$null
Assert-Check $true 'Audit initial compatible avec une suppression non encore indexee'

$report=[PSCustomObject]@{Status='passed';Tests=$checks.Count;Checks=$checks.ToArray();RealPublicationPerformed=$false}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $testRoot 'result.json') -Encoding UTF8
Write-Host "$($checks.Count) tests de publication reussis. Aucun push ni publication reels."

#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:Repository = 'Zeeroz/Temhalo'

function Get-ReleaseRoot { [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')) }

function ConvertTo-ReleaseVersion([string]$Value) {
    $match = [regex]::Match($Value, '\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-preview\.(0|[1-9][0-9]*))?\z')
    if (-not $match.Success) { throw "Version invalide : utilisez 0.7.0-preview.2 ou 0.7.0, sans le prefixe v." }
    $parts = [int[]]::new(4)
    for ($index = 1; $index -le 4; $index++) {
        $component = 0
        if ($match.Groups[$index].Success -and
            (-not [int]::TryParse($match.Groups[$index].Value, [ref]$component) -or $component -gt 65535)) { throw 'Composante de version trop grande.' }
        $parts[($index - 1)] = $component
    }
    if ($match.Groups[4].Success -and $parts[3] -eq 65535) { throw 'Le numero de preview doit rester inferieur a 65535.' }
    $revision = if ($match.Groups[4].Success) { $parts[3] } else { 65535 }
    return [version]::new($parts[0], $parts[1], $parts[2], $revision)
}

function Resolve-ReleaseTool([string]$Name, [string]$Root) {
    $command = Get-Command $Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { return $command.Source }
    if ($Name -eq 'gh') {
        $bundled = Join-Path $Root 'artifacts/tools/github-cli/bin/gh.exe'
        if (Test-Path -LiteralPath $bundled -PathType Leaf) { return $bundled }
    }
    throw "Outil absent : $Name. Installez Git, GitHub CLI et le SDK de global.json avant la premiere publication."
}

function Invoke-ReleaseNative {
    param([string]$File, [string[]]$Arguments, [switch]$Capture, [switch]$AllowFailure)
    # Explicit exit checks also work when the caller enables native error promotion.
    $PSNativeCommandUseErrorActionPreference = $false
    if ($Capture) { $lines = @(& $File @Arguments); $code = $LASTEXITCODE }
    else { & $File @Arguments | Out-Host; $code = $LASTEXITCODE; $lines = @() }
    if ($code -ne 0 -and -not $AllowFailure) { throw "Echec de $([IO.Path]::GetFileName($File)) (code $code). Publication interrompue." }
    return [PSCustomObject]@{ ExitCode = $code; Output = ($lines -join "`n") }
}

function Invoke-ReleaseScript([string]$Root, [string]$Name, [hashtable]$Parameters = @{}) {
    & (Join-Path $Root "tools/$Name") @Parameters | Out-Host
}

function Invoke-ReleaseJson([string]$Gh, [string[]]$Arguments) {
    $response = Invoke-ReleaseNative $Gh $Arguments -Capture
    return $response.Output | ConvertFrom-Json
}

function Assert-ReleaseGitContext([string]$Git) {
    $branch = (Invoke-ReleaseNative $Git @('branch','--show-current') -Capture).Output
    $origin = (Invoke-ReleaseNative $Git @('remote','get-url','origin') -Capture).Output
    if ($branch -cne 'main' -or $origin -notmatch '\A(?:https://github\.com/Zeeroz/Temhalo(?:\.git)?|git@github\.com:Zeeroz/Temhalo(?:\.git)?)\z') {
        throw 'La branche ou le remote ont change pendant la publication. Aucun push automatique ne sera effectue.'
    }
}

function Save-PreviousReleaseFiles([string]$Root, [string]$Version) {
    $packages = [IO.Path]::GetFullPath((Join-Path $Root 'artifacts/packages'))
    $names = @("Temhalo-$Version-win-x64.zip", "Temhalo-$Version-win-x64.zip.sha256",
        "Temhalo-$Version-Setup-x64.exe", "Temhalo-$Version-Setup-x64.exe.sha256", "Temhalo-$Version-Setup-x64.exe.build.json")
    $existing = @($names | Where-Object { Test-Path -LiteralPath (Join-Path $packages $_) -PathType Leaf })
    if (-not $existing.Count) { return }
    $backup = [IO.Path]::GetFullPath((Join-Path $Root ("artifacts/publications/$Version/essai-" + [guid]::NewGuid().ToString('N'))))
    $boundary = [IO.Path]::GetFullPath((Join-Path $Root 'artifacts')) + [IO.Path]::DirectorySeparatorChar
    if (-not $backup.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Dossier de sauvegarde hors du projet.' }
    [void][IO.Directory]::CreateDirectory($backup)
    foreach ($name in $existing) {
        $source = [IO.Path]::GetFullPath((Join-Path $packages $name))
        $destination = [IO.Path]::GetFullPath((Join-Path $backup $name))
        if (-not $source.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase) -or
            -not $destination.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Chemin de livraison hors du projet.' }
        Move-Item -LiteralPath $source -Destination $destination
    }
    Write-Host "Essai local precedent conserve dans $backup"
}

function Find-ReleaseRun([string]$Gh, [string]$Tag, [string]$Commit, [string]$Event = 'push', [DateTimeOffset]$NotBefore = [DateTimeOffset]::MinValue) {
    for ($attempt = 0; $attempt -lt 45; $attempt++) {
        $arguments = @('run','list','--repo',$script:Repository,'--workflow','windows.yml','--event',$Event,
            '--commit',$Commit,'--limit','30','--json','databaseId,status,conclusion,headSha,headBranch,url,createdAt')
        if ($Event -eq 'push') { $arguments += '--branch', $Tag }
        $runs = @(Invoke-ReleaseJson $Gh $arguments)
        $found = @($runs | Where-Object { $_.headSha -ceq $Commit -and ($Event -ne 'push' -or $_.headBranch -ceq $Tag) -and
            [DateTimeOffset]$_.createdAt -ge $NotBefore } | Sort-Object databaseId -Descending)
        if ($found.Count) { return $found[0] }
        Start-Sleep -Seconds 2
    }
    throw "Workflow introuvable. Consultez https://github.com/$script:Repository/actions puis relancez la meme version."
}

function Wait-ReleaseRun([string]$Gh, [long]$RunId) {
    Write-Host "Suivi : https://github.com/$script:Repository/actions/runs/$RunId"
    [void](Invoke-ReleaseNative $Gh @('run','watch',"$RunId",'--repo',$script:Repository,'--interval','30','--exit-status') -AllowFailure)
    return Invoke-ReleaseJson $Gh @('run','view',"$RunId",'--repo',$script:Repository,'--json','status,conclusion,jobs,url')
}

function Assert-PublicRelease($Release, [string]$Version) {
    if ($Release.draft -or $Release.tag_name -cne "v$Version" -or $Release.prerelease -ne $Version.Contains('-preview.')) { throw 'Release non publiee ou canal incorrect.' }
    $expected = @("Temhalo-$Version-Setup-x64.exe", "Temhalo-$Version-win-x64.zip")
    $expected += @($expected | ForEach-Object { $_ + '.sha256' })
    $names = @($Release.assets | ForEach-Object name)
    if ($names.Count -ne 4 -or @($names | Sort-Object -Unique).Count -ne 4) { throw 'Quatre actifs de release distincts sont requis.' }
    foreach ($asset in $Release.assets) {
        if ($asset.name -cnotin $expected -or $asset.state -cne 'uploaded' -or $asset.size -le 0 -or
            $asset.digest -cnotmatch '\Asha256:[a-f0-9]{64}\z' -or
            $asset.browser_download_url -cne "https://github.com/$script:Repository/releases/download/v$Version/$($asset.name)") { throw 'Actif de release non conforme.' }
        if ($asset.name -clike '*-Setup-x64.exe' -and $asset.size -gt 800MB) { throw 'Installeur trop volumineux pour le module de mise a jour.' }
    }
}

function Invoke-TemhaloPublication {
    [CmdletBinding(SupportsShouldProcess)]
    param([string]$Version)
    $root = Get-ReleaseRoot
    Push-Location -LiteralPath $root
    try {
        if (-not $IsWindows -or -not [Environment]::Is64BitOperatingSystem) { throw 'La publication locale necessite Windows x64 et PowerShell 7.' }
        if ([string]::IsNullOrWhiteSpace($Version)) { $Version = Read-Host 'Version a publier (ex. 0.7.0-preview.2)' }
        $Version = $Version.Trim()
        $requested = ConvertTo-ReleaseVersion $Version
        $tag = "v$Version"
        $git = Resolve-ReleaseTool git $root
        $gh = Resolve-ReleaseTool gh $root
        $dotnet = Resolve-ReleaseTool dotnet $root
        $top = (Invoke-ReleaseNative $git @('rev-parse','--show-toplevel') -Capture).Output
        if (-not [IO.Path]::GetFullPath($top).Equals($root, [StringComparison]::OrdinalIgnoreCase)) { throw 'Le script doit appartenir a la racine du depot Temhalo.' }
        $branch = (Invoke-ReleaseNative $git @('branch','--show-current') -Capture).Output
        if ($branch -cne 'main') { throw 'Placez les changements a publier sur la branche main avant de lancer le script.' }
        $origin = (Invoke-ReleaseNative $git @('remote','get-url','origin') -Capture).Output
        if ($origin -notmatch '\A(?:https://github\.com/Zeeroz/Temhalo(?:\.git)?|git@github\.com:Zeeroz/Temhalo(?:\.git)?)\z') { throw 'Le remote origin doit pointer vers Zeeroz/Temhalo.' }
        [void](Invoke-ReleaseNative $gh @('auth','status','--hostname','github.com') -Capture)
        $repository = Invoke-ReleaseJson $gh @('api',"repos/$script:Repository")
        if (-not $repository.permissions.push) { throw 'Le compte GitHub ne peut pas publier dans Zeeroz/Temhalo.' }
        $workflow = Invoke-ReleaseJson $gh @('api',"repos/$script:Repository/actions/workflows/windows.yml")
        if ($workflow.state -cne 'active') { throw 'Le workflow Windows doit etre actif dans GitHub Actions.' }
        $propsPath = Join-Path $root 'Directory.Build.props'
        $propsText = [IO.File]::ReadAllText($propsPath)
        [xml]$props = $propsText
        $currentText = [string]$props.Project.PropertyGroup.Version
        $current = ConvertTo-ReleaseVersion $currentText
        if ($requested -lt $current) { throw "Choisissez une version au moins egale a $currentText." }
        $head = (Invoke-ReleaseNative $git @('rev-parse','HEAD') -Capture).Output
        $status = (Invoke-ReleaseNative $git @('status','--porcelain','--untracked-files=all') -Capture).Output
        $remoteRefs = (Invoke-ReleaseNative $git @('ls-remote','--tags','origin') -Capture).Output
        $remoteCommit = $null
        foreach ($line in ($remoteRefs -split "`n")) {
            if ($line -match ('\A([a-f0-9]{40})\s+refs/tags/' + [regex]::Escape($tag) + '(\^\{\})?\z')) {
                if (-not $remoteCommit -or $Matches[2]) { $remoteCommit = $Matches[1] }
            }
        }
        $localRef = Invoke-ReleaseNative $git @('show-ref','--verify','--quiet',"refs/tags/$tag") -Capture -AllowFailure
        if ($localRef.ExitCode -notin 0,1) { throw 'Lecture du tag local impossible.' }
        $localTag = $localRef.ExitCode -eq 0
        if ($localTag) {
            $localCommit = (Invoke-ReleaseNative $git @('rev-list','-n','1',$tag) -Capture).Output
            if ($localCommit -cne $head) { throw 'Ce tag local designe un autre commit. Choisissez une nouvelle version.' }
        }
        if (($localTag -or $remoteCommit) -and ($status -or $currentText -cne $Version -or ($remoteCommit -and $remoteCommit -cne $head))) {
            throw 'Cette version est deja figee par un tag. Reprenez avec ses sources propres, ou choisissez une nouvelle version.'
        }
        $releases = @(Invoke-ReleaseJson $gh @('api',"repos/$script:Repository/releases?per_page=100"))
        foreach ($release in $releases) {
            if ($release.tag_name -ceq $tag -and -not $release.draft) {
                Assert-PublicRelease $release $Version
                Write-Host "Version deja publiee : $($release.html_url)"
                return
            }
            if (-not $release.draft -and $release.tag_name -cmatch '\Av(.+)\z') {
                try { $publishedVersion = ConvertTo-ReleaseVersion $Matches[1] } catch { continue }
                if ($publishedVersion -ge $requested) { throw 'Une version publiee plus recente existe deja. Choisissez un numero superieur.' }
            }
        }
        Invoke-ReleaseScript $root 'Test-PublicSource.ps1' @{ IncludeUntracked = $true }
        Write-Host "Version : $currentText -> $Version ; depot : $script:Repository"
        Write-Host 'Les changements non ignores du projet seront inclus. Aucun arret automatique de Temhalo ou OBS.'
        Write-Host 'Parcours : tests, ZIP, installeur, audit, commit, tag, push atomique, suivi GitHub et verification de la release.'
        if (-not $PSCmdlet.ShouldProcess("$script:Repository / $tag", 'Tester et publier les changements du projet')) { return }
        [void](Invoke-ReleaseNative $git @('fetch','origin','main'))
        $ancestor = Invoke-ReleaseNative $git @('merge-base','--is-ancestor','origin/main','HEAD') -Capture -AllowFailure
        if ($ancestor.ExitCode -ne 0) { throw 'Le depot distant a avance. Synchronisez main avec git pull --ff-only, puis relancez.' }
        if (-not $remoteCommit) {
            if ($currentText -cne $Version) {
                if ([regex]::Matches($propsText, '<Version>[^<]+</Version>').Count -ne 1) { throw 'Champ Version absent ou ambigu dans Directory.Build.props.' }
                [IO.File]::WriteAllText($propsPath, [regex]::Replace($propsText, '<Version>[^<]+</Version>', "<Version>$Version</Version>"), [Text.UTF8Encoding]::new($false))
            }
            Write-Host '1/5 - Restauration, compilation et tests locaux'
            [void](Invoke-ReleaseNative $dotnet @('restore','Temhalo.sln','-p:Platform=x64','--locked-mode'))
            [void](Invoke-ReleaseNative $dotnet @('build','Temhalo.sln','-c','Release','-p:Platform=x64','--no-restore'))
            [void](Invoke-ReleaseNative $dotnet @('test','Temhalo.sln','-c','Release','-p:Platform=x64','--no-build','--no-restore'))
            Write-Host '2/5 - ZIP, installeur et tests installation/mise a jour/desinstallation'
            Save-PreviousReleaseFiles $root $Version
            Invoke-ReleaseScript $root 'Package-Windows.ps1'
            Invoke-ReleaseScript $root 'Test-WindowsPackage.ps1' @{ ArchivePath = (Join-Path $root "artifacts/packages/Temhalo-$Version-win-x64.zip") }
            Invoke-ReleaseScript $root 'Build-Installer.ps1'
            Invoke-ReleaseScript $root 'Test-Installer.ps1'
            Write-Host '3/5 - Audit et commit de publication'
            Assert-ReleaseGitContext $git
            [void](Invoke-ReleaseNative $git @('add','--all'))
            Invoke-ReleaseScript $root 'Test-PublicSource.ps1'
            [void](Invoke-ReleaseNative $git @('diff','--cached','--stat'))
            $changes = Invoke-ReleaseNative $git @('diff','--cached','--quiet') -Capture -AllowFailure
            if ($changes.ExitCode -notin 0,1) { throw 'Lecture des changements impossible.' }
            if ($changes.ExitCode -eq 1) {
                if ($localTag) { throw 'Le tag existant ne peut pas etre modifie. Choisissez une nouvelle version pour ces changements.' }
                [void](Invoke-ReleaseNative $git @('commit','-m',"Release Temhalo $Version"))
            }
            $head = (Invoke-ReleaseNative $git @('rev-parse','HEAD') -Capture).Output
            if (-not $localTag) { [void](Invoke-ReleaseNative $git @('tag','-a',$tag,'-m',"Temhalo $Version")) }
            Write-Host '4/5 - Push du commit et du tag, sans forcer les references'
            Assert-ReleaseGitContext $git
            if ((Invoke-ReleaseNative $git @('rev-parse','HEAD') -Capture).Output -cne $head -or
                (Invoke-ReleaseNative $git @('rev-list','-n','1',$tag) -Capture).Output -cne $head) { throw 'Le commit ou le tag ont change pendant la publication.' }
            [void](Invoke-ReleaseNative $git @('push','--atomic','origin','HEAD:refs/heads/main',"refs/tags/${tag}:refs/tags/$tag"))
        }
        else { Write-Host 'Reprise de la publication du tag deja pousse ; les fichiers locaux ne sont pas reconstruits.' }
        Write-Host '5/5 - Validation et publication sur GitHub Actions'
        $run = Find-ReleaseRun $gh $tag $head
        $result = Wait-ReleaseRun $gh $run.databaseId
        if ($result.conclusion -cne 'success') {
            $buildSucceeded = @($result.jobs | Where-Object { $_.name -ceq 'build' -and $_.conclusion -ceq 'success' }).Count -eq 1
            $releaseFailed = @($result.jobs | Where-Object { $_.name -ceq 'release' -and $_.conclusion -ceq 'failure' }).Count -eq 1
            if (-not $buildSucceeded -or -not $releaseFailed) { throw "Validation GitHub en echec : $($result.url). Corrigez le probleme avant de publier une nouvelle version." }
            Write-Host 'Build valide ; tentative de reprise verifiee du brouillon.'
            $mainCommit = (Invoke-ReleaseJson $gh @('api',"repos/$script:Repository/commits/main")).sha
            $dispatchedAfter = [DateTimeOffset]::UtcNow.AddSeconds(-2)
            [void](Invoke-ReleaseNative $gh @('workflow','run','windows.yml','--repo',$script:Repository,'--ref','main',
                '-f',"recovery_run_id=$($run.databaseId)",'-f',"recovery_tag=$tag"))
            $recovery = Find-ReleaseRun $gh $tag $mainCommit 'workflow_dispatch' $dispatchedAfter
            $recovered = Wait-ReleaseRun $gh $recovery.databaseId
            if ($recovered.conclusion -cne 'success') { throw "Reprise en echec : $($recovered.url). Le brouillon est conserve." }
        }
        $public = Invoke-ReleaseJson $gh @('api',"repos/$script:Repository/releases/tags/$tag")
        Assert-PublicRelease $public $Version
        Write-Host "Publication terminee : $($public.html_url)" -ForegroundColor Green
        Write-Host "Installeur : https://github.com/$script:Repository/releases/download/$tag/Temhalo-$Version-Setup-x64.exe"
        Write-Host 'La mise a jour sera proposee dans Temhalo a la prochaine recherche du canal correspondant.'
    }
    finally { Pop-Location }
}

Export-ModuleMember -Function Invoke-TemhaloPublication

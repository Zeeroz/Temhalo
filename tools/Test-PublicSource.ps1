[CmdletBinding()]
param([switch]$IncludeUntracked)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Push-Location -LiteralPath $repoRoot
try {
    $listArguments = @('-c','core.quotepath=false','ls-files')
    if ($IncludeUntracked) { $listArguments += '--cached','--others','--exclude-standard' }
    $files = @(& git @listArguments | Sort-Object -Unique)
    if ($LASTEXITCODE -ne 0 -or $files.Count -eq 0) { throw 'Index Git absent ou vide.' }
    $forbiddenExtensions = '.dpapi','.pfx','.p12','.key','.pem','.log','.exe','.dll','.zip','.streamDeckPlugin'
    # Print paths only; never echo a matching credential into a CI log.
    $secretPattern = [regex]'(?:ghp_[A-Za-z0-9]{25,}|github_pat_[A-Za-z0-9_]{30,}|GOCSPX-[A-Za-z0-9_-]{12,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY|live_\d{4,}_[A-Za-z0-9]{15,})'
    foreach ($relative in $files) {
        if ($relative -match '(^|/)(artifacts|bin|obj|node_modules|\.vs)/|(^|/)(oauth-clients|temhalo\.launch\.local|temhalo\.local)\.json$|(^|/)client_secret[^/]*\.json$|(^|/)\.env(?:\.(?!example$)|$)' -or
            [IO.Path]::GetExtension($relative) -in $forbiddenExtensions) { throw "Fichier privé ou généré indexé : $relative" }
        $path = Join-Path $repoRoot $relative
        if ($IncludeUntracked -and -not (Test-Path -LiteralPath $path)) { continue }
        if ((Get-Item -LiteralPath $path).Length -gt 20000000) { throw "Fichier de plus de 20 Mo : $relative" }
        if ([IO.Path]::GetExtension($relative) -in '.png','.ico','.ttf','.woff','.woff2','.jpg','.jpeg') { continue }
        $content = [IO.File]::ReadAllText($path)
        if ($secretPattern.IsMatch($content)) { throw "Identifiant privé potentiel dans : $relative" }
    }
    Write-Host "Audit public : $($files.Count) fichiers vérifiés ; aucun secret connu ni binaire de livraison indexé."
}
finally { Pop-Location }

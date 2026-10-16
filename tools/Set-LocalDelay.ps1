[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateRange(0, 300)][int]$Seconds,
    [string]$SessionDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (-not $SessionDirectory) {
    $candidates = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'artifacts\relay-local') -Directory |
        Sort-Object LastWriteTime -Descending)
    foreach ($candidate in $candidates) {
        $statusPath = Join-Path $candidate.FullName 'status.json'
        if (-not (Test-Path -LiteralPath $statusPath)) { continue }
        $status = Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json
        if ($status.Stage -eq 3 -and $status.AcceptingDelayCommands -and $status.Relay.State -notin @('Stopped', 'Stopping', 'Faulted') -and
            ([DateTimeOffset]::UtcNow - [DateTimeOffset]$status.UpdatedUtc).TotalSeconds -lt 5) {
            $SessionDirectory = $candidate.FullName; break
        }
    }
}
if (-not $SessionDirectory) { throw 'Aucune session active. Utiliser -SessionDirectory si nécessaire.' }
$SessionDirectory = (Resolve-Path -LiteralPath $SessionDirectory).Path
$statusPath = Join-Path $SessionDirectory 'status.json'
$status = Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json
if ($status.Stage -ne 3 -or -not $status.AcceptingDelayCommands -or $status.Relay.State -in @('Stopped', 'Stopping', 'Faulted') -or
    ([DateTimeOffset]::UtcNow - [DateTimeOffset]$status.UpdatedUtc).TotalSeconds -ge 5) {
    throw 'La session ne peut pas accepter de commande.'
}
$requestId = [Guid]::NewGuid().ToString('D')
$temporary = Join-Path $SessionDirectory ("delay-$requestId.tmp")
$backup = Join-Path $SessionDirectory ("delay-$requestId.bak")
$requestPath = Join-Path $SessionDirectory 'delay-request.json'
$json = @{ Id = $requestId; Seconds = $Seconds } | ConvertTo-Json -Compress
[IO.File]::WriteAllText($temporary, $json, [Text.UTF8Encoding]::new($false))
# Windows PowerShell 5.1 has no File.Move(..., overwrite). Replace keeps the operation atomic.
try {
    for ($attempt = 0; ; $attempt++) {
        try {
            if ([IO.File]::Exists($requestPath)) { [IO.File]::Replace($temporary, $requestPath, $backup) }
            else { [IO.File]::Move($temporary, $requestPath) }
            break
        } catch {
            if ($attempt -ge 12) { throw }
            Start-Sleep -Milliseconds 25
        }
    }
} finally {
    foreach ($ownedFile in @($temporary, $backup)) {
        if (Test-Path -LiteralPath $ownedFile) { Remove-Item -LiteralPath $ownedFile }
    }
}
$timer = [Diagnostics.Stopwatch]::StartNew()
while ($timer.Elapsed.TotalSeconds -lt 5) {
    $status = Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json
    if ($status.AcceptedDelayCommandId -eq $requestId) {
        [PSCustomObject]@{ Id = $requestId; TargetSeconds = $Seconds; AcceptedVersion = $status.AcceptedDelayCommandVersion;
            SessionDirectory = $SessionDirectory; Note = 'Commande acceptée ; suivre Relay.Delay.Control pour la transition.' }
        return
    }
    Start-Sleep -Milliseconds 50
}
throw 'Accusé de réception absent : consulter le statut et les logs, ou vérifier une commande concurrente.'

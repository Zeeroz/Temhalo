# Relais local Temhalo sans délai ajouté

L'étape 1 raccorde les processus à un transfert C# de données encodées. La sortie est un fichier FLV local, permettant de vérifier la chaîne et les reprises sans clé de plateforme. Le parseur FLV, le buffer de délai et la réécriture des timestamps seront ajoutés à l'étape 2, après validation.

La livraison initiale reste dans `Temhalo-Etape-1.zip`. L'hôte courant utilise désormais le moteur de l'[étape 2](ETAPE-2.md), à délai nul par défaut ; le présent guide conserve la procédure et les résultats de l'étape 1.

```text
OBS H.264/AAC -> RTMP local -> MediaMTX
  -> FFmpeg lecteur (-c copy, FLV stdout)
  -> Temhalo.Core / PassthroughMediaTransfer
  -> FFmpeg émetteur (-c copy, FLV stdin)
  -> relay-0001.flv
```

Les deux FFmpeg remuxent les données sans décodage ni réencodage, selon le mode [streamcopy FFmpeg](https://ffmpeg.org/ffmpeg.html#Streamcopy). Le transfert C# réutilise un bloc de 64 KiB, respecte l'annulation et attend les écritures sans verrou bloquant. Il conserve exactement les octets qu'il reçoit. Les buffers des transports, la détection des pistes et l'attente d'une image clé peuvent introduire une latence naturelle ; « sans délai ajouté » ne signifie pas une latence totale nulle.

## Fichiers implémentés

| Fichier ou dossier | Fonction |
|---|---|
| `src/Temhalo.Core/Abstractions/IMediaTransfer.cs` | Contrat indépendant de Windows et de l'UI |
| `src/Temhalo.Core/Buffering/PassthroughMediaTransfer.cs` | Transfert binaire borné et compteur atomique |
| `src/Temhalo.Infrastructure.Windows/Processes/` | Job Windows, processus natifs et superviseur du relais local |
| `src/Temhalo.Infrastructure.Windows/Logging/NativeLogSanitizer.cs` | Masquage des URL et champs de secrets avant journalisation |
| `src/Temhalo.RelayHost/Program.cs` | Hôte console, arguments, arrêt et fichier d'état |
| `tests/Temhalo.Core.Tests/` | Intégrité, lectures fragmentées, annulation sous contre-pression |
| `tests/Temhalo.Infrastructure.Tests/` | Masquage des diagnostics natifs |
| `tools/Test-Stage1.ps1` | Source synthétique et injections de défaillances |

Les packages NuGet restent ceux de l'étape 0. WinUI n'est pas encore raccordé au superviseur : son shell reste inchangé jusqu'à l'étape 4.

## Compilation et tests unitaires

Depuis la racine du projet, dans PowerShell :

```powershell
dotnet restore Temhalo.sln -p:Platform=x64 --locked-mode
dotnet build Temhalo.sln --no-restore -c Debug -p:Platform=x64
dotnet test Temhalo.sln --no-build --no-restore -c Debug -p:Platform=x64
& .\tools\native\Get-NativeTools.ps1 -VerifyOnly
```

Sur un poste neuf, exécuter d'abord `Get-NativeTools.ps1` sans `-VerifyOnly` pour acquérir les binaires. Le runtime .NET 8 et l'outillage de l'étape 0 restent nécessaires.

## Essai manuel avec OBS

1. Fermer tout autre relais Temhalo ou serveur occupant le port TCP 1935. Le programme refuse de réutiliser ou d'arrêter un service tiers sur ce port.
2. Dans OBS, sélectionner le profil **Temhalo** : serveur `rtmp://127.0.0.1:1935/live`, clé locale `stream`, H.264, CBR, images clés toutes les secondes, zéro B-frame initialement, une piste AAC. Désactiver le délai natif OBS. Les autres réglages figurent dans [l'étape 0](ETAPE-0.md#profil-obs).
3. Préparer une scène avec mouvement, un chrono et un microphone. Un claquement de mains visible et audible aidera à contrôler la synchronisation à la lecture.
4. Lancer le relais dans un terminal dédié :

   ```powershell
   dotnet run --project .\src\Temhalo.RelayHost --no-build -c Debug
   ```

5. Le terminal affiche le dossier de cette session. Avant la publication OBS, les échecs « no stream is available » sont normaux ; le superviseur retente avec un intervalle plafonné à 10 s. Démarrer ensuite le streaming OBS. Le fichier FLV apparaît dès que l'émetteur reconnaît les pistes, et sa taille augmente.
6. Laisser tourner deux minutes. Arrêter la publication dans OBS, puis attendre quelques secondes pour la vidange des pipes. Relancer OBS : un nouveau fichier est créé automatiquement. Laisser tourner une minute de plus.
7. Appuyer sur **Ctrl+C dans le terminal du relais** pour arrêter le relais et finaliser le fichier courant. La commande suivante permet aussi un essai limité à 120 secondes :

   ```powershell
   dotnet run --project .\src\Temhalo.RelayHost --no-build -c Debug -- --run-seconds 120
   ```

8. Ouvrir les fichiers terminés dans VLC ou un lecteur compatible FLV/H.264/AAC. Vérifier l'image, le son, le mouvement et le clap. Les enregistrements sont sous `artifacts/relay-local/run-…/relay-….flv`.

Chaque lancement possède son propre dossier ; chaque paire FFmpeg possède son propre fichier, sans écrasement. Des numéros peuvent manquer si une tentative a échoué avant toute réception média.

## Contrôler les processus et les reprises

Le fichier `status.json` de la session est actualisé environ quatre fois par seconde et remplacé atomiquement. Il expose les PID du serveur, du lecteur et de l'émetteur, la tentative courante, le fichier courant et le cumul d'octets transférés vers stdin. Ce compteur ne mesure ni le délai ni le nombre d'octets effectivement enregistrés sur disque.

Pour le lire dans un second terminal :

```powershell
$session = Get-ChildItem .\artifacts\relay-local -Directory |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
$statusPath = Join-Path $session.FullName 'status.json'
$status = Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json
$status.Relay
```

Les états sont `Starting`, `StartingIngest`, `WaitingForMedia`, `Relaying`, `Draining`, `Retrying`, `Stopping`, `Stopped` et `Faulted`. `Relaying` indique que des octets ont traversé le transfert C# ; il ne constitue pas un état de connexion à une plateforme.

Pendant qu'OBS publie, simuler une panne de l'émetteur à partir du PID **de cette session** :

```powershell
$status = Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json
Stop-Process -Id $status.Relay.EmitterPid -Force
```

La paire FFmpeg redémarre automatiquement et un nouveau fichier reçoit le flux. Répéter séparément avec `ReaderPid`, puis avec `MediaMtxPid`. Après une panne de MediaMTX, OBS perd sa connexion locale et doit se reconnecter automatiquement : conserver le réglage de reconnexion du profil OBS. Vérifier de nouveaux PID, une nouvelle tentative et la progression du compteur après reprise.

Pour vérifier un fichier terminé sans lecteur graphique :

```powershell
$tools = Get-Content .\artifacts\native-tools.resolved.json -Raw | ConvertFrom-Json
$ffmpeg = ($tools.Tools | Where-Object Name -eq 'FFmpeg').Executable
$ffprobe = Join-Path (Split-Path -Parent $ffmpeg) 'ffprobe.exe'
$file = Get-ChildItem -LiteralPath $session.FullName -Filter '*.flv' |
    Where-Object Length -gt 0 | Sort-Object Name | Select-Object -First 1
& $ffprobe -v error -show_streams -show_format $file.FullName
& $ffmpeg -v error -xerror -i $file.FullName -f null -
```

Le premier contrôle doit annoncer H.264 et AAC ; le second doit se terminer avec le code 0 sur un segment finalisé normalement. Une interruption forcée de l'émetteur peut laisser la fin de son fichier incomplète. Tester la lecture normale sur un segment terminé par déconnexion de la source ou arrêt du relais ; vérifier la reprise sur le segment suivant en cas de crash.

## Essai automatique avec défaillances

Arrêter le relais manuel et la publication OBS avant de lancer :

```powershell
& .\tools\Test-Stage1.ps1
& .\tools\Test-Stage1.ps1 -StartupOnly
```

Le script crée une mire animée et un son avec l'encodeur logiciel H.264 Media Foundation de Windows et l'encodeur AAC de FFmpeg. Cet encodage sert uniquement à fabriquer la source de test ; sa publication et les deux processus du relais utilisent `-c copy`. Le test nécessite la fonctionnalité Media Foundation de Windows ; sur une édition N, ajouter le Media Feature Pack si l'encodeur n'est pas disponible.

Le test vérifie le démarrage avant la source, la transmission H.264/AAC, les crashes de chaque FFmpeg et de MediaMTX, la déconnexion/reconnexion de la source, l'arrêt demandé et l'absence d'orphelins après l'arrêt forcé de l'hôte. Il conserve les fichiers, les sorties des processus et un `result.json` sous `artifacts/validation/stage1-…/`.

L'option `-StartupOnly` contrôle séparément le refus d'un port occupé, l'absence de listeners autres que le RTMP loopback, l'isolation des variables héritées et l'arrêt sans source. Elle nécessite les commandes réseau Windows `Get-NetTCPConnection` et `Get-NetUDPEndpoint`, et produit `startup-result.json`.

La validation média porte sur un segment terminé normalement : DTS non décroissants par piste, données AAC identiques à la source, séquence de tranches vidéo H.264 identique par SHA-256 et décodage complet sans erreur. Le vérificateur sélectionne les unités NAL VCL 1 à 5 pour comparer les tranches encodées, car le remuxage peut normaliser des unités auxiliaires ; ce filtre ne s'exécute jamais dans le relais. Voir [filter_units de FFmpeg](https://ffmpeg.org/ffmpeg-bitstream-filters.html#filter_005funits).

L'écart entre les derniers DTS audio et vidéo doit rester inférieur à 250 ms pour ce segment. Cette mesure de fin de fichier complète le contrôle de lecture ; elle ne remplace pas le test perceptif avec le clap OBS ni les essais longs de synchronisation.

### Résultats observés le 15 octobre 2026

La compilation Debug x64 réussit sans avertissement ni erreur et les sept tests unitaires passent. L'essai automatique a validé les sept contrôles d'intégration ; son segment de référence contient 261 paquets contrôlés, dont 85 images aux tranches H.264 inchangées. L'écart entre les derniers DTS audio et vidéo est de 13 ms. L'essai OBS réel reste à effectuer avec la procédure ci-dessus.

Le contrôle de démarrage passe également : port occupé refusé, variables d'environnement isolées, un seul listener RTMP sur loopback, aucun listener UDP ni rapport FFmpeg indépendant, et arrêt normal sans source.

## Arrêt et limites de cette étape

Le superviseur demande d'abord au lecteur FFmpeg de s'arrêter avec `q`, laisse le transfert vider stdout et ferme stdin de l'émetteur. L'émetteur reçoit ainsi EOF et peut écrire la fin du FLV. Chaque attente possède une limite ; un processus bloqué est terminé si nécessaire.

MediaMTX sans console n'offre pas de commande stdin d'arrêt sur Windows dans cette configuration. Il est terminé après les FFmpeg. Un job Windows avec `KILL_ON_JOB_CLOSE` regroupe les trois enfants pour qu'un crash de l'hôte ne laisse pas de processus natifs orphelins.

Les reprises ont une temporisation progressive, plafonnée à 10 s après des tentatives sans données. Un port occupé, un fichier manquant ou un échec d'initialisation provoque une erreur explicite avec code de sortie 1. Une interruption normale renvoie 0.

À cette étape, les données présentes au moment d'un crash peuvent être perdues : aucun buffer de délai ne les conserve encore. Les fichiers successifs représentent des sessions distinctes ; leur continuité temporelle entre reprises ne fait pas partie de l'étape 1. Le traitement des tags et des en-têtes appartient à l'étape 2, les transitions dynamiques à l'étape 3 et la conservation pendant les reconnexions à leur implémentation dédiée.

Les journaux sont écrits dans `logs/` sous chaque session, avec rotation quotidienne et par taille de 10 MiB, sept fichiers conservés par session. Les URL et champs de secrets sont masqués avant leur écriture et les arguments de processus ne sont jamais journalisés. La gestion globale des journaux et leur vue WinUI restent prévues à l'étape 5. Aucune clé de plateforme ni sortie réseau n'est acceptée par cet hôte local.

MediaMTX écoute uniquement sur `127.0.0.1:1935`. MoQ est explicitement désactivé, au même titre que les autres protocoles inutilisés ; cette option évite l'activation des listeners supplémentaires de la version native retenue. Les variables héritées `MTX_*` sont retirées de son environnement pour que la configuration locale reste effective. `FFREPORT` est également retirée de l'environnement des enfants pour éviter la création de diagnostics indépendants du masquage Temhalo.

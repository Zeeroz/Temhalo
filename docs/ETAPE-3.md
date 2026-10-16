# Délai dynamique Temhalo pendant une session

Le relais local peut changer sa cible de 0 à 300 secondes sans relancer MediaMTX ni les FFmpeg. L'augmentation retient progressivement le contenu ; la réduction saute vers une image IDR ; le retour à zéro rejoint la dernière IDR disponible. La sortie reste un fichier FLV de test. L'interface WinUI attend l'étape 4.

## Augmentation par paliers

Avec une sortie déjà active, le moteur attend une IDR avant chaque pause. La pause demandée dure au plus **5 secondes par défaut**, puis le moteur renvoie les en-têtes AVC/AAC et reprend sur cette IDR. Il conserve tous les tags média pendant l'augmentation. L'entrée continue de lire pendant les pauses.

Avant un nouveau palier, il faut au moins une seconde de temps réel et une seconde de progression des timestamps du média émis. Le prochain palier attend ensuite une IDR ; la reprise intermédiaire peut donc dépasser une seconde. Atteindre 300 s prend plus de 300 s de temps réel. Les cibles et le délai effectivement atteint sont affichés séparément.

Une nouvelle commande pendant une pause peut raccourcir celle-ci ou déclencher un saut, mais ne prolonge pas sa date limite au-delà du plafond initial. Le plafond est une durée demandée : la gigue du système et de l'écriture peut ajouter quelques millisecondes, mesurées dans le statut.

Avant la première émission, choisir un délai règle directement le remplissage initial. Il n'y a alors aucun flux sortant à interrompre. Après une fin d'entrée, aucun nouveau palier ne commence : la pause déjà commencée se termine et les tags restants se vident au délai atteint. Si la cible n'a pas été atteinte, le statut final l'indique par `TargetUnreachedAtInputEnd` et `TargetReached = false`.

## Réduction et retour à zéro

La recherche considère les tags en attente, y compris le tag dont l'échéance était en cours. Pour une cible positive, elle choisit la dernière IDR disponible avant `maintenant − cible`. Ce choix peut commencer jusqu'à un intervalle de keyframe en arrière de la cible. Si aucune IDR antérieure n'est disponible, elle utilise la première IDR suivante et attend son échéance. Avec les images clés OBS réglées à une seconde, cette marge est normalement de l'ordre d'une seconde.

À zéro, le moteur choisit **la plus récente IDR disponible**, écarte le contenu antérieur et libère son GOP sans ajouter de délai. Le petit reliquat de GOP peut sortir en rafale avant le retour au rythme entrant. En l'absence d'IDR disponible, le moteur attend la prochaine. Redemander zéro alors que la cible et le délai appliqué sont déjà nuls laisse la lecture continuer.

Le moteur retransmet la configuration AVC/AAC correspondant au point choisi, puis la vidéo IDR et l'audio postérieur à ce point. Les configurations lues en avance pour d'autres GOP ne sont pas appliquées au GOP choisi. Le saut retire également l'intervalle abandonné de la chronologie de sortie, avec un décalage commun aux pistes audio et vidéo ; les DTS restent non décroissants par piste et le CompositionTime AVC est conservé.

Les commandes reçues avant traitement sont coalescées : **la dernière cible gagne**. La boîte de commande conserve une valeur, sans file de commandes illimitée. Le consommateur sérialise les transitions ; le producteur reste indépendant. Les limites mémoire de l'étape 2 restent effectives et ne réduisent jamais silencieusement le délai.

## Fichiers et composants

| Composant | Fonction |
|---|---|
| `Core/Timing/DelayController.cs` | Boîte de commande thread-safe avec versions et réveil des attentes |
| `Core/Timing/DelayTransitionPlanner.cs` | Paliers, reprise intermédiaire, interruption et progression |
| `Core/Buffering/FlvDelayMediaTransfer.cs` | Moteur FLV partagé, lecture et émission |
| `Core/Buffering/FlvDelayMediaTransfer.Dynamic.cs` | Attentes interruptibles, recherche IDR et saut |
| `Core/Buffering/FixedDelayMediaTransfer.cs` | Adaptateur de compatibilité pour les tests à cible fixe |
| `tools/Set-LocalDelay.ps1` | Commande locale de diagnostic et accusé de réception |
| `tools/Test-Stage3.ps1` | Essai réel de transitions et vérification du fichier |

Les chemins Core du tableau sont sous `src/Temhalo.Core/`. Aucun package NuGet n'a été ajouté. Les commandes appellent le même `DelayController.SetTarget` que pourra employer le ViewModel à l'étape 4. Le pilotage par fichier sert aux essais console : aucune API HTTP/WebSocket n'est ouverte.

## Compilation et tests

Depuis la racine du projet, dans PowerShell :

```powershell
dotnet restore Temhalo.sln -p:Platform=x64 --locked-mode
dotnet build Temhalo.sln --no-restore -c Debug -p:Platform=x64
dotnet test Temhalo.sln --no-build --no-restore -c Debug -p:Platform=x64
& .\tools\native\Get-NativeTools.ps1 -VerifyOnly
```

Sur une extraction neuve, lancer d'abord le script natif sans `-VerifyOnly`. Les prérequis et les binaires sont ceux des étapes précédentes.

## Procédure OBS

1. Garder le profil OBS Temhalo : serveur `rtmp://127.0.0.1:1935/live`, clé locale `stream`, H.264 CBR, images clés toutes les secondes, une piste AAC, zéro B-frame pour les premiers essais et délai natif OBS désactivé.
2. Dans un premier terminal, lancer le relais, puis démarrer la publication OBS :

   ```powershell
   dotnet run --project .\src\Temhalo.RelayHost --no-build -c Debug -- --delay-seconds 0
   ```

3. Attendre `Relaying`. Dans un second terminal à la racine, demander 15 secondes :

   ```powershell
   & .\tools\Set-LocalDelay.ps1 -Seconds 15
   ```

4. Observer les pauses et reprises dans le journal et le fichier d'état. Le script confirme uniquement l'acceptation de la commande ; attendre `TargetReached = true` et un âge de média proche de la cible pour constater la fin de transition.
5. Demander ensuite une réduction, puis un retour à zéro, en laissant le temps d'observer chaque transition :

   ```powershell
   & .\tools\Set-LocalDelay.ps1 -Seconds 5
   & .\tools\Set-LocalDelay.ps1 -Seconds 0
   ```

6. Tester aussi une interruption : demander 30 s, attendre `IncreasingPause`, puis demander immédiatement 0 s. La pause doit s'interrompre et la reprise attendre une IDR. OBS et les trois processus natifs doivent rester les mêmes.
7. Répéter les cibles 0, 15, 30, 60, 15, 0 pendant un essai de 30 minutes. Contrôler le buffer, l'absence d'erreur et les PID. Avec 6 Mbit/s, une cible de 60 s doit laisser une marge confortable sous les 512 MiB par défaut. Cette procédure longue reste à exécuter avec OBS ; l'essai automatique livré est plus court.
8. Faire Ctrl+C dans le premier terminal et attendre la vidange. Lire le FLV finalisé avec VLC et vérifier le mouvement, le son et un clap visible/audible. Les pauses ne deviennent pas des secondes de noir enregistrées ; les intervalles sautés lors des réductions disparaissent du fichier.

Pour consulter le statut :

```powershell
$session = Get-ChildItem .\artifacts\relay-local -Directory |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
$status = Get-Content (Join-Path $session.FullName 'status.json') -Raw | ConvertFrom-Json
$status.Relay.Delay
$status.Relay.Delay.Control
$status.Relay | Select-Object MediaMtxPid, ReaderPid, EmitterPid, Attempt, OutputFile
```

En cas de plusieurs sessions ou d'un dossier de sortie personnalisé, passer explicitement le dossier affiché au lancement :

```powershell
& .\tools\Set-LocalDelay.ps1 -Seconds 30 -SessionDirectory $session.FullName
```

Le script écrit une commande JSON avec identifiant unique, remplace le fichier atomiquement et attend son accusé de réception pendant au plus cinq secondes. L'hôte vérifie la taille, l'identifiant et la valeur 0..300. Les commandes malformées sont refusées sans arrêter la session. Les nouvelles commandes sont refusées pendant l'arrêt demandé du relais.

## Paramètres et télémétrie

Le comportement par défaut vient de `config/temhalo.defaults.json` : `RequestedMaximumPauseStepSeconds = 5` et `ResumeBetweenStepsSeconds = 1`. On peut choisir d'autres valeurs au lancement :

```powershell
# Réglages accélérés pour les essais locaux uniquement
dotnet run --project .\src\Temhalo.RelayHost --no-build -c Debug -- --pause-step-seconds 2 --resume-seconds 1
```

Le palier accepté va de 1 à 20 s et la reprise minimale de 1 à 10 s. Ces bornes sont des options du moteur ; elles ne valident aucun timeout de plateforme.

| Champ | Sens |
|---|---|
| `Relay.Delay.TargetSeconds` | Dernière cible demandée |
| `Control.ActiveDelayMilliseconds` | Délai de planification atteint, progressif pendant une pause |
| `LastMediaAgeMilliseconds` | Âge réel du dernier tag émis dans le moteur |
| `Control.AppliedVersion` | Dernière commande prise en compte par le consommateur |
| `Control.Phase` | Stable, IncreasingAwaitingKeyframe, IncreasingPause, IncreasingPlayback, ResumingKeyframe, SeekingKeyframe ou TargetUnreachedAtInputEnd |
| `Control.TargetReached` | Cible appliquée et média repris ; distinct d'un simple accusé de réception |
| `Control.CompletedPauseSteps` | Pauses terminées, y compris une pause raccourcie par une commande |
| `Control.CompletedSeeks` | Sauts ayant effectivement repris sur une IDR |
| `Control.MaximumPauseMilliseconds` | Plus longue pause observée, avec gigue de l'ordonnanceur |

Les âges minimum/maximum du média portent sur toute la session. Après une transition, utiliser le dernier âge et le contrôle courant pour juger de la convergence. Le délai total du spectateur inclut aussi les buffers d'OBS, des processus natifs, du réseau, de la plateforme et du lecteur. Le retour à zéro ne retire pas les données déjà remises à ces composants.

## Essai automatique et résultats

Arrêter la publication OBS et le relais manuel, puis lancer :

```powershell
& .\tools\Test-Stage3.ps1
```

Le script fabrique une mire H.264/AAC, la publie en temps réel et teste 0 → 6 → 3 → 0, zéro pendant une augmentation, puis deux cycles 0 → 2 → 0. Il contrôle les PID, le fichier unique, le plafond des paliers, la mémoire, les DTS, les payloads AAC, les tranches H.264 et le décodage complet. Il conserve `history.json`, les journaux et `result.json` sous `artifacts/validation/stage3-…/`.

Résultat observé le **16 octobre 2026** : compilation Debug x64 sans avertissement ni erreur ; **36 tests unitaires** passent. L'essai réel conserve les mêmes PID et le même fichier durant toutes les transitions. Il mesure 6 001 ms pour la cible 6 s, 3 014 ms pour 3 s et 0 ms après convergence vers zéro. Les trois paliers demandés de 2 s ont une durée maximale observée de 2 009 ms. Le fichier contient 1 104 paquets contrôlés, dont 431 images aux tranches H.264 inchangées, et se décode sans erreur. L'écart entre les derniers DTS A/V est de 17 ms ; le contrôle perceptif et les essais longs restent nécessaires.

La régression native à délai fixe passe aussi : 5 000 ms d'âge minimal pour une cible de 5 s, vidange après coupure de source, décodage correct et arrêt prévu en cas de budget insuffisant. Le [rapport de validation](validation/etape-3.json) conserve les mesures et distingue les essais effectués de ceux restant à réaliser. Aucun processus natif de test ne reste actif après nettoyage.

## Limites des pauses et des reprises

Pendant une pause pure, aucun nouveau paquet média n'est envoyé. Garder une connexion TCP ou échanger des contrôles RTMP ne garantit pas que la plateforme maintienne son ingest et sa lecture. Les paliers limitent les périodes silencieuses ; **ils ne garantissent pas l'absence de déconnexion**. La valeur 5 s est un point de départ à tester par destination. Le test livré vers un fichier ne valide pas Twitch, YouTube ou Kick. Le mode [streamcopy FFmpeg](https://ffmpeg.org/ffmpeg.html#Streamcopy) copie les paquets encodés sans fabriquer de nouvelles images d'attente.

Si les paliers ne conviennent pas à une destination, une alternative est de rejouer un GOP encodé complet avec son audio et des timestamps réécrits. Cela maintient des données média en sortie au prix d'une boucle visible et audible, et exige ses propres essais de compatibilité. Cette alternative et le mode fluide avec réencodage ne sont pas implémentés ici.

Le buffer appartient toujours à la paire FFmpeg courante ; sa conservation lors des crashes et les reconnexions intersessions restent prévues à l'étape 5. Les limites de codec, de RAM et de durée FLV de l'étape 2 restent applicables. L'étape 4 attend le retour utilisateur.

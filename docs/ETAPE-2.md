# Relais Temhalo à délai fixe

Ce guide décrit le jalon validé dans `Temhalo-Etape-2.zip`. Le code courant ajoute le pilotage dynamique de [l'étape 3](ETAPE-3.md) ; sans commande pendant la session, le délai choisi au lancement conserve le comportement fixe décrit ici. Les résultats ci-dessous sont ceux du jalon historique.

Le relais local utilise maintenant un parseur FLV et un buffer RAM circulaire, avec un délai choisi au lancement entre 0 et 300 secondes. Les données H.264 et AAC restent encodées : les deux FFmpeg utilisent `-c copy`. La modification du délai pendant une session attend l'étape 3.

```text
OBS -> MediaMTX -> FFmpeg lecteur / FLV stdout
  -> parseur FLV -> buffer RAM borné -> échéance d'arrivée + délai fixe
  -> reprise IDR + en-têtes AVC/AAC + timestamps réécrits
  -> FFmpeg émetteur / FLV stdin -> fichier local
```

## Comportement du moteur

Le parseur valide la signature et l'en-tête FLV v1, les tags audio/vidéo/script, les tailles 24 bits, le StreamID et les PreviousTagSize. Il accepte les lectures fragmentées et distingue une fin de flux normale d'un tag tronqué. Un tag est horodaté quand sa lecture complète se termine, avec une horloge monotone indépendante de l'heure civile.

La tâche d'entrée continue de lire pendant que la tâche de sortie attend ses échéances. Le buffer contient les tags dans une file circulaire à producteur et consommateur uniques, sans `lock` ni attente bloquante sur le chemin des paquets. Aucun tag média ne sort avant **arrivée + délai fixe**. À la fin de l'entrée, les tags complets restants conservent leur échéance ; le moteur les vide sans accélérer la lecture.

La première vidéo émise doit être marquée keyframe et contenir une unité NAL H.264 IDR. Le seul indicateur FLV ne suffit pas, et un sequence header AVC n'est pas une image de reprise. La sortie attend la configuration AVC et, si l'audio est déclaré, la configuration AAC. Les images et l'audio précédant le point de reprise sont écartés.

Les configurations restent associées aux tags consommés : une configuration lue en avance n'est jamais appliquée aux anciennes images retenues dans le buffer. Une modification AVC/AAC, une régression du DTS d'une piste ou une fin de séquence AVC impose une nouvelle attente d'IDR. Les en-têtes AVC et AAC sont réémis juste avant cette image.

Les timestamps d'entrée sont étendus en 64 bits pour gérer leur rebouclage 32 bits. Le premier point de reprise commence à zéro. Les reprises suivantes utilisent un **décalage commun aux deux pistes**, avec des DTS non décroissants par piste et sans chevauchement avec la fenêtre de présentation précédente. Le CompositionTime AVC signé reste inchangé dans le payload : les relations PTS/DTS et les écarts A/V sont conservés sur le contenu retenu. Les PTS peuvent être réordonnés normalement en présence de B-frames.

Les tags script sont validés structurellement puis omis : l'émetteur FFmpeg reconstruit les métadonnées du fichier au lieu de réutiliser une durée ou un index devenu faux. La prise en charge reste limitée au FLV classique H.264/AAC ; les autres codecs, les tags filtrés/chiffrés et Enhanced RTMP sont rejetés explicitement. Référence des types et indicateurs : [FLV dans FFmpeg](https://ffmpeg.org/doxygen/trunk/flv_8h.html).

## Mémoire et limite

Le budget par défaut est **512 MiB**, avec un avertissement à **80 %**. Il compte les tableaux de payload réellement détenus, une marge de 128 octets par tag, les en-têtes conservés et le stockage de la file. Les payloads utilisent des tableaux de taille exacte ; il n'y a pas de capacité cachée d'un pool partagé. Leur libération les rend récupérables par le GC, sans imposer une collecte à chaque tag.

La RAM du processus peut dépasser ce budget à cause du CLR, du GC et des autres composants ; les processus natifs possèdent aussi leur mémoire. Il s'agit d'un budget du buffer, pas d'une limite système de RAM totale.

La file admet au plus **65 536 tags**, et un tag individuel au plus **4 MiB** par défaut. Les paramètres correspondants sont exposés dans `FixedDelayOptions`. À 60 images/s et environ 47 paquets AAC/s, cinq minutes représentent environ 32 100 tags, sous cette capacité. Les tableaux de la file occupent environ 512 KiB même à délai nul.

Si le budget ou la capacité de tags est atteint, le moteur s'arrête avec `BufferLimitExceededException`, l'hôte annonce `Faulted` et renvoie **1**. Les processus sont nettoyés et le buffer libéré. Il n'y a ni écrasement silencieux ni émission anticipée pour réduire le délai. Les valeurs `BufferWarning` et `WarningObserved` indiquent l'alerte actuelle et son historique ; le franchissement est aussi journalisé.

À 8 Mbit/s au total, cinq minutes représentent environ 300 Mo de payload, auxquels s'ajoutent les structures du moteur. Le test de capacité utilise réellement ce volume en mémoire, avec une horloge simulée pour éviter une attente de cinq minutes.

## Compiler et vérifier

Depuis la racine du projet, dans PowerShell :

```powershell
dotnet restore Temhalo.sln -p:Platform=x64 --locked-mode
dotnet build Temhalo.sln --no-restore -c Debug -p:Platform=x64
dotnet test Temhalo.sln --no-build --no-restore -c Debug -p:Platform=x64
& .\tools\native\Get-NativeTools.ps1 -VerifyOnly
```

Sur un poste neuf, lancer le script natif sans `-VerifyOnly` pour acquérir les binaires. Aucun package NuGet supplémentaire n'est nécessaire pour cette étape ; Core reste indépendant de Windows et sans dépendance NuGet.

## Essai manuel avec OBS

1. Arrêter les autres relais Temhalo et vérifier que le port local 1935 est disponible. Dans OBS, garder le profil validé : serveur `rtmp://127.0.0.1:1935/live`, clé locale `stream`, H.264 CBR, images clés toutes les secondes, zéro B-frame pour les premiers essais, une piste AAC et délai natif OBS désactivé.
2. Lancer un délai fixe de 15 secondes avec le budget par défaut :

   ```powershell
   dotnet run --project .\src\Temhalo.RelayHost --no-build -c Debug -- --delay-seconds 15
   ```

3. Démarrer la publication OBS avec mouvement, chrono et microphone. Le relais passe de `WaitingForMedia` à `Buffering`, puis à `Relaying`. Au début, aucun fichier vidéo exploitable n'est encore produit : il faut recevoir les configurations, attendre les échéances et trouver une IDR.
4. Dans un second terminal, consulter l'état :

   ```powershell
   $session = Get-ChildItem .\artifacts\relay-local -Directory |
       Sort-Object LastWriteTime -Descending | Select-Object -First 1
   $statusPath = Join-Path $session.FullName 'status.json'
   $status = Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json
   $status.Relay.State
   $status.Relay.Delay
   ```

5. Après le démarrage de la sortie, vérifier `TargetSeconds = 15`, `MinimumMediaAgeMilliseconds >= 15000` et un `LastMediaAgeMilliseconds` proche de 15000. `MaximumMediaAgeMilliseconds` conserve le pire âge observé : pour le test de tolérance ±2 s, il doit rester sous 17000. La taille du buffer doit se stabiliser si le débit reste stable.
6. Après une minute, arrêter uniquement la publication OBS. L'état passe à `Draining` et le fichier continue de recevoir le contenu déjà retenu pendant environ 15 secondes. Attendre que le buffer se vide. Le serveur attend ensuite une nouvelle source ; une relance d'OBS ouvre une nouvelle session fichier.
7. Faire **Ctrl+C** dans le terminal du relais. L'entrée s'arrête, puis les tags retenus se vident à leur échéance avant finalisation du FLV. Avec 300 secondes de délai, cette vidange peut prendre cinq minutes : laisser le terminal ouvert jusqu'à `Stopped`.
8. Lire les fichiers finalisés avec VLC ou les commandes ffprobe/FFmpeg de [l'étape 1](ETAPE-1.md#contrôler-les-processus-et-les-reprises). Vérifier une image nette dès le début, le son et un clap visible/audible. Le fichier commence à son timestamp zéro : les 15 secondes d'attente ne sont pas enregistrées sous forme de noir ou de silence.

Le délai indiqué mesure l'âge des tags depuis leur lecture dans le moteur C#. Il n'inclut pas les buffers d'OBS, de MediaMTX ou des FFmpeg. Un fichier relu après finalisation ne mesure pas à lui seul le délai de livraison en temps réel. La comparaison sur un second appareil via une plateforme attend la sortie réseau ; les tests locaux contrôlent ici l'échéance des tags et l'intégrité du contenu.

Pour choisir d'autres paramètres au prochain lancement :

```powershell
# 30 secondes et budget de 256 MiB
dotnet run --project .\src\Temhalo.RelayHost --no-build -c Debug -- --delay-seconds 30 --buffer-mib 256

# Référence à délai nul, avec le même parseur et la même reprise IDR
dotnet run --project .\src\Temhalo.RelayHost --no-build -c Debug -- --delay-seconds 0
```

`--buffer-mib` accepte 1 à 4096 MiB. Sans argument, le programme lit `Delay.InitialSeconds`, `Buffer.MaximumBytes` et `Buffer.WarningUsageRatio` dans `config/temhalo.defaults.json`. `--run-seconds` arrête l'entrée après la durée demandée ; la vidange peut ajouter jusqu'au délai fixe à cette durée.

## Essais automatiques

Arrêter la publication OBS et le relais manuel avant de lancer :

```powershell
& .\tools\Test-Stage2.ps1
# Variante pour contrôler une autre valeur fixe
& .\tools\Test-Stage2.ps1 -DelaySeconds 3
```

Le script fabrique une mire H.264/AAC avec l'encodeur logiciel Media Foundation, puis la publie en temps réel et en copie de flux. Il vérifie l'absence de sortie pendant le remplissage, l'âge minimal des tags, la tolérance de sortie, la vidange après coupure de la source, la libération du buffer, la première keyframe, les DTS par piste, les payloads AAC et les tranches vidéo H.264 par SHA-256, puis le décodage complet. Le filtre de sélection des tranches VCL sert uniquement au vérificateur.

Un second lancement utilise volontairement 300 secondes de délai et seulement 1 MiB de budget : il doit s'arrêter sans émettre de média, avec code 1, état `Faulted`, budget jamais dépassé et mémoire du buffer libérée. Les sorties et `result.json` sont conservés sous `artifacts/validation/stage2-…/`.

Les tests unitaires couvrent les erreurs de framing, les lectures fragmentées, les tailles invalides, les configurations AVC/AAC, l'attente d'une vraie IDR, les générations de codec, les offsets de composition positifs et négatifs, le rebouclage des timestamps, leurs régressions, l'absence de chevauchement de présentation à la reprise, l'annulation, les limites mémoire/tags, le bouclage répété de la file circulaire et la capacité de 300 Mo.

## Résultats observés le 16 octobre 2026

La solution Debug x64 compile sans avertissement ni erreur et **29 tests unitaires** passent. Les contrôles natifs de reprise de l'étape 1 passent également avec le moteur à délai nul.

| Réglage testé | Âge minimal | Dernier âge | Tags émis après coupure de source | Écart des derniers DTS A/V |
|---|---:|---:|---:|---:|
| 5 s | 5 000 ms | 5 007 ms | 366 | 8 ms |
| 3 s | 3 000 ms | 3 007 ms | 215 | 11 ms |

Les deux fichiers commencent par une keyframe et se décodent sans erreur ; les données contrôlées restent inchangées. Le buffer revient à zéro après vidange et le test de dépassement mémoire échoue de la manière prévue. Le rapport synthétique est dans `docs/validation/etape-2.json`.

Sur le dernier essai à 5 s, le maximum enregistré sur tous les tags média émis est de **5 016 ms**, avec 566 paquets contrôlés et 221 images aux tranches H.264 inchangées. Les âges minimum et maximum mesurent le passage dans le moteur, sans la latence des autres composants.

## Limites avant les étapes suivantes

Dans le jalon de l'étape 2, le délai reste fixe pendant toute la session. Le code courant implémente les commandes de saut et les pauses dynamiques à l'étape 3 ; le slider WinUI et le mode fluide restent à développer.

Le buffer appartient encore à une paire FFmpeg. Un crash de l'émetteur fait perdre les données de cette paire et provoque une nouvelle session fichier ; la conservation intersessions et la reconnexion de sortie restent prévues à l'étape 5. Après une reconnexion OBS pendant la vidange d'une ancienne session, le début du nouveau flux peut manquer jusqu'au redémarrage du lecteur. L'ingest reste local et aucune clé de plateforme n'est utilisée.

Un tag final tronqué lors de la coupure du lecteur est écarté avec indicateur `TruncatedInput` et avertissement ; les tags complets déjà reçus sont vidangés. Les autres erreurs structurelles arrêtent le moteur plutôt que d'émettre un flux invalide. Le format de sortie conservant des timestamps 32 bits, une session dépassant environ 49,7 jours après rebasage est arrêtée explicitement au lieu de reboucler silencieusement.

Les essais longs avec OBS, les pannes réseau et les mesures CPU du produit complet restent à effectuer aux étapes correspondantes. Le jalon suivant est décrit dans [le guide de l'étape 3](ETAPE-3.md).

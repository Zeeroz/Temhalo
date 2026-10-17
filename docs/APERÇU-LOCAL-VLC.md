# Voir le flux retardé de Temhalo dans VLC

Le bouton **Direct → Ouvrir l’aperçu VLC** prépare le récepteur local et ouvre VLC avec le profil de lecture corrigé. Aucun terminal n’est nécessaire. Le premier serveur MediaMTX (port 1935) reçoit OBS ; un second serveur, limité à la boucle locale, remplace la plateforme pour ce test. La chaîne passthrough de Temhalo reste identique ; seul son destinataire change.

```text
OBS → MediaMTX :1935 → Temhalo et son délai → MediaMTX :1936 → VLC
```

1. Installer VLC sur ce PC s’il n’est pas déjà présent. Temhalo le trouve dans son emplacement Windows enregistré ou dans les dossiers habituels de VideoLAN.

2. Relais arrêté, dans **Temhalo → Diffusion**, ajouter **Autre serveur RTMP** avec une clé manuelle :

   | Champ | Valeur |
   |---|---|
   | Serveur | `rtmp://127.0.0.1:1936/live` |
   | Clé locale de test | `preview` |

   Désactiver les autres destinations pour ce test privé. Enregistrer la destination, choisir un délai initial de 0 s et cliquer sur **Démarrer le relais**. Temhalo prépare le récepteur de cette destination locale avant de recevoir OBS, pour éviter un retard initial dû aux tentatives de reconnexion. Les réglages sont conservés avec DPAPI ; le relais ne démarre pas automatiquement au prochain lancement.

3. Dans **OBS → Paramètres → Stream**, choisir **Personnalisé** :

   | Champ | Valeur |
   |---|---|
   | Serveur | `rtmp://127.0.0.1:1935/live` |
   | Clé locale de test | `stream` |

   Garder H.264, AAC, CBR, keyframes toutes les 1 s, zéro B-frame pour le test et le délai OBS désactivé. Cliquer sur **Démarrer le streaming**. Attendre **OBS : Connecté** dans Temhalo. La sortie peut encore indiquer une reconnexion tant que son récepteur local n’a pas démarré.

4. Dans **Direct**, cliquer sur **Ouvrir l’aperçu VLC**, sous les états OBS et Sorties. Temhalo démarre le récepteur local si nécessaire, attend que la sortie soit prête, puis ouvre une instance distincte de VLC sur `rtmp://127.0.0.1:1936/live/preview`. Patienter quelques secondes pour la reprise sur image clé. Le bouton reste désactivé tant que le relais ou l’entrée OBS ne sont pas prêts ; son texte indique la préparation en cours. Si VLC est absent ou si la sortie ne devient pas prête sous 30 secondes, un message explique de vérifier puis de réessayer.

5. Afficher un chrono dans OBS et relever la référence à 0 s. Attendre au moins **30 s de flux reçu** pour disposer de l’historique nécessaire, puis appliquer **15 s**, **+10 s**, **−10 s** et **retour à 0**, en attendant une lecture stable entre les commandes. Les deux augmentations font revenir le chrono en arrière et rejouent un passage, audio compris. Les deux réductions sautent en avant. Garder la même lecture VLC ouverte pendant toute la séquence : le retour à zéro doit retrouver une latence proche de la référence, sans les secondes de tampon supplémentaires observées avec le profil standard.

   Pour tester le manque d’historique, démarrer un nouveau relais et demander 30 s après seulement quelques secondes d’entrée OBS. Temhalo utilise l’historique déjà reçu puis complète le manque avec des pauses bornées. La ligne **Relecture · … d’historique disponible** dans Direct indique la réserve utilisable. La mémoire reste utilisée à 0 s puisque l’historique continue de se constituer.

Le profil applique `--network-caching=150`, `--clock-jitter=0` et `--avio-options={rtmp_buffer=0}`. Les pauses volontaires ne doivent pas devenir de la gigue compensée par un tampon qui grandit. La synchronisation des horloges A/V reste activée. Les options concernent cette instance ; les préférences enregistrées de VLC restent inchangées. Aucun port de contrôle à distance n’est ouvert.

Le bouton utilise uniquement une destination RTMP manuelle activée, en boucle locale (`127.0.0.1`, `localhost` ou `::1`), sur un port de 1024 à 65535 autre que 1935. Il privilégie une sortie locale déjà prête. Un service qui écoute déjà sur ce port est réutilisé et n’est jamais arrêté par Temhalo ; dans ce cas, il doit accepter la publication et la lecture RTMP au chemin configuré. Le récepteur créé par Temhalo accepte les chemins RTMP locaux sans enregistrer la clé dans sa configuration.

Pour voir uniquement l'entrée OBS avant le délai, ouvrir dans VLC `rtmp://127.0.0.1:1935/live/stream` pendant que le relais reçoit OBS. Ce n'est pas la sortie retardée.

En fin de test : arrêter OBS, laisser Temhalo vider son buffer, puis arrêter le relais. Temhalo arrête aussi le récepteur qu’il a créé. VLC reste sous votre contrôle et peut être fermé normalement. Avec uniquement cette destination de test activée, rien n’est publié vers Internet ; ce guide concerne VLC sur le même PC.

## Scripts facultatifs pour le développement

Les scripts existants restent disponibles : `& .\tools\Start-LocalPreview.ps1` lance le serveur de `config/mediamtx-preview.yml`, puis `& .\tools\Open-LocalPreview.ps1` ouvre le lecteur lorsque la sortie est prête. Dans ce mode, garder le terminal du serveur ouvert et l’arrêter avec **Ctrl+C** à la fin. Ne pas lancer manuellement le serveur d’ingest 1935 : Temhalo le gère.

Le lecteur accepte `-VlcPath 'D:\Applications\VLC\vlc.exe'`, `-Port 1937` ou `-PrintCommand`. Si la lecture locale devient instable, essayer `-CacheMilliseconds 300` augmente sa latence fixe sans réactiver la compensation de gigue. Le profil standard obtenu par **Ctrl+N** seul ne reprend pas ces options.

Références : [lecture RTMP avec VLC dans la documentation MediaMTX](https://mediamtx.org/docs/read/vlc#vlc-and-rtmp), [compensation de gigue et réinitialisation de l’horloge dans VLC 3](https://github.com/videolan/vlc/blob/3.0.x/src/input/es_out.c#L2368-L2394), [options de protocole AVIO](https://github.com/videolan/vlc/blob/3.0.x/modules/access/avio.c#L105-L115).

## Validation du bouton dans Temhalo

Build locale `0.7.0-preview.4+vlc-preview-button` : compilation Release réussie, 100 tests d’infrastructure réussis. Le test de l’interface utilise des réglages isolés et une destination exclusivement locale.

- Le bouton est désactivé avec une consigne lorsque le relais est arrêté, OBS déconnecté ou la destination locale absente.
- Le récepteur démarre avant OBS sur `127.0.0.1:1936` ; le délai actuel reste affiché à `00:00` pendant l’essai sans délai.
- Le clic dans Direct ouvre VLC 3.0.24 sur la mire OBS, avec les trois options corrigées vérifiées dans les arguments du processus.
- L’arrêt du relais libère les ports 1935 et 1936. Le test automatisé vérifie aussi qu’un serveur local préexistant reste sous le contrôle de son propriétaire.
- Le bouton et sa consigne restent lisibles à 320 px et à la largeur habituelle de l’application.
- Les fichiers de réglages Temhalo et de configuration OAuth sont identiques par SHA-256 avant et après l’essai.

## Validation de la relecture par défaut — 17 octobre 2026

Build locale `0.7.0-preview.4+replay-default`, OBS 32.2.2 et VLC 3.0.24. Les 51 tests Core, 10 tests de présentation et 100 tests d’infrastructure passent. Un essai logiciel utilise le même client de connexion directe que le plugin Stream Deck ; les appuis physiques restent à tester par l’utilisateur.

Avec un historique suffisant, les cibles 15 → 25 → 15 → 0 s ont donné respectivement 15,000 / 25,015 / 15,000 / 0,000 s à la sortie du moteur. Aucune phase de pause n’a été observée, et le processus FFmpeg émetteur est resté le même. La même lecture VLC est restée ouverte pendant toute la séquence. La session réelle a ensuite dépassé cinq minutes et l’indication d’historique a plafonné à 05:00.

La capture FLV de 50 s contient 1 501 paquets vidéo et 2 345 paquets audio, sans recul de DTS. Le décodage audio/vidéo avec la base de temps du flux conservée ne signale aucune erreur. Un premier contrôle vers le muxer `null` à cadence fixe signalait trois doublons de DTS dus à l’arrondi au trentième de seconde aux points de coupe ; ce contrôle a été remplacé par `-fps_mode passthrough -enc_time_base demux`, adapté aux timestamps FLV en millisecondes. Ces mesures ne constituent pas une validation de Twitch, YouTube ou Kick.

## Validation locale du 17 octobre 2026

Quatre appuis physiques Stream Deck, OBS 32.2.2, VLC 3.0.24 et Temhalo 0.7.0-preview.4, sur une mire 720p30 H.264/AAC à 4 Mbit/s. La publication OBS a duré environ 8 min 50. La même connexion de lecture VLC est restée ouverte pendant tous les changements.

| Commande | Délai du relais, médiane | Décalage total observé dans VLC |
|---|---|---|
| 15 s | 15,043 s | 15,25 à 15,34 s |
| +10 s | 25,043 s | 25,32 s |
| −10 s | 15,043 s | 15,78 s |
| Retour à 0 | 0,034 s | 0,59 à 1,17 s ; derniers relevés stables autour de 1,15 s |

Le tampon résiduel d’environ 5,8 s du profil standard n’a pas été reproduit. Aucune réouverture du flux VLC entre les quatre commandes. Les deux réductions reprennent sur une keyframe ; aucun retour de timestamp audio ou vidéo ni erreur de décodage dans la capture. Les médianes de synchronisation A/V varient de 20 ms entre le début et la fin, sans dérive progressive dans les portions stables.

Le lecteur peut écarter des images pendant ses recalages : son compteur cumulatif n’est pas nul. Ce compteur reste constant entre les deux derniers relevés stables ; aucun tampon audio perdu n’est signalé. Le profil privilégie le rattrapage lors des changements. Ce résultat local ne garantit pas le comportement d’un lecteur de plateforme ni une latence absolue nulle.

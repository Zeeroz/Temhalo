# Voir le flux retardé de Temhalo dans VLC

Le premier serveur MediaMTX (port 1935) reçoit OBS. Un second serveur, limité à la boucle locale et au chemin `live/preview`, remplace la plateforme pour ce test. La chaîne passthrough de Temhalo reste identique ; seul son destinataire change.

```text
OBS → MediaMTX :1935 → Temhalo et son délai → MediaMTX :1936 → VLC
```

1. Arrêter le relais Temhalo s'il tourne, puis ouvrir PowerShell à la racine du projet et lancer :

   ```powershell
   & .\tools\Start-LocalPreview.ps1
   ```

   Garder ce terminal ouvert. Le script utilise le MediaMTX déjà téléchargé par `tools/native/Get-NativeTools.ps1` et `config/mediamtx-preview.yml`. Le port 1936 doit être libre : arrêter notamment l'ancien récepteur FFmpeg `-listen 1` du guide de l'étape 4 si on l'utilisait. Ne pas lancer manuellement le serveur d'ingest 1935 : Temhalo le gère.

2. Dans **Temhalo → Destination**, choisir **Plateforme RTMP / RTMPS**, **Personnalisé** :

   | Champ | Valeur |
   |---|---|
   | Serveur | `rtmp://127.0.0.1:1936/live` |
   | Clé locale de test | `preview` |

   Choisir un délai initial de 0 s et cliquer sur **Démarrer le relais**. Depuis l'étape 5, **Enregistrer la destination** permet de conserver ces réglages avec DPAPI pour le prochain lancement, sans démarrer automatiquement le relais.

3. Dans **OBS → Paramètres → Stream**, choisir **Personnalisé** :

   | Champ | Valeur |
   |---|---|
   | Serveur | `rtmp://127.0.0.1:1935/live` |
   | Clé locale de test | `stream` |

   Garder H.264, AAC, CBR, keyframes toutes les 1 s, zéro B-frame pour le test et le délai OBS désactivé. Cliquer sur **Démarrer le streaming**. Attendre **Entrée OBS : Connecté** et **Émission confirmée** dans Temhalo.

4. Dans VLC : **Média → Ouvrir un flux réseau** (**Ctrl+N**), saisir :

   ```text
   rtmp://127.0.0.1:1936/live/preview
   ```

   Cliquer sur **Lire**. Ouvrir VLC une fois la publication active ; sinon relancer la lecture après son démarrage.

5. Afficher un chrono dans OBS, appliquer 15 s dans Temhalo, attendre la fin de l'augmentation par paliers, puis revenir à 0 s. L'image peut se figer pendant les pauses prévues. VLC ajoute sa propre mise en cache : le délai observé comprend celle-ci et les latences OBS/FFmpeg. Si VLC conserve du retard après le retour à zéro, arrêter puis rouvrir le flux pour vider son buffer.

Pour réduire la latence propre au lecteur, on peut essayer 300 ms dans **Outils → Préférences → Afficher les paramètres : Tous → Entrée / Codecs → Cache réseau (ms)**, enregistrer puis rouvrir le flux. C'est un réglage de test ; augmenter la valeur si la lecture devient instable. Il ne supprime pas le délai du moteur.

Pour voir uniquement l'entrée OBS avant le délai, ouvrir dans VLC `rtmp://127.0.0.1:1935/live/stream` pendant que le relais reçoit OBS. Ce n'est pas la sortie retardée.

En fin de test : arrêter OBS, laisser Temhalo vider son buffer, arrêter le relais, puis faire **Ctrl+C** dans le terminal du serveur d'aperçu. Les deux serveurs n'écoutent que sur `127.0.0.1` : rien n'est publié vers Internet et ce guide concerne VLC sur le même PC.

Référence : [lecture RTMP avec VLC dans la documentation MediaMTX](https://mediamtx.org/docs/read/vlc#vlc-and-rtmp).

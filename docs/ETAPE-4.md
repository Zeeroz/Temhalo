# Interface native Temhalo — étape 4

> Livraison historique validée. L'étape 5 ajoute la persistance DPAPI, les raccourcis et la conservation du buffer entre reconnexions. Pour le fonctionnement courant et les emplacements des logs, consulter [ETAPE-5.md](ETAPE-5.md).

L'application WinUI 3 pilote le superviseur et le moteur des étapes précédentes. Elle présente les données reçues pendant la session : aucun scénario simulé ne fournit les compteurs du dashboard. La maquette HTML de l'étape A reste un document graphique indépendant.

## Écran et commandes

Le dashboard reprend BRAND.md : fonds Nuit/Ardoise, menthe Halo pour les commandes, lavande Écho pour la cible, Space Grotesk pour les titres, Inter pour l'interface et JetBrains Mono pour les compteurs. Les fontes et leurs licences sont embarquées ; aucune installation de police ni requête à Google Fonts n'est nécessaire.

La navigation supérieure donne accès au tableau de bord, à la destination et au journal. Le bouton de démarrage/arrêt reste accessible dans les trois vues. Sous 920 DIP utiles, le suivi passe sous la commande du délai. Les presets reviennent à la ligne ; en très petite largeur, les valeurs et l'en-tête s'empilent et la navigation utilise son menu de débordement natif. La page défile verticalement.

- Le slider prépare une valeur entière entre 0 et 300 s. Le NumberBox partage ce brouillon ; valider une saisie avec Entrée ou quitter le champ la synchronise avec le slider.
- **Appliquer** envoie le brouillon au `DelayController`. Modifier un brouillon ne modifie pas la cible appliquée.
- Les presets **0, 15, 30 et 60 s** envoient immédiatement leur valeur. **Personnalisé** place le focus dans le champ.
- Une valeur invalide, vide, fractionnaire ou hors limites déclenche une aide d'erreur et empêche son application. La télémétrie ne remplace pas le brouillon.
- Pendant l'arrêt et la vidange, les commandes sont désactivées. Fermer la fenêtre demande d'abord la vidange au délai atteint ; la fenêtre reste ouverte jusqu'à la fin. Cela peut prendre environ cinq minutes avec un buffer rempli à 300 s. Ne pas terminer de force le processus si l'on souhaite conserver la fin du fichier.

L'augmentation reste découpée en pauses de 5 s au maximum par défaut, avec reprise intermédiaire et IDR. Les réductions et le retour à zéro conservent les règles de [l'étape 3](ETAPE-3.md). Aucun réencodage n'est ajouté au relais.

## Sens des indicateurs

| Indicateur | Source et limite |
|---|---|
| Délai actuel | Âge du dernier tag émis par le moteur, affiché en minutes/secondes. Pendant une pause, il reste la dernière mesure connue ; l'état décrit l'ajout en cours. |
| Cible appliquée | Dernière cible envoyée au moteur, distincte du brouillon. Avant démarrage, elle définit le remplissage initial. |
| Entrée OBS | Connexion du publisher sur `live/stream`, suivie dans les diagnostics JSON de la version MediaMTX fixée. Une connexion de lecture ou de contrôle ne suffit pas. |
| Sortie | En attente tant que l'émetteur n'a pas confirmé de progression média ; puis Écriture active ou Émission confirmée. Cela ne confirme pas la lecture par un spectateur. |
| Débits | Différences de compteurs FLV entrant dans Core et sortant vers stdin FFmpeg, sur environ une seconde. Incluent le framing FLV ; excluent l'overhead réseau. Une pause peut afficher 0 en sortie. |
| Durée du live | Temps depuis la première publication observée dans la session du relais, y compris les pauses et interruptions suivantes. |
| Buffer RAM | Budget du moteur, avec configurations et stockage de file ; distinct de la RAM totale du processus et des FFmpeg. Alerte à 80 %. |

L'UI se rafraîchit à environ 1 Hz. Le moteur conserve son horloge monotone et ses échéances indépendantes du dispatcher. La latence totale comporte aussi OBS, les FFmpeg, la plateforme et le lecteur. Zéro ne rappelle pas le média déjà transmis à ces composants.

## Destination et clé

Le choix initial reste **Fichier de test local (FLV)**. Chaque démarrage crée un dossier `artifacts/relay-ui/run-…/` ; chaque nouvelle paire FFmpeg y produit son propre fichier. Le journal permet d'ouvrir ce dossier.

La sortie **Plateforme RTMP / RTMPS** propose Twitch, YouTube, Kick et Personnalisé. Le nom choisi sert au libellé ; copiez l'URL d'ingest actuelle fournie par la plateforme, sans y inclure la clé. Aucune URL de plateforme n'est choisie automatiquement. La clé est saisie séparément dans un PasswordBox. Le démarrage publie vers l'URL et la clé indiquées ; modifier une destination exige d'arrêter le relais.

Le serveur doit être une URL RTMP/RTMPS sans identifiants, paramètres ni fragment. La clé ne doit être ni vide ni contenir des espaces. Les erreurs sont affichées sans reprendre la clé. Les arguments natifs ne sont pas journalisés ; les diagnostics masquent les URL et la valeur exacte de la clé.

**Toute la configuration reste en mémoire dans cette fenêtre.** Ni l'URL ni la clé ne sont sauvegardées par cette étape. Fermer puis rouvrir l'application rétablit la sortie fichier et vide la clé. Le stockage DPAPI reste prévu à l'étape 5. Comme dans toute exécution FFmpeg RTMP avec clé dans l'URL, un outil système autorisé à inspecter les processus peut lire les arguments du processus ; DPAPI protège le stockage sur disque, pas cette commande d'exécution.

La limite RAM est réglable entre 1 et 4096 Mio avant démarrage. Un budget insuffisant arrête la session avec erreur explicite ; il ne réduit pas silencieusement le délai.

## Préparation et lancement

Depuis la racine extraite du projet, avec les prérequis de l'étape 0 :

```powershell
dotnet restore Temhalo.sln -p:Platform=x64 --locked-mode
dotnet build Temhalo.sln --no-restore -c Debug -p:Platform=x64
dotnet test Temhalo.sln --no-build --no-restore -c Debug -p:Platform=x64
& .\tools\native\Get-NativeTools.ps1
& .\tools\Start-Temhalo.ps1
```

L'application n'ouvre pas d'ingest avant le clic sur **Démarrer le relais**. Arrêter RelayHost et toute autre instance utilisant le port 1935 avant ce clic. Un port occupé provoque une erreur visible ; Temhalo ne réutilise ni n'arrête le serveur appartenant à un autre processus.

Les binaires et le fichier de résolution natif restent sous `artifacts/`. Pour cette livraison de sources, l'application cherche `Temhalo.sln` depuis son exécutable ou le dossier courant ; conserver l'arborescence extraite. La distribution indépendante du dépôt appartient à l'étape 6.

## Test fonctionnel local avec OBS

1. Garder **Fichier de test local**, limite 512 Mio, et démarrer le relais.
2. Publier avec le profil OBS validé : serveur `rtmp://127.0.0.1:1935/live`, clé locale `stream`, H.264 CBR, AAC, keyframe 1 s, délai OBS désactivé. Commencer avec zéro B-frame.
3. Attendre Entrée OBS **Connecté**, Sortie **Écriture active**, puis des débits non nuls. L'analyse initiale des processus natifs peut ajouter quelques secondes d'attente.
4. Saisir 45 s puis valider le champ avec Entrée : le slider doit suivre, la cible appliquée rester inchangée. Glisser ensuite le slider à 5 s et cliquer sur Appliquer. Observer la pause puis **Délai atteint** et une mesure proche de 5 s.
5. Cliquer sur 15 s, attendre la convergence, puis préparer et appliquer 5 s. Le retour se fait sur IDR. Cliquer enfin sur 0 s et observer le rattrapage. Tester aussi 0 pendant une augmentation en cours.
6. Préparer 301, -1, une fraction et une saisie vide : aucune ne doit être envoyée au moteur. Après validation du champ, une erreur apparaît et Appliquer est désactivé. Un preset valide doit permettre de repartir.
7. Préparer un brouillon de 42 s sans l'appliquer et attendre plusieurs rafraîchissements : il doit rester 42 s. Changer de vue puis revenir ; le brouillon doit rester présent.
8. Pendant la session, vérifier que les champs de destination sont désactivés et que le journal reçoit des événements. Les presets restent utilisables hors vidange.
9. Couper OBS avec un buffer rempli : l'entrée passe à Déconnecté et le contenu complet se vide au délai atteint. Arrêter ensuite le relais, attendre la finalisation et lire le FLV dans VLC. Vérifier le mouvement, le son et un clap visible/audible.
10. Refaire un essai, remplir 5 s puis fermer la fenêtre pendant la publication : elle doit afficher Vidange en cours, rester réactive, puis se fermer sans processus enfant restant.

Une mire synthétique permet de vérifier l'UI sans OBS. Démarrer le relais puis lancer dans un autre terminal :

```powershell
& .\tools\Send-TestPattern.ps1 -Seconds 120
```

L'encodage H.264/AAC sert uniquement à fabriquer la source de test. La publication de cette source et la chaîne Temhalo utilisent `-c copy`. Laisser le script finir, attendre la vidange, puis arrêter le relais. Si l'on ferme volontairement le relais avant la source, celle-ci peut signaler une connexion interrompue : c'est la coupure attendue de ce test.

## Test de la destination RTMP en boucle locale

Pour regarder la sortie retardée **en direct dans VLC**, suivre le [guide d'aperçu local](APERÇU-LOCAL-VLC.md), qui utilise un second serveur MediaMTX sur le port 1936. Le récepteur FFmpeg ci-dessous sert à enregistrer la sortie dans un fichier ; il ne sert pas de serveur de lecture pour VLC. Choisir l'un de ces deux tests, car ils utilisent le même port.

Ce contrôle ne nécessite aucun compte et ne transmet qu'une mire à `127.0.0.1`.

1. Dans un terminal à la racine, lancer un récepteur FFmpeg sur un port local libre :

   ```powershell
   $native = Get-Content .\artifacts\native-tools.resolved.json -Raw | ConvertFrom-Json
   $ffmpeg = ($native.Tools | Where-Object Name -eq 'FFmpeg').Executable
   $received = Join-Path (Get-Location) ('artifacts\received-' + [Guid]::NewGuid().ToString('N') + '.flv')
   & $ffmpeg -hide_banner -loglevel warning -listen 1 -i rtmp://127.0.0.1:1936/live/temhalo-ui-test -c copy -f flv -n $received
   ```

2. Dans Destination, choisir Plateforme RTMP / RTMPS, Personnalisé, serveur `rtmp://127.0.0.1:1936/live`, clé fictive `temhalo-ui-test`. Démarrer le relais puis la mire dans un troisième terminal.
3. Vérifier **Émission confirmée**, appliquer 5 s, attendre la reprise puis arrêter proprement le relais. Le récepteur peut signaler la fin de connexion ; contrôler surtout son fichier finalisé.
4. Décoder le fichier reçu :

   ```powershell
   & $ffmpeg -v error -xerror -i $received -map 0:v:0 -map 0:a:0 -f null NUL
   ```

Ce contrôle valide le raccordement RTMP local et le décodage, pas les timeouts des plateformes ni TLS/RTMPS. Pour un essai réel, remplacer l'URL et la clé par celles de sa propre plateforme, puis vérifier son tableau de bord et la lecture depuis un second appareil. Les pauses par paliers ne garantissent pas le maintien de la session distante.

## Relecture visuelle et accessibilité

- Vérifier 1280 × 820, puis 720 × 640 ; maximiser et restaurer la fenêtre. Le contenu doit utiliser la largeur disponible, les commandes rester accessibles par défilement vertical et les deux colonnes se rétablir.
- Pour ouvrir directement une petite fenêtre : `& .\tools\Start-Temhalo.ps1 -WindowSize 720x640`. Le format 320x640 vérifie aussi le menu de débordement et l'empilement des valeurs.
- Parcourir avec Tab et Maj+Tab. Tester les flèches/Home/End du slider, le NumberBox, les presets et la navigation. Les états et erreurs ont un texte, indépendamment de leur couleur.
- Sur le poste de validation, contrôler les échelles Windows 100/125/150/200 %, le texte agrandi, un thème de contraste Windows et Narrateur. Ces contrôles système et lecteur d'écran restent à effectuer par l'utilisateur ; la palette comporte des ressources de contraste système et les contrôles conservent leurs pairs d'automatisation natifs.
- Pendant une pause, vérifier le texte de transition ; le compteur actuel n'invente pas de nouvelle mesure. Aucun aperçu vidéo, glow ou animation décorative du logo n'est ajouté.

Les captures réelles de l'application sont dans `design/native/`. Les dimensions des PNG peuvent être légèrement inférieures à la taille extérieure demandée, car la capture omet les bordures invisibles Windows.

## Architecture et validation

`Temhalo.Presentation` est une bibliothèque .NET 8 sans WinUI, pour le brouillon, les commandes et la projection des métriques. `Temhalo.Presentation.Tests` vérifie les comportements de saisie et l'indépendance vis-à-vis de la télémétrie. Ces deux projets utilisent les versions NuGet déjà fixées ; aucun nouveau fournisseur de dépendance n'est ajouté. Core conserve le traitement des médias ; Infrastructure conserve la supervision ; `App/Services/RelaySession` compose les services et `App/Views/DashboardPage` gère la vue.

La compilation Debug x64 et 45 tests unitaires passent. La régression dynamique native de l'étape 3 passe également. L'UI a été pilotée sur une source réelle synthétique vers un fichier local, puis vers un récepteur RTMP local. Les fichiers finalisés ont été décodés sans erreur. Le [rapport de validation](validation/etape-4.json) précise les mesures, les captures et les limites.

Les raccourcis globaux, la persistance DPAPI et la conservation du buffer au-delà d'un crash de l'émetteur restent à l'étape 5. L'API locale, le restream et le mode fluide restent en V2. Les essais OBS de 30 minutes, les pannes réseau des plateformes et la mesure CPU du produit complet restent à réaliser. **L'étape 5 attend le retour utilisateur.**

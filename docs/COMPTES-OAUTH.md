# Temhalo — Connexion des comptes et procédure de test

Cette version implémente le parcours WinUI **Vos plateformes → Relier OBS → Lancer
votre live**, ainsi que les connecteurs de comptes Twitch, YouTube et Kick. Les clés
manuelles restent disponibles. Les applications développeur ne sont pas encore
enregistrées : les essais OAuth réels nécessitent la configuration ci-dessous.

## Lancer et vérifier la version

Depuis la racine du projet, avec le SDK défini dans `global.json` :

```powershell
& .\tools\Test-Accounts.ps1
& .\tools\Start-Temhalo.ps1 -Configuration Release
```

Le premier script restaure les dépendances verrouillées, compile en Release x64 et
exécute les tests. Les résultats TRX sont dans `artifacts/validation/accounts`.
Le script de lancement existant utilise la version compilée ; arrêter l'application
avant de recompiler. L'ancien installeur n'est pas mis à jour par cette opération.

Les tests automatisés utilisent des réponses HTTP fictives, de vrais retours HTTP
sur loopback et DPAPI Windows. Ils ne se connectent à aucun compte réel et ne lancent
aucune diffusion. Ils couvrent notamment PKCE/state, annulation, contrôle du client
Twitch, attente Device Code, rotation des jetons, sélection YouTube, lecture du flux
Kick, erreurs sans secrets et restrictions du service Kick.

## Configuration des applications développeur

Copier `config/oauth-clients.example.json` sous le nom **oauth-clients.json** dans
`%LOCALAPPDATA%\Temhalo`. Pour une session `--settings-directory`, placer le fichier
dans ce répertoire isolé. Un fichier public livré à côté de l'exécutable peut aussi
être utilisé ; le fichier utilisateur est prioritaire. Redémarrer Temhalo après
modification. Aucun identifiant d'une autre application ne doit être réutilisé.

Le lanceur de développement accepte aussi `config/temhalo.launch.local.json` (local,
exclu de Git) avec `Version: 1` et `SettingsDirectory` contenant un chemin absolu.
Ce profil fixe le dossier des réglages et des clients OAuth quand Windows redirige
LocalAppData selon le contexte de lancement. `-SettingsDirectory` explicite reste
prioritaire. Le profil ne contient aucun jeton ni clé ; aucun fichier de compte n'est
déplacé ou effacé par le lanceur. Il concerne la version de travail, pas l'installeur.

| Champ | Valeur à renseigner |
| --- | --- |
| TwitchClientId | Client ID d'une application Twitch de type Public |
| YouTubeClientId | Client ID Google de type Application de bureau |
| YouTubeDesktopClientSecret | Valeur du JSON **Desktop** Google si nécessaire ; jamais un secret d'application Web |
| KickClientId | Client ID de l'application Temhalo enregistrée chez Kick |
| KickBrokerUrl | Origine HTTPS du service Kick Temhalo, sans `/oauth/token` |
| KickCallbackPort | 18473 par défaut ; identique à celui enregistré chez Kick et configuré sur le service |

Ce fichier contient la configuration des clients, pas les jetons des utilisateurs.
Les jetons et les clés mémorisés sont dans `settings.dpapi`, chiffré pour le compte
Windows courant. Aucun mot de passe de plateforme n'est saisi dans Temhalo. Un
fichier malformé ou une plateforme non configurée laisse la saisie manuelle utilisable.

### Twitch

1. Enregistrer Temhalo dans la [console Twitch](https://dev.twitch.tv/console/apps),
   avec un client **Public**. Compléter les champs requis par la console.
2. Reporter son Client ID. Aucun secret Twitch n'est embarqué.
3. Temhalo utilise le [Device Code Flow officiel](https://dev.twitch.tv/docs/authentication/getting-tokens-oauth/)
   et demande seulement `channel:read:stream_key`.
4. Cliquer **Connecter mon compte** : le navigateur ouvre Twitch ; confirmer le code
   affiché dans Temhalo. Le compte n'est ajouté qu'après validation de l'autorisation.

Temhalo vérifie le client, le compte et le scope avec `/validate` à la connexion,
avant le démarrage et toutes les 55 minutes pendant une session OAuth. Les jetons de
renouvellement publics peuvent tourner : une nouvelle paire est sauvegardée avant
la requête suivante. Une révocation demande de reconnecter le compte. Le serveur
d'émission utilise l'ingest automatique officiel Twitch, sans imposer une ville ;
en clé manuelle, son adresse est modifiable.

### YouTube

1. Créer un projet Google Cloud et activer **YouTube Data API v3**.
2. Configurer l'écran de consentement et les utilisateurs de test tant que le projet
   est en mode test. Créer un client OAuth **Application de bureau**, puis recopier
   les champs du JSON téléchargé dans la configuration ci-dessus.
3. Le parcours emploie le navigateur système, PKCE S256, `state` aléatoire et un retour
   HTTP éphémère sur **127.0.0.1**. Le scope demandé est `youtube.readonly`.
4. Activer le streaming sur la chaîne et préparer un live dans YouTube Studio, avec
   un flux associé. Après connexion, choisir ce live dans Temhalo. **Actualiser les
   lives** recharge la liste ; l'ajout reste désactivé sans sélection.

Les lives terminés/révoqués et les événements sans flux associé sont exclus. Temhalo
ne crée aucun événement, ne change aucun titre et n'appelle pas `transition` : les
options de démarrage automatique de YouTube Studio gardent leur effet habituel.
La clé RTMP est récupérée depuis le flux associé au live choisi, juste avant le
démarrage. RTMPS est préféré lorsqu'il est fourni par l'API. L'accès peut nécessiter
la vérification du projet Google avant une distribution publique. Les comptes de
test Google peuvent avoir des jetons de renouvellement de courte durée.

Sources : [OAuth installé](https://developers.google.com/identity/protocols/oauth2/native-app),
[liste des lives](https://developers.google.com/youtube/data/live/reference/rest/v3/liveBroadcasts/list),
[flux et ingestion](https://developers.google.com/youtube/data/live/reference/rest/v3/liveStreams).

### Kick et service de connexion fourni

Kick requiert actuellement `client_secret` pour l'échange et le renouvellement, même
avec PKCE. Le projet **services/Temhalo.AccountBroker** garde ce secret sur le serveur.
La partie média continue à aller directement du PC à la plateforme : ce service ne
reçoit aucune vidéo, aucun buffer ni aucune clé RTMP. Il traite temporairement le
code/verifier ou le jeton de renouvellement et renvoie les jetons au PC, sans stockage.

1. Enregistrer Temhalo chez Kick avec le retour exact :
   **http://localhost:18473/oauth/callback**. Employer `localhost` pour éviter le
   problème de réécriture de `127.0.0.1` documenté par Kick.
2. Configurer le serveur avec les variables d'environnement `Kick__ClientId`,
   `Kick__ClientSecret`, `Kick__CallbackPort`. Fournir le secret via le gestionnaire
   de secrets de l'hébergeur ; ne pas le mettre dans un fichier livré avec Temhalo.
3. Compiler/publier le service :

```powershell
dotnet publish .\services\Temhalo.AccountBroker -c Release -o .\artifacts\account-broker
```

4. Déployer ce dossier sur un hôte ASP.NET Core 8, derrière HTTPS, avec ces variables.
   Exposer `POST /oauth/token` et `GET /health`. L'adresse de retour est fixée côté
   serveur ; aucun endpoint, secret ou URI fourni par le client n'est relayé librement.
5. Reporter le même Client ID, le port et l'URL HTTPS du service dans le fichier local.
   Temhalo demande `channel:read streamkey:read`, ouvre le site officiel Kick, puis
   récupère l'URL et la clé via l'API publique Channels.

Pour un essai de développeur, le service peut écouter sur
`http://127.0.0.1:18474` (`ASPNETCORE_URLS`) ; c'est l'unique exception HTTP côté client.
Son secret reste dans l'environnement de ce processus local. Le port 18473 doit être
libre pendant le consentement. Aucun service n'a été déployé sur Internet par cette
implémentation. La limitation par IP (30 requêtes/minute), l'absence de CORS et les
restrictions des requêtes sont fournies ; lors du déploiement, conserver HTTPS, une
protection contre l'abus et désactiver la capture des corps/requêtes dans le proxy,
l'APM et l'hébergeur. Le service applicatif ne journalise pas les requêtes ni les corps.

Sources : [OAuth Kick](https://docs.kick.com/getting-started/generating-tokens-oauth2-flow),
[API Channels et streamkey:read](https://docs.kick.com/apis/channels).

## Procédure de test dans l'application

### Sans application développeur — utilisable immédiatement

1. Ouvrir **Diffusion**. Vérifier navigation supérieure, absence de bloc logo ajouté,
   puis **Twitch → Connecter mon compte**. Une aide doit annoncer la configuration
   manquante, sans faux succès ni ouverture d'une page de connexion Temhalo.
2. Choisir **Utiliser une clé manuellement**. Le serveur Twitch est prérempli ; sur
   YouTube le serveur RTMPS est prérempli ; sur Kick, renseigner serveur et clé.
3. Tester **Autre serveur RTMP** avec `rtmp://127.0.0.1:1936/live` et la clé `preview`,
   ou utiliser **Options → Préparer la destination locale**. Aucun véritable identifiant
   n'est nécessaire. Lancer `tools/Start-LocalPreview.ps1` pour la lecture VLC.
4. Annuler une modification après changement de plateforme : la destination et sa
   méthode enregistrées doivent rester intactes ; aucune clé ne doit changer de plateforme.
5. Activer/désactiver une destination, en ajouter plusieurs (maximum quatre), revenir
   dans le formulaire et vérifier la persistance après redémarrage.
6. Continuer vers OBS, copier le serveur et la **clé locale stream**, confirmer le
   guide. La case ne doit pas afficher « OBS connecté ».
7. À l'étape 3, **Activer Temhalo**, puis démarrer OBS avec H.264/AAC, CBR, images clés
   toutes les secondes. Ouvrir `rtmp://127.0.0.1:1936/live/preview` dans VLC. Les états
   reçu/émis doivent suivre le média réel. Tester 0 → 15 → 30 → 0 secondes et l'arrêt.
8. En mode test FLV local (Options), vérifier qu'aucune destination de plateforme
   n'est contactée. Si le port 1935/1936 est occupé, arrêter soi-même le serveur en
   conflit ou utiliser l'environnement de test approprié.

### Avec les applications développeur configurées

1. Pour Twitch, YouTube et Kick : accepter une connexion dans le navigateur, puis
   ajouter la destination. Le profil doit être affiché ; pour YouTube, sélectionner
   un live. Le compte lié ne doit pas être indiqué comme « Émission active ».
2. Refuser le consentement, annuler dans Temhalo et fermer le formulaire. Aucun
   remplacement de la destination précédente ne doit être enregistré.
3. Passer à la clé manuelle, refermer/rouvrir cette option et changer de plateforme :
   le brouillon propre à chaque plateforme doit être conservé dans ce formulaire.
4. Redémarrer Temhalo avec mémorisation cochée : la connexion est conservée. Sans
   mémorisation, seul le profil est conservé et le compte doit être reconnecté au
   prochain lancement. Les jetons ne sont pas récupérables par un autre compte Windows.
5. Démarrer un live de test autorisé et vérifier le choix de chaîne/live, l'audio,
   l'image et la reconnexion du relais. Les étapes d'autorisation et de diffusion
   peuvent avoir des effets réels : choisir volontairement son canal et son live.
6. Révoquer Temhalo depuis la plateforme : le prochain contrôle/pré-démarrage doit
   demander une reconnexion. Une émission RTMP déjà lancée peut continuer avec sa clé ;
   la révocation OAuth n'est pas une commande d'arrêt du live.
7. Redimensionner la fenêtre (compacte puis maximisée), parcourir les étapes et les
   formulaires avec Tab/Entrée/Échap, vérifier le scroll vertical et les états désactivés.

**Limite de validation actuelle :** les applications développeur n'étant pas créées,
les consentements, quotas et émissions sur les véritables plateformes restent à
valider après leur enregistrement. La procédure ci-dessus ne prétend pas les avoir exécutés.

## Conservation et retrait

La migration garde les anciennes destinations manuelles et leur chiffrement. **Retirer**
une destination oublie localement ses clés et jetons ; cela ne révoque pas l'autorisation
sur le site de la plateforme. Pour révoquer l'accès, utiliser les applications connectées
du compte Twitch/Google/Kick. Les profils/titres restent locaux ; aucun journal n'est
envoyé automatiquement. Les vieux guides et installateurs sont conservés.

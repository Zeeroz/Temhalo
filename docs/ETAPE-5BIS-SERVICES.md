# Temhalo — étape 5 bis : multistream, overlay et dock OBS

> Archive de l'étape 5 bis. L'overlay est retiré des sources actuelles et Stream Deck se connecte directement à l'application. Consultez le [guide actuel](CONTROLES-LOCAUX.md) ; les instructions d'overlay ci-dessous ne s'appliquent plus.

Les trois raccordements sont implémentés sur l'interface validée : un délai commun, jusqu'à quatre destinations, une source navigateur d'overlay et un dock de commande. Navigation supérieure et identité Temhalo conservées. **L'étape 6 n'est pas commencée.**

## Préparer et lancer

Il faut désormais le **runtime ASP.NET Core 8 x64**, en plus du runtime .NET 8 et des prérequis Windows existants. Il fournit Kestrel ; aucune dépendance payante ni règle de pare-feu supplémentaire. Le runtime est déjà installé sur le poste de validation. Le packaging de ces prérequis appartient à l'étape 6.

```powershell
dotnet --list-runtimes
dotnet restore Temhalo.sln -p:Platform=x64 --locked-mode
dotnet build Temhalo.sln -c Debug -p:Platform=x64 --no-restore
& .\tools\native\Get-NativeTools.ps1
& .\tools\Start-Temhalo.ps1
```

Le serveur navigateur démarre avec l'application sur **127.0.0.1:43120**. Le relais média démarre uniquement sur commande. Le port **1935** reste réservé à l'entrée OBS et doit être libre.

Dans **Réglages → Confidentialité**, l'état de l'accès OBS et son port sont affichés. En cas de conflit sur 43120, choisir un autre port puis **Appliquer le port / relancer l'accès OBS**. Cela ne redémarre pas le relais média. Un changement de port demande de recopier les liens dans OBS.

## Configurer plusieurs destinations

1. Dans **Diffusion**, choisir **RTMP / RTMPS**.
2. Cliquer **Ajouter une destination**, renseigner nom, plateforme, serveur et clé, puis cocher **Activer cette destination**.
3. Répéter pour les autres sorties, au maximum quatre. Les cartes permettent de sélectionner celle à modifier.
4. **Enregistrer la destination** enregistre l'ensemble de la liste. Chaque destination choisit séparément si sa clé doit être conservée. Les clés conservées et les liens privés OBS sont dans le document DPAPI existant, jamais dans un JSON en clair.
5. Démarrer le relais puis publier OBS comme auparavant : serveur `rtmp://127.0.0.1:1935/live`, clé locale `stream`, H.264/AAC, CBR, images clés toutes les secondes.

Seules les sorties activées publient. Aucune destination active produit une erreur explicite. La configuration des sorties est verrouillée pendant une session. Les destinations et clés de l'ancienne configuration sont reprises ; leur liste est conservée lors de l'enregistrement. Choisir **Fichier FLV local** conserve la liste RTMP pour un essai ultérieur.

Le dashboard et les cartes indiquent les sorties actives/en reconnexion. Le débit sortant agrège les octets fournis aux émetteurs. Ce compteur n'est pas un acquittement par les plateformes. Le débit montant nécessaire croît avec le nombre de sorties : deux copies d'un flux à 8 Mbit/s demandent environ 16 Mbit/s, hors audio et surcoût du transport.

## Test local simultané avec VLC

Deux terminaux, après préparation des outils natifs :

```powershell
# Terminal A
& .\tools\Start-LocalPreview.ps1 -Port 1936
```

```powershell
# Terminal B
& .\tools\Start-LocalPreview.ps1 -Port 1937
```

Ces serveurs restent sur la boucle locale. Dans Temhalo, ajouter et activer :

| Nom | Serveur | Clé locale | URL à ouvrir dans VLC |
|---|---|---|---|
| Local A | `rtmp://127.0.0.1:1936/live` | `preview` | `rtmp://127.0.0.1:1936/live/preview` |
| Local B | `rtmp://127.0.0.1:1937/live` | `preview` | `rtmp://127.0.0.1:1937/live/preview` |

Enregistrer, démarrer le relais, puis publier OBS. Une mire synthétique peut remplacer OBS :

```powershell
& .\tools\Send-TestPattern.ps1 -Seconds 120
```

Ouvrir les deux URLs dans deux instances VLC. Appliquer 15 s, attendre les paliers, puis réduire à 5 s et à 0 s. Vérifier image, son, évolution du délai et états des deux sorties. Le cache VLC ajoute sa propre latence ; comparer avec le chronomètre OBS et avec une mesure de référence à 0 s.

Arrêter volontairement le serveur de réception A avec Ctrl+C : B doit continuer, A doit annoncer sa reconnexion. Relancer le terminal A ; rouvrir son flux dans VLC si le lecteur a fermé la connexion. Après la reprise, réappliquer la cible ou 0 s pour rattraper le retard conservé. Arrêter Temhalo avant les serveurs de réception pour laisser finir la vidange.

## Ajouter l'overlay à OBS

1. Dans **Overlay**, choisir Signature, Repère, Fil du temps ou Chrono ; régler position, taille, accent, libellé et visibilité à 0 s.
2. Cliquer **Enregistrer l'overlay** : l'aperçu natif reste une simulation ; seul l'enregistrement applique cet habillage à la source navigateur.
3. Cliquer **Copier le lien privé de l'overlay**. Le lien contient un accès en lecture, conservé avec DPAPI et stable après redémarrage.
4. Dans OBS, ajouter une **Source navigateur**, désactiver « Fichier local », coller le lien, régler **1920 × 1080**. Conserver le CSS transparent par défaut ; ne pas ajouter de fond opaque.
5. Vérifier le badge par-dessus la scène, puis changer de composition et enregistrer dans Temhalo : la source doit suivre sans changer d'URL.

Les ressources graphiques et les polices sont servies par Temhalo, sans CDN. La racine et le corps de la page sont transparents ; le badge Signature conserve son fond Nuit. Pour garder la source chargée lors des changements de scène, désactiver sa fermeture lorsqu'elle n'est pas visible. Les propriétés sont documentées par [OBS — Browser Source](https://obsproject.com/kb/browser-source).

**Placement important :** cette source est capturée dans OBS, avant le buffer. Elle subit le même délai que l'image. Les changements de badge apparaîtront donc au public avec ce décalage, et l'image reste figée pendant les pauses du passthrough. Ce raccordement ne compose pas un indicateur après le buffer. Le délai affiché est le délai local commun ; une destination en reprise peut avoir un retard supplémentaire, indiqué séparément dans Temhalo et dans le dock.

## Ajouter le dock à OBS

1. Dans **Diffusion → Depuis OBS**, cliquer **Copier le lien privé du dock**.
2. Dans OBS : **Docks → Docks de navigateur personnalisés** ; nommer le dock « Temhalo », coller le lien et appliquer. L'emplacement du menu dépend de la version/langue d'OBS.
3. Le dock propose délai mesuré, cible, presets enregistrés, saisie/slider, protection, direct, +10 s, −10 s et états des destinations.

Vérifier successivement :

- Preset 15 s : la cible du Direct WinUI doit devenir 15 s.
- +10 s : elle devient 25 s, quelle que soit une saisie non appliquée.
- Saisir 42 : la cible reste 25 tant qu'**Appliquer** n'a pas été cliqué.
- Appliquer : la cible devient 42 s. Les valeurs hors 0–300 et les fractions sont refusées.
- Protection : utilise la cible du raccourci de protection enregistrée dans Réglages.
- 0 s : demande le rattrapage sur image clé, comme dans le Direct.
- Fermer Temhalo : les commandes deviennent indisponibles ; relancer avec le même fichier DPAPI : le dock se reconnecte sans recopier le lien.

Le dock peut préparer une cible avant le démarrage du relais. Il ne démarre ni n'arrête OBS et ne nécessite pas obs-websocket. La publication d'état passe par l'UI environ une fois par seconde, avec interrogation HTTP toutes les 500 ms. Le rendu du dock a été vérifié à 320 px de large ; le défilement vertical garde les commandes accessibles.

## Comportement du multistream et limites

La chaîne est : **OBS → MediaMTX → FFmpeg lecteur → buffer Core commun → distribution des tags retenus → un FFmpeg émetteur par sortie**. Tous restent en `-c copy`. Le parseur et l'algorithme de délai restent communs ; les charges utiles sont partagées par références, sans les copier pour chaque destination.

- Chaque sortie possède sa file, son rythme, ses timestamps, ses reprises et ses en-têtes AVC/AAC. Une diminution du délai vide les retards de sortie concernés et reprend sur IDR en gardant ouverts les émetteurs sains.
- Une sortie en panne retient ce qu'elle peut dans une file bornée à **4096 tags ou 16 MiB de charges utiles**, plus le paquet en cours et ses configurations. Les charges utiles retenues restent comptées dans le même budget RAM que Core. Une réserve conservatrice de 512 KiB par destination couvre les structures de file et est comprise dans la limite configurée.
- Si une autre sortie fonctionne et qu'une file est pleine, la sortie lente abandonne sa file et reprend sur une IDR plus récente. Cette portion est perdue pour cette destination. Les autres continuent.
- Si toutes les sorties sont indisponibles, la distribution retient le buffer commun au lieu d'en vider silencieusement le contenu. Le budget RAM reste la limite finale ; son dépassement arrête la session avec une erreur, sans réduire le délai de protection.
- Le média déjà confié aux pipes, à FFmpeg et au réseau n'a pas d'acquittement de bout en bout. Une perte ou répétition autour d'une panne reste possible. Une reconnexion peut recréer une session côté plateforme.
- La mesure commune, le retard d'une branche et la latence de lecture sont distincts. Après panne, une branche peut rester plus en retard ; réappliquer une cible ou 0 s demande le rattrapage. Les pauses de 5 s conservent les limites de l'étape 3 et ne garantissent pas le maintien d'une session sur toutes les plateformes.

Les destinations partagent les mêmes pistes H.264/AAC. Pistes VOD séparées, réencodage fluide, détection automatique du profil OBS et obs-websocket restent hors de ce raccordement.

## Accès local et confidentialité

Le serveur Kestrel écoute explicitement sur **127.0.0.1**, sans URL de configuration susceptible de l'ouvrir au réseau. Il refuse les hôtes et origines étrangers. Deux capacités aléatoires de 256 bits distinguent la lecture et les commandes ; elles sont conservées dans DPAPI. Les liens les placent dans le fragment `#…`, qui n'est pas envoyé avec les requêtes HTTP de page. Le navigateur utilise ensuite l'en-tête `Authorization`.

Les liens sont privés : ne pas les publier ni les coller dans une destination RTMP. L'overlay ne peut pas commander le délai. Aucun cookie, accès CORS, ressource externe, upload ou journalisation de requêtes n'est activé. Les snapshots ne contiennent ni URL de publication, ni clé de stream, ni chemin de session. Les logs Temhalo masquent aussi les valeurs des capacités. Un programme exécuté sous le même compte Windows reste dans le périmètre de confiance du poste.

L'API HTTP est disponible pour une intégration locale ultérieure : `GET /api/state` avec l'un des accès ; `POST /api/delay` avec l'accès du dock, `Content-Type: application/json` et un corps comme `{"action":"set","seconds":30}`, `{"action":"adjust","seconds":10}` ou `{"action":"protect"}`. Corps borné à 1 KiB ; commandes invalides refusées ; une commande que l'UI ne peut pas prendre en charge à temps est annulée. Aucun WebSocket n'est nécessaire pour ces deux pages.

Le binding et la sélection du port s'appuient sur [la documentation Kestrel](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-8.0).

## Tests automatisés et contrôles effectués

Fermer l'application avant compilation. Pour le test natif, les ports 1935 et 1936 doivent être libres ; aucun service concurrent n'est arrêté par le script.

```powershell
& .\tools\Test-Stage5bis.ps1
# Seulement les tests unitaires/HTTP si les ports média sont occupés :
& .\tools\Test-Stage5bis.ps1 -SkipNative
```

Résultats sur ce poste : compilation Debug x64 sans erreur ni avertissement, restauration verrouillée réussie, **72 tests réussis** (40 Core, 25 infrastructure, 7 présentation). Les ajouts vérifient partage/libération des références, indépendance des sorties, rétention quand toutes sont indisponibles, rattrapage sans recréer les connexions saines, capacités HTTP, hôte/origine, validation des commandes et stockage chiffré de plusieurs clés/liens.

Le test natif réel vérifie deux fichiers simultanés, le crash d'un émetteur sans redémarrage de l'autre, 2 → 4 → 0 s, la reprise IDR, les timestamps monotones, le décodage H.264/AAC et la libération du buffer. Il publie ensuite vers deux destinations RTMP locales et capture/décode les deux flux. Rapport sous `artifacts/validation/stage5-…/multistream-result.json`.

Le dock a été testé dans Chromium, relié à une instance WinUI utilisant une configuration DPAPI fictive dédiée : presets, +10, saisie non appliquée, application, reflet WinUI, perte de connexion, reconnexion et largeur 320 px. Les fonds CSS de l'overlay sont transparents et son état réel est servi avec le lien en lecture. Les captures sont sous `design/native-etape-5bis-services/`. L'insertion finale dans une scène et le dock OBS reste à vérifier avec la procédure ci-dessus ; aucune scène/profil personnel n'a été modifié.

La campagne de 30 minutes, la mesure CPU/RAM à 8 Mbit/s avec quatre sorties et les essais Twitch/YouTube/Kick restent à effectuer. Les résultats locaux ne garantissent pas leur politique de timeout. Ces validations et le packaging seront repris après votre retour, à l'étape 6.

# Architecture du relais Temhalo

La chaîne imposée est conservée : OBS publie vers MediaMTX en local ; un FFmpeg lecteur remuxe le flux en FLV vers stdout ; le moteur C# reçoit, conserve et planifie les tags ; un FFmpeg émetteur par destination lit le FLV depuis stdin puis publie vers la plateforme. Tous les FFmpeg de ce chemin utilisent `-c copy`.

```text
OBS H.264 + AAC
  -> RTMP 127.0.0.1:1935/live/stream
MediaMTX
  -> FFmpeg lecteur, FLV stdout
Temhalo.Core
  -> sortie unique, ou distribution partagée à 2–4 sorties
  -> FLV stdin de chaque FFmpeg émetteur
Plateforme(s) RTMP ou RTMPS
```

## Responsabilités

| Projet | Responsabilité prévue |
|---|---|
| Temhalo.Core | FLV, codecs de configuration, tags, budget mémoire, horloge, sélection des images clés et planification |
| Temhalo.Infrastructure.Windows | Processus, pipes, supervision, erreurs natives, secrets, journaux et raccourcis Windows |
| Temhalo.RelayHost | Hôte de diagnostic pour tester la chaîne sans UI |
| Temhalo.App | WinUI 3, vues, cycle de vie et composition de la session |
| Temhalo.Presentation | Brouillon, commandes et projection des métriques, sans dépendance WinUI |
| Projets de tests | Tests des comportements du moteur et des défaillances, ajoutés avec les fonctionnalités |

Core ne dépend pas de Windows, des plateformes ou de l'UI. Les services Windows l'adapteront aux flux et processus concrets. RelayHost et App composeront les mêmes services, sans réimplémenter le moteur.

L'étape 1 a raccordé cette chaîne à une sortie fichier locale avec `IMediaTransfer` et `PassthroughMediaTransfer`. Le superviseur et les adaptateurs de processus sont implémentés dans l'infrastructure ; RelayHost les compose pour les essais sans UI. Voir [la procédure initiale du relais local](ETAPE-1.md).

L'étape 2 remplace le transfert brut de l'hôte par `FixedDelayMediaTransfer` : parseur strict, file RAM SPSC, tâches d'entrée/sortie indépendantes, échéances sur horloge monotone et reprise IDR avec en-têtes du bon codec. Le délai est choisi au lancement. Le dépassement du budget arrête la session sans raccourcir le délai ; une fin d'entrée normale vide les tags restants à leur échéance. La réécriture utilise un décalage commun A/V avec conservation du CompositionTime AVC. Voir [la procédure et les limites de l'étape 2](ETAPE-2.md).

L'étape 3 fait évoluer ce moteur en `FlvDelayMediaTransfer`, avec un adaptateur conservant l'API fixe. `DelayController` reçoit la dernière cible et réveille les attentes ; `DelayTransitionPlanner` sérialise les paliers, les réductions et les reprises. RelayHost expose une commande de diagnostic par fichier et distingue cible demandée, délai appliqué et âge du dernier média. Voir [la procédure dynamique et les résultats](ETAPE-3.md).

L'étape 4 compose ces mêmes services dans WinUI 3. `RelaySession` exécute le superviseur hors du dispatcher ; le ViewModel conserve le brouillon indépendamment des métriques rafraîchies à 1 Hz. La sortie fichier reste initiale, avec une destination RTMP/RTMPS optionnelle et une clé en mémoire uniquement. La connexion du publisher est suivie dans les diagnostics JSON de MediaMTX ; la progression FFmpeg confirme la sortie après émission de média. Voir [le guide UI et les limites de ces indicateurs](ETAPE-4.md).

L'étape 5 conserve cette composition et donne au moteur une durée de vie de session, indépendante des connexions natives. `TransferSessionsAsync` reçoit les sessions d'entrée et un `IFlvOutputConnection` ; `SupervisedFlvOutput` recrée seulement l'émetteur. Les générations d'entrée sont attachées aux tags avant publication dans la file, pour consommer les configurations à leur échéance. Les services `GlobalHotkeyService`, `DpapiSettingsStore` et `SafeFileLogger` restent dans l'infrastructure Windows. Voir [le guide courant](ETAPE-5.md).

## Lecture, mémoire et temps

La lecture de stdout continue pendant une pause ou une reconnexion de sortie : la tâche de lecture et la tâche de libération sont distinctes. Sinon, le pipe se remplirait, bloquerait le lecteur FFmpeg et propagerait une contre-pression jusqu'à OBS. La conservation est bornée par le budget RAM et la capacité de tags ; un dépassement arrête explicitement la session.

Les données média sont conservées dans une file circulaire bornée avec des tableaux de taille exacte. L'heure d'arrivée vient d'une horloge monotone, indépendante des corrections de l'horloge civile. Le budget compte les payloads détenus, y compris ceux des configurations conservées. Le chemin des paquets n'emploie pas de verrou bloquant. Les commandes dynamiques passent par une valeur atomique versionnée indépendante : la dernière cible gagne, sans file de commandes illimitée.

Une limite atteinte ne réduit jamais silencieusement le délai de protection : l'étape 2 arrête la session avec une erreur explicite et l'état Faulted. Le seuil d'alerte mémoire est configurable.

L'arrivée des tags et leurs timestamps média sont deux informations différentes. La réécriture applique une correspondance commune audio/vidéo : DTS de sortie monotones par piste, conservation du décalage de composition AVC entre PTS et DTS, gestion du rebouclage des timestamps FLV et reprise IDR lors des régressions. Depuis l'étape 5, une nouvelle session OBS conserve la sortie existante ; seule une nouvelle connexion d'émetteur fichier crée un nouveau FLV. Incrémenter arbitrairement chaque timestamp pour le rendre unique pourrait créer une dérive A/V ; les tests couvrent ce défaut.

Les en-têtes AVC et AAC sont associés à leur génération de codec, conservés et renvoyés lors d'une reprise. Un en-tête AVC n'est pas une image clé. La reprise vidéo attend une véritable IDR H.264. Un changement de configuration ou un retour des timestamps en arrière impose une nouvelle reprise.

## Délai dynamique et continuité

Augmenter le délai en passthrough oblige à retenir du contenu. Pendant une suspension pure, aucun nouveau média n'est envoyé. Une connexion TCP encore ouverte ne garantit pas que la plateforme maintienne sa session ou que le lecteur conserve une lecture correcte.

Le moteur implémente un ajout progressif, avec pauses demandées de 5 s au maximum par défaut, juste avant une IDR. Entre deux pauses, il émet au moins 1 s de média sur au moins 1 s de temps réel et attend l'IDR suivante. Les valeurs sont configurables ; leur compatibilité reste à mesurer par destination. Un ajout de 300 s prend plus de 300 s de temps réel. La gigue système peut dépasser légèrement la durée demandée. Ce mécanisme réduit les longues périodes sans données mais **ne garantit pas une absence de déconnexion**.

Avant la première émission, la cible règle directement le remplissage initial. Après une fin d'entrée, le moteur termine une pause déjà engagée, puis vide le buffer au délai atteint sans commencer de nouveau palier. Il signale une cible restée inatteinte. Une commande peut interrompre une attente ou une pause sans attendre l'ancienne échéance.

Une alternative en passthrough consiste à rejouer un GOP complet déjà encodé, avec ses données audio associées et des timestamps réécrits, pendant la constitution du délai. Elle maintient une émission de média sans nouvel encodage, au prix d'une boucle visible et audible. Elle demande de vérifier les frontières d'images clés, les en-têtes et le décodage ; répéter un tag vidéo arbitraire ne suffit pas. Cette alternative sera soumise au choix utilisateur avant son implémentation si les paliers ne conviennent pas.

Une image figée avec silence garantis et un comportement homogène sur les plateformes peut exiger une source d'attente encodée compatible ou un encodage de secours. Cela élargirait le fonctionnement du MVP et demanderait un accord explicite. Le mode fluide avec variation de vitesse reste réservé à la V2.

Réduire le délai supprime une portion de contenu et reprend à la dernière IDR disponible avant la nouvelle cible, ou à la première suivante si nécessaire. Zéro choisit la plus récente IDR disponible et rattrape son GOP. Les en-têtes du bon codec précèdent la reprise et un décalage commun A/V retire l'intervalle abandonné de la chronologie de sortie. Le mode 0 s signifie le minimum local réalisable ; la mesure inclut l'intervalle d'images clés et les autres buffers. Le moteur ne peut pas retirer les données déjà transmises aux processus natifs ou au lecteur.

Ces limites découlent de l'absence de nouveaux paquets pendant la rétention et du mode [streamcopy FFmpeg](https://ffmpeg.org/ffmpeg.html#Streamcopy). L'étape 3 vérifie les transitions vers un fichier local ; les comportements d'ingest propres aux plateformes et l'essai de 30 minutes avec OBS restent à mesurer.

## Défaillances et secrets

Le superviseur conserve l'ingest et le buffer lorsque la sortie tombe, dans la limite mémoire. Une chute d'OBS permet la vidange des données restantes puis l'attente d'une nouvelle publication. Les redémarrages emploient des temporisations annulables, un délai maximal d'écriture et un arrêt borné des pipes. Les métriques distinguent les connexions, la présence de média et le retard mesuré. Le contenu conservé après une panne est émis à vitesse normale ; une dette de délai est exposée. Réappliquer une cible ou zéro retire cette dette par recherche d'IDR, avec abandon explicite de la portion sautée.

Les données déjà transmises à stdin FFmpeg et à la plateforme ne disposent pas d'acquittement média de bout en bout. Le buffer de Core peut survivre au crash de l'émetteur, sans promettre la récupération des données qui avaient quitté ce buffer. Le processus Temhalo doit rester vivant : ce MVP n'utilise aucun journal média sur disque.

Les paramètres de lancement FFmpeg peuvent contenir une clé dans l'URL de sortie. Ils ne sont pas journalisés. stdout/stderr natifs passent par un masquage avant écriture ou affichage, et le formateur de fichier protège aussi les événements formatés. DPAPI CurrentUser chiffre l'ensemble des réglages sous `%LOCALAPPDATA%/Temhalo/settings.dpapi` ; la sérialisation en clair ne passe jamais par un fichier temporaire. La clé demeure nécessaire en mémoire pendant l'exécution, et les arguments natifs restent inspectables par un outil système autorisé. Les commandes et rapports de préparation ne contiennent aucune clé de plateforme.

Les raccourcis globaux s'inscrivent sur le HWND WinUI avec RegisterHotKey, sans répétition automatique. Leur callback reste sur le thread UI ; il utilise les mêmes commandes versionnées que les contrôles de délai. Le stockage et les inscriptions sont effectués hors du chemin des paquets. Les conflits sont signalés et l'ensemble précédent est restauré lorsque c'est possible.

## Multistream et accès OBS — étape 5 bis

`RelayOutputGroup` conserve le chemin à une sortie et crée `FanoutFlvOutput` pour deux à quatre destinations. Le moteur et le parseur sont partagés. `FlvTagWriter` détecte le contrat `IFlvPacketSink` : il transmet les tags typés et leurs timestamps au distributeur sans sérialisation ni nouvelle copie des charges utiles. Chaque branche retient les tags avec un comptage de références, puis possède son rythme, sa chronologie et son `SupervisedFlvOutput`.

Les files de branche sont bornées à 4096 tags et 16 MiB de charges utiles. Les charges partagées restent dans le budget Core jusqu'à leur dernière libération ; une réserve conservatrice de 512 KiB par branche couvre les structures de file. Une sortie lente peut perdre sa seule file et reprendre sur IDR si une autre émet. Si aucune ne fonctionne, la contre-pression retient le buffer commun jusqu'à la limite mémoire. Une réduction explicite vide les retards des branches et reprend sur IDR sans fermer les émetteurs sains. Ces choix et la possibilité de pertes aux frontières des processus sont détaillés dans [le guide des services](ETAPE-5BIS-SERVICES.md).

`LocalControlServer` reste dans l'infrastructure Windows : Kestrel sur 127.0.0.1, port configurable, ressources intégrées et deux capacités distinctes de lecture/commande conservées avec DPAPI. Il expose un instantané immuable publié environ une fois par seconde par l'UI. Le dock appelle les mêmes commandes que WinUI via le dispatcher ; une échéance annule les commandes qui n'ont pas été prises en charge. Le serveur n'exécute aucun traitement média. L'overlay utilise les seuls réglages enregistrés et reste capturé en amont du buffer lorsqu'il est inséré dans OBS.

## Déploiement Windows — étape 6

`ApplicationLayout` choisit explicitement le dossier portable marqué à côté de l'EXE avant toute recherche d'une solution de développement. La configuration est lue dans l'installation ; réglages, cache, logs et sessions sont écrits dans le profil utilisateur, indépendamment du dossier de travail. Le profil `Portable.pubxml` inclut les runtimes .NET/ASP.NET Core et Windows App SDK sans trimming. Une cible de publication ajoute explicitement le PRI et les XBF de l'application, nécessaires au chargement de ses vues hors du projet.

`NativeToolProvisioner` acquiert les outils média fixés au premier démarrage du relais : verrou de préparation entre instances, téléchargement vers un fichier partiel unique, SHA-256 avant extraction, dossier intermédiaire, refus de traversée/liens et publication du dossier complet seulement après vérification. Les DLL sont revérifiées avant usage ; les chemins retournés ne dépendent pas d'un manifeste absolu du poste de développement. L'annulation ferme la préparation et empêche un démarrage tardif après fermeture de la fenêtre.

Le chemin de développement et RelayHost conservent leur manifeste résolu existant. La chaîne de paquets, les codecs et le mode passthrough restent communs aux deux déploiements. Voir [les contrôles et limites du package](ETAPE-6.md).

## Préparation de la V2

Les commandes du délai et les instantanés de télémétrie sont indépendants du parseur. L'API HTTP locale est désormais utilisée par le dock ; un adaptateur Stream Deck ou un WebSocket pourra s'y ajouter. Le fichier de commande de diagnostic de RelayHost reste sans service réseau.

Le multistream partage désormais les charges utiles et borne chaque position de lecture. Les pistes VOD séparées, le mode fluide avec réencodage et obs-websocket demandent encore des adaptateurs dédiés. Ils ne sont pas implémentés par ce raccordement.

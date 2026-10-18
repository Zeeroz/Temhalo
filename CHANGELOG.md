# Historique

## 0.7.0-preview.9

- Nouvelle interface WinUI 3 fidèle à la DA Temhalo : navigation en haut, Direct centré sur le délai actuel et la cible appliquée, presets immédiats et saisie personnalisée repliée.
- Première configuration guidée en trois étapes : plateforme, réglages OBS, démarrage. Les profils déjà configurés accèdent au Direct ; la lecture du guide OBS reste distincte de la réception réelle du flux.
- Formulaires de plateforme intégrés à Diffusion, avec enregistrement et annulation des modifications. La connexion de compte est proposée lorsque sa configuration OAuth est disponible ; la clé manuelle reste accessible et devient le parcours par défaut autrement.
- État OBS et état de chaque sortie séparés ; préparation, attente du flux, reconnexion et pression mémoire accompagnées d’explications. Confirmation avant l’arrêt depuis l’interface et annulation de la préparation du relais.
- Réglages simplifiés : mises à jour et Stream Deck visibles, mémoire, débits et journaux regroupés dans le diagnostic. Mise en page adaptée aux fenêtres étroites et au plein écran ; graphiques actualisés à la consultation du diagnostic.
- Réglages DPAPI, comptes et commandes directes Stream Deck conservés, y compris les cinq touches de délai et de démarrage/arrêt. Le trajet média passthrough et la relecture restent inchangés.

## 0.7.0-preview.8

- Réglages réunis sur une seule page : mises à jour, connexion Stream Deck, mémoire et journaux. Suppression des pages Vos commandes et Confidentialité, des raccourcis clavier globaux et des presets personnalisables dans l'application.
- Suppression du dock OBS, de son serveur HTTP et de ses ressources, ainsi que du mode fichier/test local et de l'aperçu VLC dans l'application. Le protocole direct Stream Deck et ses actions sont conservés.
- Reprise du parcours Diffusion selon la configuration déjà enregistrée ; accès guidé depuis le bouton principal sans destination. Graphiques redessinés uniquement sur le Direct visible.
- Dépendances de démarrage inutilisées et ressources web dupliquées retirées ; symboles de débogage exclus des livraisons. L'installeur saute l'accueil lors d'une mise à jour et propose la relance par défaut.
- Client public Twitch intégré pour connecter un nouveau PC sans configuration personnelle. Comptes, destinations et profils Stream Deck conservés.

## 0.7.0-preview.7

- Site de présentation public sur GitHub Pages, avec démonstration du délai, commandes Stream Deck simulées, téléchargements et guide rapide OBS.
- Actualisation automatique des téléchargements à chaque publication : versions stables et previews, installeur, ZIP et notes de version. Les brouillons et fichiers incomplets sont écartés.
- Préparation du HTML pendant le déploiement, sans appel à l'API GitHub par les visiteurs. Tests de sélection des versions et validation du déclenchement depuis le workflow de publication Windows.

## 0.7.0-preview.6

- Nouvelle touche Stream Deck « Démarrer / arrêter le relais » : triangle menthe à l'arrêt, carré lavande en activité, états de démarrage/arrêt affichés et commandes bloquées pendant les transitions. La connexion est directe à Temhalo ; OBS reste indépendant.
- Commandes de démarrage et d'arrêt explicites, appuis rapides filtrés, expiration et reconnexion sans rejeu. Les quatre touches de délai et leurs valeurs restent compatibles.
- L'installeur propose l'installation ou la mise à jour du plugin lorsque Stream Deck est détecté et mémorise ce choix. Il ouvre le paquet fourni avec Temhalo dans Stream Deck, qui conserve les profils et les touches.
- Sans Stream Deck, l'option est désactivée. Le mode silencieux n'ouvre aucune application externe ; un échec d'ouverture du paquet laisse Temhalo installé et indique la procédure manuelle.
- Tests du cycle complet du relais, des commandes et des doubles appuis, des neuf panneaux du plugin et des parcours d'installation avec et sans Stream Deck.

## 0.7.0-preview.5

- Relecture activée par défaut : augmenter le délai revient à une keyframe de l'historique, avec le son correspondant, sans ré-encodage. Une pause par paliers complète uniquement l'historique manquant.
- Historique glissant de cinq minutes conservé en RAM, y compris à 0 s, dans la limite mémoire commune au buffer. L'interface indique la durée disponible ; les données déjà lues sont libérées en priorité en cas de pression mémoire.
- Réduction du délai et retour à 0 avec reprise sur keyframe, timestamps continus et synchronisation audio/vidéo. Les quatre commandes du plugin Stream Deck restent compatibles.
- Bouton « Ouvrir l'aperçu VLC » dans Direct pour une destination RTMP locale, avec récepteur local préparé automatiquement et paramètres VLC adaptés au retour à 0 sans relancer la lecture. VLC doit être installé séparément.
- Tests de régression pour la relecture, les changements rapides, les reconnexions, la mémoire et l'aperçu local ; validation OBS/VLC et des quatre touches Stream Deck.

## 0.7.0-preview.4

- Connexion automatique et directe du plugin Stream Deck à Temhalo par un canal Windows réservé au compte courant ; une seule connexion partagée, états poussés et reconnexion sans rejeu des commandes.
- Nouveau dessin des touches preset, ajout, retrait et retour à 0 : pictogrammes lisibles, valeurs sur une ligne et couleurs Temhalo. Panneau de réglages compact avec enregistrement des valeurs et état de connexion.
- Conservation des actions et des valeurs des touches ; suppression de l'ancienne association par lien privé du dock.
- Retrait de l'overlay : onglet, aperçu, personnalisation et source navigateur. Navigation Direct / Diffusion / Réglages ; lecture des anciens réglages DPAPI conservée.
- Dock OBS facultatif et désactivé par défaut, indépendant du plugin Stream Deck. Le trajet média passthrough et les destinations restent inchangés.

Pour migrer depuis preview.3, mettez aussi à jour le plugin Stream Deck avec le fichier `plugins/streamdeck/Temhalo.streamDeckPlugin` livré dans le dossier de Temhalo ou le ZIP portable. Les anciennes copies du plugin utilisant le dock doivent être remplacées pour communiquer avec cette version de l'application.

## 0.7.0-preview.3

- Plugin Stream Deck facultatif : protection, retour direct, preset, ajout/retrait, délai mesuré, état OBS/sorties et réglage à la molette du Stream Deck +.
- Association locale commune aux touches via le lien privé du dock, stockage global exclu des profils exportés et reconnexion sans rejeu des commandes.
- Plugin et sa documentation inclus dans les paquets Windows, avec validation officielle Elgato et tests automatisés.
- Correction du texte de la case « Ouvrir Temhalo » sur le fond sombre de la dernière page de l'installeur.

## 0.7.0-preview.2

- Script unique de publication : numéro de version, tests, paquets Windows, commit/tag, push atomique et suivi de GitHub Actions.

## 0.7.0-preview.1

- Publication open source du projet Windows, de ses tests, documents et ressources graphiques.
- Recherche de mises à jour dans Réglages, canaux stable/preview et téléchargement avec contrôle SHA-256.
- Installation uniquement après arrêt du relais, fermeture propre de Temhalo et conservation des réglages.
- CI Windows et publication des installeurs/ZIP dans GitHub Releases à partir des tags de version.
- Interface guidée pour Twitch, YouTube et Kick, avec OAuth configurable et clé manuelle en option.
- Sélection automatique du serveur Twitch pour éviter la latence constatée avec l’ancien alias régional.

Les fonctionnalités historiques comprennent le délai dynamique de 0 à 300 s, le passthrough H.264/AAC, jusqu’à quatre sorties, l’overlay et le dock OBS locaux, les raccourcis globaux, les reprises et le stockage DPAPI.

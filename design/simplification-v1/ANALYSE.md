# Simplification de Temhalo — proposition 1

16 octobre 2026. **Maquette à valider avant toute modification de l'application**,
conformément au choix de l'utilisateur pendant cette analyse.

## Examen de DynamicDelay

DynamicDelay **4.2.1**, déjà ouvert sur ce PC, a été parcouru dans son interface
Windows avec Computer Use : Direct, Diffusion dans son état multistream courant,
adresse d'entrée OBS dépliée, Réglages, les trois écrans du tutoriel et Overlay
avec « Personnaliser plus ». Le tutoriel a été quitté avec Échap avant sa validation.
L'application a été rendue à Direct. Aucune diffusion, installation, modification
de destination, activation d'option ou commande d'envoi de journaux n'a été lancée.
Les modes Twitch seul et serveur personnalisé sont également visibles dans les
captures précédemment fournies par l'utilisateur ; leurs formulaires n'ont pas
été modifiés pendant cette inspection.

L'examen porte sur les parcours et textes visibles. Il ne démontre ni le
fonctionnement de son backend, ni la fiabilité de ses promesses de streaming.
Aucun code ou asset de DynamicDelay n'est réutilisé.

| Observation | Ce qui aide | Décision Temhalo proposée |
| --- | --- | --- |
| Parcours de mise en route avec états | On sait quelle action faire ensuite | Deux étapes obligatoires, plateformes puis OBS ; overlay et dock restent facultatifs |
| Presets directement sur l'accueil | Commande rapide pendant une partie | Presets au premier plan ; « Autre délai » déplie slider et saisie |
| Explication des plateformes en termes de destination | Vocabulaire proche du streamer | « Plateformes » et « Ajouter une plateforme » remplacent le choix RTMP/RTMPS au premier niveau |
| Réglages regroupés sur une page | Moins d'allers-retours | Une page avec favoris, raccourcis, options avancées et aide ; suppression des sous-onglets |
| Overlay avec réglages supplémentaires repliés | L'essentiel reste visible | Style, position et taille visibles ; texte et couleur dans Personnaliser |
| Direct annonce « prêt » alors que Diffusion demande une configuration | Un message global peut être trompeur | Les états Temhalo reposent sur les validations et la réception réelles ; instructions lues ≠ flux reçu |
| Options recommandées dans le tutoriel | Réduit les choix initiaux, mais mélange usage et télémétrie | Aucun envoi automatique ajouté ; aucun réglage système à accepter pour finir la prise en main |
| « Délai instantané » avec préchargement | Intéressant pour une évolution | Hors de cette refonte : l'historique avec relecture n'existe pas dans le moteur actuel |

## Ce qui complique actuellement Temhalo

L'analyse des sources WinUI et des services existants met en évidence :

- Direct expose simultanément mesure, cible, brouillon, frise, compteurs FLV,
  reconnexions, réserve et événements. La commande utile se perd dans les détails.
- Diffusion commence par un schéma et un choix de protocole, puis demande nom,
  activation, plateforme, URL, clé et mémorisation. Le choix de plateforme ne
  prépare actuellement pas son adresse : `ProviderChanged` actualise les libellés.
- Le guide OBS est une boîte de texte isolée. Les deux valeurs à copier ne sont
  pas présentées comme deux champs distincts dans le parcours de connexion.
- Le bouton « Enregistrer la destination » enregistre en fait toute la liste.
- Les rubriques Au quotidien, Vos commandes, Continuité et Confidentialité
  demandent de deviner où sont les ports, les presets ou les journaux.
- Certaines indications parlent de fonctions encore à raccorder. Les retirer
  de la vue normale simplifie l'outil sans supprimer un service implémenté.

Références locales examinées : `DashboardPage.xaml`, ses fichiers partiels,
`DashboardViewModel`, `RelaySession`, `AppSettings`, `InterfacePreferences` et
le guide des services de l'étape 5 bis.

## Parcours proposé

Première utilisation : **choisir la plateforme et sa clé → copier les deux
valeurs dans OBS → activer Temhalo → lancer le stream dans OBS**.
Ensuite : **ouvrir Temhalo, l'activer, lancer OBS, choisir son délai**.

La clé de plateforme reste dans Temhalo, chiffrée avec DPAPI. OBS utilise
`rtmp://127.0.0.1:1935/live` et la clé locale `stream`. Le tutoriel DynamicDelay
conserve le service Twitch et sa clé dans OBS ; ce comportement ne se transpose
pas au chemin d'ingestion fixe de Temhalo sans un chantier distinct.

« Activer Temhalo » prépare l'écoute locale. Ce bouton ne lance pas OBS.
L'émission commence lorsque le flux arrive vers les plateformes activées.
Le premier démarrage peut préparer les outils média. Aucun démarrage de
diffusion automatique à l'ouverture de l'application n'est proposé ici.

### Direct

Une valeur dominante, les favoris et une courte explication de transition.
Le délai demandé reste visible à côté de la mesure ; une saisie en cours ne le
remplace pas. Les connexions OBS et plateformes restent distinctes.
Les débits et la durée sont lisibles ; frise, statistiques, RAM et journaux sont
accessibles dans Détails du live. Les alertes importantes restent visibles même
quand ces détails sont fermés.

### Diffusion

Une seule colonne de progression, avec deux étapes numérotées dans la largeur
utile. Les plateformes sont listées directement. Leur éditeur préremplit le
nom et cache les options réseau inutiles. Kick et le serveur personnalisé
demandent explicitement l'adresse fournie par le service ; une future adresse
par défaut Twitch/YouTube devra être vérifiée avant son raccordement.
Une clé et un serveur renseignés donnent « paramètres enregistrés », jamais
« plateforme connectée » avant un état de sortie confirmé.

Le mode fichier local reste accessible dans Tester sans plateforme. L'aperçu
VLC demande actuellement un serveur de réception supplémentaire et un script :
la maquette l'explique sans promettre une préparation automatique inexistante.
Le dock OBS est dans une section facultative au bas du même parcours.

### Overlay

Les quatre compositions Temhalo sont conservées : Signature, Repère, Fil du
temps et Chrono. Un aperçu compact laisse les réglages à portée sans un long
défilement. Une commande d'installation donne les étapes de la source navigateur.
L'aperçu est explicitement simulé ; un lien copié ne prouve pas l'ajout dans OBS.

### Réglages

Une page, sans sous-navigation : Délais favoris, Raccourcis clavier,
Options avancées et Aide. La RAM, le port navigateur, l'export des événements
et l'accès aux fichiers restent disponibles. Aucun faux bouton de mise à jour,
de langue, de démarrage Windows ou d'intégration bot n'est ajouté.

## Portée de la maquette

La maquette est écrite de zéro en HTML/CSS/JS, avec les assets Temhalo approuvés.
Navigation supérieure, logo/nom seulement dans la barre de titre, couleurs et
polices de BRAND.md conservés. Les interactions sont en mémoire : aucun réseau,
relais, fichier utilisateur, API, stockage local ou presse-papiers réel commandé.
Utiliser uniquement des clés fictives dans les exemples.

Les données de Direct sont simulées. Augmenter le délai affiche une transition,
sans inventer une nouvelle mesure ; le bouton de démonstration « Terminer la
transition » permet d'inspecter l'état final. Zéro signifie minimum local.

Après validation, le raccordement WinUI devra garder les commandes métier,
identifiants de destinations, chiffrement, reconnexions, limites mémoire et
réglages existants. La simplification visuelle n'autorise aucune perte de clé ni
suppression de fonctionnalité. Le moteur et l'architecture RTMP/FLV restent les mêmes.

## Explication à un nouvel utilisateur

« Dans Diffusion, choisissez votre plateforme et indiquez sa clé. Copiez
l'adresse et la clé locale dans OBS, puis activez Temhalo et démarrez votre
stream dans OBS. Pendant le live, cliquez sur 15, 30 ou 60 secondes. Ajouter
du délai crée des pauses ; revenir à zéro saute les images en attente. »

## Procédure de revue

1. Ouvrir `Temhalo-simple.html`. Le bandeau Maquette reste visible.
2. Dans Direct, appliquer 60 s, vérifier mesure/cible distinctes, puis terminer
   la transition simulée. Déplier Autre délai ; essayer 301 s et une fraction.
3. Sélectionner « Prêt à démarrer » dans le sélecteur de démonstration. Dans
   Diffusion, ajouter une plateforme avec une clé factice ; vérifier l'édition,
   la désactivation et le maximum de quatre destinations.
4. Lire le guide OBS : serveur et clé locale distincts, ordre d'activation clair.
   Tester les guides d'overlay et du dock, puis le mode fichier local.
5. Dans Overlay, choisir un style, une position et une taille, changer de style
   puis revenir : les préférences de chaque composition sont conservées.
6. Dans Réglages, ajouter 45 s, le retrouver sur Direct ; essayer des raccourcis
   identiques pour deux actions et une limite RAM invalide.
7. Comparer les scénarios d'attente, de reconnexion, de coupure OBS, de pression
   mémoire et de port occupé ; les alertes n'exigent pas d'ouvrir Détails.
8. Réduire puis agrandir la fenêtre, tester Tab, les dialogues et Échap.

Les résultats de rendu et d'interaction sont dans `validation.json`. L'état des
sources et de BRAND.md est comparé aux empreintes `application-before.json`.

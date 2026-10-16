# Temhalo — Proposition 03

**Maquettes à valider · 16 octobre 2026 · Étape 5 bis**

Ouvrir **Temhalo-maquettes-5bis-R3.html** dans un navigateur. Les logos approuvés
et les trois familles typographiques sont intégrés au document. Il fonctionne
hors ligne et reste indépendant de l’application .NET.

## Le parti pris

La composition part du **décalage entre un événement et sa diffusion**. Cette
relation temporelle guide le Direct ; le chemin du média guide la Diffusion.
Le halo ouvert et son segment décalé portent l’identité de l’Overlay.

Une navigation horizontale compacte, sous la barre de titre, sépare le pilotage,
le trajet, l’habillage et les préférences. Le panneau latéral et son bloc logo /
nom / signature sont supprimés. L’icône et le nom de la fenêtre restent dans la
barre de titre. Les états OBS et sorties sont regroupés en bas de la fenêtre.

Les compositions des quatre écrans, les compteurs et les commandes sont
conservés. Les valeurs mesurées, cibles appliquées et saisies restent distinctes.

### Direct — Voir le décalage

Un seul plan de travail rapproche la mesure et la commande du délai. La frise
situe le média émis et la cible par rapport à l’entrée OBS. Son échelle s’adapte
aux valeurs : elle ne prétend pas représenter la latence du lecteur distant.

L’état des connexions suit une lecture entrée → sorties. Le buffer, la réserve,
les reconnexions et le contenu sauté restent visibles. Le graphique des 60
dernières secondes se déplie dans **Activité du flux**.

### Diffusion — Dessiner le parcours

OBS alimente le point de temporisation Temhalo, puis les sorties configurées.
Le logo joue ici son rôle de repère de marque au centre du trajet, sans être
modifié. Les sorties peuvent être ajoutées, désactivées ou sélectionnées pour
édition. Le formulaire est placé sous le trajet.

Le sélecteur de type donne accès aux plateformes, au serveur RTMP personnalisé
et au test local VLC/fichier. Les liens vers la source navigateur et le dock OBS
forment deux intégrations facultatives. Les modifications préparent le prochain
démarrage ; elles ne changent pas les sorties du Direct de démonstration actif.

### Overlay — Signer le temps

La scène occupe le centre de l’éditeur. Les compositions se choisissent dans une
galerie sous l’aperçu, puis le placement, le format et le texte se règlent dans
trois groupes lisibles.

- **Signature** : halo original, libellé et compteur sur deux lignes.
- **Repère** : deux traits décalés, menthe et lavande, pour ancrer le statut.
- **Fil du temps** : une ligne et un repère accompagnent le délai.
- **Chrono** : la valeur seule, adossée à un trait discret.

Les positions, tailles, libellés, accents et visibilité en direct sont conservés
séparément par composition pendant la revue. Les accents de l’habillage ne
recolorent jamais le logo approuvé.

### Réglages — Suivre les habitudes

Une rangée de rubriques donne accès à un formulaire à la fois : **Au quotidien**,
**Vos commandes**, **Continuité** et **Confidentialité**. La rubrique initiale
réunit les presets et raccourcis, les commandes les plus utiles en live.

Les préférences, la connexion OBS, la mémoire, les journaux et les mises à jour
restent accessibles dans leur rubrique. L’historique avec relecture reste signalé
**à étudier** ; aucune activation ne suggère que le comportement est déjà défini.

## Direction artistique

Les décisions approuvées de BRAND.md sont conservées :

- Fonds Nuit `#0D1218`, Ardoise `#151D26`, Brume sombre `#1D2935`.
- Halo `#7CE8CD` pour les actions et le focus ; Écho `#B9B2FF` pour les cibles.
- Space Grotesk pour les titres, Inter pour les textes, JetBrains Mono pour les
  valeurs. Les licences OFL sont jointes.
- Logo « Halo décalé » réutilisé depuis les fichiers approuvés, sans redessin.
- Fonds unis, arrondis mesurés, contours utiles, aucun glow ou mouvement décoratif.
- Navigation horizontale dans toutes les tailles. Le contenu récupère la largeur
  du panneau latéral supprimé, avec les retraits de la DA.

Les changements portent sur la composition et les parcours proposés. BRAND.md,
les assets approuvés et les projets de l’application sont inchangés.

## Procédure de revue

1. **Direct :** déplacer le slider. La cible appliquée ne change qu’après
   **Appliquer**. Essayer 60 s puis 0 s : la frise et la transition changent.
   Tester aussi la saisie invalide 301 s.
2. Utiliser **Scénario Direct** pour comparer ajout du délai, reconnexion Twitch,
   coupure OBS, pression mémoire et attente d’entrée. Déplier l’activité.
3. **Diffusion :** ajouter une sortie fictive, modifier son nom et désactiver sa
   case. Explorer les types RTMP et Test local. L’estimation d’upload suit les
   sorties activées dans le brouillon.
4. **Overlay :** comparer les quatre compositions et les trois états. Changer
   placement, taille et texte, puis changer de composition et revenir. Vérifier
   la visibilité à 0 s et la remise à zéro.
5. **Réglages :** parcourir les quatre rubriques. Ajouter 45 s aux presets et le
   retrouver sur Direct. Saisir un raccourci identique pour deux actions et
   essayer Enregistrer pour voir l’erreur.
6. Réduire puis agrandir la fenêtre ; tester Tab, les flèches sur les navigations
   et Échap pour fermer un guide.

Les captures principales se trouvent dans `captures/Temhalo-*-1280.png`. Les
variantes 720/320 montrent l’adaptation ; les vues supplémentaires montrent
l’activité, les états du Direct et les rubriques des Réglages.

## Portée de l’aperçu

Les interactions restent en mémoire. Recharger la page rétablit les exemples.
Aucun flux, processus, raccourci Windows, API, presse-papiers ou réglage OBS réel
n’est commandé. Les clés de démonstration ne sont pas sauvegardées. Les boutons
Copier, Exporter, Enregistrer, Démarrer et Mise à jour donnent un retour simulé.
Le choix de langue prépare une préférence ; la proposition reste en français.

Les adresses HTTP au port 43120 représentent une proposition future. Aucun port
n’est ouvert. Le dock devra être authentifié ; les clés de stream n’y seront
pas exposées. L’intégration cloud d’un bot et la piste VOD audio séparée restent
des travaux dédiés. Le multistream illustré ne constitue pas un backend ajouté.

Les scènes Direct sont statiques. Un overlay inséré dans OBS traverse le délai
du média ; son état à l’écran ne devient pas une mesure instantanée des lecteurs
distants. Zéro représente le minimum local du futur relais.

## Vérification

`validation.json` consigne les rendus, les interactions et les contrôles de
débordement, de 320 à 1920 px. Les quatre rubriques des Réglages sont également
contrôlées en large et en compact. Les trois familles typographiques et tous les
logos sont chargés sans requête réseau.

Les empreintes des **95 fichiers** suivis dans src/tests/config, BRAND.md et la
configuration .NET sont comparées à l’état avant les maquettes : inchangées.
Le mode de contraste forcé du navigateur est capturé. Ces vérifications ne sont
pas un audit Narrateur/DPI de l’application native ni des tests de streaming.

`maquettes.template.html` et `interactions.js` contiennent les sources de
conception ; `chrome.css` porte la révision de la navigation et de l’en-tête.
`build.cjs` embarque les assets existants du dépôt ; `verify.cjs`
produit les rendus avec Playwright et Sharp, sans dépendance ajoutée à .NET.

**Attendre la validation de cette proposition avant de modifier l’application.**

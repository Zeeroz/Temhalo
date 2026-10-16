# Temhalo — Maquettes de l’étape 5 bis

16 octobre 2026 · **Proposition à valider avant toute modification de l’application.**

Ouvrir `Temhalo-maquettes-5bis.html` dans un navigateur. Le document embarque les
polices et les logos approuvés et fonctionne sans connexion. Il représente la
future interface WinUI 3 ; il n’est pas connecté au moteur Temhalo.

## Les quatre écrans

| Écran | Contenu proposé |
|---|---|
| Direct | Délai mesuré, cible appliquée, brouillon numérique/slider, presets, états de chaque connexion, débits sur 60 s, RAM, réserve et contenu sauté. |
| Diffusion | Assistant OBS, destinations multiples, RTMP personnalisé, test local VLC/fichier, adresses proposées pour l’overlay et le dock. |
| Overlay | Aperçu 16:9, trois états, styles Pastille/Halo/Discret/Compteur, position, taille, accent et libellé. Chaque style conserve ses propres réglages dans l’aperçu. |
| Réglages | Préférences, lancement Windows, connexion OBS, presets, délai de protection et raccourcis, mémoire, diagnostics, accès local et mises à jour. |

## Direction artistique conservée

- Nuit `#0D1218`, Ardoise `#151D26`, Brume sombre `#1D2935`.
- Halo `#7CE8CD` pour les commandes et sélections ; Écho `#B9B2FF` pour la cible.
- Textes Givre, Brume et Acier ; états Signal, Vigilance et Alerte.
- Space Grotesk pour les titres, Inter pour l’interface, JetBrains Mono pour les
  valeurs. Les polices embarquées proviennent des fichiers approuvés du projet.
- Logo « Halo décalé » réutilisé sans redessin ni recoloration. Arrondis de
  8/12 DIP, espacements selon la grille 4/8/12/16/20/24/28/32.
- Pleine largeur utile ; deux colonnes en large, une colonne en compact, sans
  défilement horizontal. Les écrans longs défilent verticalement.

## Procédure de revue

1. **Direct :** modifier le slider ou la saisie. La cible appliquée ne change
   qu’avec **Appliquer**. Les presets s’appliquent au clic. Essayer 60 s puis
   0 s. L’augmentation montre une transition ; elle ne simule pas une application
   instantanée du délai. Une valeur de 301 s produit une erreur.
2. Dans **Scénario Direct**, comparer délai stable, ajout du délai, reconnexion,
   déconnexion OBS, alerte mémoire et attente d’OBS. Ces scènes sont des exemples
   indépendants ; elles ne déroulent pas une session vidéo réelle.
3. **Diffusion :** ajouter une destination fictive, la désactiver, puis explorer
   RTMP personnalisé et Test local. Aucun changement n’affecte le Direct actif :
   la configuration est destinée au prochain démarrage.
4. **Overlay :** changer le style, la position, la taille et le libellé ; changer
   de style puis revenir pour retrouver ses réglages. Comparer les trois états
   et désactiver la visibilité en direct. Tester la remise à zéro et le guide OBS.
5. **Réglages :** ajouter le preset 45 s, puis le retrouver sur Direct. Attribuer
   le même raccourci à deux actions et cliquer sur Enregistrer pour voir l’erreur.
   Modifier le délai du raccourci de protection et ouvrir le journal fictif.
6. Réduire la fenêtre, puis l’agrandir. Tester Tab, les flèches sur les onglets,
   les contrôles natifs au clavier et Échap pour fermer un guide.

Les captures principales sont dans `captures/Temhalo-*-1280.png`. Les variantes
720 et 320 montrent l’adaptation ; les autres captures illustrent les états.
`Temhalo-planche-5bis.png` donne une vue d’ensemble des quatre écrans.

## Décisions de parcours à valider

- Le panneau Direct distingue **mesure**, **cible appliquée** et **saisie**.
- L’exemple multistream comporte Twitch et YouTube ; les valeurs sont simulées.
  La mesure principale indique sa sortie de référence et passe sur YouTube
  pendant le scénario de reconnexion Twitch. La réserve et la RAM restent
  visibles ; aucun taux de perte réseau n’est déduit des tags FLV.
- Les destinations se configurent pour le prochain démarrage, sans mutation
  silencieuse d’une diffusion active.
- La page Overlay présente l’habillage inséré dans OBS. Celui-ci traverse le
  même délai que l’image ; il ne représente pas un indicateur instantané ajouté
  après la diffusion. Le fond quadrillé est un repère de transparence.
- L’historique avec relecture reste **à étudier**, sans interrupteur d’activation.
- L’accès cloud pour une commande de chat et la piste VOD audio séparée
  nécessitent des travaux dédiés. Ils ne sont pas présentés comme disponibles.
- Les adresses HTTP au port **43120** sont proposées pour la conception. Aucun
  port n’est ouvert et ce numéro ne constitue pas encore une configuration de
  l’application. Le dock devra utiliser une authentification lors de son ajout.
- Aucun envoi automatique de diagnostic ; un éventuel partage reste volontaire.

## Limites volontaires du prototype

Les contrôles manipulent uniquement des données en mémoire. Fermer ou recharger
la page rétablit les exemples. Pas d’API, de WebSocket, de lecture des paramètres
utilisateur, de connexion RTMP, de processus natif ni d’inscription RegisterHotKey.
Les boutons Copier, Enregistrer, Exporter, Vérifier les mises à jour et Démarrer /
Arrêter affichent une confirmation simulée. Le presse-papiers n’est pas modifié.
Le sélecteur de langue présente le futur choix ; la maquette reste en français.
Les scènes Direct sont statiques et ne prédisent pas les délais d’une plateforme.

## Vérifications effectuées

`validation.json` consigne le rendu Chromium, le chargement des trois familles,
les images, les interactions et les contrôles de débordement sur les quatre
écrans, de 320 à 1920 px, y compris un retour grande → petite largeur.
Le mode de contraste forcé du navigateur est également capturé ; cela ne vaut
pas un audit de l’application native avec Narrateur et DPI Windows.

Les empreintes de `src`, `tests`, `config`, `BRAND.md` et des fichiers de solution
et configuration .NET sont comparées avant/après : **inchangées**.
Ces contrôles ne sont pas des tests de streaming ni une validation de l’étape 6.

## Sources de conception

`maquettes.template.html` est la source lisible. `build.cjs` embarque les assets
existants. `verify.cjs` produit les captures et le rapport avec Playwright et
Sharp disponibles dans l’environnement de conception ; aucune dépendance n’est
ajoutée aux projets .NET. Les licences des polices accompagnent les maquettes.

```powershell
& '<chemin-vers-node.exe>' '.\design\etape-5bis\verify.cjs' '<dossier-node_modules-avec-playwright-et-sharp>'
```

**Prochaine action : attendre la validation utilisateur des maquettes avant de
modifier le code de l’application.**

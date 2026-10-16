# Temhalo — panneau Stream Deck compact

Maquette interactive validée par l’utilisateur : touches simples et panneau compact, puis intégration et installation du plugin autorisées. Ouvrir `Temhalo-streamdeck-integre.html` dans un navigateur, sans serveur. Ce prototype reste indépendant des applications installées.

## Intention

Le plugin existant utilise déjà le SDK officiel Elgato et des actions reconnues par Stream Deck. Cette proposition remplace sa longue page de configuration par des contrôles compacts, à l’emplacement prévu par l’hôte : fond gris, police Segoe UI et lignes courtes. Les accents menthe/lavande et les images de touches conservent l’identité de BRAND.md. Le cadre Stream Deck est illustratif ; ce fichier ne s’exécute pas dans l’application.

- Délai prédéfini : nombre, curseur 0–300 s, valeurs rapides.
- Ajouter/retirer : pas réglable 1–60 s, distinct du délai total.
- Direct : aucune valeur à régler ; retour demandé sur image clé.
- Connexion commune aux touches, repliée lorsqu’elle est configurée ; instructions à la demande.
- Configuration de touche enregistrée automatiquement à la validation du nombre ou au relâchement du curseur. Déplacer un curseur ne commande jamais le live.
- Valeurs de démonstration conservées uniquement en mémoire jusqu’au rechargement. Aucun SDK, WebSocket, appel réseau, stockage local ou accès au presse-papiers.
- Direction graphique simplifiée : fond nuit uni, contour discret et grands chiffres blancs (75 à 92 unités SVG). Le signe + reçoit la menthe et le signe − la lavande ; le retour à zéro apparaît en menthe avec le libellé DIRECT. L’unité reste sur une ligne séparée. Les explications se trouvent dans le panneau.
- Présentation à 112 px et vérification à 72 px. Les touches ne comportent aucun dégradé, reflet, relief, badge ou logo. La planche de présentation reste hors de la simulation de l’application Stream Deck.
- Présentation du deck agrandie sur ordinateur et planche séparée d’images de 72 × 72 pixels CSS, pour juger le dessin sans l’agrandir. Une taille CSS ne simule pas la luminosité ni la taille physique du matériel.

La maquette n’ajoute pas de champ Titre : les images actuelles du plugin gèrent déjà le texte des touches et le manifeste désactive le titre fourni par l’hôte. Les autres actions et la molette restent dans le périmètre du plugin existant ; la maquette se concentre sur les quatre touches préparées pour l’utilisateur.

## Vérification manuelle

1. Cliquer les quatre touches de la première rangée. Le panneau présente les réglages correspondants ; aucun flux n’est piloté.
2. Pour la première touche, saisir 30 puis valider ou relâcher le curseur à une autre valeur. Le nombre et l’aperçu se mettent à jour ; revenir à 15 s via le raccourci.
3. Pour +/−, régler le pas puis changer de touche et revenir : les valeurs des deux commandes sont indépendantes. Les valeurs invalides ne sont pas appliquées.
4. Choisir les états simulés « Temhalo fermé » et « À relier ». Ouvrir Connexion et l’aide. Utiliser seulement un texte fictif pour la simulation de liaison.
5. Ouvrir à faible largeur et naviguer au clavier. Les réglages essentiels du panneau replié doivent tenir sans défilement à sa largeur nominale de 378 px.

## Mise en œuvre

Le plugin utilise sa WebView officielle Stream Deck avec des contrôles HTML locaux, au rendu compact validé, et garde le SDK Elgato existant. Ses UUID d’actions, réglages par touche, connexion locale partagée et file de commandes sont conservés. Les confirmations d’enregistrement sont corrélées à chaque panneau et les écritures sont ordonnées. Les messages d’état identiques sont filtrés. Aucune migration du SDK ni promesse de performance mesurée ne découle de cette refonte.

Documentation officielle : https://docs.elgato.com/streamdeck/sdk/guides/ui/

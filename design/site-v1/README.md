# Temhalo — Maquette du site de présentation

Maquette locale validée, conservée comme référence historique. La version publique est maintenue dans `website/` et publiée sur https://zeeroz.github.io/Temhalo/. Ce dossier ne sert pas de source au déploiement.

## Ouvrir

Ouvrir `dist/index.html` dans un navigateur. Les fichiers HTML, CSS, JavaScript, SVG et polices sont locaux ; aucun outil de compilation ni service externe n’est nécessaire. Un serveur HTTP local permet aussi de vérifier le presse-papiers dans un contexte sécurisé localhost.

## Parcours proposé

1. Promesse « Votre live. Votre tempo. » et démonstration interactive du délai.
2. Explication de la relecture, du retour à 0 et du flux sans réencodage.
3. Cinq commandes Stream Deck, avec les dessins réellement utilisés par le plugin.
4. Reconnexion, stockage chiffré et mises à jour.
5. Quatre étapes de démarrage et paramètres OBS copiables.
6. Réponses aux principales questions.
7. Téléchargement de preview.6, version portable, notes de version et sources.

## Vérifier

- Dans la démonstration du haut, choisir 30 s puis 0 s : valeur, cible, repère temporel et explication changent.
- Dans la section Stream Deck, essayer 15 s, +10 s, −10 s et 0 s. La cinquième touche démarre/arrête uniquement la simulation ; son dessin et le texte de retour changent.
- Les quatre presets restent sélectionnables au clavier. Les touches Stream Deck sont de vrais boutons nommés pour les lecteurs d’écran.
- Copier le serveur et la clé locale OBS. En cas de refus du presse-papiers, le champ est sélectionné et une consigne explique la copie manuelle.
- Déplier les questions fréquentes, naviguer au clavier et vérifier le site à 320, 390, 768 et 1440 px.
- Les liens de téléchargement ciblent de **vrais fichiers de la release officielle** : un clic peut télécharger l’installeur ou le ZIP. Il ne les exécute pas.

La démonstration ne contacte jamais Temhalo, OBS, Stream Deck ou une plateforme. Les états, compteurs et destinations montrés sont simulés. Elle ne simule pas le temps de transition du moteur.

Validation effectuée dans Chrome : largeurs 320, 390, 768, 1024 et 1440 px, sans débordement horizontal. Les presets, les cinq touches simulées, les bornes 0–300 s, la copie et son alternative manuelle, les questions fréquentes et la navigation ont été contrôlés. Les trois familles de polices se chargent localement ; aucune erreur JavaScript ni requête externe pendant ces interactions. Les captures ordinateur/mobile sont dans `captures/`.

## DA et contenus

Référence : `BRAND.md`. Palette Nuit, Ardoise, Halo et Écho ; Space Grotesk, Inter et JetBrains Mono embarquées localement. Les SVG du logo sont repris sans modification. Les six dessins de touches sont exportés depuis `plugins/streamdeck/src/render.mjs` (quatre actions et les deux états du relais).

Les notices OFL des trois polices sont dans `dist/assets/fonts/licenses/`. Le site et les dessins de touches suivent la licence MIT du projet, copiée dans `dist/LICENSE.txt` ; les polices conservent leurs licences.

Le contenu s’appuie sur les fonctions de preview.6 et sur les guides existants. Il ne promet ni latence totale nulle ni absence garantie de coupure. La connexion par compte indique son prérequis OAuth. Aucun identifiant personnel, jeton, compte réel ni lien privé n’est inclus.

Dans la version publique `website/`, le bandeau « Maquette locale » et la directive `noindex` ont été retirés. La version des liens de téléchargement est explicite et doit suivre les prochaines releases ; voir `website/README.md`.

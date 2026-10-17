# Site de présentation de Temhalo

Adresse publique : **https://zeeroz.github.io/Temhalo/**.

Ce dossier est la source du site publié. La maquette validée et ses captures restent dans `design/site-v1/` comme référence historique. Pour modifier le site, éditer les fichiers de ce dossier uniquement.

## Publication

Le workflow `.github/workflows/pages.yml` publie le contenu de `website/` après un push sur `main` qui modifie ce dossier ou le workflow. Il peut aussi être lancé manuellement dans **Actions → GitHub Pages → Run workflow**. Le dépôt utilise **Settings → Pages → Source: GitHub Actions**.

Le site est statique : HTML, CSS, JavaScript, SVG et polices locales. Les chemins relatifs permettent de le publier sous `/Temhalo/`. Aucun serveur applicatif, compte utilisateur ou service payant n’est requis. Les démos sont simulées et ne contrôlent pas l’application Windows.

## Vérification locale

Depuis la racine du dépôt :

```powershell
python -m http.server 4186 --bind 127.0.0.1 --directory website
```

Ouvrir `http://127.0.0.1:4186/`, puis tester les presets, les cinq touches simulées, le lien **Guide rapide**, les copies OBS, la FAQ et la section de téléchargement. Contrôler le rendu mobile et l’absence d’erreur de chargement des polices ou des images. Arrêter le serveur avec Ctrl+C après la vérification.

## Télécharger une prochaine version

Les liens pointent explicitement vers **0.7.0-preview.6**. Lors de la publication d’une prochaine version, mettre à jour la version affichée ainsi que les liens de l’installeur, du ZIP et des notes de version dans `index.html`. Vérifier d’abord que les fichiers de la release sont publics et disponibles, puis pousser la modification sur `main`.

Ne pas utiliser `/releases/latest/download/` pour les previews : GitHub n’y sélectionne pas les prereleases. La recherche des mises à jour dans Temhalo reste indépendante du site.

## Identité et licences

Référence visuelle : `BRAND.md`. Le code suit la licence MIT du projet ; `LICENSE.txt` et les notices OFL de `assets/fonts/licenses/` sont distribués avec le site. Le logo et les dessins de touches proviennent des ressources Temhalo existantes. Aucun compte, clé privée, jeton OAuth ni état réel d’un live n’est inclus.

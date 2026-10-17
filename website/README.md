# Site de présentation de Temhalo

Adresse publique : **https://zeeroz.github.io/Temhalo/**.

Ce dossier est la source du site publié. La maquette validée et ses captures restent dans `design/site-v1/` comme référence historique. Pour modifier le site, éditer les fichiers de ce dossier uniquement.

## Publication

Le workflow `.github/workflows/pages.yml` prépare et publie automatiquement le site :

- après une publication, modification ou suppression de release dans GitHub ;
- après la réussite du workflow Windows sur `main` ou un tag `v…`, y compris la reprise d’une publication ;
- après un push sur `main` qui modifie `website/`, `tools/website/` ou le workflow ;
- sur demande dans **Actions → GitHub Pages → Run workflow**.

Le second déclencheur couvre les releases publiées par le `GITHUB_TOKEN` du script existant : GitHub ne déclenche pas d’autre workflow `release` dans ce cas. Un événement de release manuel demande un déploiement depuis `main`, pour utiliser la version actuelle du site même si le tag est plus ancien. Aucun jeton personnel supplémentaire n’est nécessaire. Le dépôt utilise **Settings → Pages → Source: GitHub Actions**.

Le site est statique : HTML, CSS, JavaScript, SVG et polices locales. Les chemins relatifs permettent de le publier sous `/Temhalo/`. Aucun serveur applicatif, compte utilisateur ou service payant n’est requis. Les démos sont simulées et ne contrôlent pas l’application Windows.

## Vérification locale

Depuis la racine du dépôt, avec Node.js 24 et Python :

```powershell
node --test tools/website/releases.test.mjs
node tools/website/build.mjs
# La commande affiche le dossier créé, par exemple artifacts/website-XXXXXX.
python -m http.server 4186 --bind 127.0.0.1 --directory artifacts/website-XXXXXX
```

Ouvrir `http://127.0.0.1:4186/`, puis tester les presets, les cinq touches simulées, le lien **Guide rapide**, les copies OBS, la FAQ et la section de téléchargement. Contrôler le rendu mobile et l’absence d’erreur de chargement des polices ou des images. Arrêter le serveur avec Ctrl+C après la vérification.

## Télécharger une prochaine version

Publier normalement avec `Publier-Temhalo.ps1` : aucune modification des liens du site n’est nécessaire. `tools/website/build.mjs` consulte les releases paginées et `tools/website/releases.mjs` choisit le numéro de version le plus élevé, selon le même ordre stable/preview que l’application. Une preview plus récente est proposée ; une stable remplace ses propres previews. Republier une ancienne maintenance ne fait pas reculer le téléchargement.

Seules les releases publiques non brouillon, dont l’installeur x64, le ZIP portable et leurs deux fichiers SHA-256 sont entièrement téléversés, sont éligibles. Les noms, URL du dépôt, tailles et empreintes déclarées sont vérifiés. Une release incomplète est ignorée au profit de la dernière complète ; une erreur API ou l’absence de candidat arrête le déploiement et conserve le site précédemment publié.

La version affichée, le libellé stable/preview, les deux téléchargements et les notes de version sont injectés dans le HTML avant publication. Le fichier généré `release.json` expose les métadonnées sélectionnées pour les vérifications. Les visiteurs reçoivent une page statique complète, utilisable sans JavaScript pour les téléchargements, et n’appellent pas l’API GitHub. Le jeton de construction reste dans GitHub Actions.

Ne pas remplacer les emplacements `{{…}}` de `index.html` par des liens fixes. Ne pas utiliser `/releases/latest/download/` pour les previews : GitHub n’y sélectionne pas les prereleases. La recherche des mises à jour dans Temhalo reste indépendante du site.

## Identité et licences

Référence visuelle : `BRAND.md`. Le code suit la licence MIT du projet ; `LICENSE.txt` et les notices OFL de `assets/fonts/licenses/` sont distribués avec le site. Le logo et les dessins de touches proviennent des ressources Temhalo existantes. Aucun compte, clé privée, jeton OAuth ni état réel d’un live n’est inclus.

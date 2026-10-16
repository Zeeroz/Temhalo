# Temhalo — maquette simplifiée

**Proposition à valider avant le code · 16 octobre 2026.**

Ouvrir **Temhalo-simple.html dans un navigateur**. Le fichier est autonome,
fonctionne hors ligne et embarque les trois fontes, leurs notices de licence
et le symbole Temhalo approuvé. Recharger rétablit les données de démonstration.

- **Direct** : délai dominant, favoris, Autre délai pour la saisie précise.
  Les compteurs et courbes se déplient dans Détails du live.
- **Diffusion** : plateformes puis connexion OBS, dans un parcours vertical.
  Overlay, dock et fichier local restent facultatifs.
- **Overlay** : compositions Temhalo et aperçu 16:9 compact ; position, taille,
  visibilité puis personnalisation supplémentaire.
- **Réglages** : une seule page, sans sous-onglets ; favoris visibles,
  raccourcis, mémoire/port et aide dans des sections dépliables.

Le bouton d'activation prépare Temhalo. Le stream se lance ensuite dans OBS.
La navigation reste en haut ; le nom et le logo sont seulement dans la barre
de titre. Le moteur, les réglages chiffrés et l'installeur n'ont pas été modifiés.

## Essais rapides

1. **Direct** : appliquer 60 s. La mesure reste à 30 s pendant la transition ;
   « Terminer la transition · démo » affiche ensuite l'état final simulé.
2. Déplier **Autre délai** : tester 45 s, puis une valeur invalide comme 301.
   La saisie seule ne change pas le délai demandé.
3. Choisir **Prêt à démarrer** dans le bandeau de démonstration, puis **Diffusion**.
   Ajouter une plateforme avec une clé factice, lire les deux champs OBS et
   le guide de démarrage. Les modifications sont verrouillées en session active.
4. **Overlay** : changer style, position et taille. Les préférences de chaque
   composition sont distinctes ; le bouton Ajouter dans OBS ouvre sa procédure.
5. **Réglages** : ajouter un favori, retrouver sa commande sur Direct, ouvrir les
   raccourcis et les options avancées.
6. Comparer les scénarios du bandeau : connexion perdue, OBS absent, pression
   mémoire et port occupé. Aucune commande n'agit sur votre PC.

Toutes les interactions restent en mémoire. Copier, ouvrir un dossier, exporter,
appliquer un port ou activer Temhalo donnent un retour de démonstration ; aucun
presse-papiers, réseau, API, touche globale ou fichier utilisateur n'est commandé.
Ne pas saisir de clé réelle dans cette maquette.

## Fichiers

- `Temhalo-simple.html` : maquette autonome interactive.
- `Temhalo-simple-planche.png` : aperçu des quatre vues ; les miniatures montrent
  le haut des écrans, les captures complètes sont dans `captures/`.
- `captures/` : 21 rendus, dont les quatre vues en 1280/720/320 px, les alertes,
  la transition, le guide, les réglages avancés et le contraste forcé.
- `ANALYSE.md` : observations de DynamicDelay, comparaison au code Temhalo,
  décisions proposées, limites et procédure de revue détaillée.
- `validation.json` : 81 contrôles de disposition, interactions et absence de
  requête HTTP(S). Les 125 empreintes des fichiers protégés sont inchangées.
- `maquette.template.html`, `style.css`, `interactions.js`, `build.cjs`,
  `verify.cjs` : sources de conception et de vérification.

## Reconstruire

Avec Node.js et Playwright/Sharp déjà disponibles dans un dossier de dépendances :

```powershell
node .\design\simplification-v1\build.cjs
node .\design\simplification-v1\verify.cjs CHEMIN_ABSOLU_NODE_MODULES
```

La vérification ne lance pas Temhalo, OBS ou un relais. Les rendus de navigateur
ne constituent pas un test WinUI natif ni un audit Narrateur/DPI de l'application.
L'aperçu miniature de l'overlay est réduit dans son canevas 16:9 ; ses textes ne
sont pas les libellés des contrôles de l'application.

**Attendre la validation de cette maquette avant de modifier les sources WinUI.**

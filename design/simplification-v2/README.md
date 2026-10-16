# Maquette de la diffusion guidée Temhalo

Proposition du 16 octobre 2026, à valider avant toute modification de l’application Windows.

Ouvrir **Temhalo-diffusion-guidee.html** dans un navigateur. La maquette s’ouvre sur
Diffusion et fonctionne hors ligne, avec les polices et le symbole Temhalo incorporés.
L’ancienne proposition reste disponible dans `design/simplification-v1`.

## Le parcours

1. **Vos plateformes** : choisir une plateforme, renseigner ses identifiants et
   l’ajouter. Une liste unique couvre aussi bien une chaîne que le multistream,
   jusqu’à quatre destinations. Chaque ligne se modifie séparément.
2. **Relier OBS** : copier l’adresse locale et la clé `stream`, puis suivre le
   guide des réglages. La case « J’ai terminé » confirme une action de l’utilisateur ;
   elle n’annonce aucune détection automatique d’OBS.
3. **Lancer votre live** : activer l’écoute Temhalo, démarrer le stream dans OBS,
   puis vérifier séparément la réception et l’émission. Les états de cette maquette
   sont des exemples commandés par le sélecteur Situation.

Navigation en haut, nom et symbole uniquement dans la barre de titre. Nuit,
Ardoise, Halo et Écho ainsi que Space Grotesk, Inter et JetBrains Mono reprennent
BRAND.md. Les réglages avancés restent repliés. Overlay, dock et fichier local
sont accessibles dans une section facultative.

## Les formulaires

| Plateforme | Champs principaux | Aide adaptée |
| --- | --- | --- |
| Twitch | Clé de stream | Tableau de bord créateur ; serveur Paris préparé, choix Francfort/Londres ou adresse personnalisée dans les options |
| YouTube | Clé du live | Salle de contrôle du direct ; serveur RTMPS préparé, remplaçable dans les options |
| Kick | Adresse et clé | Deux valeurs associées à la même chaîne, fournies par le tableau de bord |
| TikTok | Adresse et clé du live | Parcours manuel réservé aux comptes disposant déjà de ces valeurs |
| Facebook | Adresse et clé de la diffusion | Valeurs fournies pour la diffusion choisie, sans adresse universelle imposée |
| Restream | Adresse et clé du flux | Flux Encoder / RTMP ; les chaînes finales se choisissent dans Restream |
| Autre serveur RTMP | Adresse et clé séparées | Autre plateforme ou serveur de test local |

Chaque formulaire conserve son propre brouillon quand on change de plateforme.
Une clé Twitch n’est pas recopiée dans le formulaire YouTube. Annuler ou Échap
abandonne les modifications. Les erreurs apparaissent près des champs concernés.
Désactiver une destination conserve ses paramètres et sa clé dans la démonstration.

La connexion de compte TikTok, le double format et les pistes VOD séparées ne sont
pas simulés comme des capacités disponibles. Cette proposition conserve le parcours
manuel compatible avec un relais RTMP ; l’ajout de libellés de plateformes ne modifie
pas les services natifs existants.

## Essayer la proposition

1. Choisir Twitch. Cliquer Ajouter sans clé pour examiner le message, puis entrer
   une valeur fictive comme `demo-twitch` et ajouter la plateforme.
2. Ajouter Kick : vérifier la présence des deux champs serveur et clé. Pour cet
   aperçu, utiliser `rtmps://example.invalid/live` et `demo-kick`.
3. Modifier une destination, changer de plateforme puis revenir : chaque brouillon
   reste distinct. Annuler et vérifier que la destination n’a pas changé.
4. Continuer vers OBS. Ouvrir les réglages vidéo et audio. Cocher la fin du guide,
   puis continuer : les connexions restent « En attente du flux ».
5. Activer Temhalo dans la maquette : l’écoute est simulée. Choisir Diffusion active,
   puis Plateforme interrompue dans Situation pour comparer les états.
6. Revenir à Prêt à démarrer pour rééditer les destinations. Vérifier les options
   facultatives et réduire la fenêtre. Direct, Overlay et Réglages restent accessibles.

Toutes les valeurs restent en mémoire, sans réseau, stockage ni presse-papiers réel.
N’utiliser aucune clé réelle. Recharger rétablit l’état initial vide.

## Fichiers et contrôles

- `Temhalo-diffusion-guidee.html` : maquette interactive autonome.
- `Temhalo-diffusion-planche.png` : aperçu de la sélection, du formulaire Kick,
  du guide OBS et de la vérification des connexions.
- `captures/` : rendus des sept formulaires, des étapes, des états de connexion,
  des petites fenêtres et du contraste élevé.
- `validation.json` : résultats des contrôles de navigateur et comparaison des
  empreintes des 125 fichiers protégés de l’application.
- `maquette.template.html`, `style.css`, `interactions.js`, `build.cjs` et
  `verify.cjs` : sources de conception et de contrôle de cette proposition.

Les sources WinUI, le moteur, les tests de l’application, la configuration,
BRAND.md et l’installeur sont inchangés. Les contrôles de navigateur ne constituent
pas une validation du rendu WinUI natif ou d’un stream réel.

Reconstruction avec Node.js et les dépendances Playwright/Sharp disponibles :

```powershell
node .\design\simplification-v2\build.cjs
node .\design\simplification-v2\verify.cjs CHEMIN_ABSOLU_NODE_MODULES
```

## Références pour les aides

Les serveurs Twitch proposés et le serveur YouTube préparé suivent les
[définitions officielles OBS](https://github.com/obsproject/obs-studio/blob/master/plugins/rtmp-services/data/services.json).
Les explications utilisent les procédures de
[YouTube pour RTMPS](https://support.google.com/youtube/answer/10364924),
[Kick pour le serveur et la clé](https://help.kick.com/en/articles/7066931-how-to-stream-on-kick-com)
et [Restream pour un flux RTMP](https://support.restream.io/en/articles/6505602-go-live-with-streaming-software).
TikTok et Facebook demandent les valeurs déjà fournies par le service ; aucun
accès automatique à un compte ni aucune adresse publique fixe n’est promis.

**Attendre la validation de la maquette avant le raccordement WinUI.**

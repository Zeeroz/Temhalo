# Maquette Diffusion avec connexion de compte

Proposition Temhalo du 16 octobre 2026, à valider avant le code WinUI.

Ouvrir **Temhalo-diffusion-comptes.html** dans un navigateur. Cette version conserve
le parcours Plateformes → OBS → Lancement et la DA approuvée. Les versions précédentes
restent intactes dans `design/simplification-v1` et `design/simplification-v2`.

## Ce qui change

- **Twitch, YouTube et Kick** commencent par « Connecter mon compte ».
- **Utiliser une clé manuellement** ouvre les champs et aides de la version précédente.
  Refermer cette option ramène au parcours de compte, sans perdre le brouillon manuel.
- Après une autorisation simulée, le profil est visible. **Sur YouTube, choisir le live
  est nécessaire avant d’ajouter la destination.**
- La liste distingue **Compte lié** et **Clé manuelle**. Le compte lié ne confirme ni
  réception OBS ni émission vers la plateforme.
- Un refus, une indisponibilité ou une expiration permettent de réessayer ou de passer
  à la clé manuelle. Annuler une modification conserve la destination enregistrée.
- TikTok, Facebook, Restream et le serveur personnalisé gardent le parcours manuel.

La palette Nuit, Ardoise, Halo et Écho, Space Grotesk, Inter et JetBrains Mono,
la navigation supérieure et le logo uniquement dans la barre de titre sont conservés.
Direct, Overlay et Réglages gardent leur présentation précédente.

## Procédure de revue

1. Choisir **Twitch → Connecter mon compte**. Le panneau indique explicitement
   qu’aucun site n’est ouvert. Choisir **Autorisation acceptée**, puis **Ajouter Twitch**.
2. Ajouter **YouTube**, accepter l’autorisation simulée et choisir un live dans la liste.
   Le bouton d’ajout reste désactivé tant qu’aucun live n’est choisi.
3. Ajouter **Kick** et essayer d’abord **Autorisation refusée**, puis **Connexion
   indisponible**. Le bouton permet de réessayer et la clé reste accessible.
4. Ouvrir **Utiliser une clé manuellement**. Pour cet aperçu, saisir
   `rtmps://example.invalid/live` et une clé fictive telle que `demo-kick`.
   Refermer puis rouvrir l’option : le brouillon est conservé.
5. Modifier un compte lié. Déplier **Autre état de connexion · démo**, puis simuler
   une expiration. Le compte doit être reconnecté, ou la clé manuelle utilisée.
   Annuler laisse la destination enregistrée dans son état antérieur.
6. Passer au guide OBS puis au lancement. Les connexions restent en attente tant
   que le scénario de démonstration n’indique pas de flux reçu.
7. Réduire la fenêtre ; tester Tab, Entrée, Échap, les options et les erreurs.

Les profils et lives sont fictifs. **Aucune connexion OAuth réelle, aucun compte
réel, aucun jeton ni aucune clé n’est récupéré.** Les résultats d’autorisation sont
des commandes de démonstration dans Temhalo, sans reproduction d’une page de connexion
ou de consentement de plateforme. Toutes les interactions restent en mémoire, hors
ligne ; recharger réinitialise la maquette. Ne pas saisir de véritables identifiants.

## Livrables et contrôle

- `Temhalo-diffusion-comptes.html` : maquette autonome avec les fontes et leurs notices.
- `Temhalo-comptes-planche.png` : aperçu connexion, clé manuelle, live YouTube et liste des comptes.
- `captures/` : formulaires, états d’autorisation, parcours OBS, petites fenêtres et contraste élevé.
- `validation.json` : résultats de vérification du navigateur, des interactions et des empreintes.
- `maquette.template.html`, `style.css`, `interactions.js`, `build.cjs`, `verify.cjs` : sources de la proposition.

La vérification compare les 125 fichiers protégés à la version initiale. Les sources
WinUI, le moteur, les configurations, BRAND.md et l’installeur sont inchangés.
Les contrôles de navigateur ne constituent pas une validation d’OAuth réel, de WinUI
natif ni d’une diffusion sur une plateforme.

Reconstruction avec Node.js et les dépendances Playwright/Sharp disponibles :

```powershell
node .\design\simplification-v3\build.cjs
node .\design\simplification-v3\verify.cjs CHEMIN_ABSOLU_NODE_MODULES
```

Le futur raccordement devra enregistrer Temhalo auprès des plateformes, employer
leurs parcours d’autorisation officiels et protéger les jetons localement. Références :
[Twitch OAuth](https://dev.twitch.tv/docs/authentication/getting-tokens-oauth/),
[API de clé Twitch](https://dev.twitch.tv/docs/api/reference/#get-stream-key),
[YouTube Desktop OAuth](https://developers.google.com/youtube/data/guides/auth/installed-apps),
[YouTube liveStreams](https://developers.google.com/youtube/data/live/reference/rest/v3/liveStreams),
[API Channels Kick et permission streamkey:read](https://docs.kick.com/apis/channels).

**Attendre la validation de cette proposition avant de modifier l’application.**

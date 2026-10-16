# Temhalo pour Stream Deck

Contrôlez le délai directement dans Temhalo : aucun dock OBS, lien privé ou port à configurer. Windows 10/11 et Stream Deck 6.6 ou ultérieur sont requis. Le plugin utilise le runtime Node fourni par Stream Deck ; aucun runtime supplémentaire n'est demandé à l'utilisateur.

**Compatibilité : Temhalo et le plugin doivent être en version `0.7.0-preview.4` ou ultérieure. La version `0.7.0-preview.3` utilise encore la liaison HTTP.** Mettez à jour les deux composants ; les UUID d'actions et les valeurs des touches sont conservés.

## Installation

1. Installez l'application et le fichier `Temhalo.streamDeckPlugin` de la même construction. Le plugin est distribué dans `plugins/streamdeck` de Temhalo et dans le ZIP portable.
2. Ouvrez Temhalo. Ajoutez une action du groupe **Temhalo** sur votre Stream Deck : la connexion est automatique pour le compte Windows courant, sur ce PC.
3. Réglez la valeur avec le champ, le curseur ou les choix rapides. Le nombre s'enregistre avec Entrée ou en quittant le champ ; le curseur au relâchement. **Enregistré** confirme la réponse du plugin. Ces réglages n'appliquent aucune commande au live.
4. Appuyez sur une touche pour appliquer sa commande. Temhalo reste responsable du moteur et du trajet du flux.

Toutes les touches partagent une seule connexion. Les mesures sont envoyées par Temhalo lorsqu'elles changent ; aucun polling HTTP. La fermeture de l'application affiche l'indisponibilité, puis la connexion reprend automatiquement à sa réouverture. Une commande non confirmée n'est jamais rejouée.

Les touches preset, ajout, retrait et retour à 0 reprennent le dessin validé : horloge, signe +, signe − lavande et retour au direct, avec la valeur et son unité sur une même ligne. Un contour ambre signale une transition ou une indisponibilité du flux ; « HORS LIGNE » ou « OCCUPÉ » remplace la valeur lorsqu'une commande ne peut pas être utilisée.

## Actions

| Action | Effet |
|---|---|
| Protection | Applique le délai de protection enregistré dans les raccourcis de Temhalo. |
| Direct · 0 s | Demande le minimum local et la reprise sur une image clé. |
| Appliquer un délai | Preset de 0 à 300 s ; plusieurs touches peuvent avoir des valeurs différentes. |
| Ajouter / retirer | ±10 s par défaut ; pas réglable de 1 à 60 s. |
| Délai actuel | Mesure réelle et cible distinctes, sans commande au live. |
| État du flux | Entrée OBS et nombre de sorties prêtes, sans commande au live. |
| Régler à la molette | Stream Deck + : tourner ajoute/retire le pas ; appuyer ou toucher applique la protection. |

« 0 s » signifie le minimum local : OBS, le réseau, la plateforme et le lecteur ajoutent leur latence. Une mesure absente affiche « — ». Les touches peuvent préparer une cible lorsque le relais est arrêté ; elles ne démarrent ni n'arrêtent le relais ou OBS.

## Développement

Node.js 24 et pnpm 11.25.0 sont nécessaires uniquement à la construction. Les dépendances sont verrouillées ; le bundle cible Node 20 pour Stream Deck 6.6/6.8. Depuis la racine du dépôt :

```powershell
./tools/Build-StreamDeck.ps1
```

Le script restaure sans scripts d'installation, construit, teste, valide avec l'outil officiel Elgato et produit `artifacts/streamdeck/packages/Temhalo.streamDeckPlugin` avec son SHA-256. Il ne modifie pas les profils installés. La version suit celle de Temhalo.

Les tests Node utilisent un vrai canal Windows et un hôte Stream Deck simulé. Les tests .NET connectent aussi le vrai client Node au vrai serveur de l'application. Ils vérifient commandes, bornes, expirations, données envoyées, coupures et reprise sans rejeu. Avec Playwright disponible : `node tools/check-inspector.mjs <node_modules-de-playwright>` vérifie les huit panneaux à 320–460 px, l'enregistrement et l'état de connexion. Une validation matérielle reste nécessaire, notamment pour les molettes et écrans tactiles.

La connexion utilise un canal Windows limité au compte courant. Les clés de diffusion et jetons OAuth ne sont jamais communiqués au plugin. Les anciennes capacités de liaison sont effacées des réglages globaux lors de la migration. Les valeurs de chaque touche restent dans le profil Stream Deck. Ce mécanisme ne protège pas contre un autre programme exécuté sous le même compte Windows.

[Procédure de test et transport direct](../../docs/CONTROLES-LOCAUX.md). Plugin indépendant d'Elgato, non publié sur Marketplace. Code MIT et notices de dépendances incluses. [SDK officiel](https://docs.elgato.com/streamdeck/sdk/).

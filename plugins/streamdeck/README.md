# Temhalo pour Stream Deck

Des commandes de délai et un suivi lisible pendant le live, avec le halo, la menthe et la lavande de Temhalo. Windows 10/11, logiciel Stream Deck **6.6 ou plus récent**, Temhalo **0.7.0-preview.1 ou plus récent**. Le logiciel Stream Deck fournit Node.js 20 ; aucun runtime supplémentaire n'est demandé aux utilisateurs du plugin.

## Installation et connexion

1. Installez Temhalo. Double-cliquez sur **Temhalo.streamDeckPlugin**, puis acceptez l'installation dans Stream Deck. Le fichier est livré dans `plugins/streamdeck` du dossier de Temhalo et dans le ZIP portable. Il peut aussi être distribué seul.
2. Dans Temhalo : **Diffusion → Options : test local, overlay et dock OBS → Piloter depuis OBS → Copier le lien privé du dock**. Le serveur navigateur doit être actif ; son port est réglable dans Réglages → Confidentialité.
3. Dans Stream Deck, glissez une action du groupe **Temhalo** sur une touche. Collez le lien dans ses propriétés et cliquez sur **Relier**. La connexion est commune à toutes les touches ; le champ est ensuite effacé.
4. Configurez vos délais et vos pas si nécessaire. Lancez le relais et OBS depuis leurs applications habituelles.

Temhalo et Stream Deck doivent fonctionner sur le **même PC**. Le lien de l'overlay permet seulement la lecture et ne convient pas : utilisez bien celui du **dock**. Ne partagez jamais ce lien. Il ne s'agit pas de la clé Twitch/YouTube/Kick.

## Les actions

| Action | Effet |
|---|---|
| Protection | Applique le délai de protection enregistré dans les raccourcis de Temhalo. La valeur est lue dans Temhalo. |
| Direct · 0 s | Demande le retour au minimum local sur une image clé. |
| Appliquer un délai | Preset de 0 à 300 s ; ajoutez plusieurs touches pour 15, 30, 60 s… |
| Ajouter du délai | +10 s par défaut ; pas personnalisable de 1 à 60 s. |
| Retirer du délai | −10 s par défaut ; pas personnalisable de 1 à 60 s. |
| Délai actuel | Lecture seule : mesure réelle et cible distinctes. Une pression actualise l'état. |
| État du flux | OBS reçu/absent et nombre de sorties prêtes. Une pression actualise l'état. |
| Régler à la molette | Stream Deck + : tourner ajoute/retire 5 s par cran, pas réglable. Appuyer ou toucher brièvement applique la protection. |

Exemple de disposition à huit touches : **Protection · Direct · +10 · −10**, puis **15 s · 30 s · Délai actuel · État du flux**. Les touches lecture seule restent utiles à côté du jeu. La molette évite de multiplier les presets.

Les touches reflètent aussi les modifications faites dans Temhalo, le dock OBS ou les raccourcis clavier. Une mesure absente apparaît « — », jamais comme un délai nul. La couleur ambre signale une transition, une entrée absente ou une sortie indisponible. Une cible appliquée n'est pas une promesse que la construction du délai est terminée. « Sortie prête » ne certifie pas la lecture chez les viewers.

L'augmentation conserve les paliers du moteur ; elle doit accumuler le média nécessaire. La réduction reprend sur image clé. « 0 s » signifie le minimum local réalisable : OBS, le réseau, la plateforme et le lecteur ajoutent leur latence. Les touches peuvent préparer une cible lorsque le relais est arrêté ; elles ne démarrent ni n'arrêtent le relais ou OBS.

## Fiabilité et accès privé

Un seul poller HTTP est partagé par les touches visibles, toutes les 500 ms. Temhalo publie ses mesures environ une fois par seconde ; l'affichage n'est pas une mesure à la milliseconde. La disparition de toutes les actions arrête l'interrogation.

Les commandes sont sérialisées, bornées et expirent rapidement ; elles sont abandonnées si la connexion tombe. Un POST dont la réponse est perdue peut avoir été appliqué : le plugin ne le répète pas et invite à vérifier la cible. La reprise automatique ne rejoue aucune commande. Les touches lecture seule n'envoient jamais de modification.

Seul `http://127.0.0.1:<port>/dock#<capacité>` est accepté. Aucune redirection HTTP ni serveur distant n'est suivi. Les requêtes ne transportent que la capacité d'accès local, jamais les comptes ou les clés de diffusion. Les valeurs et pas appartiennent aux réglages de chaque touche ; la capacité appartient aux **réglages globaux locaux** Stream Deck, exclus des profils exportés. Cela ne constitue pas une protection contre les autres logiciels exécutés sous le même compte Windows. Les traces de protocole du SDK sont désactivées.

## Développement et tests

Node.js 24 et pnpm 11.25.0 sont nécessaires pour construire, pas pour utiliser le plugin. Versions et empreintes des dépendances sont verrouillées dans `pnpm-lock.yaml`. Le runtime officiel `@elgato/streamdeck` 2.1.2 conserve la compatibilité avec Stream Deck 6.6/6.8 et son Node 20 ; le bundle est ciblé Node 20.

```powershell
./tools/Build-StreamDeck.ps1
```

Le script restaure sans scripts d'installation, teste, construit, fait valider le manifeste par l'outil officiel Elgato, puis produit `artifacts/streamdeck/packages/Temhalo.streamDeckPlugin` et son SHA-256. Il n'installe rien dans les profils Stream Deck. Les anciens builds sont conservés sous `artifacts/streamdeck/previous-*`.

Les tests contrôlent les liens, commandes, limites, affichages, refus d'accès, redirections, pertes de connexion, concurrence et absence de rejeu. Un hôte Stream Deck simulé vérifie le protocole du bundle officiel avec l'API locale réelle simulée. Une validation matérielle reste nécessaire pour les écrans, molettes, écrans tactiles et profils de chaque modèle.

### Procédure de validation avec un appareil

1. Reliez le plugin au dock, ajoutez les huit actions adaptées à votre appareil et vérifiez la lecture des presets/pas.
2. Avec un chrono OBS et une sortie locale VLC, appliquez 0 → 15 → 30 → 0 s. Vérifiez cible, transition et délai réel dans Temhalo et sur la touche.
3. Appuyez rapidement plusieurs fois sur +10/−10 ; la cible doit suivre, bornée à 0–300. Sur Stream Deck +, tournez dans les deux sens et appuyez pour appliquer la protection.
4. Modifiez le délai depuis Temhalo : les touches doivent suivre sans nouvelle association.
5. Fermez Temhalo, appuyez sur une touche, puis rouvrez-le. Le plugin doit afficher l'indisponibilité, se reconnecter et ne rejouer aucune commande. Vérifiez la cible avant de reprendre.
6. Essayez un lien d'overlay ou un accès invalide : aucune commande ne doit être appliquée.
7. Exportez un profil de test et recherchez la capacité utilisée pour ce test : elle ne doit pas y figurer. La connexion doit être refaite sur un autre PC.

Ce plugin est indépendant d'Elgato. Il n'est pas publié sur Marketplace ; une soumission au catalogue nécessitera sa procédure de validation. Le code Temhalo reste sous MIT ; les notices des dépendances utilisées sont livrées dans le plugin.

Références : [SDK officiel](https://docs.elgato.com/streamdeck/sdk/), [réglages globaux et profils](https://docs.elgato.com/streamdeck/sdk/guides/settings/), [distribution](https://docs.elgato.com/streamdeck/sdk/introduction/distribution/).

# Contrôles locaux de Temhalo

Ce guide décrit Temhalo `0.7.0-preview.4` et les sources actuelles. La connexion directe nécessite l'application et le plugin mis à jour ensemble ; `0.7.0-preview.3` utilise encore le lien du dock. L'overlay est retiré de l'application, des réglages et du serveur navigateur. Les anciens paramètres chiffrés restent lisibles, sans réinitialiser les comptes ou destinations.

## Stream Deck

1. Ouvrez Temhalo, puis installez le fichier `Temhalo.streamDeckPlugin` correspondant à cette construction dans Stream Deck.
2. Ajoutez les actions Temhalo à vos touches : elles se connectent automatiquement à l'application du même compte Windows.
3. Préparez **15 s**, **+10 s**, **−10 s** et **Direct 0 s**. Modifier les réglages d'une touche ne change pas le live ; appuyer sur la touche applique la commande.
4. Relais arrêté, vérifiez la cible dans Temhalo : 15 → 25 → 15 → 0. Fermez puis rouvrez Temhalo : les touches doivent retrouver l'application sans association manuelle et sans rejouer les pressions faites pendant l'arrêt.
5. Avec une sortie locale VLC et un chrono dans OBS, vérifiez 0 → 15 → 0. L'augmentation accumule le délai ; la réduction reprend sur une image clé. La cible est distincte du délai effectivement mesuré.

Le plugin ne démarre ni OBS ni le relais. Le retour à 0 demande le minimum local ; le lecteur et les plateformes conservent leur propre latence. Sous **Réglages → Confidentialité → Stream Deck**, l'état indique si un client est connecté. Les deux logiciels doivent tourner sous le même compte Windows ; évitez d'en lancer un en administrateur et l'autre normalement.

## OBS et destinations

Le trajet média reste inchangé : OBS → MediaMTX → FFmpeg lecteur → moteur de délai → un émetteur FFmpeg par destination. Dans Diffusion, ajoutez jusqu'à quatre plateformes, puis suivez la configuration OBS. Pour tester sans diffusion publique : [aperçu local dans VLC](APERÇU-LOCAL-VLC.md).

Le dock navigateur OBS est un contrôle facultatif distinct du plugin. Il est désactivé par défaut et n'est pas nécessaire pour Stream Deck. Pour l'utiliser, activez-le dans **Réglages → Confidentialité → Dock OBS**, puis copiez son lien privé depuis Diffusion. Le listener reste limité à `127.0.0.1`. Les anciennes adresses `/overlay` retournent 404.

## Transport et vérification technique

Une connexion Windows *named pipe* bidirectionnelle est partagée par toutes les touches visibles. Son nom inclut le SID Windows et son accès est limité au compte courant avec `PipeOptions.CurrentUserOnly`. Aucun port HTTP, dock, clé de diffusion ou jeton OAuth n'est utilisé par le plugin. Les mesures sont poussées lorsqu'elles changent ; le rafraîchissement de la mesure suit celui de l'application, environ une seconde. Un heartbeat de cinq secondes détecte les connexions devenues silencieuses. Aucune comparaison de consommation CPU n'est revendiquée.

Les commandes sont séquentielles et expirent rapidement. Une réponse perdue entraîne l'abandon de la file ; une commande dont l'exécution est incertaine n'est jamais répétée automatiquement. L'absence d'état est affichée comme indisponible. Les sorties partagées avec le plugin ne contiennent que leurs indicateurs de disponibilité, sans noms, adresses ou clés.

```powershell
dotnet build Temhalo.sln -c Release -p:Platform=x64 --no-restore
dotnet test Temhalo.sln -c Release -p:Platform=x64 --no-build --no-restore
./tools/Build-StreamDeck.ps1
```

Les tests comprennent le vrai client Node du plugin connecté au vrai serveur .NET par un canal Windows isolé, les pressions successives, les coupures, la reconnexion sans rejeu, les limites de trames, les expirations et la lecture des anciens réglages DPAPI. Les tests de l'inspecteur utilisent la WebView simulée et ne remplacent pas une vérification sur l'appareil.

Références : [IPC Node.js](https://nodejs.org/api/net.html#ipc-support), [canaux .NET](https://learn.microsoft.com/dotnet/standard/io/pipe-operations), [SDK Stream Deck](https://docs.elgato.com/streamdeck/sdk/).

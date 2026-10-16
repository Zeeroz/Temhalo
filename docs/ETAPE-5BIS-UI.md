# Temhalo — étape 5 bis : interface WinUI 3

> Ce document décrit la livraison initiale de l'interface, désormais validée. Les limites de raccordement ci-dessous sont historiques : multistream, source navigateur et dock sont maintenant implémentés. Consulter [le guide courant et sa procédure de test](ETAPE-5BIS-SERVICES.md).

Les maquettes R3 ont été validées. Cette livraison implémente la navigation supérieure **Direct, Diffusion, Overlay, Réglages**, sans barre latérale ni second bloc logo/nom. La barre de titre Windows conserve l'icône et le nom Temhalo. La palette et les polices de `BRAND.md` sont conservées. L'étape 6 n'est pas commencée.

## Ce qui fonctionne

| Vue | Comportements raccordés |
|---|---|
| Direct | Démarrage/arrêt du relais, délai mesuré et cible distincts, frise temporelle, presets immédiats, slider et champ 0–300 s avec application explicite, états d'entrée/sortie, RAM et avertissement, tags en réserve/sautés, reconnexions, historique réel des débits sur 60 s et accès au journal. |
| Diffusion | Trajet OBS → temporisation → destination, configuration de l'unique sortie fichier ou RTMP/RTMPS, plateforme, URL et clé masquée, sauvegarde DPAPI, guide OBS et préparation de la destination locale VLC. |
| Overlay | Quatre compositions originales, aperçu local 1920 × 1080 dans trois états simulés, quatre positions, taille, accent HEX, libellé, visibilité à 0 s. Chaque composition conserve ses réglages ; sauvegarde locale chiffrée. |
| Réglages | Presets personnalisables (0 s obligatoire, huit valeurs au maximum), raccourcis globaux et délai de protection, limite RAM pour le prochain démarrage, journal, export manuel des événements affichés, ouverture des dossiers de logs/session. |

L'architecture média, le parseur et le buffer Core restent inchangés. Le traitement des paquets ne passe pas par le dispatcher de l'interface. Les valeurs de débit viennent des différences de compteurs espacées d'au moins 750 ms ; une reconnexion réinitialise la référence sans créer de pic artificiel. L'historique reste borné à une minute.

Le document DPAPI v1 accepte désormais une extension d'interface facultative. Les anciens fichiers restent lisibles. Les réglages des compositions sont édités en mémoire puis conservés par **Enregistrer l'overlay**. L'ajout/suppression d'un preset enregistre les préférences immédiatement. **Rétablir les compositions** prépare les valeurs par défaut ; enregistrer pour les conserver.

## Limites visibles dans l'interface

- Une seule destination réelle est prise en charge. Le multistream attend son implémentation.
- L'overlay est un **éditeur et aperçu local**. Aucun serveur HTTP/WebSocket, source navigateur active, dock OBS ni commande de bot n'est présenté comme disponible.
- La vérification automatique du profil OBS, obs-websocket, l'anglais, le lancement avec Windows, les mises à jour et l'historique avec relecture nécessitent encore leur raccordement.
- Un overlay ajouté en amont dans OBS serait retardé avec l'image. Un futur indicateur de transition destiné au public devra tenir compte de ce placement.
- Les pauses de 5 s séparées de reprises conservent les limites documentées à l'étape 3 : aucune garantie de maintien de session pour toutes les plateformes.

## Compiler et lancer

Depuis la racine du projet, avec les prérequis de l'étape 0 :

```powershell
dotnet restore Temhalo.sln -p:Platform=x64 --locked-mode
dotnet build Temhalo.sln -c Debug -p:Platform=x64 --no-restore
dotnet test Temhalo.sln -c Debug -p:Platform=x64 --no-build --no-restore
& .\tools\native\Get-NativeTools.ps1
& .\tools\Start-Temhalo.ps1
```

Pour tester sans modifier vos réglages habituels, fermer les autres instances de Temhalo et utiliser un répertoire réservé au test :

```powershell
& .\tools\Start-Temhalo.ps1 -WindowSize 1280x820 `
    -SettingsDirectory .\artifacts\essai-interface
```

Ce paramètre explicite isole uniquement le fichier DPAPI. Les journaux conservent leur rotation habituelle ; les sessions de test sont enregistrées sous `artifacts/relay-ui/`. L'application ne lance aucun relais automatiquement. Ne pas ouvrir plusieurs instances pour tester les raccourcis : Windows refuse une combinaison déjà enregistrée par une autre instance.

## Relecture des quatre vues

1. **Direct** : vérifier la navigation en haut et l'absence de marque redondante. Choisir 30 s : la cible devient `00:30`. Préparer 42 s au champ/slider : la cible reste 30 s jusqu'à **Appliquer**. Valider 301, −1 ou 1,5 au champ : message d'erreur et bouton désactivé. Revenir à 0 s. Sans flux, le délai mesuré reste `—`.
2. **Réglages → Vos commandes** : ajouter 45 s, revenir au Direct et vérifier le nouveau preset. Refaire l'ajout : doublon refusé. Tester une combinaison dupliquée pour deux actions : enregistrement refusé. Remettre quatre combinaisons distinctes puis enregistrer. Les contrôles gardent un nom accessible et un focus visible.
3. **Overlay** : choisir successivement Signature, Repère, Fil du temps et Chrono. Modifier le libellé, l'accent, la taille et la position d'une composition ; les autres conservent leurs valeurs. Essayer `#ZZZZZZ` : erreur, aperçu valide précédent conservé. Comparer les trois états ; désactiver l'affichage à 0 s et vérifier que le badge disparaît seulement en aperçu En direct. Enregistrer, fermer et relancer avec le même répertoire : paramètres retrouvés.
4. **Diffusion** : comparer le fichier FLV et RTMP/RTMPS. La clé reste masquée. Les champs sont désactivés pendant une session. **Configurer l'entrée** ouvre les paramètres OBS à recopier ; il ne modifie pas OBS. **Préparer la destination locale** renseigne le port 1936 et la clé `preview` sans démarrer de serveur.
5. **Réglages → Continuité** : enregistrer une autre limite RAM. Elle s'applique au démarrage suivant, sans modifier le budget d'une session déjà active.
6. **Confidentialité** : vérifier les événements récents, exporter manuellement un fichier texte et ouvrir les dossiers. L'export concerne les 200 événements affichés au maximum ; les journaux avec rotation restent accessibles séparément. Aucun envoi automatique.
7. Fermer puis relancer aux tailles `720x960` et `320x820`. Les colonnes s'empilent et les commandes restent accessibles par défilement vertical. À très petite largeur, la navigation supérieure emploie son menu de débordement ; les rubriques des réglages et compositions utilisent une liste compacte. Tester aussi fenêtre agrandie et parcours Tab/Maj+Tab.

## Test réel vers un fichier local

Le port **1935 doit être libre** : fermer volontairement le relais concurrent avant de démarrer Temhalo. Ne pas changer le port OBS vers 1936 : ce port concerne la sortie locale facultative.

1. Dans Diffusion, sélectionner **Fichier FLV local**, puis revenir au Direct et appliquer 0 s.
2. Cliquer **Démarrer le relais**. Publier OBS vers `rtmp://127.0.0.1:1935/live`, clé `stream`, H.264/AAC, CBR, images clés toutes les secondes. Une mire synthétique est aussi disponible :

```powershell
& .\tools\Send-TestPattern.ps1 -Seconds 120
```

3. Vérifier entrée connectée, écriture active, débit et progression du live. Déplier **Activité du flux** : les courbes doivent suivre les mesures, sans données de démonstration.
4. Appliquer 15 s, attendre les paliers, réduire à 5 s puis revenir à 0 s. Vérifier l'évolution de la cible, de la mesure et de la frise. Les diminutions reprennent sur image clé.
5. Arrêter OBS puis **Arrêter le relais** et attendre la vidange. Ouvrir le dossier de session depuis Confidentialité ; lire le fichier `relay-0001.flv` dans VLC. Vérifier image et son. Le [guide VLC local](APERÇU-LOCAL-VLC.md) permet aussi une lecture en direct après délai via le serveur 1936.
6. Pour les incidents de processus/réseau et les mesures de synchronisation, suivre les [tests de l'étape 5](ETAPE-5.md). La campagne de robustesse de 30 minutes et le packaging restent dans l'étape 6.

## Vérification effectuée sur ce poste

Compilation Debug x64 sans avertissement ni erreur ; **65 tests réussis** (36 Core, 22 infrastructure, 7 présentation). Les tests ajoutés couvrent les références de débit lors des reconnexions, les rafraîchissements intermédiaires, la borne d'historique et la compatibilité/encryption des préférences par composition.

L'interface a été lancée nativement avec un répertoire DPAPI dédié. Navigation, ajout/restauration d'un preset, sauvegarde/restauration des compositions, rejet d'une cible hors borne et alerte de port occupé ont été vérifiés. Contrôles visuels à 1280 px, 720 px, 320 px et en fenêtre agrandie 1920 × 1032 ; accès aux quatre vues par le menu compact et ouverture/fermeture du guide OBS validés. Les captures natives se trouvent dans `design/native-etape-5bis/`. Le contraste élevé conserve les ressources Windows existantes mais n'a pas été activé sur le poste pendant ce contrôle.

**Le test avec média n'a pas été exécuté pour cette livraison** : DynamicDelay occupait le port 1935. Ce processus a été laissé intact. Les tests unitaires du moteur passent ; ils ne remplacent pas le test OBS/VLC ni une session sur une plateforme.

## Fichiers principaux

- `src/Temhalo.App/Views/DashboardPage.xaml` et ses partiels `.xaml.cs` / `.Interface.cs` : structure, navigation et commandes.
- `src/Temhalo.App/Controls/TelemetryCanvas.cs` : frise et historique des débits.
- `src/Temhalo.App/Controls/OverlayPreview.cs` : rendu local des quatre compositions.
- `src/Temhalo.App/Controls/WrapPanel.cs` : dispositions adaptatives natives.
- `src/Temhalo.Presentation/BitrateHistory.cs` : historique indépendant de WinUI.
- `src/Temhalo.Infrastructure.Windows/Settings/InterfacePreferences.cs` : préférences persistantes.
- `BRAND.md` : référence visuelle mise à jour pour la R3.

Le ZIP de livraison contient les sources complètes et les ressources nécessaires. Les binaires natifs, les sorties compilées, les réglages privés et les fichiers de session sont exclus. L'installeur sera traité après validation à l'étape 6.

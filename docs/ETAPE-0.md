# Préparation du projet Temhalo

L'étape 0 met en place une solution compilable, des dépendances verrouillées, l'entrée RTMP locale et un profil OBS de référence. La chaîne média sera raccordée à l'étape 1. Le dossier de travail peut être ouvert tel quel : la solution et les projets portent le nom Temhalo.

## Arborescence

```text
Temhalo.sln
global.json
Directory.Build.props
Directory.Packages.props
NuGet.Config
BRAND.md
README.md
src/
  Temhalo.Core/                       net8.0, aucune dépendance NuGet
    Abstractions/                    ports et commandes du moteur
    Flv/                             parseur, en-têtes et réécriture
    Buffering/                       stockage borné et mémoire
    Timing/                          horloge et planification
  Temhalo.Infrastructure.Windows/     processus, DPAPI, logs, hotkeys
    Processes/
    Security/
    Logging/
    HotKeys/
  Temhalo.RelayHost/                  diagnostic sans WinUI
  Temhalo.App/                        squelette WinUI 3 x64
    Styles/Colors.xaml               palette validée
tests/
  Temhalo.Core.Tests/                 tests du moteur aux étapes 2 et 3
  Temhalo.Infrastructure.Tests/       tests de supervision à partir de l'étape 1
config/
  mediamtx.yml                       configuration de l'ingest local
  temhalo.defaults.json              valeurs par défaut sans clé de stream
  native-tools.lock.json             versions natives, URL et SHA-256
tools/
  native/Get-NativeTools.ps1          acquisition et vérification
  Test-Stage0.ps1                    contrôles sans diffusion
  branding/                          génération de l'identité validée
docs/
  ETAPE-0.md
  ARCHITECTURE.md
  DEPENDANCES.md
assets/brand/                        SVG, PNG, ICO et polices validés
design/                              maquette approuvée et captures
artifacts/                           téléchargements et contrôles locaux, ignorés par Git
```

Lors de la livraison de l'étape 0, les fichiers `.gitkeep` réservaient les dossiers du moteur, les projets de tests étaient préparés sans tests factices et l'hôte console affichait son état de préparation avant de s'arrêter. L'[étape 1](ETAPE-1.md) ajoute désormais le relais et ses tests. Le shell WinUI montre le logo ; son dashboard sera implémenté à l'étape 4 depuis la maquette validée.

## Outils et ciblage

La préparation utilise **Windows x64, SDK .NET 10.0.401, C# 12 et cibles .NET 8**. `global.json` accepte les correctifs de la même bande de SDK. Les projets Windows compilent contre les API du SDK Windows 26100 et déclarent un minimum Windows 10 build 17763. Ce minimum d'API ne remplace pas les essais sur Windows 10 et Windows 11 prévus au packaging.

Le SDK .NET 10 n'impose pas le runtime .NET 10 au produit : `Temhalo.Core` cible `net8.0` et les projets Windows ciblent `net8.0-windows10.0.26100.0`. Voir les explications Microsoft sur la [version du SDK Windows et le minimum de l'OS](https://learn.microsoft.com/en-us/windows/apps/get-started/versioning-overview).

Le poste de développement a un runtime .NET 8. L'application WinUI est configurée sans MSIX pour le développement, avec les composants Windows App SDK déployés à côté de l'exécutable. Le runtime .NET reste fourni par le poste à cette étape. Le format de distribution et l'autonomie complète seront traités à l'étape 6. [Déploiement Windows App SDK](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)

## Packages NuGet

Les versions directes sont centralisées dans `Directory.Packages.props`. Chaque projet produit un `packages.lock.json` qui fixe également ses dépendances transitives. Une restauration ordinaire met à jour les verrous ; les contrôles suivants emploient `--locked-mode`.

| Package | Version | Rôle |
|---|---|---|
| Microsoft.WindowsAppSDK | 2.5.1 | WinUI 3, fenêtre et runtime Windows App SDK |
| Microsoft.Windows.SDK.BuildTools | 10.0.26100.9169 | Outillage de compilation des ressources Windows |
| CommunityToolkit.Mvvm | 8.4.2 | ViewModels et commandes, à utiliser à l'étape 4 |
| Microsoft.Extensions.Hosting | 8.0.1 | Composition et cycle de vie de l'hôte |
| Microsoft.Extensions.Hosting.Abstractions | 8.0.1 | Services de fond dans l'infrastructure |
| Microsoft.Extensions.Logging.Abstractions | 8.0.3 | Contrats de journalisation |
| Microsoft.Extensions.Options | 8.0.2 | Options typées |
| System.Security.Cryptography.ProtectedData | 8.0.0 | DPAPI Windows à l'étape 5 |
| Serilog.Extensions.Hosting | 8.0.0 | Intégration des logs à l'hôte |
| Serilog.Settings.Configuration | 8.0.4 | Configuration de la journalisation |
| Serilog.Sinks.File | 7.0.0 | Fichiers et rotation |
| Microsoft.NET.Test.Sdk | 18.11.0 | Exécution des futurs tests |
| xunit | 2.9.3 | Assertions et tests du moteur et de l'infrastructure |
| xunit.runner.visualstudio | 4.0.1 | Adaptateur de découverte et d'exécution |

Core utilise les bibliothèques standard .NET pour les flux, la mémoire, les canaux et le temps. Il ne référence ni WinUI, ni un wrapper FFmpeg. FFmpeg et MediaMTX restent des exécutables séparés. Les packages de journalisation et de sécurité sont préparés ; leur présence n'implémente pas encore la rotation ou le chiffrement.

Références des versions : [NuGet](https://www.nuget.org/packages), [Windows App SDK 2.5.1](https://github.com/microsoft/WindowsAppSDK/releases/tag/v2.5.1). La branche Windows App SDK 1.8 a terminé son service ; le squelette utilise la branche 2.0 courante, distribuée sous le numéro de package 2.5.1. [Cycle de service Microsoft](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels)

## Dépendances natives

Le manifeste fixe MediaMTX **1.21.2** et FFmpeg **n8.1.3-18-g46d899d7a0**, construit depuis la branche 8.1 par BtbN le 15 octobre 2026. La build FFmpeg choisie est partagée, LGPL, avec RTMP, RTMPS, FLV et les pipes. L'URL datée et l'empreinte SHA-256 remplacent un lien mobile `latest`.

```powershell
& .\tools\native\Get-NativeTools.ps1
```

Le script télécharge les deux archives, vérifie leurs empreintes avant extraction, rejette les chemins sortant du dossier prévu et vérifie les fichiers extraits. Les installations suivantes réutilisent les archives et ressources validées. Les DLL de FFmpeg restent à côté de `bin/ffmpeg.exe` ; copier seulement l'exécutable casserait cette build partagée.

Les chemins installés sont enregistrés dans `artifacts/native-tools.resolved.json`. Les archives et exécutables de développement restent dans `artifacts/`, hors du contrôle de version. Voir [les licences et le choix de distribution](DEPENDANCES.md).

## Configuration MediaMTX

`config/mediamtx.yml` active une seule entrée :

```text
rtmp://127.0.0.1:1935/live/stream
```

L'écoute se limite à IPv4 loopback. Seules la publication et la lecture du chemin `live/stream` sont autorisées pour les clients locaux. Les autres chemins et protocoles sont fermés. L'API de contrôle, les métriques et le profilage sont désactivés. Le refus d'un second publisher évite qu'une autre connexion OBS remplace silencieusement la première.

La valeur `stream` est un identifiant d'ingest local, pas une clé de plateforme. La clé Twitch, YouTube ou Kick n'entre pas dans cette configuration. Le trafic local RTMP n'est pas chiffré ; l'isolation repose sur l'adresse loopback et n'isole pas deux programmes du même poste. [Configuration MediaMTX](https://mediamtx.org/docs/references/configuration-file)

Validation sans ouvrir de serveur :

```powershell
$native = Get-Content .\artifacts\native-tools.resolved.json -Raw | ConvertFrom-Json
$mediamtxExe = ($native.Tools | Where-Object Name -eq 'MediaMTX').Executable
& $mediamtxExe ('--validate-conf=' + (Join-Path (Get-Location).Path 'config\mediamtx.yml'))
```

Le démarrage effectif de ce serveur appartient au relais supervisé de l'étape 1.

## Profil OBS

Créer un profil OBS nommé **Temhalo** à partir du profil de travail, puis préparer les réglages suivants. Les menus ci-dessous correspondent aux sections Flux, Sortie, Vidéo, Audio et Avancé ; leur traduction peut varier selon la version. La séparation serveur et clé est documentée dans le [guide OBS](https://obsproject.com/kb/OBS-Studio-Overview).

### Flux

| Champ | Valeur |
|---|---|
| Service | Personnalisé |
| Serveur | `rtmp://127.0.0.1:1935/live` |
| Clé de stream locale | `stream` |
| Authentification de l'ingest local | Désactivée |

OBS assemble le serveur et la clé pour produire `/live/stream`. Garder la clé de la plateforme pour la future configuration de Temhalo. À ce stade, préparer et enregistrer le profil suffit ; l'essai de diffusion attend le relais de l'étape 1.

### Sortie en mode avancé

| Paramètre | Valeur de départ |
|---|---|
| Encodeur vidéo | H.264 matériel disponible : NVIDIA NVENC, Intel Quick Sync ou AMD AMF |
| Alternative sans encodeur matériel | x264, preset adapté à la charge d'OBS |
| Contrôle du débit | CBR |
| Débit vidéo | 6 000 kbit/s pour le premier profil commun |
| Intervalle d'images clés | **1 seconde**, valeur explicite ; ne pas laisser 0/automatique |
| Profil H.264 | High |
| B-frames | **0** pour les premiers essais du MVP |
| Look-ahead / images B adaptatives | Désactivés si l'encodeur les propose |
| Rescale dans l'onglet de sortie | Désactivé ; régler la taille dans Vidéo |
| Audio | AAC, stéréo, 48 kHz, 160 kbit/s, une piste |
| Piste VOD séparée | Désactivée pour les premiers essais |

Le réglage à une seconde est imposé par Temhalo pour rapprocher les points de reprise. Le choix de zéro B-frame simplifie la première validation temporelle ; il ne remplace pas la gestion correcte de DTS et PTS. La désactivation concerne les options disponibles : leur nom varie selon l'encodeur.

### Vidéo et avancé

- Première sortie de référence : **1280 × 720 à 60 images/s**, SDR, NV12 8 bits, Rec.709 et plage limitée. Le canevas peut conserver la résolution de travail ; la résolution de sortie porte le réglage 720p.
- Délai de diffusion natif d'OBS : **désactivé**, pour éviter d'additionner deux buffers indépendants.
- Reconnexion automatique OBS : activée, intervalle initial 2 s et nombre d'essais élevé, par exemple 20.
- Débit dynamique OBS en cas de congestion : désactivé pour les premiers essais reproductibles.
- Restream multitrack, changement d'encodeur, de codec ou de résolution pendant le live : à laisser désactivés pendant la validation du MVP.

Ce profil H.264/AAC est un point de départ prudent, pas une configuration de qualité optimale pour tous les contenus. Les capacités propres à Twitch, YouTube et Kick seront revérifiées à la connexion de chaque destination. Le test de capacité du buffer à **8 Mbit/s pendant 300 s** se fera d'abord localement ; il n'impose pas ce débit à Twitch. Les recommandations OBS rappellent que le débit doit respecter la connexion et la plateforme. [Guide OBS](https://obsproject.com/kb/OBS-Studio-Overview)

## Budget mémoire et paramètres

`config/temhalo.defaults.json` propose 0 s au démarrage, un maximum de 300 s, les presets 0/15/30/60 s et un preset de protection de 60 s. Le budget du buffer est **536 870 912 octets, soit 512 MiB**, avec avertissement à 80 %, environ 410 MiB. Ces options seront raccordées au moteur lors de son implémentation.

À 8 Mbit/s au total, 300 secondes représentent 300 000 000 octets, soit environ 286 MiB. L'audio s'ajoute si les 8 Mbit/s ne décrivent que la vidéo. Les en-têtes, les structures du moteur et les réservations de mémoire nécessitent une marge. La RAM totale du produit inclura aussi le CLR, l'UI et les processus natifs ; le budget du buffer n'est pas une limite de RAM globale.

Les paramètres de pause 5 s / reprise 1 s sont des hypothèses de préparation pour l'étape 3, pas une garantie contre les déconnexions de plateforme. Voir [les limites et alternatives](ARCHITECTURE.md).

La destination reste vide et aucune clé n'est stockée. La rotation envisagée est quotidienne et par taille de 10 MiB, avec au plus 7 fichiers ; elle sera implémentée avec le masquage des secrets. Les fichiers de données iront sous `%LOCALAPPDATA%\Temhalo`, séparés des ressources installées.

## Procédure de contrôle

```powershell
dotnet --version
dotnet restore Temhalo.sln -p:Platform=x64 --locked-mode
dotnet build Temhalo.sln --no-restore -c Debug -p:Platform=x64
& .\tools\native\Get-NativeTools.ps1
& .\tools\Test-Stage0.ps1
```

Résultats attendus : les six projets compilent ; les deux dépendances natives passent la vérification d'empreinte ; MediaMTX valide son YAML ; FFmpeg annonce une licence LGPL v3 ou ultérieure et fournit les protocoles RTMP/RTMPS/file/pipe ainsi que le muxer et demuxer FLV. Le rapport est écrit dans `artifacts/validation/stage0.json`.

Le script de contrôle n'exécute pas de tests du moteur : ils seront ajoutés avec le code correspondant. Il ne lance pas le relais et ne demande pas de clé de stream. Attendre le retour utilisateur avant de raccorder la chaîne de l'étape 1.

### Résultats observés le 15 octobre 2026

- Restauration des six projets en mode verrouillé : réussie.
- Compilation Debug x64 des six projets : réussie, zéro avertissement et zéro erreur.
- Archives et fichiers natifs : empreintes vérifiées.
- MediaMTX 1.21.2 : configuration valide.
- FFmpeg : licence LGPL v3 ou ultérieure, protocoles requis et FLV disponibles.
- Aucun serveur d'ingest ni relais média lancé ; les essais de diffusion, de délai et de reprise restent à effectuer aux étapes correspondantes.

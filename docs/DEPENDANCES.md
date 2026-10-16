# Dépendances natives et licences Temhalo

L'option initiale d'embarquer MediaMTX et FFmpeg est remplacée pour la **préversion 0.6.0-preview.1** par une acquisition directe depuis leurs releases officielles, au premier démarrage du relais. Le ZIP portable inclut .NET/ASP.NET Core et WinUI, mais aucun binaire de ces deux outils média. Les URL et SHA-256 restent fixés ; les fichiers amont et leurs notices sont conservés sans modification dans le cache utilisateur. Les lancements suivants peuvent utiliser ce cache hors ligne. Ce choix est documenté dans [l'étape 6](ETAPE-6.md).

En développement, `tools/native/Get-NativeTools.ps1` acquiert ces fichiers dans `artifacts/`. Ce téléchargement préalable permet de compiler et vérifier le poste sans construire immédiatement un installeur. Les URL ne suivent pas `latest` et les empreintes sont conservées dans le manifeste versionné.

## MediaMTX

Version : **1.21.2**, archive officielle Windows amd64 du projet bluenviron/mediamtx. Licence : **MIT**. L'archive extraite conserve `LICENSE` et la configuration amont. Temhalo emploie sa propre configuration dans `config/mediamtx.yml`.

Conserver la notice de copyright et le texte de licence dans les redistributions. Sources : [release](https://github.com/bluenviron/mediamtx/releases/tag/v1.21.2), [licence](https://github.com/bluenviron/mediamtx/blob/v1.21.2/LICENSE).

## FFmpeg

Version vérifiée : **n8.1.3-18-g46d899d7a0-20261015**, branche 8.1, archive BtbN datée `autobuild-2026-10-15-13-03`. Le projet FFmpeg publie son code source et référence des fournisseurs de builds Windows, dont BtbN, depuis sa [page de téléchargement](https://ffmpeg.org/download.html).

La build choisie est **LGPL-3.0-or-later** : elle active `--enable-version3`, n'active ni `--enable-gpl` ni `--enable-nonfree`, et annonce la LGPL version 3 ou ultérieure avec `ffmpeg -L`. Elle utilise des DLL partagées. Garder le contenu installé nécessaire, en particulier les DLL adjacentes à `bin/ffmpeg.exe`, et sa notice `LICENSE.txt`.

Le passthrough H.264/AAC ne nécessite pas libx264 ni libx265 : les flux sont encodés dans OBS. Une future stratégie de réencodage devra réexaminer les options et licences de la build choisie.

À l'étape de distribution, préparer les notices, les textes de licence, les informations de build et l'accès aux sources correspondantes de FFmpeg et de ses bibliothèques, selon les exigences applicables. Le seul lien vers une archive binaire ne fournit pas les sources correspondantes. Conserver aussi les recettes de compilation et les références des dépendances. Le téléchargement préalable ou l'exécution par processus séparé ne supprime pas ces obligations. Voir les [considérations officielles FFmpeg](https://ffmpeg.org/legal.html), la [build verrouillée](https://github.com/BtbN/FFmpeg-Builds/releases/tag/autobuild-2026-10-15-13-03) et les [sources FFmpeg correspondantes](https://github.com/FFmpeg/FFmpeg/tree/46d899d7a0).

Le package collecte les licences, notices et métadonnées des composants NuGet et runtimes qu'il fournit, dans `Licenses/`. Il conserve aussi les licences des fontes. Les outils média sont acquis directement en amont ; une future variante qui les embarque demandera le dossier de sources correspondant décrit ci-dessus. Le code propre de Temhalo est sous licence MIT ; voir `LICENSE` à la racine. Les licences des dépendances restent distinctes.

## .NET et interface

Depuis le raccordement navigateur de l'étape 5 bis, `Temhalo.Infrastructure.Windows` référence `Microsoft.AspNetCore.App` pour Kestrel. Les exécutions depuis les sources nécessitent les runtimes .NET/ASP.NET Core 8 x64 ; le package portable de l'étape 6 les inclut, avec Windows App SDK. Les ressources navigateur et fontes sont intégrées ; aucun CDN ni package web payant.

Les packages NuGet sont déclarés et verrouillés, avec leurs propres licences et notices. Le développement utilise un outillage gratuit ; la compilation en ligne de commande n'exige pas l'achat de Visual Studio. Pour un IDE, employer une édition dont les conditions correspondent à l'usage prévu.

Les polices Space Grotesk, Inter et JetBrains Mono conservent leurs notices OFL dans `assets/brand/fonts/licenses/`. Les fontes complètes servent de sources ; les sous-ensembles WOFF2 sont destinés à la maquette hors ligne. La bibliothèque Core ne dépend d'aucun package de traitement vidéo payant.

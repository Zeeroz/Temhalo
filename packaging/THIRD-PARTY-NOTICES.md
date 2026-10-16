# Composants de Temhalo

Cette archive contient l'application Temhalo, le runtime .NET/ASP.NET Core et Windows App SDK, les bibliothèques NuGet et les fontes nécessaires. Leurs licences, notices et métadonnées NuGet sont conservées dans `Licenses/` et `Assets/Fonts/Licenses/`. `Licenses/nuget-inventory.json` répertorie les versions et leur provenance. Le code propre de Temhalo est sous licence MIT, reproduite dans `LICENSE`. Les composants tiers conservent leurs propres licences et leurs auteurs.

Les polices Inter, Space Grotesk et JetBrains Mono sont fournies sous OFL, avec leurs notices. Le logo et les icônes Temhalo sont les ressources originales approuvées du projet.

## Outils média acquis au premier démarrage du relais

Les binaires MediaMTX et FFmpeg ne sont pas redistribués dans cette archive. L'application les télécharge directement depuis les releases GitHub fixées dans `config/native-tools.lock.json`, vérifie les empreintes avant extraction, puis conserve leurs fichiers de licence et leurs DLL sans modification.

- MediaMTX : MIT, [projet et sources](https://github.com/bluenviron/mediamtx), version et archive exactes dans le manifeste.
- FFmpeg : build LGPL partagée, sans `--enable-gpl` ni `--enable-nonfree`, [licences officielles](https://ffmpeg.org/legal.html), [fournisseur BtbN](https://github.com/BtbN/FFmpeg-Builds). Version, SHA-256, sources FFmpeg et recettes de build exactes dans le manifeste. Le dossier téléchargé garde `LICENSE.txt` et la documentation amont.

Une future archive embarquant ces outils devra également préparer les sources correspondantes et les éléments de redistribution requis ; ce package choisit l'acquisition amont directe. Les détails de ce choix sont dans le guide de l'étape 6 et `docs/DEPENDANCES.md` des sources.

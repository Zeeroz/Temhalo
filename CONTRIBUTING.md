# Contribuer à Temhalo

Ouvrez une issue pour décrire un problème reproductible ou discuter d’une évolution, puis une pull request ciblée. N’incluez jamais de clé de stream, jeton OAuth, fichier DPAPI ni lien privé d’overlay/dock dans une issue ou une capture.

Utilisez Windows x64 et le SDK fixé par `global.json`. Le guide de compilation est dans [README.md](README.md). Restaurez en mode verrouillé, compilez en Release x64 et lancez les tests avant une pull request. Les dépendances NuGet sont centralisées dans `Directory.Packages.props` ; mettez à jour les fichiers `packages.lock.json` si vous changez une dépendance.

Conservez le passthrough du MVP, la synchronisation audio/vidéo et la séparation Core/Infrastructure/Presentation/App. Pour l’interface, référez-vous à [BRAND.md](BRAND.md) : navigation en haut, thème sombre et aucun bloc de marque supplémentaire.

Les essais de média utilisent une destination locale. Aucune CI ne doit publier de live ni accéder à un vrai compte. Les tests d’installation emploient une identité HKCU et un répertoire isolés sous `artifacts/validation/installer`.

Avant de partager des fichiers, exécutez `tools/Test-PublicSource.ps1` après les avoir ajoutés à l’index. Cet audit repère plusieurs formats de secrets connus ; une relecture reste nécessaire. Les anciennes archives et les configurations personnelles restent locales.

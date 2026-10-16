# Mises à jour de Temhalo

## Dans l’application

1. Ouvrez **Réglages → Au quotidien → Mises à jour**.
2. Cliquez sur **Rechercher une mise à jour**. Une recherche au démarrage peut être activée ou désactivée ici.
3. Si une version plus récente est disponible, consultez les notes puis cliquez sur **Télécharger la mise à jour**. Le téléchargement peut être annulé.
4. Une fois votre live terminé, arrêtez le relais. Le bouton **Installer et fermer Temhalo** devient disponible.
5. Suivez l’assistant d’installation. Cochez **Ouvrir Temhalo** sur sa dernière page pour relancer l’application.

Le téléchargement seul ne coupe jamais le relais. L’installation ne démarre pas si le relais fonctionne ou se vide. L’installeur attend la fermeture du processus Temhalo, puis met à jour l’emplacement déjà enregistré pour ce compte Windows, sans privilèges administrateur. Les réglages, comptes OAuth, clés DPAPI, journaux et outils téléchargés restent dans le profil utilisateur.

Les **versions de test** (`-preview.N`) sont proposées par défaut dans cette première version preview ; désactivez cette option pour ne recevoir que les versions stables. Une version stable est plus récente que toutes les previews portant les mêmes numéros majeur/mineur/correctif. Une version plus ancienne n’est jamais proposée.

Une copie portable ou lancée depuis les sources peut chercher et télécharger une version, mais ne remplace pas son dossier. Le téléchargement ouvre son dossier pour permettre une première installation manuelle. Les futures mises à jour intégrées s’appliquent à la copie installée.

Temhalo consulte les 100 releases récentes du dépôt officiel, sans authentification ni transmission de réglages. Une release doit être publiée, comporter un tag `vX.Y.Z` ou `vX.Y.Z-preview.N` et un installeur `Temhalo-X.Y.Z[-preview.N]-Setup-x64.exe`. Les brouillons et les fichiers sans empreinte SHA-256 GitHub sont ignorés. Les premières versions n’ont pas de certificat Authenticode : la vérification SHA-256 garantit la conformité à l’actif publié sur GitHub, et non une signature indépendante de GitHub.

## Publier une nouvelle version

Prérequis : droits d’écriture sur `Zeeroz/Temhalo`, GitHub Actions activé, Windows x64, SDK `global.json`, runtimes .NET/ASP.NET Core 8 pour les tests. La publication ne nécessite aucun secret OAuth. Le workflow emploie son `GITHUB_TOKEN` avec l’écriture de contenu uniquement dans le job de release.

1. Modifiez le code et incrémentez `<Version>` dans `Directory.Build.props`, par exemple `0.7.0-preview.2`.
2. Lancez les vérifications et produisez les livrables localement :

   ```powershell
   dotnet restore Temhalo.sln -p:Platform=x64 --locked-mode
   dotnet build Temhalo.sln -c Release -p:Platform=x64 --no-restore
   dotnet test Temhalo.sln -c Release -p:Platform=x64 --no-build --no-restore
   ./tools/Package-Windows.ps1
   ./tools/Build-Installer.ps1
   ./tools/Test-Installer.ps1
   ```

3. Relisez et committez aussi `packaging/windows/uninstall-inventory.json`. Il conserve l’union des fichiers livrés depuis les anciennes versions pour supprimer les fichiers devenus obsolètes lors des mises à jour. La CI refuse une publication si cet inventaire aurait besoin d’être modifié. Chaque version de livraison doit être unique ; les scripts refusent d’écraser une archive ou un installeur existant.
4. Ajoutez les changements à Git, exécutez `./tools/Test-PublicSource.ps1`, puis poussez votre commit sur `main`.
5. Créez et poussez le tag correspondant exactement à la version :

   ```powershell
   git tag -a v0.7.0-preview.2 -m "Temhalo 0.7.0-preview.2"
   git push origin v0.7.0-preview.2
   ```

6. Le workflow Windows restaure les packages verrouillés, compile, teste, construit le ZIP et l’installeur, puis teste une installation isolée avec mise à jour et désinstallation. Il dépose ensuite quatre actifs dans un brouillon de release : EXE, ZIP et leurs deux fichiers `.sha256`. Le brouillon devient public seulement après vérification des empreintes fournies par GitHub.
7. La nouvelle release apparaît dans Temhalo à la prochaine recherche. Une simple modification de `main` ou un tag sans release ne constitue pas une mise à jour distribuée.

Les rapports `.build.json`, journaux, captures de validation et profils privés ne sont pas téléversés. Les sources et les ressources de marque sont dans Git ; les binaires vont dans les releases. Les actions tierces du workflow sont fixées à des commits précis.

En cas d’échec de publication, consultez le job GitHub Actions. Une release déjà existante n’est jamais remplacée automatiquement. Si le job **build** du tag a réussi et que les quatre fichiers du brouillon sont présents, ouvrez **Actions → Windows → Run workflow** sur `main`, puis renseignez `recovery_run_id` (identifiant numérique du run du tag) et `recovery_tag` (par exemple `v0.7.0-preview.1`). Le workflow vérifie le commit du tag, le résultat du build et la provenance de l’archive, télécharge exactement ses fichiers et compare leurs tailles/SHA-256 au brouillon avant publication par identifiant numérique. Aucun fichier n’est remplacé ou reconstruit. Une release déjà publiée avec ces mêmes fichiers est seulement vérifiée.

Si le brouillon est incomplet ou les fichiers diffèrent, la reprise est refusée : inspectez-le manuellement ou publiez une nouvelle version. Ne republiez pas un binaire différent sous un tag déjà distribué. Les archives temporaires GitHub Actions sont conservées sept jours ; les actifs publiés dans Releases restent disponibles indépendamment.

## Procédure de test

- **Recherche** : sur la version courante, aucune version identique ou antérieure n’est proposée. En cas de réseau coupé ou de quota API atteint, le message permet de réessayer et le relais continue.
- **Canaux** : une release preview plus récente apparaît uniquement lorsque les versions de test sont autorisées ; une stable apparaît dans les deux canaux.
- **Intégrité** : les tests automatisés HTTP fictifs injectent un fichier modifié, tronqué et trop volumineux ; aucun installeur exploitable n’est conservé. Le fichier est vérifié une seconde fois avant lancement.
- **Annulation** : annulez un téléchargement, puis recommencez. Le fichier temporaire est supprimé et l’installeur ne démarre pas.
- **Protection du live** : lancez le relais local, téléchargez la mise à jour et vérifiez que le bouton d’installation reste désactivé. Arrêtez le relais avant de continuer.
- **Conservation** : sur une installation d’une version antérieure, enregistrez presets, destinations et comptes, installez la nouvelle version, relancez et vérifiez ces données. Ne les exportez pas en clair.
- **Installation** : `tools/Test-Installer.ps1` valide les fichiers par SHA-256, les raccourcis, l’attente du processus parent, le refus de rétrogradation et la conservation du profil à l’installation, à la réparation et à la désinstallation.

Les tests unitaires restent locaux et n’exécutent aucun binaire téléchargé. La validation de la chaîne publique complète nécessite au moins deux releases, une version installée plus ancienne, puis une mise à jour proposée par GitHub. Une première release ne suffit pas à revendiquer ce scénario comme testé.

Références : [API des releases GitHub](https://docs.github.com/en/rest/releases/releases), [actifs et empreintes SHA-256](https://docs.github.com/en/rest/releases/assets).

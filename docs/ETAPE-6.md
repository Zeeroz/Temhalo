# Temhalo — étape 6 : robustesse et package Windows

Version **0.6.0-preview.1**, Windows x64. Cette livraison prépare une application portable, avec les ressources Temhalo et des tests reproductibles. Le rapport final des contrôles est `docs/validation/etape-6.json` dans les sources. Les essais sur un poste Windows vierge et les plateformes publiques restent distincts des essais locaux.

## Lancer sans environnement de développement

1. Extraire **tout le dossier Temhalo** du ZIP `Temhalo-0.6.0-preview.1-win-x64.zip` dans un dossier de votre compte Windows, par exemple Documents. Conserver l'EXE, les DLL, `Assets/` et `config/` ensemble.
2. Lancer `Temhalo.App.exe`. Aucun SDK, Visual Studio, installation globale de .NET/ASP.NET Core/Windows App SDK ni droit administrateur n'est requis par ce mode.
3. Choisir d'abord **Fichier FLV local**, délai 0 s, puis **Démarrer**. Au premier démarrage du relais, l'application télécharge MediaMTX et FFmpeg depuis les releases GitHub fixées, vérifie SHA-256 avant extraction, puis revérifie les fichiers extraits. Le journal et le texte d'état indiquent la préparation. Internet est nécessaire pour cette acquisition initiale ; les démarrages suivants réutilisent le cache vérifié.
4. Publier depuis OBS : serveur `rtmp://127.0.0.1:1935/live`, clé locale `stream`, vidéo H.264, audio AAC, CBR, intervalle d'image clé **1 s**. Désactiver le délai OBS pour mesurer celui de Temhalo.
5. Le serveur du dock et de l'overlay démarre avec l'application sur `127.0.0.1:43120`, indépendamment du relais média. Les liens privés se copient depuis les vues validées. Le guide `GUIDE-OBS.md` du ZIP décrit leur insertion et le multistream.

Les fichiers écrits se trouvent sous `%LOCALAPPDATA%\Temhalo` : `settings.dpapi` (réglages et clés chiffrés pour votre compte), `logs/`, `tools/` (archives et outils vérifiés), `sessions/` (fichiers des sessions). Le dossier d'installation peut être déplacé entre deux lancements. Les anciens réglages DPAPI restent compatibles. Pour les tests isolés, `--data-directory "CHEMIN"` redirige ces données ; `--settings-directory` reste disponible pour les anciens scénarios de validation.

Ce ZIP est **non signé**, sans modification de pare-feu, service Windows, lancement avec Windows ni installation silencieuse dans le système. Il ne contient aucun réglage personnel ni clé de stream. Les contrôles d'intégrité détectent une modification ; ils ne remplacent pas une signature d'éditeur. Ne contournez pas un blocage de sécurité pour exécuter une archive dont la provenance n'est pas établie.

## Choix de distribution et licences

Le profil `Portable.pubxml` inclut .NET/ASP.NET Core **8.0.31** et Windows App SDK. Le format retenu est un dossier autonome dans un ZIP : les bibliothèques et ressources restent visibles et remplaçables. Ce choix s'appuie sur [le déploiement autonome Windows App SDK](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps). Ce n'est pas un installeur MSI/MSIX ni un EXE contenant à lui seul toutes les ressources.

Contrairement à l'option initialement envisagée d'embarquer les outils média, cette livraison les **acquiert directement auprès des projets amont au premier démarrage du relais**. Cela conserve leurs archives, notices et DLL intactes, allège le ZIP et évite d'annoncer un dossier de redistribution FFmpeg complet sans ses sources correspondantes. Le téléchargement est borné et annulable, l'extraction refuse les sorties de dossier et les liens symboliques ; une installation interrompue n'est pas publiée. Aucun script PowerShell ni changement de politique d'exécution n'est utilisé par l'application.

MediaMTX et FFmpeg gardent les versions et SHA-256 de `config/native-tools.lock.json`. La build FFmpeg choisie est partagée, LGPL, sans activation GPL/nonfree ; le relais utilise `-c copy`. Les sources et recettes exactes sont référencées dans le manifeste, les licences amont restent dans le cache. Voir [FFmpeg — licences et redistribution](https://ffmpeg.org/legal.html). Si une future édition embarque leurs binaires, elle devra préparer les sources correspondantes et les autres éléments requis avant redistribution.

Les licences et métadonnées des composants NuGet et runtimes inclus figurent dans `Licenses/`, les notices OFL dans `Assets/Fonts/Licenses/`. Le package contient `THIRD-PARTY-NOTICES.md`, un inventaire des versions et un manifeste SHA-256 par fichier. Logo, ICO multitailles, PNG d'icônes et écran de démarrage proviennent des ressources Temhalo approuvées. L'icône de l'EXE et de la fenêtre utilise `temhalo.ico` ; le PNG de splash est fourni comme ressource, sans ajouter un écran d'attente artificiel.

Le ciblage .NET 8 est conservé conformément au projet. Son support se termine le [10 novembre 2026](https://dotnet.microsoft.com/en-us/platform/support/policy) : une version maintenue au-delà devra être migrée et republiée. Les runtimes embarqués demandent une nouvelle publication pour recevoir leurs correctifs.

## Vérification rapide du ZIP

Depuis les sources, PowerShell :

```powershell
& .\tools\Test-WindowsPackage.ps1 -ArchivePath .\artifacts\packages\Temhalo-0.6.0-preview.1-win-x64.zip
```

Le script extrait dans un dossier de validation neuf, vérifie le SHA-256 du ZIP, chaque fichier annoncé, la présence des runtimes, l'ICO et ses tailles 16/32/48/256, les notices et l'absence de configuration privée. Il laisse le dossier extrait disponible pour le test de lancement. Il ne démarre ni ne tue une instance personnelle de Temhalo.

Sur une autre machine **Windows 10/11 x64**, de préférence sans SDK ni runtimes installés :

1. Extraire dans un chemin avec espaces, ouvrir depuis un raccourci dont le dossier de travail est différent ; vérifier ouverture, logo de fenêtre, quatre vues et absence d'erreur de runtime.
2. Démarrer en ligne avec un cache vide, puis arrêter et fermer. Relancer sans Internet avec le cache complet : l'UI, le fichier local, l'overlay et le dock doivent rester utilisables. Une publication vers une plateforme nécessite évidemment sa connexion réseau.
3. Vérifier une sauvegarde puis une réouverture des destinations et presets. Les clés restent masquées ; aucun log ne doit les contenir.
4. Laisser une autre application occuper 1935 ou le port HTTP : Temhalo doit signaler le conflit et laisser le processus concurrent intact. Changer seulement le port navigateur depuis Réglages si nécessaire.
5. Fermer pendant le téléchargement initial : aucun relais ne doit démarrer ensuite. Relancer : l'acquisition doit repartir proprement.

## Test manuel OBS/VLC et plateformes

Pour les deux récepteurs locaux et le dock/overlay, suivre `GUIDE-OBS.md` (ou `docs/ETAPE-5BIS-SERVICES.md`). Les ports de réception VLC sont 1936 et 1937, distincts de l'entrée OBS 1935.

1. Afficher un chronomètre et une source A/V avec repères synchrones dans OBS. Mesurer une référence à 0 s, puis les écarts supplémentaires pour 15, 30, 60 et 300 s. Les caches VLC et la latence plateforme s'ajoutent au délai local ; comparer l'écart par rapport à la référence. Tolérance visée : ±2 s après stabilisation.
2. Changer plusieurs fois les délais pendant **30 minutes** : augmenter, réduire, appliquer 0 et utiliser les raccourcis/dock. Observer les reprises sur image clé, l'audio, la cible et la mesure séparées. Le passage à 300 s demande le temps de constitution du buffer et les reprises entre paliers.
3. Arrêter puis relancer OBS ; le contenu restant doit se vider puis l'outil attendre la reprise. Arrêter un émetteur de la session de test : les autres sorties doivent rester actives. Arrêter un serveur RTMP de réception puis le relancer : reconnexion et retard supplémentaire doivent être visibles.
4. Sur un live de test privé/non répertorié lorsque la plateforme le permet, reproduire l'essai et une coupure Internet volontaire sur votre poste. Le test automatisé local retire ses récepteurs au lieu de désactiver la carte réseau. Une reconnexion RTMP n'assure pas que la plateforme conserve la même session publique.
5. Vérifier manuellement la synchronisation avec les repères A/V après chaque saut. Les tests automatiques de décodage et timestamps ne constituent pas une mesure perceptuelle de synchronisation.

Les pauses du passthrough peuvent provoquer buffering, fin de session ou reconnexion selon la plateforme ; elles ne peuvent pas être garanties transparentes. L'overlay OBS est capturé avant le buffer et est retardé avec l'image. Une sortie en reprise peut conserver un retard propre ; réappliquer la cible ou 0 demande son rattrapage.

## Campagne automatisée réelle

```powershell
dotnet restore Temhalo.sln -p:Platform=x64 --locked-mode
& .\tools\native\Get-NativeTools.ps1
& .\tools\Test-Stage6.ps1
# Tests unitaires/HTTP uniquement :
& .\tools\Test-Stage6.ps1 -SkipSoak
```

Fermer les relais de test avant cette commande ; **1935 et 1936 doivent être libres**. Aucun processus concurrent n'est arrêté. Par défaut, l'essai utilise une vraie horloge pendant au moins **1800 s**, quatre destinations RTMP locales, une mire H.264/AAC proche de 8 Mbit/s et un délai initial de 300 s, puis des changements répétés. Seule la création de la mire est encodée ; la chaîne testée reste en passthrough.

Les scénarios injectent le crash d'un émetteur, du lecteur et de l'ingest MediaMTX, une absence d'OBS et une indisponibilité des quatre destinations. Les références partagées doivent être libérées après arrêt. Les quatre captures finales sont décodées et contrôlées pour les en-têtes, la première IDR et les DTS monotones par piste. Les tests unitaires couvrent aussi limites RAM, réécriture temporelle, capacités HTTP, chiffrement, conflits et reprise du téléchargement.

Les rapports sont sous `artifacts/validation/stage5-…/` : `native.log`, `performance.csv`, captures et `robustness-result.json`. Le nom de dossier historique ne change pas le champ `Stage: 6` du rapport. Le CPU est normalisé sur tous les processeurs logiques ; son périmètre est l'hôte du moteur, l'ingest MediaMTX, le lecteur et les quatre émetteurs. L'émetteur de mire, le récepteur local et les captures sont exclus. L'hôte de diagnostic n'est pas une mesure de l'UI WinUI ; les essais visuels du package sont consignés séparément. Le working set inclut le runtime et les processus natifs ; le budget logique du buffer compte les charges utiles et la réserve de file.

`-DurationSeconds 600` fournit un essai plus court, **pas une validation de 30 minutes**. Ctrl+C annule et ferme uniquement les processus possédés par le test. Une relance écrit dans un nouveau dossier et conserve les rapports précédents.

## Réparer et reconstruire

Si une empreinte du cache ne correspond plus, aucun outil altéré n'est lancé. Fermer Temhalo ; renommer `%LOCALAPPDATA%\Temhalo\tools` en `tools-a-conserver`, puis relancer en ligne pour acquérir un cache neuf. Ne pas modifier `settings.dpapi` : les réglages et les clés n'ont pas besoin d'être supprimés. Une première acquisition hors ligne échoue explicitement ; réessayer **Démarrer** une fois Internet rétabli.

```powershell
& .\tools\Package-Windows.ps1
```

Le script restaure en mode verrouillé, publie Release x64 avec le profil portable, collecte ressources/notices, refuse les fichiers privés, produit un manifeste et un ZIP avec `.sha256`. Il refuse de remplacer une archive de même version. Les téléchargements natifs et les sorties de session du poste de développement sont exclus. Le ZIP de sources de l'étape 6 contient les scripts, projets, tests, guides et ressources permettant de reproduire cette publication.

La validation Windows 10 et Windows 11 sur machines vierges, les politiques de timeout Twitch/YouTube/Kick, les mesures perceptuelles A/V et la signature de distribution restent à consigner avant une diffusion publique maintenue. Aucun résultat local ne les remplace.

## Résultats de cette livraison

- Compilation Release x64 et restauration verrouillée réussies, sans erreur ni avertissement ; **81 tests réussis** : 40 Core, 34 infrastructure et 7 présentation.
- Essai réel de **1800 secondes**, quatre sorties locales, mire **8,65 Mbit/s**, délai initial de 300 s, pannes et reprises décrites ci-dessus. Les quatre captures finales sont décodables avec IDR/en-têtes et DTS monotones ; buffer final à zéro. Aucun nouveau démarrage d'émetteur après les dix premières minutes, où se concentrent les pannes provoquées.
- CPU moyen en émission **0,31 %**, P95 **1,36 %**, maximum d'un échantillon **5,20 %**. L'objectif strict « toujours sous 5 % » n'est donc pas entièrement satisfait ; la moyenne et le P95 sont sous le seuil. Le périmètre inclut le moteur de diagnostic et ses processus média, sans l'UI.
- Buffer logique à cinq minutes : **314,45 Mio** pour cette mire légèrement au-dessus de 8 Mbit/s. Working set maximal cumulé hôte et processus média : **721,43 Mio**. Le budget logique ne représente pas toute la mémoire des runtimes/processus ; le GC peut conserver des pages après une réduction, puis les restituer.
- ZIP de **916 fichiers contrôlés**, ressources XAML PRI/XBF incluses, ICO multitailles et notices présents. Ouverture vérifiée hors du projet avec un chemin contenant des espaces et un autre dossier de travail ; une première ouverture a également été effectuée avec les racines .NET globales dirigées vers un dossier inexistant. Les modules coreclr/hostfxr/WinUI sont chargés depuis le dossier publié.
- Depuis l'EXE du ZIP final : acquisition depuis le cache vérifié, publication synthétique vers fichier FLV, attente après fin de source, arrêt avec budget à zéro et décodage du fichier réussis. Réglages et journaux ont été isolés des données personnelles ; aucune scène OBS personnelle n'a été modifiée.

Rapports détaillés dans les sources : `docs/validation/etape-6.json`, `etape-6-robustesse.json` et `etape-6-performance.csv`. La courbe complète se trouve dans `design/native-etape-6/Temhalo-performance-30min.png` ; elle se régénère avec `tools/Plot-Robustness.py` et matplotlib (outil de développement facultatif).

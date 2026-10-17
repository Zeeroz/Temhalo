# Installer Temhalo sous Windows

Version 0.7.0-preview.6 · Windows x64 · 17 octobre 2026

## Installation

1. Terminer votre diffusion et fermer toute instance de Temhalo.
2. Lancer `Temhalo-0.7.0-preview.6-Setup-x64.exe`.
3. Choisir le raccourci Bureau et, si proposé, **Plugin Stream Deck (facultatif)**, puis **Installer**.
4. Ouvrir Temhalo depuis le Bureau ou **Démarrer → Temhalo → Temhalo**.

L'installation se fait pour le compte courant dans `%LOCALAPPDATA%\Programs\Temhalo`,
sans élévation administrateur. Le menu Démarrer contient aussi **Désinstaller Temhalo**.
Le raccourci du Bureau est proposé et coché par défaut. Le logo et les icônes
proviennent de l'identité Temhalo validée.

## Plugin Stream Deck

Si l'association Windows des fichiers `.streamDeckPlugin` est détectée, l'option
du plugin est cochée par défaut. Vous pouvez la décocher ; le choix est conservé
pour les prochaines mises à jour. L'installeur ouvre le paquet livré dans
Stream Deck à la fin de la copie. Terminez les éventuelles confirmations dans
Stream Deck. Les profils, touches et valeurs existantes restent gérés par Elgato.

Sans Stream Deck, l'option est désactivée et Temhalo s'installe normalement.
Après avoir installé Stream Deck 6.6 ou ultérieur, ouvrez **Réglages** dans Temhalo
et utilisez **Installer / mettre à jour le plugin**. Vous pouvez aussi ouvrir
`plugins/streamdeck/Temhalo.streamDeckPlugin` dans le dossier de Temhalo.
Ce fichier reste fourni même lorsque l'option est décochée. Si son ouverture
échoue, l'installeur affiche son emplacement et termine l'installation de Temhalo.

La touche **Démarrer / arrêter le relais** contrôle Temhalo selon son état réel.
OBS se lance séparément ; l'arrêt du relais termine son émission vers les plateformes.

L'application inclut .NET 8, ASP.NET Core 8 et Windows App SDK : aucun SDK ou
Visual Studio n'est nécessaire. Windows 10 version 1809 minimum est déclaré ;
les essais sur une installation vierge de Windows 10 et Windows 11 restent à faire.
Cette préversion et son installeur ne sont pas signés avec un certificat éditeur.

MediaMTX et FFmpeg sont téléchargés depuis les versions officielles verrouillées
au premier démarrage du relais, puis vérifiés par SHA-256. Internet est nécessaire
à cette première acquisition ; le cache vérifié est réutilisable ensuite.
Le fichier `GUIDE-RAPIDE.md` explique la configuration des plateformes et d'OBS.

## Mise à jour et réparation

Fermer Temhalo après la diffusion, puis relancer le nouvel installeur. Il reprend
le dossier déjà enregistré et remplace les fichiers de l'application. Relancer
la même version répare également les fichiers installés manquants.

Les fichiers connus des anciennes versions et devenus obsolètes sont retirés
grâce à un inventaire compilé dans l'installeur. Conserver cet inventaire dans
les sources lors des futures livraisons. Une version numérique antérieure
à la version installée est refusée.

Si un EXE/DLL est utilisé ou un fichier n'est pas modifiable, l'installeur demande
de fermer Temhalo et de réessayer. Il ne termine aucun processus et ne planifie
pas de redémarrage de Windows. Les fichiers connus sont vérifiés avant copie ;
une panne de courant ou une saturation du disque en cours de copie peut toutefois
nécessiter de relancer l'installeur. Ce mécanisme n'est pas transactionnel.

## Désinstallation

Fermer Temhalo, puis utiliser **Paramètres Windows → Applications → Temhalo →
Désinstaller**, ou l'entrée **Désinstaller Temhalo** du menu Démarrer.

La désinstallation retire les fichiers déclarés de l'application, les raccourcis
du Bureau et du menu Démarrer, l'EXE de désinstallation et l'entrée HKCU dans les
applications installées. Elle retire ensuite les dossiers devenus vides.
Elle n'efface pas récursivement le dossier d'installation : les fichiers que
vous y avez ajoutés sont conservés.

**Les données du profil restent dans `%LOCALAPPDATA%\Temhalo`** : configuration
DPAPI, clés de stream chiffrées, journaux et outils téléchargés. Les éventuels anciens
fichiers de sessions locales sont également conservés. Une réinstallation avec le même compte Windows
peut les réutiliser. Leur suppression définitive reste une action manuelle,
après sauvegarde des enregistrements souhaités.

Le plugin déjà installé dans Stream Deck et ses profils sont conservés.
Pour retirer ce plugin, utilisez la gestion des plugins de Stream Deck.

Les réglages DPAPI sont liés au compte Windows ; conserver leurs fichiers
ne permet pas de les déchiffrer depuis un autre compte.

## Contrôle conseillé

1. Installer en gardant le raccourci du Bureau coché.
2. Vérifier les deux raccourcis, leur icône et l'entrée dans les applications Windows.
3. Lancer depuis le Bureau ; vérifier les trois onglets et les réglages conservés.
4. Fermer Temhalo, relancer l'installeur et vérifier que les réglages sont identiques.
5. Temhalo ouvert, tenter une mise à jour : un message doit demander de le fermer.
   Choisir Annuler pour laisser votre session en cours ; ne pas le tester en live public.
6. Fermer Temhalo, désinstaller et vérifier le retrait des raccourcis et de l'entrée.
7. Vérifier que les données du profil sont toujours présentes, puis réinstaller.

Le test automatisé crée un dossier, des raccourcis de test et une clé HKCU
distincts ; il ne modifie ni vos raccourcis personnels ni vos réglages Temhalo.
Il contrôle les fichiers installés par comparaison SHA-256 avec le ZIP validé.

## Reconstruire

Depuis la racine du projet, sous PowerShell 7 :

```powershell
& .\tools\Build-Installer.ps1
& .\tools\Test-Installer.ps1
```

Le ZIP `artifacts/packages/Temhalo-0.7.0-preview.8-win-x64.zip` et sa somme
`.sha256` doivent être présents. Pour le reconstruire d'abord, utiliser
`tools/Package-Windows.ps1` après avoir conservé toute archive déjà livrée.
L'installeur est écrit sous `artifacts/packages/`, avec sa somme SHA-256 et
son rapport `.build.json`. Un fichier existant n'est jamais écrasé implicitement :
utiliser `-OutputPath` pour choisir un autre nom.

Le compilateur NSIS est acquis en ZIP officiel dans `artifacts/toolchains/`,
vérifié avec l'empreinte verrouillée dans `packaging/windows/nsis.lock.json`,
puis extrait dans un dossier de construction unique. Il n'est pas installé
dans Windows. La reconstruction fonctionne hors ligne si ce ZIP et le package
applicatif sont déjà en cache. La somme du ZIP NSIS provient du
[jeu de données officiel des versions](https://nsis-dev.github.io/release-data/versions.json).
NSIS autorise l'usage commercial selon ses [licences](https://nsis.sourceforge.io/Docs/AppendixI.html),
dont les textes complets sont fournis ; aucune dépendance payante n'est ajoutée.

`packaging/windows/uninstall-inventory.json` est l'union des fichiers livrés.
La construction de production l'enrichit ; il ne faut pas retirer les anciens
chemins sans prévoir leur migration. Le mode de validation utilise la même
source NSIS, avec uniquement les destinations des raccourcis et l'identifiant
de registre redirigés, un profil de données isolé et un ancien fichier factice
pour vérifier le retrait des fichiers obsolètes. La version numérique peut être fournie
avec `-NumericVersion` ; elle doit croître lors des futures préversions.

Pour une installation automatisée, l'option `/S` lance le mode silencieux.
Ce mode n'ouvre jamais Stream Deck et ne modifie pas le choix interactif du plugin.
Un dossier dédié peut être choisi avec `/D=C:\chemin\Temhalo` en dernier argument,
sans guillemets après `/D=` même si le chemin contient des espaces. Une mise à jour
reprend toujours le dossier déjà enregistré. Les chemins longs sont pris en charge
si Windows les autorise déjà ; sinon, un chemin dépassant la limite est refusé
avant copie. L'installeur ne modifie pas cette stratégie Windows.

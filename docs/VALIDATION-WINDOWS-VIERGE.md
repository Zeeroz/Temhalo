# Validation de Temhalo dans un Windows vierge

Le scénario a été **exécuté avec succès le 16 octobre 2026** dans une session
neuve du Bac à sable Windows 11, build 26100, distincte du profil de développement.
Le [rapport de cette session](validation/rapport-windows-vierge-2026-10-16.md)
détaille les preuves et les limites. Les résultats d'une nouvelle session restent
en attente jusqu'à sa propre fin ; la construction du kit ne vaut pas validation.

## Préparation et lancement

L'utilisateur a choisi le Bac à sable sur ce PC, avec redémarrage manuel.
La fonctionnalité Windows a été activée avec `-NoRestart`, puis le redémarrage
manuel a été effectué. Pour une nouvelle session, ouvrir le fichier `Temhalo-Windows-Vierge.wsb`
du dossier indiqué par `artifacts/validation/clean-windows/latest-lab.json`.

Le dossier `Input` contient l'installeur validé, une révision de validation
de l'installeur, OBS officiel, une mire vidéo avec son et chrono, et l'agent
autonome. Aucun SDK ou runtime global n'est installé pour Temhalo. L'agent
possède son propre runtime et ne modifie pas les paramètres de sécurité
ni la stratégie d'exécution PowerShell.

L'invité voit les entrées en lecture seule et peut écrire uniquement dans le
dossier partagé `Results`. Les sources du projet, les profils OBS et les données
Temhalo du poste hôte ne sont pas partagés. Le réseau est activé pour l'acquisition
initiale des outils média. Microphone, caméra et presse-papiers de l'hôte ne sont
pas redirigés. Le kit réserve 8 Gio de mémoire à l'invité.

**Conserver la même session Sandbox pendant tout le scénario.** La fermeture
de cette fenêtre détruit le profil invité et ses clés DPAPI. Les rapports et
la capture synthétique déjà exportés dans `Results` restent sur l'hôte.

## Scénario

1. L'agent refuse une exécution sur le compte de développement, vérifie l'absence
   de Temhalo et de ses données, puis installe le véritable EXE.
2. Il vérifie les fichiers par SHA-256, les raccourcis et l'entrée de désinstallation.
3. Il enregistre une configuration de test DPAPI : mémoire 384 MiB, presets
   0/12/24/48 s, style Fil, raccourcis désactivés, clé témoin sans compte de plateforme.
4. Il lance Temhalo et vérifie que .NET et WinUI proviennent de son dossier installé.
   Cliquer **Démarrer le relais** dans Temhalo : l'application acquiert les outils
   média à froid. L'agent vérifie leur lancement **avant** d'installer OBS, afin
   que les dépendances d'OBS ne masquent pas un défaut de Temhalo ou des outils.
5. Il installe OBS 32.2.2 depuis son installeur officiel verrouillé par SHA-256,
   puis le lance dans le Windows invité.
6. Les manipulations OBS et Temhalo ci-dessous sont effectuées dans les interfaces.
   L'agent attend le vrai état « OBS connecté », puis commande 15 s et 0 s via
   l'API locale existante du dock.
7. Après l'arrêt du stream, du relais et de la fenêtre Temhalo, il inspecte le
   fichier relayé : taille non nulle, présence de H.264 et AAC, décodage complet.
   Il exporte cette
   capture synthétique et le rapport ffprobe.
8. Il installe une révision numérique supérieure, compare les octets DPAPI et
   la configuration déchiffrée en mémoire, puis relance Temhalo.
9. Vérifier les réglages dans l'interface mise à jour, puis fermer normalement
   Temhalo. L'agent désinstalle et vérifie fichiers, raccourcis, registre et
   conservation des réglages chiffrés.

La mise à jour emploie les **mêmes binaires applicatifs** validés avec une révision
de l'installeur passant de `0.6.0.1` à `0.6.0.2`. C'est un test du mécanisme
d'installation et de conservation des données ; il ne valide pas une migration
de schéma vers une future version du moteur.

## Manipulations OBS

Dans Temhalo, conserver la sortie **Fichier local**, puis démarrer le relais.
La première acquisition de MediaMTX/FFmpeg peut prendre quelques instants.

Dans OBS, fermer l'assistant automatique initial et configurer :

- Service personnalisé ; serveur `rtmp://127.0.0.1:1935/live` ; clé locale `stream`.
- Encodeur logiciel H.264/x264 ; CBR 6 000 kbit/s ; keyframe 1 seconde
  (valeur effectivement utilisée lors de la session validée).
- Audio AAC ; 48 kHz ; piste 1.
- Canevas et sortie 1280 × 720 ; 30 images/s.
- Une source **Source média** lisant
  `C:\TemhaloValidation\Input\test-pattern.mp4`, avec lecture en boucle.
  La source fournit la mire, le chrono et le son ; aucune capture du poste hôte.

Démarrer la diffusion OBS. L'agent effectuera les transitions 0 → 15 → 0 s.
À l'étape `close-before-upgrade` du rapport, arrêter la diffusion dans OBS,
arrêter le relais puis fermer normalement Temhalo. À l'étape `verify-updated-ui`,
contrôler la configuration et fermer à nouveau Temhalo.

Le presse-papiers de l'hôte reste désactivé. La saisie au clavier dans l'invité
fonctionne ; si un outil de contrôle colle du texte via le presse-papiers hôte,
utiliser à la place le champ URL des **Propriétés → Document Web** du fichier
`OBS-local.url`, puis copier/coller cette adresse uniquement dans l'invité.
Ce raccourci contient une adresse locale de test et ne doit pas être ouvert
comme une page Internet. Le choix du média peut se faire dans le sélecteur de
fichiers en parcourant `C:\TemhaloValidation\Input`.

## Preuves et limites

`Results/latest-run.json` indique le sous-dossier de la session ; son fichier
`result.json` indique l'étape courante, les actions attendues et les
contrôles réalisés. `passed` ne doit apparaître qu'après la désinstallation.
`baseline.json` consigne le Windows effectivement testé et le niveau du compte ;
le compte Sandbox peut être administrateur. Les rapports n'exportent ni clé
de stream, ni liens privés, ni document déchiffré.

Cette session valide un Windows isolé de la même famille/build que l'hôte.
Elle ne couvre pas à elle seule Windows 10, un compte standard sur un autre PC,
les plateformes publiques, la mesure sur un second appareil ou la synchronisation
A/V perceptive. La mesure automatique de délai est celle du moteur ; le fichier
exporté permet un contrôle visuel et audio supplémentaire.

Sources : [configuration Sandbox](https://learn.microsoft.com/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-configure-using-wsb-file),
[installation Sandbox](https://learn.microsoft.com/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-install),
[OBS 32.2.2](https://github.com/obsproject/obs-studio/releases/tag/32.2.2),
[paramètres de lancement OBS](https://obsproject.com/kb/launch-parameters).

Pour reconstruire le kit depuis la racine avec PowerShell 7 :

```powershell
& .\tools\New-CleanWindowsValidation.ps1
```

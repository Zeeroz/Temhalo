# Validation Windows vierge — 16 octobre 2026

**Résultat : réussi.** Installation, publication OBS locale, mise à jour avec
conservation des réglages et désinstallation ont été exécutées dans le même
Windows invité. Aucun correctif applicatif n'a été nécessaire.

## Environnement et périmètre

- Windows Sandbox neuf : Windows 11 build **26100** ; compte
  `WDAGUtilityAccount`, administrateur. Aucun SDK .NET, profil Temhalo ou OBS
  préexistant. .NET et WinUI ont été chargés depuis le dossier installé de Temhalo.
- Installeur Temhalo `0.6.0-preview.1`, révision Windows **0.6.0.1** ; mise à jour
  de test **0.6.0.2**, avec les **mêmes 920 fichiers applicatifs**.
- OBS officiel **32.2.2**, installé après le premier lancement de Temhalo et
  l'acquisition à froid de MediaMTX/FFmpeg par l'application.
- Publication RTMP exclusivement locale ; mire synthétique en boucle avec
  chrono et signal sonore. x264, CBR **6 000 kbit/s**, keyframe **1 s**,
  **1280 × 720 à 30 i/s**, AAC stéréo **48 kHz**. Sortie Temhalo en fichier FLV.
- Entrées partagées en lecture seule, résultats séparés en écriture ; aucun
  profil hôte, clé de plateforme réelle, microphone ou caméra de l'hôte partagé.

## Contrôles effectués

| Contrôle | Résultat observé |
| --- | --- |
| Installation réelle dans le profil vierge | Réussie ; 920 fichiers conformes par SHA-256 |
| Raccourcis et enregistrement Windows | Bureau, menu Démarrer, icônes et entrée de désinstallation conformes |
| Premier lancement avant installation OBS | .NET et WinUI locaux chargés ; acquisition et exécution des outils média réussies |
| OBS → MediaMTX → FFmpeg → moteur → FFmpeg → fichier | Flux reçu ; débit affiché autour de 6,1 Mbit/s |
| Transition 0 → 15 s | Mesure du moteur dans la plage 13–17 s, sans arrêter OBS |
| Retour 15 → 0 s | Mesure du moteur dans la plage 0–2 s |
| Capture relayée | H.264 High avec B-frames + AAC LC ; décodage complet sans erreur FFmpeg |
| Arrêt normal | Publication OBS, relais puis fenêtre Temhalo arrêtés normalement |
| Mise à jour 0.6.0.1 → 0.6.0.2 | 920 fichiers conformes ; octets DPAPI inchangés ; réglages et clé témoin déchiffrables |
| Relancement après mise à jour | Réussi ; mémoire 384 MiB, presets 0/12/24/48 s, raccourcis désactivés, protection 48 s et overlay Fil du temps vérifiés dans l'interface |
| Désinstallation réelle | Fichiers applicatifs, raccourcis et entrée Windows retirés |
| Données préservées | Réglages DPAPI et clé témoin conservés ; fichier personnel témoin laissé intact par le désinstalleur |

L'agent a terminé avec `Status=passed` à **14:28:50 +02:00** : **41 contrôles
fonctionnels et d'entrées**, plus **920 vérifications de fichiers à chacune des
deux installations**, soit 1 881 assertions réussies, zéro échec. Les captures
de l'interface complètent les comparaisons automatiques des réglages.

Le fichier personnel témoin a ensuite été supprimé par le test qui l'avait
créé, puis le dossier d'installation devenu vide a été retiré. OBS et le Bac à
sable ont été fermés après export des preuves. La conservation des données par
le désinstalleur a été vérifiée **avant** la destruction de cette session jetable.

## Fichiers et traçabilité

- [Résumé machine et empreintes](windows-vierge.json).
- [Résultat brut de l'agent](../../artifacts/validation/clean-windows/lab-20261016-134734-5cdbf5bb/Results/run-20261016-120934-f45ed9bd/result.json).
- [Rapport ffprobe](../../artifacts/validation/clean-windows/lab-20261016-134734-5cdbf5bb/Results/run-20261016-120934-f45ed9bd/media-probe.json).
- [Capture FLV de test — 33 639 158 octets](../../artifacts/validation/clean-windows/lab-20261016-134734-5cdbf5bb/Results/run-20261016-120934-f45ed9bd/obs-relayed.flv).
- [Captures de preuve](../../artifacts/validation/clean-windows/lab-20261016-134734-5cdbf5bb/Results/run-20261016-120934-f45ed9bd/screenshots).
- Archive transportable : `Temhalo-Validation-Windows-Vierge-2026-10-16.zip`
  à la racine du projet, avec rapports, vidéo synthétique et captures.

L'installeur validé est resté inchangé, SHA-256 :
`68639487e32bd762e872d2bec0962f2439b662eae5106842732df55718962270`.
Les preuves n'exportent ni le document de réglages, ni la clé témoin, ni les
liens privés du dock/overlay.

## Limites

Cette session couvre Windows 11 build 26100 dans Sandbox, avec un compte
administrateur. Elle ne valide pas Windows 10, d'autres builds, un compte
standard sur un autre PC, une plateforme publique ou un live de 30 minutes.
Les mesures de délai sont celles du moteur ; la mesure sur un second appareil
et la synchronisation A/V perceptive restent à effectuer.

La mise à jour utilise les mêmes binaires applicatifs et une révision supérieure
de l'installeur. Elle valide la conservation des données et le remplacement des
fichiers, sans constituer un test de migration vers un futur schéma de réglages.

La procédure pour reproduire l'essai est dans
[VALIDATION-WINDOWS-VIERGE.md](../VALIDATION-WINDOWS-VIERGE.md).

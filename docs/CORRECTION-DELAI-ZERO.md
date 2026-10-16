# Temhalo — retard résiduel à une cible de zéro

Diagnostic et correction du 16 octobre 2026. Mise à jour : cause reproduite et
correction vérifiée vers Twitch après le premier correctif de statut.

## Cause identifiée : ancien préréglage d'ingest Twitch

Temhalo imposait `rtmp://live-cdg.twitch.tv/app` aux comptes Twitch et le proposait
en clé manuelle. Sur ce poste, sa résolution passait par les alias
`cdg → jfk50 → ord03 → use20`. La connexion TCP prenait environ 95–117 ms, contre
15–16 ms pour l'ingest automatique officiel. Il ne s'agit pas du délai vidéo total.

Le test comparatif a conservé le même ingest OBS, le même moteur, les mêmes options
FFmpeg et la même clé de test. **Seul le nom de serveur a changé.** Les émissions
utilisaient le mode officiel Twitch `?bandwidthtest=true`, sans publication d'un
nouveau live ni notification. Les journaux contiennent seulement des compteurs et
des temps, sans clés, jetons ou contenu vidéo. La destination temporaire est
protégée par DPAPI dans le dossier de diagnostic ignoré, jamais dans les sources
ni dans un fichier distribué.

| Mesure, secondes 10 à 45 | Ancienne adresse | Ingest automatique |
| --- | --- | --- |
| Âge des paquets échantillonnés | 1 976 à 4 540 ms | 0 à 1 ms |
| Tags en attente à la fin | 491 | 0 |
| Temps cumulé dans les écritures vers FFmpeg | 42 066 ms | 853 ms |

Les piles .NET montraient l'attente dans l'écriture du pipe de l'émetteur. Le seul
changement de serveur supprime cette attente continue : l'accumulation provenait
du chemin d'émission imposé par l'ancien préréglage Twitch. Ce diagnostic n'attribue
pas une cause interne précise aux routeurs ou serveurs de Twitch.

La valeur commune au connecteur de compte et au formulaire manuel est désormais
**`rtmp://ingest.global-contribute.live-video.net/app`**, recommandée par Twitch.
Le chargement remplace l'ancien préréglage dans les destinations de type Twitch
existantes, sans changer leurs clés, comptes ou préférences. Les autres serveurs
Twitch et les destinations de type serveur personnalisé sont préservés. La lecture
ne réécrit pas les réglages ; le prochain enregistrement normal persistera la valeur.

Sources : [ingests officiels Twitch](https://help.twitch.tv/s/twitch-ingest-recommendation?language=en_US),
[mode de test Twitch Inspector](https://inspector.twitch.tv/).

Un second essai vers Twitch a exécuté **0 → 15 → 0 s** pendant 90 secondes, avec le
même émetteur et sans erreur : à 15 s, les mesures stables étaient de **15 000 à
15 015 ms** ; après retour à zéro, **0 à 1 ms**, sans tags accumulés à la fin.
Ces mesures concernent le moteur, pas le lecteur Twitch, et ne garantissent pas
une latence nulle sur toutes les connexions Internet.

Résultats : `artifacts/validation/latency-root-cause/network-comparison.json`,
`baseline-network.jsonl`, `global-network.jsonl`, `global-dynamic.jsonl` et rapports
TRX du même dossier. Compilation Release x64 sans avertissement ni erreur ;
**104 tests réussis** (42 Core, 53 Infrastructure, 9 Presentation), dont la migration
des anciens réglages chiffrés et le choix de l'ingest automatique.

## Constat

La capture montre une mesure locale de 00:02 pour une cible de 00:00, alors que
l'interface annonce « Délai atteint ». Ce compteur est l'âge du dernier paquet
émis par le moteur C# depuis sa réception, après l'écriture dans la sortie. Il ne
mesure pas la latence de lecture de Twitch, YouTube ou VLC, ni les buffers situés
avant/après le moteur.

Une lecture seule de la télémétrie de la session a ensuite mesuré 5 229 à 5 627 ms
à cible zéro, en phase Stable. En parallèle, une seconde lecture du même ingest
OBS, avec le moteur puis un FFmpeg émetteur vers un fichier local, est restée à
0 ms sur les échantillons après démarrage pendant un essai de 20 secondes.
Cette valeur est une mesure arrondie à la milliseconde, pas une latence totale nulle.

La comparaison situe l'accumulation sur le chemin d'émission de cette session.
Elle ne départage pas à elle seule le réseau, le serveur distant et les attentes
internes à FFmpeg. Aucune émission réelle n'a été arrêtée ou relancée pour ce diagnostic.

## Premier correctif : affichage et purge explicite

- Le moteur publie `OutputBacklog` et retire l'état « cible atteinte » lorsque
  l'âge mesuré dépasse la consigne d'au moins 1 000 ms. La valeur réelle reste visible.
- Une nouvelle commande explicite **0 s** cherche la dernière image clé disponible
  lorsqu'un retard résiduel est mesuré, même si la consigne était déjà zéro et
  qu'aucune erreur de connexion n'a été levée. Les configurations H.264/AAC sont
  renvoyées et la continuité des timestamps est conservée. Les données déjà
  confiées à FFmpeg ou au lecteur distant ne peuvent pas être purgées par ce saut.
- Aucun saut automatique n'est ajouté : une sortie momentanément lente conserve
  les paquets tant que les limites existantes le permettent. Un zéro sain reste
  sans saut supplémentaire.
- Le dashboard et le dock expliquent le retard de sortie ; l'overlay ne le
  présente plus comme un état « En direct ».
- L'émetteur réseau utilise `-tcp_nodelay 1`, compatible avec le FFmpeg embarqué,
  pour éviter le regroupement Nagle des petites écritures RTMP. Le passthrough
  `-c copy` reste inchangé. Le gain sur Internet reste à mesurer ; cela ne corrige
  pas une liaison dont le débit disponible est insuffisant.

Référence : [option TCP_NODELAY dans FFmpeg](https://ffmpeg.org/ffmpeg-protocols.html#rtmp).

## Validation du premier correctif

Les deux nouveaux scénarios moteur reproduisaient les défauts avant correction :
sortie lente sans déconnexion annoncée comme stable, et zéro répété sans purge.
Ils passent après correction. Deux cas de présentation vérifient une cible zéro
et une cible de 15 secondes, avec maintien de la mesure réelle et du bon message.

Compilation Debug x64 : zéro avertissement et zéro erreur.
Tests : **102 réussis** (42 Core, 51 Infrastructure, 9 Presentation).
Résultats TRX : `artifacts/validation/zero-delay`.

Un essai RTMP de boucle locale sur le port 19439 vérifie aussi l'émetteur avec
TCP_NODELAY, sans destination extérieure. Les mesures sont consignées dans
`artifacts/validation/zero-delay/local-rtmp.jsonl`. Ce test ne remplace pas une
vérification sur le serveur de diffusion réel.

## Essai utilisateur

La version déjà ouverte ne reçoit pas les modifications à chaud. La version
corrigée est compilée en **Release x64**. L'installeur antérieur n'est pas remplacé.

1. À la fin de la session en cours, arrêter le relais puis fermer Temhalo.
2. Depuis la racine du projet, lancer :

```powershell
.\tools\Start-Temhalo.ps1 -Configuration Release
```

Le profil local du lanceur réutilise les réglages et comptes enregistrés.

3. Démarrer le relais et OBS à 0 s. Vérifier le serveur automatique dans la
   destination Twitch, observer la mesure pendant une minute, puis faire 15 → 0 s.
4. Si un retard d'au moins une seconde persiste à zéro, vérifier que le statut
   signale la sortie ralentie. Réappuyer sur **0 s** : le moteur doit abandonner
   le retard encore dans son buffer en reprenant sur une image clé, sans relancer
   OBS ni l'émetteur. Une sortie toujours bloquée peut retenir le saut jusqu'à la
   fin de l'écriture en cours, et un réseau trop lent peut recréer le retard.
5. Comparer avec le test local et vérifier le son, l'image et leur synchronisation.
   Pour mesurer le délai vu par les spectateurs, utiliser le chronomètre OBS sur
   un second appareil : cette mesure inclut aussi les latences de la plateforme
   et du lecteur.

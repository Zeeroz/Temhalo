# Temhalo — étape 5 : raccourcis, reprises, journaux et DPAPI

**Repères dans la nouvelle interface de l'étape 5 bis :** l'ancienne vue Destination devient **Diffusion**, Raccourcis devient **Réglages → Vos commandes**, et Journal devient **Réglages → Confidentialité**. Le bouton de sauvegarde des raccourcis s'appelle désormais **Enregistrer les commandes**. Les procédures média ci-dessous restent applicables ; le [guide d'interface courant](ETAPE-5BIS-UI.md) décrit les nouveaux écrans.

Les étapes 0 à 4 ont été validées. Cette livraison ajoute les services Windows demandés au MVP. L'identité de `BRAND.md`, WinUI 3, .NET 8 et la chaîne OBS → MediaMTX → lecteur FFmpeg → Core → émetteur FFmpeg sont conservés. Aucun réencodage n'est ajouté au relais. L'étape 6 attend votre retour.

## Préparer et lancer

Depuis la racine du projet, sur le poste Windows x64 préparé à l'étape 0 :

```powershell
dotnet restore Temhalo.sln -p:Platform=x64 --locked-mode
dotnet build Temhalo.sln --no-restore -c Debug -p:Platform=x64
dotnet test Temhalo.sln --no-build --no-restore -c Debug -p:Platform=x64
& .\tools\native\Get-NativeTools.ps1
& .\tools\Start-Temhalo.ps1
```

Fermer les autres instances du relais avant démarrage. Le port 1935 doit être libre ; Temhalo ne termine et ne réutilise aucun service concurrent, par exemple DynamicDelay. Le lancement de l'application restaure les réglages mais **ne lance aucune diffusion automatiquement**.

## Raccourcis globaux

La vue **Raccourcis** configure les quatre actions et la valeur du preset de protection.

| Action | Combinaison initiale | Effet |
|---|---|---|
| Preset de protection | Ctrl+Alt+F6 | Applique 300 s par défaut, ou le preset configuré entre 0 et 300 s |
| Délai zéro | Ctrl+Alt+F7 | Demande le retour à la dernière IDR disponible |
| Ajouter 10 s | Ctrl+Alt+F8 | Ajoute 10 s à la cible appliquée, au maximum 300 s |
| Retirer 10 s | Ctrl+Alt+F9 | Retire 10 s à la cible appliquée, au minimum 0 s |

Cliquer sur **Appliquer et enregistrer les raccourcis**. Une combinaison comporte au moins Ctrl, Alt, Maj/Shift ou Win, puis une lettre A–Z, un chiffre 0–9, F1–F11, une flèche, Home, End, PageUp, PageDown ou Insert. Exemple : `Ctrl+Alt+P`. F12 est réservé par Windows. Deux écritures équivalentes, comme `Alt+Ctrl+F6` et `Ctrl+Alt+F6`, comptent comme le même raccourci.

Les raccourcis utilisent `RegisterHotKey`, `WM_HOTKEY` et `MOD_NOREPEAT`, sans hook clavier permanent. Ils agissent pendant une session active, y compris lorsque Temhalo est en arrière-plan ; ils sont ignorés pendant la vidange et lorsque le relais est arrêté. Ils suivent la cible appliquée, jamais un brouillon non validé. Le maintien d'une touche ne doit pas répéter les commandes. La fermeture de la fenêtre libère les inscriptions.

Une combinaison occupée déclenche un message explicite avec le code Windows. Les anciennes combinaisons sont restaurées si l'application d'un nouvel ensemble échoue ; si leur restauration échoue à son tour, l'erreur signale la désactivation. Une seconde instance ne récupère pas les raccourcis de la première. Référence : [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey).

## Destination et clé chiffrée

Dans **Destination**, choisir fichier ou RTMP/RTMPS et renseigner les valeurs, puis cliquer sur **Enregistrer la destination**. La destination, la plateforme, la limite mémoire et les raccourcis sont conservés. **Mémoriser la clé sur ce compte Windows** décide si la clé rejoint la configuration enregistrée. Décocher puis enregistrer retire la clé du document enregistré ; la valeur saisie reste utilisable dans la fenêtre courante. **Oublier la clé enregistrée** retire la clé du document et vide le champ.

Le document entier est chiffré avec DPAPI `CurrentUser`, dans :

```text
%LOCALAPPDATA%\Temhalo\settings.dpapi
```

La sérialisation en clair reste en mémoire. Les tableaux de travail sont effacés ; le fichier temporaire contient uniquement le résultat DPAPI et remplace le précédent dans le même dossier. Une annulation avant remplacement préserve l'ancien fichier. Une configuration altérée ou indéchiffrable produit un message, sans effacer silencieusement le fichier existant. Aucun repli vers un JSON en clair n'est prévu.

Le fichier doit être utilisé sous le compte Windows qui l'a protégé. DPAPI protège le stockage : un processus exécuté sous ce même compte peut demander son déchiffrement. La clé est nécessaire en mémoire pendant l'exécution ; l'URL passée à FFmpeg reste inspectable par un outil système disposant de l'accès au processus. Elle n'est pas journalisée. Référence : [protection de données .NET / DPAPI](https://learn.microsoft.com/en-us/dotnet/standard/security/how-to-use-data-protection).

## Reconnexions et conservation du buffer

Le buffer appartient désormais à la session C#, indépendamment des connexions natives.

| Incident | Comportement |
|---|---|
| OBS se déconnecte | Les tags complets encore en RAM se vident à leur échéance. Temhalo attend ensuite la publication suivante. |
| FFmpeg lecteur tombe | Le lecteur redémarre ; le buffer et l'émetteur sont conservés. Le dernier tag incomplet est abandonné. |
| MediaMTX tombe | Le serveur local redémarre ; le buffer et l'émetteur sont conservés. OBS doit republier, avec sa reconnexion automatique ou une relance de publication lors du test. |
| FFmpeg émetteur ou destination tombe | L'entrée continue, le consommateur conserve la file RAM et recrée seulement l'émetteur. La reprise attend une IDR et renvoie AVC/AAC et l'en-tête FLV. |
| Émetteur fichier tombe | Un nouveau fichier `relay-0002.flv`, etc. est ouvert. Le fichier interrompu n'est ni écrasé ni concaténé aveuglément. |
| Budget RAM ou capacité de tags atteints | La session s'arrête avec une erreur explicite ; le délai de protection n'est pas réduit automatiquement. |

Les sessions OBS sont distinguées dans la file : une nouvelle configuration ne remplace pas prématurément celle des paquets anciens encore retardés. Leur changement de timestamps reprend avec un décalage commun audio/vidéo. Le producteur et le consommateur restent séparés, sans verrou bloquant sur le chemin des paquets.

La sortie attend 1, 2, 4, 8 puis 10 s entre échecs consécutifs, avec retour au délai minimal après progression média. FFmpeg limite les opérations réseau à 10 s ; les écritures bloquées vers son stdin sont bornées à 15 s. La détection n'est donc pas instantanée. Sans média à envoyer, la prochaine reprise effective attend du média exploitable. Les redémarrages et attentes sont annulables.

Une reprise conserve les tags encore détenus par Core, puis saute au plus un GOP incomplet avant la prochaine IDR exploitable. **Le média déjà confié aux pipes/FFmpeg ou au serveur n'est pas acquitté par le lecteur distant** : son éventuelle perte ou répétition lors d'une panne ne peut pas être exclue. La reconnexion RTMP peut également créer une nouvelle session côté plateforme. Les pauses par paliers ne garantissent toujours pas le maintien de cette session.

Après une panne, Core conserve le contenu restant et le rythme de lecture normal. Le retard réel peut donc dépasser la cible. L'interface affiche **retard conservé** ; réappliquer la cible, ou demander **0 s**, effectue le rattrapage sur IDR. Cette action abandonne volontairement le contenu sauté. Aucun rattrapage silencieux n'est effectué pendant la panne.

La limite RAM inclut aussi le contenu accumulé pendant la reconnexion. Un budget dimensionné pour cinq minutes de délai ne peut pas garantir cinq minutes supplémentaires de panne. Le dashboard avertit à 80 % par défaut. Pendant une session active sans OBS, environ 0,5 Mio peuvent rester réservés à la file et aux configurations même lorsqu'il n'y a plus de tags en attente ; cette réserve est libérée à l'arrêt.

**Arrêter le relais** arrête le lecteur puis demande la vidange. Une destination durablement indisponible ne peut pas recevoir cette fin de flux : l'arrêt est borné à environ 320 s, plus la fermeture des processus, puis le contenu non transmissible est libéré avec un avertissement. La fenêtre reste réactive pendant cette attente. La survie du buffer au crash du processus Temhalo lui-même n'est pas prévue : le stockage reste en RAM.

## Journaux

Dans **Journal**, les 200 événements récents sont affichés et deux boutons ouvrent les journaux ou le dossier de session. Les journaux de l'application sont centralisés dans :

```text
%LOCALAPPDATA%\Temhalo\logs\temhalo-*.log
```

Rotation quotidienne et par taille : **10 Mio par fichier, 7 fichiers conservés**, avec une limite pouvant être dépassée du dernier événement. Cette rétention couvre les lancements successifs de l'UI. Les fichiers FLV de test restent dans `artifacts/relay-ui/run-…/` et ne sont pas supprimés par la rotation des logs.

Les logs contiennent les événements de processus, demandes de délai, raccourcis exécutés, transitions, avertissements RAM et erreurs. Les arguments des processus ne sont pas enregistrés. Les URL, champs de credentials et valeurs exactes des clés connues sont masqués avant affichage ; le formateur de fichier masque aussi les propriétés et exceptions formatées. Le flux FFmpeg `-progress` sert à la télémétrie, sans générer une ligne par compteur. RelayHost garde ses logs par dossier de session avec le même formateur.

## Procédure de test manuel

1. Fermer tout autre relais sur 1935. Lancer Temhalo, garder **Fichier de test local**, mettre 0 s puis démarrer. Publier dans OBS vers `rtmp://127.0.0.1:1935/live`, clé locale `stream`, H.264 CBR, AAC et keyframes 1 s, délai OBS désactivé. Pour le premier essai, garder zéro B-frame.
2. Dans **Raccourcis**, régler provisoirement le preset à 15 s et enregistrer. Mettre une autre application au premier plan ; tester les quatre combinaisons. Les cibles doivent évoluer sans redémarrage des processus. Préparer un brouillon différent avant +10 s : l'action doit partir de la cible appliquée. Maintenir une touche : une seule commande doit partir. Revenir à zéro.
3. Tester deux combinaisons identiques : erreur et anciennes combinaisons toujours actives. Ouvrir une seconde instance : elle doit expliquer le conflit Windows ; la première doit rester pilotable. Fermer la seconde. Tester aussi la désactivation puis la réactivation des raccourcis.
4. Appliquer 5 s et attendre. Arrêter OBS : l'entrée doit passer à Déconnecté, le contenu restant continuer, puis la session attendre OBS. Redémarrer la publication sans arrêter Temhalo : attendre la reprise IDR. En sortie fichier, le même émetteur/fichier doit continuer.
5. Dans le Gestionnaire des tâches, terminer **uniquement le lecteur FFmpeg de cette session**, identifié par son PID dans les logs. Vérifier sa reprise sans arrêt de l'émetteur et sans remise à zéro du budget de session. Refaire séparément pour MediaMTX avec OBS configuré pour se reconnecter.
6. Arrêter le relais et suivre le [guide RTMP local / VLC](APERÇU-LOCAL-VLC.md) : démarrer `tools/Start-LocalPreview.ps1`, destination `rtmp://127.0.0.1:1936/live`, clé fictive `preview`. Reprendre OBS et VLC, remplir 5 s, puis couper le serveur d'aperçu avec Ctrl+C. L'entrée doit continuer et le buffer grossir. Relancer le serveur, attendre **Émission confirmée**, puis réappliquer 5 s ou demander 0 pour rattraper. Relancer la lecture VLC si son propre cache ou sa connexion restent bloqués.
7. Refaire avec le seul émetteur FFmpeg terminé volontairement. Le lecteur doit garder son PID. Pour les essais de pannes, n'arrêter aucun processus extérieur à la session. Un serveur redevenu disponible ne doit pas exiger de redémarrer toute l'application.
8. Arrêter proprement le relais. Enregistrer une destination locale avec une **clé fictive unique** et la mémorisation cochée. Fermer et rouvrir : URL, clé masquée, mémoire et raccourcis doivent revenir sans diffusion automatique. Vérifier l'absence de cette chaîne fictive dans le fichier DPAPI et les logs. Utiliser uniquement une clé de test pour cette vérification.
9. Tester **Oublier la clé enregistrée**, puis fermer/rouvrir : le champ doit être vide, les autres réglages conservés. Refaire en décochant la mémorisation puis en enregistrant. Une configuration DPAPI altérée doit produire une erreur et rester sur disque jusqu'à un enregistrement explicite ; sauvegarder sa configuration de test avant cet essai.
10. Examiner **Journal**, les événements des raccourcis, reprises et transitions. Ouvrir les journaux. Vérifier les vues à 1280, 720 et 320 px, le retour petite → grande fenêtre, le clavier et les messages d'erreur. Terminer la source, attendre la vidange et arrêter Temhalo ; décoder le fichier final avec FFmpeg ou le lire dans VLC.

La mire locale remplace OBS pour un essai rapide : démarrer le relais, puis `& .\tools\Send-TestPattern.ps1 -Seconds 120` dans un second terminal. L'encodage sert uniquement à fabriquer la mire ; toute la chaîne du relais reste en `-c copy`.

## Vérifications automatisées et résultats

Arrêter les relais UI/console et l'aperçu sur 1936 avant les tests natifs :

```powershell
dotnet test Temhalo.sln --no-build --no-restore -c Debug -p:Platform=x64
& .\tools\Test-Stage3.ps1
& .\tools\Test-Stage5.ps1
```

La validation native 5 utilise uniquement les ports loopback 1935 et 1936, des processus qu'elle possède et une mire synthétique. Elle ne publie vers aucun compte. Prévoir quelques minutes. Ses résultats et médias sont écrits sous `artifacts/validation/stage5-…/`. Les scripts des étapes 1 et 2 restent des documents/tests historiques de ces livraisons ; le test 1 complet attendait une nouvelle sortie après une reconnexion OBS, comportement remplacé par la conservation de session. Pour le comportement courant des pannes, employer le test 5.

Le [rapport de cette livraison](validation/etape-5.json) décrit la compilation, **60 tests unitaires**, le test natif de reprises, la régression dynamique, le décodage et les observations UI. Le test de rotation emploie une limite réduite et vérifie les fichiers retenus et le masquage des propriétés et exceptions. Les paramètres de production restent 10 Mio / 7 fichiers.

Les essais sur Twitch/YouTube/Kick, une véritable coupure Internet, 30 minutes de changements dans OBS, la mesure CPU sur le poste cible et les échelles/accessibilités système restent à effectuer. Les tests locaux ne les remplacent pas. L'installeur, la distribution autonome et la campagne de robustesse de l'étape 6 ne sont pas commencés.

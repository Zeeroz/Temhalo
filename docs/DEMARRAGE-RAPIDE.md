# Démarrer avec Temhalo

1. Installez Temhalo. Le raccourci Bureau est proposé et le menu Démarrer est inclus.
   Si vous utilisez Stream Deck, gardez son plugin coché. Temhalo s'ouvre à la fin.
2. Dans **Diffusion**, ajoutez vos plateformes. Twitch peut être relié avec **Connecter
   mon compte** ; la clé manuelle reste disponible. YouTube et Kick nécessitent leur
   configuration OAuth ou une clé manuelle. Les connexions mémorisées sont chiffrées
   avec DPAPI pour votre compte Windows.
3. Suivez **Relier OBS** : service **Personnalisé**, serveur
   `rtmp://127.0.0.1:1935/live`, clé locale `stream`. Utilisez H.264/AAC, CBR et un
   intervalle d'images clés de **1 seconde**. Désactivez le délai intégré d'OBS.
4. Activez le relais dans Temhalo, puis démarrez le streaming dans OBS. Lors du premier
   lancement, MediaMTX et FFmpeg sont téléchargés et vérifiés automatiquement.
5. Dans **Direct**, choisissez 0, 15, 30 ou 60 secondes, ou saisissez votre délai de
   0 à 300 secondes. Les presets s'appliquent au clic ; une saisie se valide avec
   **Appliquer**. La latence propre à la plateforme et au lecteur s'ajoute.

## Stream Deck

Les touches se règlent dans l'application Elgato Stream Deck et se connectent directement
à Temhalo. Le plugin peut être installé ou mis à jour depuis **Réglages** dans Temhalo.
Les actions preset, +10, −10, retour à 0 et démarrage/arrêt du relais restent disponibles.
L'action **Protection** demande 300 secondes ; les presets individuels restent réglables.
Les profils et valeurs déjà présents sur votre deck sont conservés lors des mises à jour.

## Réglages

Une seule page contient les mises à jour, l'état du Stream Deck, la limite mémoire et
les journaux. Les anciens menus **Vos commandes** et **Confidentialité**, les raccourcis
clavier globaux, les presets personnalisables de l'application, le dock OBS et l'aperçu
VLC/test local ont été retirés. Les boutons du Direct et le champ numérique restent
disponibles pour piloter le délai.

Pour obtenir de l'aide, ouvrez **Journaux et diagnostic** puis exportez les événements.
Les clés et adresses de publication sont masquées. Aucune diffusion ne démarre toute seule.

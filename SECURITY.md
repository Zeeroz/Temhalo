# Signaler un problème de sécurité

Ne publiez aucun secret, fichier de réglages, lien privé d’overlay/dock ou journal brut dans une issue publique. Pour un problème exploitable, utilisez **Security → Advisories → Report a vulnerability** si le signalement privé est activé sur GitHub. Sinon, ouvrez une issue demandant un canal privé, sans détail permettant l’exploitation.

Les clés et jetons utilisateur sont chiffrés par DPAPI pour le compte Windows courant. Cela ne protège pas d’un programme malveillant exécuté sous ce même compte. Le broker Kick garde son secret côté serveur ; les identifiants de développement personnels ne font pas partie du dépôt.

Les mises à jour sont téléchargées uniquement depuis les releases de `Zeeroz/Temhalo`, en HTTPS, avec taille et SHA-256 contrôlées à partir des métadonnées de l’API GitHub, puis une nouvelle vérification avant lancement. Cette intégrité dépend de la sécurité du dépôt et de GitHub ; elle n’est pas une signature Authenticode. Activez la double authentification du compte mainteneur et protégez les droits de publication.

Une mise à jour ne s’installe jamais pendant un relais actif. Les binaires média sont téléchargés depuis leurs sources amont et vérifiés avec les empreintes fixées dans `config/native-tools.lock.json`.

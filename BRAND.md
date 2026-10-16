# Temhalo — Mini brand book

Version 1.11 · 17 octobre 2026 · Stream Deck direct et retrait de l'overlay

**Statut : identité visuelle, étapes 0 à 6 et étape 5 bis (interface et services) validées par l'utilisateur. Installeur Windows 0.6.0-preview.1 autorisé, avec raccourcis Bureau/menu Démarrer et désinstallation conservant les données du profil.**

Ce document constitue la référence de l'identité et de l'interface de Temhalo. Les règles de DA sont validées. Les sections 11 et 12 consignent le concept et les fichiers approuvés à l'étape A. Le prototype visuel et l'outillage de génération des images sont indépendants de l'application .NET.

## 1. Positionnement

Temhalo est un outil Windows de contrôle du délai de diffusion pour les streamers qui veulent mieux maîtriser le moment où leurs actions deviennent visibles. Il accompagne les sessions compétitives, les échanges avec le public et les situations où une marge temporelle réduit l'exposition au stream sniping.

**Personnalité : calme, précise, protectrice.**

- Calme : une lecture immédiate, une présence discrète, des alertes proportionnées.
- Précise : les valeurs mesurées, les objectifs et les transitions sont distingués.
- Protectrice : le streamer garde la maîtrise de la commande et comprend son effet.

**Promesse : « Ajustez le délai de votre live à la demande, depuis une commande claire. »**

Signature : **« Votre live. Votre tempo. »**

Descriptif court : **« Contrôle du délai de diffusion »**. Équivalent anglais : **« Live stream delay control »**. Signature anglaise : **« Your stream. Your timing. »**

Le nom public s'écrit **Temhalo**, avec une seule capitale, sans espace ni accent. Prononciation indicative : « tem-ha-lo », trois syllabes. Les identifiants techniques pourront employer `Temhalo` ou `temhalo` selon leur convention.

## 2. Ton de voix et vocabulaire

La voix est directe, posée et utile. L'interface française emploie des libellés courts ; les explications s'adressent à l'utilisateur avec « vous ». Une alerte indique l'événement, son effet et l'action possible.

| Situation | Formulation proposée |
|---|---|
| Commande | Appliquer 30 s |
| Augmentation | Ajout du délai en cours |
| Réduction | Réduction du délai en cours |
| Entrée absente | OBS déconnecté. Le buffer continue de se vider. |
| Sortie interrompue | Connexion à la plateforme interrompue. Reconnexion en cours. |
| Buffer vide | En attente du flux OBS |
| Mémoire sous pression | Buffer proche de la limite mémoire |
| Limite atteinte | Limite mémoire atteinte. Consultez l'état du buffer. |

Les messages doivent correspondre à l'état réellement observé. Une reconnexion n'est annoncée que si une tentative existe ; la conservation du buffer n'est affirmée que si elle est effective. Le comportement exact à la limite mémoire sera défini avec le moteur.

Le délai affiché mesure le délai ajouté localement par Temhalo. « 0 s » signifie le minimum local réalisable. La latence d'OBS, du réseau, de la plateforme et du lecteur reste indépendante. Les mots « protégé », « connecté » et « en direct » ne remplacent pas une mesure ou un état précis. Les communications publiques n'annoncent pas de protection absolue, de latence totale nulle ou d'absence garantie de coupure.

## 3. Direction visuelle

L'univers de Temhalo associe un **bleu nuit légèrement ardoisé**, une **menthe lumineuse mais douce** et une **lavande désaturée**. Une touche de sable réchauffe les supports de marque. Le contraste et la typographie donnent la hiérarchie ; les effets restent discrets.

La notion de halo guide la sensation de protection et d'espace. Le symbole retenu est un anneau ouvert avec un segment décalé. La composition reste stable, aérée, précise et lisible pendant une partie.

Répartition indicative sur un écran courant : environ 85 % de surfaces neutres, 10 % de texte et structure, 5 % de couleurs de marque et d'état. Ces proportions servent de garde-fou visuel, pas de métrique d'implémentation.

## 4. Palette et rôles

Les couleurs sont opaques, exprimées en sRGB. Les noms de jetons ci-dessous serviront à centraliser les ressources WinUI ; une même couleur peut avoir plusieurs rôles sémantiques sans dupliquer sa valeur.

| Jeton | Nom | HEX | RGB | Usage |
|---|---|---|---|---|
| `Brand.Primary` | Halo | `#7CE8CD` | 124, 232, 205 | Action principale, commande de délai active, focus |
| `Brand.Secondary` | Écho | `#B9B2FF` | 185, 178, 255 | Valeur de délai cible et repères secondaires |
| `Brand.Accent` | Sable | `#F0D6A1` | 240, 214, 161 | Petite touche éditoriale sur les supports de marque |
| `Background.Canvas` | Nuit | `#0D1218` | 13, 18, 24 | Fond principal opaque |
| `Background.Surface` | Ardoise | `#151D26` | 21, 29, 38 | Zone de commande et groupes fonctionnels |
| `Background.Raised` | Brume sombre | `#1D2935` | 29, 41, 53 | Menus, boîtes de dialogue, surfaces surélevées |
| `Text.Primary` | Givre | `#F1F5F9` | 241, 245, 249 | Valeurs et texte principal |
| `Text.Secondary` | Brume | `#B4C1CF` | 180, 193, 207 | Libellés et texte explicatif |
| `Text.Muted` | Acier | `#96A7B9` | 150, 167, 185 | Unités, horodatages et métadonnées |
| `Text.OnBrand` | Encre | `#0D1218` | 13, 18, 24 | Texte sur un fond de marque clair |
| `Stroke.Decorative` | Joint | `#2C3C4C` | 44, 60, 76 | Séparateurs décoratifs uniquement |
| `Stroke.Control` | Repère | `#687E94` | 104, 126, 148 | Limites de champs, piste inactive du slider |
| `State.Success` | Signal | `#75D8A5` | 117, 216, 165 | Connexion établie ou opération terminée |
| `State.Warning` | Vigilance | `#F6C66D` | 246, 198, 109 | Reconnexion, transition ou pression mémoire |
| `State.Error` | Alerte | `#FF8D9A` | 255, 141, 154 | Échec ou anomalie exigeant une action |
| `Action.Hover` | Halo clair | `#9AEFD9` | 154, 239, 217 | Survol d'une action pleine Halo |
| `Action.Pressed` | Halo dense | `#5DCEB2` | 93, 206, 178 | Appui d'une action pleine Halo |
| `Brand.LightBackground` | Papier | `#F4F7FA` | 244, 247, 250 | Support clair de marque ou impression |

Halo identifie une commande, Signal un état confirmé. La proximité de leurs teintes impose un libellé et une forme distincts. Sable reste réservé à l'éditorial ; Vigilance porte les avertissements. Sur le dashboard, Halo et Écho sont les deux couleurs de marque dominantes ; les couleurs d'état apparaissent selon les besoins.

### Associations autorisées et contraste

Les ratios ont été calculés sur les valeurs sRGB opaques, avec la formule de luminance relative WCAG. Les décisions de conformité utilisent les ratios non arrondis ; les nombres ci-dessous sont arrondis pour la lecture.

| Premier plan / fond | Contraste | Affectation |
|---|---:|---|
| Givre / Nuit | 17,16:1 | Texte et valeurs sur le fond principal |
| Givre / Brume sombre | 13,49:1 | Texte dans une surface surélevée |
| Brume / Brume sombre | 8,07:1 | Texte secondaire, cas le moins contrasté des trois fonds |
| Acier / Brume sombre | 6,00:1 | Métadonnées lisibles |
| Halo / Brume sombre | 10,05:1 | Texte coloré, repère et focus sur fond sombre |
| Écho / Brume sombre | 7,65:1 | Délai cible |
| Sable / Brume sombre | 10,44:1 | Accent éditorial |
| Signal / Brume sombre | 8,53:1 | État OK |
| Vigilance / Brume sombre | 9,30:1 | Avertissement |
| Alerte / Brume sombre | 6,71:1 | Erreur |
| Encre / Halo | 12,79:1 | Libellé d'une action principale pleine |
| Encre / Halo clair | 14,08:1 | Même action au survol |
| Encre / Halo dense | 9,79:1 | Même action pendant l'appui |
| Repère / Brume sombre | 3,52:1 | Contours et éléments graphiques indispensables |
| Encre / Papier | 17,49:1 | Texte sur support de marque clair |

La règle de conception est un minimum de **4,5:1 pour tout texte actif**, y compris les petites unités, et **3:1 pour les repères graphiques nécessaires**. Elle est volontairement uniforme pour les textes, même lorsque les grands caractères pourraient bénéficier d'une exception WCAG.

Givre sur Halo n'offre que 1,34:1 : les boutons pleins clairs utilisent Encre. Joint n'offre que 1,31:1 sur Brume sombre : il ne peut pas être l'unique limite d'un champ ou l'indicateur d'un état. Repère est une couleur de structure, pas une couleur de petit texte.

Un état comprend un texte et, quand utile, une icône de forme identifiable. La couleur seule ne transmet jamais une information essentielle. Les pastilles d'état éventuelles ont un fond sombre opaque et un texte d'état clair ; toute nouvelle teinte de fond impose un nouveau contrôle de contraste. La baisse d'opacité globale d'un contrôle est à éviter : les couleurs finales doivent rester connues et vérifiables.

Les modes de contraste Windows prennent priorité sur la palette de marque et utilisent les couleurs système. Ce contrôle de palette n'est pas un audit de conformité de l'application, qui n'existe pas encore.

## 5. Typographies

Les trois familles sont disponibles sous **SIL Open Font License 1.1**, avec usage commercial et incorporation autorisés sous les conditions de la licence. Les fichiers de licence et mentions de copyright accompagneront les fontes distribuées. Les polices sont libres sous licence, pas dans le domaine public.

| Fonction | Famille | Graisses retenues | Taille / interligne de référence |
|---|---|---|---|
| Titre de page | Space Grotesk | 600 | 28 / 36 DIP ; 24 / 32 en compact |
| Titre de section | Space Grotesk | 500 ou 600 | 18 / 26 DIP |
| Signature et titres éditoriaux | Space Grotesk | 500 ou 600 | 40–64 DIP selon le support ; reflow en petit format |
| Texte d'interface | Inter | 400 | 14 / 20 DIP |
| Boutons et libellés forts | Inter | 500 ou 600 | 14 / 20 DIP |
| Métadonnées et aide courte | Inter | 400 ou 500 | 12 / 18 DIP minimum |
| Délai actuel | JetBrains Mono | 500 | 48 / 56 DIP ; 40 / 48 en compact |
| Délai cible et indicateurs | JetBrains Mono | 400 ou 500 | 18–24 / 26–32 DIP |
| Logs | JetBrains Mono | 400 | 12 / 18 DIP minimum |

Dans l'interface, les titres restent en casse de phrase. Le nom Temhalo conserve sa casse de marque. La graisse 700 n'est pas nécessaire pour le MVP. Les capitales espacées restent exceptionnelles sur les supports éditoriaux ; elles ne servent pas aux libellés de commande.

Les chiffres utilisent une chasse stable ; les ligatures de programmation sont désactivées dans les compteurs et les logs. Les unités restent accolées à leur valeur par une espace insécable. Les nombres français utilisent la virgule décimale.

Les tailles WinUI sont des DIP, pas des pixels physiques. Le layout doit absorber l'agrandissement du texte et la mise à l'échelle Windows. Les fontes seront embarquées localement pour l'application ; aucune installation de police ni connexion à Google Fonts ne sera requise par le produit. Les fichiers exacts seront figés lors de leur intégration. Les familles de repli de dépannage sont Segoe UI pour les textes et Consolas pour les chiffres ; elles ne constituent pas le rendu de référence.

Sources : [Space Grotesk](https://fonts.google.com/specimen/Space+Grotesk), [Inter](https://fonts.google.com/specimen/Inter), [JetBrains Mono](https://fonts.google.com/specimen/JetBrains+Mono). Licences officielles : [Space Grotesk — OFL](https://github.com/floriankarsten/space-grotesk/blob/master/OFL.txt), [Inter — licence](https://github.com/rsms/inter/blob/master/LICENSE.txt), [JetBrains Mono — OFL](https://github.com/JetBrains/JetBrainsMono/blob/master/OFL.txt).

## 6. Formes, traits, icônes et matière

- Grille d'espacement : 4, 8, 12, 16, 20, 24, 32, 40 et 48 DIP.
- Rayon des boutons et champs : 8 DIP. Groupes fonctionnels : 12 DIP. Surfaces de dialogue : 16 DIP lorsque le contrôle natif le permet. Les pastilles entièrement arrondies sont réservées aux petits états.
- Contours : 1 DIP pour la structure ; 2 DIP pour le focus. Éviter les empilements de cadres.
- Icônes : une seule famille de pictogrammes Fluent/Windows fournie avec WinUI, en style contour régulier. Taille usuelle 16 ou 20 DIP ; 24 DIP pour une action isolée. Épaisseur visuelle proche de 1,5 DIP à 20 DIP, sans redessiner les glyphes natifs.
- Les icônes qui agissent ont un libellé visible ou un nom accessible. Les actions fréquentes ne dépendent pas d'une infobulle.
- Fonds opaques et unis. Le niveau d'une surface est d'abord indiqué par sa couleur. Ombre native discrète uniquement pour les menus et dialogues flottants.
- Pas de glow permanent, de flou derrière les compteurs ou de dégradé sur les surfaces de travail. Un éclairage diffus peut être exploré ultérieurement sur un support de communication, après validation du logo, à distance du texte.
- La géométrie doit rester nette à petite taille. Le logo aura son propre dessin original, distinct des icônes de commande.

## 7. Mouvement

| Interaction | Durée indicative | Comportement |
|---|---:|---|
| Survol et appui | 100–120 ms | Transition de couleur unique |
| Sélection d'un preset | 140–160 ms | Changement discret du fond et du repère |
| Ouverture d'un panneau | 160–200 ms | Apparition sobre, sans rebond |
| Valeur mesurée | Immédiat | Remplacement du texte sans roulement ni déplacement |

Les durées sont des plafonds de confort visuel ; elles ne ralentissent pas l'exécution d'une commande. Aucun clignotement continu, effet de respiration ou mouvement décoratif sur le délai. L'indicateur natif d'attente est réservé à une attente effective. Si Windows désactive les animations, les changements se font directement avec un état textuel statique.

Le dashboard présente une fréquence de rafraîchissement lisible, de l'ordre d'une fois par seconde pour les compteurs, à préciser lors de l'implémentation. La fréquence de rendu UI reste indépendante de l'horloge précise du moteur.

## 8. Principes de l'interface WinUI 3

### Hiérarchie et structure

1. Lire le **délai actuel** et le **délai cible**.
2. Modifier le délai via les presets, le slider et le champ numérique.
3. Vérifier séparément l'entrée OBS et la sortie plateforme.
4. Consulter le buffer, les débits, la durée du live et les logs.

Le bloc de délai porte la hiérarchie. Les états de connexion sont proches et toujours visibles. Les statistiques secondaires sont regroupées en lignes, avec libellés et unités, plutôt que réparties dans une multitude de cartes. La configuration de destination et les logs disposent d'un accès stable. Aucun aperçu vidéo n'est ajouté au MVP par simple décoration.

Les contrôles natifs WinUI, leurs comportements clavier et leurs informations d'accessibilité sont conservés. Les styles et couleurs seront centralisés dans un dictionnaire de ressources Temhalo ; chaque état du thème y aura une affectation sémantique. Une valeur graphique ne sera pas recopiée localement dans chaque page.

### Grille et adaptation

- Padding de page large : gauche 28, haut 24, droite 28, bas 28 DIP. Compact : 20, 20, 20, 24 DIP.
- Padding d'un groupe : 24 DIP, ou 20 DIP en compact. Écart entre groupes : 24 DIP. Entre un libellé et son champ : 8 DIP. Entre commandes voisines : 8–12 DIP.
- Largeur de contenu disponible d'au moins 920 DIP : deux colonnes possibles, environ 2:1, délai à gauche et suivi à droite. La largeur s'entend après la navigation et les retraits.
- Sous 920 DIP : une colonne, commandes et champs empilés, presets sur plusieurs lignes si nécessaire. Défilement vertical ; aucun défilement horizontal de la page.
- Le contenu occupe la largeur utile avec ses retraits. Les cartes ne sont pas artificiellement étirées en hauteur.
- Fenêtre de travail de référence : 1280 × 820 DIP. Contrôle complémentaire à 720 × 640 DIP, écran maximisé et texte agrandi. Ces dimensions sont des cas de conception, pas des limites système déjà implémentées.
- Hauteur des boutons et champs : environ 40 DIP, avec zone d'action effective d'au moins 44 × 44 DIP pour les petites commandes. Les valeurs sont réévaluées si le texte ou le mode de saisie le nécessite.

### Commande du délai

Le slider et le NumberBox représentent la même **nouvelle cible**, entre 0 et 300 secondes, par pas de 1 seconde. Ils partagent un seul brouillon de valeur. Une saisie invalide reste explicitement signalée et ne déclenche aucune commande.

Le bouton **« Appliquer »** envoie le brouillon. Glisser le slider ou saisir une valeur ne multiplie pas les sauts du moteur. Les presets **0 s, 15 s, 30 s, 60 s et Personnalisé** sont des commandes rapides : les presets numériques appliquent leur valeur immédiatement ; Personnalisé donne accès à la saisie puis à Appliquer. Ces comportements seront confirmés visuellement dans la future maquette.

**Délai cible** désigne la dernière cible appliquée, pas le brouillon. **Délai actuel** désigne la mesure du moteur. Pendant la transition, les deux restent affichés et un état textuel décrit l'action. La télémétrie ne remplace jamais une saisie en cours.

La piste remplie et le curseur du slider utilisent Halo. La piste inactive utilise Repère. Le slider décrit une cible de délai, pas l'avancement d'un chargement. Les repères majeurs sont 0, 60, 120, 180, 240 et 300 s ; la valeur exacte est toujours accessible dans le champ numérique. La valeur appliquée cible utilise Écho ; le délai actuel utilise Givre pour rester l'information dominante.

### Presets, focus et états de contrôle

- Action principale : fond Halo, texte Encre ; Halo clair au survol, Halo dense pendant l'appui.
- Preset sélectionné : fond Halo et texte Encre, accompagné d'un état sélectionné accessible. Le choix ne signifie pas que le délai réel a déjà atteint la cible.
- Commandes secondaires : fond sombre, texte Givre, contour Repère lorsque le contour identifie le contrôle. Le survol change une seule propriété visuelle principale.
- Focus : repère visible de 2 DIP, Halo sur fond sombre, avec un espace sombre de 2 DIP autour d'un bouton plein clair pour maintenir sa lisibilité. Les repères système de contraste ont priorité.
- Inactif : texte Acier sur fond sombre et état désactivé explicite ; les dernières mesures restent lisibles. Une aide explique le motif d'indisponibilité lorsqu'il n'est pas évident.
- Erreur de saisie : texte explicatif Alerte sous le champ, icône identifiable et état accessible. Le texte saisi est conservé pour correction.

Le parcours Tab suit l'ordre visuel. Les flèches ajustent la valeur du slider ; le champ numérique offre une alternative complète au glissement. Les contrôles exposent un nom, une unité, une valeur et un état aux technologies d'assistance. Une transition importante est annoncée une fois ; les compteurs n'interrompent pas continuellement le lecteur d'écran.

### États du dashboard

| État | Traitement visuel et contenu |
|---|---|
| En attente d'OBS | Texte Acier, « En attente du flux OBS », mesures indisponibles « — » |
| OBS connecté | Signal + libellé « OBS connecté » |
| Sortie connectée | Signal + libellé « Plateforme connectée » |
| Ajout ou réduction du délai | Vigilance + libellé de transition, actuel et cible maintenus |
| OBS absent avec données restantes | Vigilance, indication du vidage et de la réserve restante |
| Sortie en reconnexion | Vigilance + « Reconnexion en cours », conservation effective du buffer indiquée |
| Échec nécessitant une action | Alerte + explication et accès à l'action pertinente |
| Buffer proche de la limite | Vigilance, quantité occupée et limite côte à côte |

« Connecté » décrit la connexion observée par le relais ; il ne certifie pas la lecture par tous les spectateurs. L'entrée et la sortie ne sont jamais fusionnées en un seul voyant. Un arrêt demandé, une attente et une erreur sont des états différents.

### Formats des valeurs

| Donnée | Format de référence |
|---|---|
| Délai actuel et cible | `mm:ss`, par exemple `00:30` et `05:00` |
| Saisie et presets | Secondes entières, par exemple `30 s` |
| Durée du live | `hh:mm:ss` |
| Débit | `8,0 Mb/s`, avec période de mesure cohérente |
| Buffer | `286 MiB / 512 MiB`, valeurs d'exemple |
| Horodatage des logs | `HH:mm:ss.fff`, date présente dans les fichiers |
| Mesure absente ou périmée | `—` ou dernière mesure explicitement marquée comme ancienne |

Les logs utilisent une vue texte filtrable ou une liste simple, avec heure, niveau et message. Le niveau possède un libellé, y compris lorsque sa couleur est visible. Les clés de stream et URL contenant un secret restent masquées dans l'interface de diagnostic.

## 9. Règles d'usage de la marque

Ces règles constituent le cahier des charges du futur logo ; ses minima seront éprouvés à l'étape de dessin avant validation finale.

- Fonds de référence : Nuit pour l'identité sombre ; Papier ou blanc uni pour la version noire. Le futur logo blanc servira sur fond sombre uni. La version colorée sera privilégiée sur Nuit ; toute autre association sera vérifiée.
- Sur une photographie, utiliser un aplat opaque suffisamment large pour garantir la lecture. Éviter les détails d'image directement derrière le symbole ou le nom.
- Espace de protection : définir `u` comme le quart de la hauteur du symbole. Conserver au moins `u` sur chaque côté de l'ensemble symbole + nom et du symbole isolé. La boîte d'espace de protection ne fait pas partie de l'image visible.
- Taille minimale visée pour le logo horizontal : 120 px de large à l'écran et 30 mm à l'impression, sous réserve de lisibilité du dessin final. En dessous, employer le symbole seul.
- Symbole autonome : 24 px recommandés. La déclinaison 16 px sera spécifiquement simplifiée et contrôlée ; les fichiers 32, 48 et 256 px feront également l'objet d'une vérification à taille réelle.
- Pour l'icône d'app, les marges optiques seront adaptées à chaque taille afin que le symbole reste lisible dans la barre des tâches et la zone de notification.
- Le nom et le symbole restent proportionnels. La signature est un élément séparé ; elle n'est jamais imposée à petite taille.

À proscrire : étirer, incliner, ajouter une ombre portée ou un glow au logo ; modifier ses proportions ou ses couleurs au cas par cas ; poser la marque sur un fond insuffisamment contrasté ; substituer un pictogramme système au symbole ; incorporer les logos OBS ou des plateformes dans le logo Temhalo ; présenter le simple échantillon typographique du présent brand book comme le logo final.

La piste A « Halo décalé » a été choisie. Son dessin développé, la composition horizontale, les versions monochromes et les adaptations optiques sont décrits dans la section 12 ; leur validation finale reste à obtenir.

## 10. Contrôle et procédure de validation

### Contrôles déjà effectués

- Calcul des contrastes de chaque couleur de texte, marque et état sur les trois fonds sombres. Le cas le moins favorable retenu dans le tableau est Brume sombre.
- Vérification des textes de bouton sur les trois fonds Halo, Halo clair et Halo dense.
- Distinction entre contour décoratif et contour nécessaire à la manipulation.
- Consultation des licences officielles des trois familles et des exigences WCAG de contraste.
- Examen visuel de l'échantillon de couleurs et de typographies dans Chrome, avec les trois fontes chargées, à des largeurs de contenu de 288, 328, 488 et 768 px : aucun débordement horizontal détecté. Ce contrôle porte sur la planche de marque, pas sur la future application.

Références : [WCAG — contraste du texte](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html), [WCAG — contraste des éléments non textuels](https://www.w3.org/WAI/WCAG22/Understanding/non-text-contrast.html).

### Validation de la direction artistique

Le 15 octobre 2026, l'utilisateur a validé le brand book et la direction artistique de Temhalo : positionnement, palette, typographies, ton de voix et principes d'interface. Il a ensuite choisi la piste A « Halo décalé » et autorisé la création des déclinaisons et de la maquette. Il demande une nouvelle validation avant l'étape 0.

### Contrôles à réaliser sur les futurs livrables

Les logos seront vérifiés en couleur, en noir et en blanc, sur fonds autorisés et à taille réelle. La future maquette puis l'UI seront examinées au clavier, avec lecteur d'écran, en contraste Windows, à 100/125/150/200 % de mise à l'échelle et avec texte agrandi. Le redimensionnement petite → grande → petite fenêtre, les longs libellés français et les états d'erreur devront rester utilisables. La conformité de l'application sera évaluée sur ces rendus réels.

## 11. Choix du concept — piste A retenue

Trois pistes ont été comparées sous forme d'esquisses géométriques dans la même palette. L'utilisateur a retenu la piste A ; B et C sont conservées ci-dessous comme historique du choix et ne sont pas développées.

### A — Halo décalé

Un anneau ouvert et un segment légèrement décalé représentent une enveloppe protectrice et l'intervalle de temps choisi.

- Expression : calme, protection, maîtrise.
- Atout : association directe avec le nom Temhalo et silhouette compacte.
- Développement retenu : anneau ouvert et segment décalé de 14 unités sur un master de 128 unités ; ouverture et épaisseur adaptées pour les petites tailles. Le symbole reste fixe dans l'application.

### B — Écho du flux

Deux tracés de même profil, décalés dans l'espace, représentent le même signal à l'entrée puis à la sortie retardée.

- Expression : circulation, précision, transmission.
- Atout : métaphore directe du fonctionnement du produit.
- Point à travailler si cette piste est retenue : assurer une silhouette identifiable en monochrome et éviter une lecture d'égaliseur audio ou de réglage générique.

### C — Réserve temporelle

Trois bandes décalées composent une pile d'instants conservés, dont la dernière bande évoque la prochaine portion du flux à libérer.

- Expression : réserve, stabilité, robustesse.
- Atout : géométrie pleine, simple, adaptée à une future icône compacte.
- Point à travailler si cette piste est retenue : donner au décalage une signature propre pour éviter un simple pictogramme de liste ou de stockage.

**Recommandation artistique : piste A, Halo décalé**, pour sa cohérence avec le nom et la personnalité validée. La piste B privilégie l'explication de la fonction ; la piste C privilégie l'idée de buffer.

**Choix validé : A — Halo décalé.**

## 12. Livraison du logo, des icônes et de la maquette

### Dessin validé

Le master du symbole est écrit en SVG sur une grille de **128 × 128 unités**. L'anneau menthe couvre 276° ; le segment lavande couvre 58° et son centre est décalé de 14 unités vers la droite. Le trait de 12 unités et les extrémités rondes prolongent le caractère calme de la DA. La rupture et le décalage sont conservés dans les versions monochromes.

Le mot-symbole est composé à partir de Space Grotesk 600, avec un crénage conservé et un léger ajustement d'approche. Les lettres sont converties en tracés : les SVG n'utilisent ni police externe, ni texte à interpréter, ni image incorporée. Le logo horizontal possède un viewBox de **500 × 152 unités**. Les SVG couleur ont un fond transparent et sont prévus pour le fond Nuit ; les fichiers de prévisualisation utilisent des fonds opaques de référence.

La silhouette est fixe. Elle ne tourne pas pendant le chargement et sa couleur ne signale pas à elle seule l'état du relais.

### Adaptations optiques

| Tailles | Adaptation |
|---|---|
| 16 et 20 px | Trait de 14 unités, ouverture élargie et segment légèrement plus court |
| 24 px | Trait de 13 unités et ajustement intermédiaire |
| 32 à 256 px | Géométrie du master |

Chaque taille est rendue séparément depuis sa géométrie. Le fichier ICO incorpore les images correspondantes ; la version 16 px n'est pas obtenue par réduction automatique de l'image 256 px.

L'espace de protection reste **un quart de la hauteur visible du symbole**, soit 23 unités pour le master. Il est mesuré depuis les bords visibles ; les marges transparentes déjà présentes dans le fichier peuvent en couvrir une partie. Les icônes d'application emploient leurs marges optiques propres pour exploiter leur petite surface. Les tailles minimales de référence restent 120 px pour le logo horizontal et 24 px pour le symbole générique ; la version optique dédiée permet l'usage à 16 px.

### Inventaire des fichiers

Les chemins ci-dessous sont relatifs à la racine du projet.

| Livrable | Fichiers |
|---|---|
| Logo horizontal couleur | `assets/brand/svg/temhalo-logo-horizontal-color.svg` |
| Logo horizontal blanc / noir | `assets/brand/svg/temhalo-logo-horizontal-white.svg` et `temhalo-logo-horizontal-black.svg` |
| Symbole seul couleur / blanc / noir | `assets/brand/svg/temhalo-symbol-color.svg`, `temhalo-symbol-white.svg`, `temhalo-symbol-black.svg` |
| Icône Windows multi-tailles | `assets/brand/temhalo.ico` |
| SVG et PNG d'application | `assets/brand/icons/app/temhalo-{taille}.svg` et `.png` |
| PNG pour barre des tâches | `assets/brand/icons/taskbar/temhalo-{taille}.png` |
| Zone de notification sombre | `assets/brand/icons/tray-white/temhalo-{taille}.png` et `assets/brand/icons/temhalo-tray-white.ico` |
| Zone de notification claire | `assets/brand/icons/tray-black/temhalo-{taille}.png` et `assets/brand/icons/temhalo-tray-black.ico` |
| Écran de démarrage sombre | `assets/brand/splash/temhalo-splash-dark.svg` et `.png`, 1240 × 600 px |
| Gabarits visuels Windows | `assets/brand/windows/` : Square44x44Logo, Square150x150Logo, StoreLogo et SplashScreen aux échelles 100, 125, 150, 200 et 400 % |
| Planche couleur et monochromes | `assets/brand/previews/temhalo-logo-sheet.png` |
| Contrôle des petites icônes | `assets/brand/previews/temhalo-icon-proof.png` |
| Polices et licences | `assets/brand/fonts/`, notices dans `fonts/licenses/`, provenance dans `fonts/sources.json` |
| Maquette autonome hors ligne | `design/Temhalo-dashboard.html` |
| Captures de la maquette | `design/Temhalo-dashboard-1280.png`, `Temhalo-dashboard-720.png`, `Temhalo-dashboard-320.png`, `Temhalo-dashboard-reconnexion.png` |
| Rapports de contrôle | `assets/brand/validation.json`, `assets/brand/manifest.json`, `design/validation.json` |
| Archive de livraison | `Temhalo-Identite-v1.zip` |

Tailles incorporées aux trois fichiers ICO : **16, 20, 24, 32, 40, 48, 64, 96, 128 et 256 px**. Les PNG du symbole et de la zone de notification conservent un canal alpha. Les versions blanches sont destinées aux fonds sombres ; les versions noires aux fonds clairs. Les versions monochromes sont fournies pour le symbole et le logo horizontal.

Les SplashScreen transparents sont centrés dans un gabarit de 620 × 300 px à 100 %, avec le fond d'accueil à définir en Nuit. Les autres échelles en sont les multiples. Le gabarit 620 × 300 est documenté par [Microsoft pour les assets SplashScreen](https://learn.microsoft.com/en-us/windows/uwp/launch-resume/add-a-splash-screen). Ces fichiers sont des ressources graphiques : leur raccordement effectif à WinUI 3 et au packaging choisi sera traité dans les étapes d'implémentation. Ils ne constituent pas un manifeste ni un installeur.

### Maquette de l'écran principal

La maquette présente le logo dans la barre de titre, une navigation supérieure, la commande du délai dominante, puis les connexions OBS et plateforme séparées, les débits, la durée du live, l'occupation mémoire et les derniers événements. Le journal détaillé, la destination et les associations de raccourcis disposent d'un écran accessible par la navigation.

- Slider et champ numérique synchronisés sur un brouillon de 0 à 300 secondes entières. La cible appliquée reste distincte du brouillon.
- « Appliquer » déclenche une transition visuelle simulée ; les presets numériques agissent immédiatement sur l'aperçu.
- Le preset 0 revient immédiatement au délai local simulé nul. Le moteur de reprise sur keyframe sera implémenté plus tard.
- Scénarios de lecture : diffusion stable, ajout du délai, sortie en reconnexion, OBS déconnecté, pression mémoire.
- Les vues Destination et Raccourcis sont des présentations visuelles. La clé est fictive, masquée et non éditable. Aucun secret réel n'est attendu.
- Les polices du prototype sont incorporées. Le fichier HTML n'effectue aucune requête réseau et conserve les interactions uniquement en mémoire.

Les valeurs du buffer, les temps, les débits et les transitions sont simulés pour l'évaluation graphique. Les touches globales, processus de diffusion, stockage DPAPI et reconnections réelles appartiennent aux étapes du produit. Le prototype ne modifie ni OBS ni une plateforme.

### Vérifications effectuées

- 70 SVG parsés comme XML ; viewBox présent, absence de texte dépendant d'une police, de script, d'image ou de référence externe.
- 66 PNG ouverts et vérifiés, dimensions des quatre familles d'icônes contrôlées et transparence des coins vérifiée.
- Trois ICO ouverts et leurs dix images décodées individuellement ; les tailles annoncées sont présentes.
- Planche des logos, versions blanche/noire, écran de démarrage et tailles 16, 32, 48 et 256 px examinés visuellement.
- Maquette contrôlée à 320, 360, 720, 920, 1024, 1280 et 1920 px, sans débordement horizontal. Une colonne sous le seuil de largeur utile prévu ; deux colonnes au-dessus.
- Contrôle à zoom navigateur de 200 %, navigation aux flèches et touche Home, préparation d'une valeur au clavier, validation des bornes et rejet d'une valeur fractionnaire.
- Saisie d'un brouillon sans changement prématuré de cible, commande Appliquer, preset 0, arrêt/reprise de l'aperçu, modification de la plateforme affichée et scénarios d'alerte vérifiés.
- Contrôle dans le rendu de conversation et dans le fichier autonome ; aucune erreur JavaScript ni requête HTTP(S) observée.
- Passe de finition : alignements, lisibilité à petite taille, survol d'un bouton désactivé et contraste du bandeau de démonstration corrigés.

Ces contrôles qualifient les fichiers et la maquette. Le rendu WinUI natif, les technologies d'assistance Windows, le comportement réel de la zone de notification et les tests du moteur restent à réaliser lors des étapes correspondantes.

### Procédure de relecture pour validation

1. Ouvrir `assets/brand/previews/temhalo-logo-sheet.png` et comparer les versions couleur, blanche et noire.
2. Examiner `assets/brand/previews/temhalo-icon-proof.png` à 100 % : les aperçus 16, 32, 48 et 256 px sont à taille réelle. Ouvrir aussi les PNG individuels pour éviter le redimensionnement automatique d'une visionneuse.
3. Ouvrir `design/Temhalo-dashboard.html` dans un navigateur. Saisir 45 s : la cible appliquée reste à 30 s jusqu'au clic sur Appliquer. Essayer ensuite le preset 0, les flèches du slider et une saisie hors limites.
4. Passer par les cinq scénarios et vérifier la distinction entre OBS, plateforme, transition et mémoire. Explorer les trois autres vues.
5. Réduire la fenêtre puis l'agrandir ; vérifier que les valeurs et commandes restent accessibles. Confirmer le logo, les icônes et la maquette ou demander des ajustements.

### Régénération des fichiers graphiques

L'outillage est placé dans `tools/branding/`. Il n'ajoute pas de dépendance à la future application. Les fichiers sources des fontes proviennent du dépôt Google Fonts au commit `1d0e46c7933245c8180fe83e47917961c913bbdb` ; leurs URL et empreintes sont enregistrées. Les notices OFL sont conservées. Les WOFF2 de prévisualisation sont des sous-ensembles statiques des fontes complètes fournies.

Avec Python 3.12+ et Node.js disponibles, depuis la racine du projet :

```powershell
python -m pip install -r tools/branding/requirements.txt
npm install --prefix tools/branding
python tools/branding/build_brand.py
node tools/branding/rasterize.cjs
python tools/branding/build_preview.py
python tools/branding/package_brand.py --zip
```

Le téléchargement des polices se produit uniquement si leurs fichiers sources manquent. Les étapes suivantes utilisent les ressources locales. `build_preview.py --inline CHEMIN` permet également d'écrire le fragment de prévisualisation de conversation. Les SVG sources de l'identité sont écrits directement en code ; les PNG sont des rendus de ces vecteurs.

**Étape A entièrement validée et règles visuelles inchangées. Étapes 0 à 5 et interface de l'étape 5 bis validées.** Les ressources natives se trouvent dans `src/Temhalo.App/Styles/`, les fontes dans `Assets/Fonts` après compilation et les captures réelles dans `design/native/` et `design/native-etape-5bis/`. Le guide courant est `docs/ETAPE-5BIS-SERVICES.md`. Les raccourcis, la mémorisation des destinations et les journaux reprennent les composants natifs, les couleurs sémantiques et les espacements existants. L'archive `Temhalo-Identite-v1.zip` conserve l'instantané de la livraison graphique initiale ; le présent fichier est la référence à jour.

## 13. Interface native de l'étape 5 bis

Les maquettes R3 fondent cette évolution. Depuis la simplification du 17 octobre 2026, la navigation **Direct / Diffusion / Réglages** reste en haut à toutes les tailles ; l'overlay est retiré à la demande de l'utilisateur. Le menu de débordement natif prend le relais à très petite largeur. Aucune barre latérale. Le nom et l'icône de l'application figurent seulement dans la barre de titre Windows : aucun bloc logo, nom ou signature supplémentaire dans l'en-tête.

Le symbole reste autorisé lorsqu'il porte un sens dans le produit : point de temporisation du trajet OBS → sortie. Cet usage fonctionnel ne constitue pas un second en-tête de marque.

- **Direct** : le délai réellement observé et la cible appliquée restent distincts du brouillon. La frise représente leur position dans le temps ; le slider prépare une commande. Les courbes de débit utilisent les compteurs réels des 60 dernières secondes. Un flux absent reste explicitement en attente.
- **Diffusion** : trajet du flux avant les cartes et champs des destinations. Jusqu'à quatre destinations réelles, dont les états proviennent des émetteurs ; aucune sortie ni détection automatique du profil OBS simulée.
- **Réglages** : rubriques horizontales ; choix natif compact aux petites tailles. Les libellés et champs partagent une ligne quand l'espace le permet, puis s'empilent. Les données privées restent locales et chiffrées selon les services existants.

La palette, les trois polices et les géométries du symbole restent celles des sections précédentes. Les contrôles natifs gardent leurs états clavier, focus, survol, pression et désactivation. Les ressources sémantiques conservent les substitutions du contraste élevé Windows. Grille sur 4 px, marges de page 28 px ou 20 px en mode compact, espacement de section 24 px, champs et commandes de 44 px minimum sauf petites actions contextuelles.

Sources : `src/Temhalo.App/Views/DashboardPage.xaml`, ses fichiers partiels, `Controls/TelemetryCanvas.cs` et `Controls/WrapPanel.cs`. Captures natives initiales : `design/native-etape-5bis/` ; procédure courante : `docs/CONTROLES-LOCAUX.md`.

### Déclinaisons navigateur de l'étape 5 bis

Le dock reprend Nuit, Halo et Écho, avec Space Grotesk pour le titre, Inter pour les commandes et JetBrains Mono pour les temps. Ses commandes restent utilisables à 320 px ; les groupes se replient verticalement. Il ne crée pas un second bloc de marque dans l'application. Les ressources sont intégrées à `Temhalo.Infrastructure.Windows/LocalWeb/Assets` et servies localement.

La source d'overlay n'est plus exposée. Le dock navigateur est facultatif, désactivé par défaut et indépendant de Stream Deck. Le plugin se connecte directement à l'application et reçoit ses états. Une mesure absente affiche « — », un relais arrêté est nommé et une perte de connexion reste explicite. Les anciennes captures sous `design/native-etape-5bis-services/` documentent l'interface historique.

## 14. Déploiement Windows de l'étape 6

La préversion portable conserve la DA et les vues approuvées. Le symbole de l'EXE et de la barre de titre reprend l'ICO multitailles original ; aucun bloc de marque supplémentaire n'est introduit. Les SVG, fontes et notices, PNG d'icônes et ressource de splash sont fournis dans le dossier de l'application. Le PRI et les XBF de Temhalo font partie de la publication, pour conserver les styles et vues sur une installation déplacée.

Le nom de livraison est `Temhalo-0.6.0-preview.1-win-x64.zip`. Les textes de préparation distinguent téléchargement, vérification, attente OBS, état arrêté et erreur. Les chemins privés et liens de commande ne figurent pas dans le nom de l'archive ni dans son manifeste. Guide courant : `docs/ETAPE-6.md` ; capture réelle du package : `design/native-etape-6/Temhalo-portable-direct.jpg`.

## 15. Touches Stream Deck

Le dessin `design/streamdeck-v3/` est validé et intégré. Pour les commandes preset, ajout, retrait et retour à 0 : fond Ardoise, angle de 8 unités sur une grille 144 × 144, trait de pictogramme de 5 unités. L'horloge, le signe + et le retour au direct utilisent Halo ; le signe − utilise Écho. La valeur blanche et son unité grise partagent la ligne inférieure. Aucun logo ni petit libellé d'action n'est ajouté à la touche.

La variante normale correspond à la maquette à 72 et 144 px. Un contour Vigilance signale les transitions et problèmes de flux. Les états hors ligne et occupé remplacent la valeur par un libellé explicite ; ils ne présentent pas une cible comme une mesure confirmée. Les touches de suivi conservent leur distinction entre mesure et cible. Le panneau de réglages reprend les contrôles compacts de Stream Deck et indique la connexion directe à Temhalo.

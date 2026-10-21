# Checker San

Application native Windows 11, en français, pour comparer **deux dossiers dans les deux sens**, y compris les sous-dossiers et dossiers vides. Le même scan indique si le dossier 1 contient tout le dossier 2, et si le dossier 2 contient tout le dossier 1. Aucun des deux n’est désigné comme copie de référence ou copie à conserver. Tous les types de fichiers sont pris en charge, sans filtre d’extension. Un second mode recherche les **doublons de contenu dans un seul dossier**, sous-dossiers inclus, puis permet de retirer les copies sélectionnées. L’interface utilise des fonds noirs et gris neutres, avec des boutons rose doux.

## Démarrer

Si vous venez de cloner ce dépôt, compilez d’abord avec `./build.ps1 -Test` sous Windows, avec le SDK .NET 10 installé (voir « Sources et compilation »). Les exécutables sont générés dans `dist` et ne sont pas versionnés dans le dépôt.

Ouvrez **`dist/CheckerSan/CheckerSan.exe`**, ou double-cliquez sur **`Lancer Checker San.cmd`** à la racine du projet. L’archive prête à partager est **`dist/CheckerSan-Windows-x64.zip`**. L’exécutable Windows x64 est autonome : aucune installation de Python ou .NET n’est nécessaire. Vous pouvez déplacer ce fichier dans un autre emplacement. Le premier démarrage peut être un peu plus long, le temps d’extraire ses composants dans le répertoire temporaire Windows.

1. Choisissez les **dossiers 1 et 2**, dans l’ordre de votre choix. Les chemins UNC réseau sont acceptés s’ils sont accessibles.
2. Laissez « Retrouver aussi les fichiers déplacés ou renommés » coché pour rechercher les contenus à d’autres emplacements.
3. Cliquez sur **Comparer** et attendez la fin. Dans **Options du scan**, les vérifications précédentes sont réutilisées par défaut après revalidation des fichiers ; désactivez cette option pour recalculer les empreintes. **Reprendre** restaure les dossiers de la session enregistrée.
4. Consultez les **deux verdicts d’inclusion**, filtrez **Tous les écarts**, puis sélectionnez une ligne pour voir les détails et ouvrir son emplacement dans l’Explorateur.
5. Exportez au besoin un rapport CSV pour Excel ou JSON. L’export contient **toutes les lignes**, indépendamment du filtre affiché, les racines, les verdicts, les dates, les empreintes calculées et les erreurs. Enregistrez-le dans un **nouveau fichier, en dehors des dossiers comparés**.

Les chemins peuvent être saisis, collés ou déposés sur les champs. La recherche filtre les noms, chemins et détails. Le bouton ⇄ inverse les dossiers. **Arrêter et garder** interrompt la lecture et conserve les vérifications déjà terminées pour une reprise ; le résultat arrêté reste explicitement incomplet.

## Dossiers sur un autre ordinateur

Vous pouvez comparer un dossier local avec un dossier réseau, ou deux dossiers réseau. Collez un chemin UNC complet comme `\\MON-PC\Partage\Dossier`, `\\192.168.1.20\Partage\Dossier`, ou un chemin de lecteur réseau comme `Z:\Dossier`. Le bouton **Parcourir…** permet aussi de naviguer vers un partage.

Le chemin doit être accessible dans l’Explorateur Windows avec votre session habituelle. Si Windows demande des identifiants, établissez d’abord cette connexion dans l’Explorateur ; Checker San utilise ces accès et ne conserve aucun mot de passe. Il n’est pas nécessaire de lancer l’application en administrateur.

Les fichiers sont lus directement via le réseau, sans les recopier localement. La vérification complète doit transférer les octets à lire : son débit dépend donc du réseau. En cas d’erreur réseau reconnue comme temporaire, Checker San réessaie automatiquement **trois fois**, avec des attentes de **1, 2 puis 4 secondes**. Les délais de réponse de Windows s’ajoutent à ces attentes. Une erreur persistante interrompt le scan avec un résultat incomplet et conserve les vérifications terminées. Un accès refusé ou un fichier verrouillé est signalé sans ces tentatives réseau.

L’arrêt peut attendre la fin du délai de réponse réseau de Windows ; l’interface reste accessible pendant le scan. Après une coupure, rétablissez la connexion puis utilisez **Reprendre**.

La prise en charge utilise les accès fichiers Windows standard (UNC/SMB et lecteurs mappés). Le partage de l’autre ordinateur n’étant pas encore accessible lors de la livraison, la connexion réelle reste à valider lorsqu’il sera disponible.

## Arrêter et reprendre

Checker San enregistre localement les empreintes des **fichiers dont la lecture complète a abouti**, ainsi que les deux dossiers et l’option de recherche des déplacements. Le fichier de reprise est **`%LOCALAPPDATA%\CheckerSan\Sessions\resume.json`**. Il contient des chemins, des métadonnées et des empreintes ; il ne contient pas de copie des fichiers comparés.

- **Arrêter et garder** arrête la comparaison et sauvegarde l’avancement terminé. Fermer la fenêtre pendant un scan demande aussi cet arrêt avant la fermeture.
- **Reprendre** recharge les dossiers et l’option de la session enregistrée, refait l’inventaire, puis reprend la vérification. La session reste disponible après redémarrage de l’application, y compris après une comparaison terminée.
- **Comparer** analyse les dossiers affichés et peut réutiliser les empreintes valides de leur dernière session grâce à **Réutiliser les vérifications précédentes**, coché par défaut dans **Options du scan**. Désactivez cette option pour recalculer tous les SHA-256 nécessaires. Une session d’autres dossiers ou d’autres options de recherche des déplacements est ignorée.

Avant de réutiliser une empreinte, le moteur vérifie le fichier ouvert : taille, dates, attributs, identité du fichier et du volume, et date de changement fournie par Windows. Si ces informations ont changé ou ne sont pas disponibles, **le fichier est relu entièrement**. Le résultat indique le nombre d’empreintes réutilisées. Les inventaires et le contrôle final sont toujours refaits : une reprise ne réutilise pas simplement l’ancien verdict.

La reprise se fait **fichier par fichier**. Un fichier interrompu avant la fin de son calcul SHA-256 est relu depuis le début ; les fichiers terminés et toujours valides peuvent être réutilisés. Cela évite de relire toute la collection après une interruption, sans promettre une reprise au milieu d’un fichier.

Le fichier de reprise doit rester en dehors des deux dossiers comparés, y compris à travers leurs liens et jonctions. Un emplacement situé dans les dossiers comparés est refusé avant le scan. Si une sauvegarde échoue ensuite, l’application affiche un avertissement : la comparaison peut continuer, mais la sauvegarde de l’avancement n’est pas garantie. Une difficulté de sauvegarde de session ne modifie pas à elle seule le verdict sur les fichiers ; une erreur de lecture ou une comparaison interrompue empêche un verdict positif.

## Compléter un dossier ou les deux

Après une comparaison complète sans erreur, cliquez sur **Compléter les dossiers…**. Trois choix sont proposés :

| Choix | Opération |
|---|---|
| Compléter les deux dossiers (1 ⇄ 2) | Copie vers le 2 les chemins présents seulement dans le 1, et vers le 1 ceux présents seulement dans le 2, en une seule opération. |
| Compléter le dossier 2 (1 → 2) | Ajoute uniquement dans le dossier 2 les éléments qui lui manquent. |
| Compléter le dossier 1 (2 → 1) | Ajoute uniquement dans le dossier 1 les éléments qui lui manquent. |

Si le dossier 1 contient `A.txt` et `B.txt`, et le dossier 2 `A.txt` et `C.txt`, avec un contenu identique pour `A.txt`, l’option dans les deux sens ajoute `B.txt` dans le 2 et `C.txt` dans le 1. Les deux contiennent ensuite `A.txt`, `B.txt` et `C.txt`.

Vérifiez l’aperçu avant de lancer : il affiche les racines, les fichiers, les dossiers et le volume par direction ; chaque ligne du transfert dans les deux sens indique **1 → 2** ou **2 → 1**. Les dossiers vides sont aussi créés. Un fichier dont le contenu a été retrouvé sous un autre nom est **recopié à son chemin manquant** : les deux noms peuvent donc être conservés dans chaque dossier, afin de compléter leurs arborescences.

**Un même chemin avec deux contenus différents n’est jamais remplacé automatiquement.** Les conflits fichier/dossier et les éléments situés sous ces conflits sont exclus du transfert. Le nombre de chemins présentant un contenu différent ou un conflit est signalé dans l’aperçu. Ces écarts restent à résoudre séparément. Une copie terminée signifie que les éléments prévus ont été copiés, pas que les deux dossiers sont forcément identiques.

Chaque fichier est écrit dans un temporaire `.checkersan-….partial`, à côté de sa destination. Son contenu complet est vérifié avec SHA-256 avant un renommage qui refuse de remplacer un fichier existant. Les originaux restent à leur place. Les autorisations et flux NTFS alternatifs ne sont pas copiés.

**Arrêter et garder** interrompt aussi une copie. Le journal **`%LOCALAPPDATA%\CheckerSan\Sessions\copy.json`** conserve la liste, les identités des fichiers **et le sens de chaque transfert**. **Compléter / reprendre…** permet de poursuivre après une coupure ou un redémarrage, y compris au milieu du second sens : la partie déjà transférée est relue et comparée à la source, puis le transfert continue à la suite. Les fichiers terminés sont contrôlés avant d’être ignorés. Les anciens journaux de copie dans un seul sens restent compatibles. Les tentatives réseau suivent la même limite de trois essais. Si le système ne fournit pas l’identité nécessaire au fichier temporaire, ou si son contenu a changé, la reprise de ce fichier est refusée pour préserver les données.

Ne renommez pas les temporaires pendant une copie interrompue. Une nouvelle copie peut remplacer le journal précédent : privilégiez la reprise de la copie en cours. Si vous abandonnez volontairement un transfert, ses `.partial` peuvent rester dans la destination ; les originaux ne sont pas supprimés. Après une copie, relancez une comparaison pour actualiser le verdict entre les dossiers.

Cette opération ajoute les éléments manquants à la demande. Elle ne surveille pas les dossiers en permanence et ne propage aucune suppression.

## Comprendre le résultat

Deux réponses sont toujours affichées côte à côte : **« Le dossier 1 contient tout le dossier 2 »** et **« Le dossier 2 contient tout le dossier 1 »**. « Oui » exige les mêmes chemins relatifs, les mêmes contenus et tous les dossiers, même vides. « Non » signifie que cette inclusion n’est pas satisfaite. « Non vérifié » signifie que la comparaison n’a pas encore abouti ou reste incomplète ; ce n’est pas une preuve d’absence. Les rapports CSV/JSON contiennent les deux directions.

| Résultat | Signification |
|---|---|
| Les deux dossiers sont identiques | Mêmes fichiers, mêmes chemins relatifs et mêmes dossiers, contenus confirmés par SHA-256. |
| Le dossier 2 contient tout le dossier 1 | Chaque élément du dossier 1 est identique au même chemin dans le dossier 2. Le dossier 2 possède des éléments supplémentaires. |
| Le dossier 1 contient tout le dossier 2 | Chaque élément du dossier 2 est identique au même chemin dans le dossier 1. Le dossier 1 possède des éléments supplémentaires. |
| Aucun dossier ne contient entièrement l’autre | Des chemins manquent ou des contenus diffèrent. Les deux réponses sont « Non ». |
| Vérification incomplète | Annulation, erreur, élément ignoré ou modification pendant le scan : aucun verdict positif. |

**Retrouvé ailleurs** indique un contenu du dossier 1 retrouvé sous un autre nom ou dans un autre sous-dossier du dossier 2. La ligne correspondant à son chemin réel dans le dossier 2 reste également affichée comme « Absent du dossier 1 » : ce chemin précis n’existe pas dans le dossier 1. Un seul fichier peut correspondre à plusieurs doublons ; il ne suffit jamais à rendre les arborescences identiques ni à prouver une inclusion stricte. Une copie retrouvée ailleurs ne masque pas un conflit au chemin d’origine. Cette recherche complémentaire n’altère pas les deux verdicts d’inclusion.

Un **dossier commun** signifie que ce dossier existe des deux côtés. Le verdict global prend aussi en compte tous ses descendants.

## Méthode et performances

Le temps du premier scan dépend surtout des octets à lire et du débit des disques ou du réseau. Pour confirmer deux grandes copies identiques sans empreintes réutilisables, **il faut toujours lire intégralement les fichiers candidats des deux côtés**. Le mode rapide ne remplace pas cette preuve par les noms, tailles ou dates.

Dans **Options du scan**, deux optimisations sont activées par défaut :

- **Réutiliser les vérifications précédentes** évite de relire les fichiers déjà vérifiés lorsque leur identité Windows, leur date de changement et leurs métadonnées sont restées valides. L’inventaire et le contrôle final sont refaits. Pour imposer de nouveaux calculs SHA-256, décochez cette option.
- **Présélection rapide des gros fichiers** lit trois blocs de 64 Kio, au début, au milieu et à la fin des candidats d’au moins 4 Mio. Si ces blocs diffèrent, leurs contenus sont différents et la lecture complète peut être évitée. **Des blocs égaux ne prouvent jamais l’égalité** : les SHA-256 complets restent nécessaires. Cette option aide surtout lorsque beaucoup de gros fichiers de même taille ont des contenus différents. Sur des copies identiques lues pour la première fois, elle ajoute un petit surcroît de lectures ; vous pouvez la désactiver.

Le moteur applique aussi ces réductions de travail :

- Inventaire de tous les éléments, y compris les fichiers cachés et les dossiers vides, sans filtre d’extension.
- Réutilisation des métadonnées fournies lors de l’énumération des fichiers, pour réduire les requêtes réseau individuelles. Les dossiers et les fichiers candidats restent recontrôlés.
- Comparaison des chemins relatifs et des types. Des tailles différentes prouvent déjà que les contenus diffèrent.
- Recherche des déplacements indexée par taille, échantillons et empreintes complètes, pour éviter de parcourir les mêmes candidats à répétition.
- SHA-256 complet par blocs d’environ 1 Mio, avec une lecture séquentielle d’un fichier à la fois. La mémoire utilisée ne dépend pas de la taille du fichier et une empreinte terminée est réutilisée dans le même scan.

Le volume lu affiché inclut les échantillons et les lectures SHA-256. La barre d’avancement du hachage se fonde sur un **maximum de lecture prévu** : certains candidats peuvent finalement être écartés. Une tentative réseau interrompue peut nécessiter de relire des octets. Désactiver **Retrouver aussi les fichiers déplacés ou renommés** réduit le travail lorsque seuls les chemins identiques vous intéressent.

## Rechercher et retirer les doublons d’un dossier

Cliquez sur **Doublons dans un dossier…** en haut de la fenêtre. Choisissez le dossier local ou réseau à analyser, puis lancez la recherche. Elle inclut ses sous-dossiers et tous les types de fichiers. Les résultats regroupent des **contenus strictement identiques confirmés par SHA-256 complet**, même avec des noms différents ; ce n’est pas une recherche de photos, sons ou vidéos simplement similaires.

Pour limiter les lectures, seuls les fichiers partageant une même taille deviennent candidats. Des échantillons permettent d’écarter des contenus différents avant les SHA-256 complets. Les liens physiques qui désignent un même fichier sur le disque ne sont pas comptés comme plusieurs copies indépendantes lorsque leur identité Windows est disponible.

La recherche dispose de son propre journal, **`%LOCALAPPDATA%\CheckerSan\Sessions\duplicates.json`**, distinct de la comparaison à deux dossiers. Les résultats de lecture réutilisables sont contrôlés avec l’identité et les métadonnées du fichier ouvert. Le journal permet de poursuivre après un arrêt ou une fermeture, avec un nouvel inventaire ; une lecture interrompue repart au début du fichier. Les tentatives réseau sont les mêmes que pour la comparaison : trois essais avec des attentes de 1, 2 puis 4 secondes. Le journal doit rester hors du dossier analysé.

**Aucun fichier n’est présélectionné pour être retiré.** Cochez les copies souhaitées, ou utilisez le bouton explicite qui sélectionne les copies supplémentaires en conservant un fichier par groupe. Vérifiez les chemins et modifiez cette sélection selon les emplacements que vous souhaitez garder. Il faut conserver au moins une copie de chaque groupe.

Deux actions distinctes sont proposées après aperçu de la sélection :

| Action | Conséquence |
|---|---|
| Mettre à l’écart | Déplace les fichiers sélectionnés dans `.CheckerSan-Quarantine/<identifiant de session>/<chemin relatif>`, à l’intérieur du dossier analysé. Cette action ne libère **aucun espace disque**. Les fichiers restent récupérables manuellement. |
| Supprimer définitivement | Retire les fichiers sélectionnés après une confirmation spécifique. Ils ne passent pas par la Corbeille Windows. |

Avant l’action, Checker San revalide les chemins, les identités et les **SHA-256 complets des fichiers sélectionnés et de la copie conservée**. La copie conservée reste ouverte sans partage d’écriture ni de suppression pendant le traitement du groupe. Un résultat incomplet, une sélection de toutes les copies d’un groupe ou un fichier modifié bloque l’opération concernée. Si le système de fichiers ne fournit pas les identités nécessaires, l’application refuse le retrait plutôt que de se fier aux seuls noms et dates.

Pour restaurer une mise à l’écart, ouvrez le dossier de quarantaine et le sous-dossier de session indiqué, puis remettez les fichiers à leurs chemins relatifs d’origine avec l’Explorateur. Si un fichier existe déjà à cet emplacement, vérifiez les deux contenus avant de choisir quoi conserver. Il n’y a pas encore de bouton de restauration intégré.

Le dossier de quarantaine est reconnu grâce à son marqueur `checker-san-quarantine.json`. Seule une quarantaine reconnue à la racine du dossier analysé est exclue des prochains **scans de doublons** ; un dossier portant seulement le même nom n’est pas ignoré. La comparaison entre deux dossiers continue de voir cette arborescence. Ne retirez pas son marqueur pour un usage normal de la quarantaine.

Après une mise à l’écart, une suppression, une erreur ou une interruption pendant le retrait, relancez la recherche pour actualiser les groupes. Les actions déjà effectuées ne sont pas annulées par un arrêt. Le volume des copies supplémentaires est une somme des tailles de fichiers ; l’espace réellement rendu au disque après suppression peut dépendre du système de fichiers.

## Portée de la vérification

La comparaison seule ne modifie aucun fichier des dossiers comparés. L’option de copie écrit uniquement les éléments manquants après validation de son récapitulatif ; elle ne supprime jamais les originaux et n’écrase pas les destinations existantes. Le **mode Doublons** ajoute une exception volontaire : seules les copies sélectionnées peuvent être mises à l’écart ou supprimées après aperçu et confirmation. Les fichiers ne sont pas envoyés à un service Internet ; les transferts réseau vont uniquement au dossier choisi.

La comparaison couvre le **flux principal des fichiers et leur arborescence**, quel que soit leur format : documents, archives, images ou autres fichiers. Les métadonnées intégrées au fichier font partie du contenu et sont donc comparées. Elle ne vérifie pas les autorisations NTFS, les flux alternatifs NTFS, les attributs de sauvegarde ou l’égalité des dates entre les deux copies.

Les liens symboliques, jonctions et autres points de réanalyse, y compris certains fichiers cloud, sont signalés comme non vérifiés et ne sont pas traversés. Les dossiers racines identiques ou imbriqués sont refusés, même à travers un alias de jonction. Les chemins ne différant que par la casse sont traités selon le comportement Windows usuel ; une collision ambiguë dans un dossier est signalée.

Les fichiers vérifiés sont ouverts sans partage d’écriture pendant la lecture. Un second inventaire contrôle les ajouts, suppressions et changements de métadonnées. Cela ne constitue pas un instantané VSS : **évitez de modifier les dossiers pendant le scan**. Le résultat décrit l’état contrôlé à la fin du scan, pas des changements ultérieurs. Relancez une comparaison après toute modification avant de retirer une copie. Les empreintes SHA-256 apportent une assurance pratique extrêmement forte de l’identité du contenu, sans constituer une comparaison octet par octet indépendante du hachage.

## Sources et compilation

L’application utilise WPF et [.NET 10 fourni par Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Elle ne dépend d’aucune bibliothèque applicative tierce.

- `FolderChecker.Core` : inventaire, SHA-256, présélection, contrôle des mutations, reprise, tentatives réseau, verdicts, recherche et retrait contrôlé des doublons.
- `FolderChecker.App` : interface WPF, ouverture des emplacements et rapports.
- `FolderChecker.Tests` : tests du moteur sur des arborescences temporaires isolées.
- `FolderChecker.App.Tests` : tests de rapports, protections d’export et invalidation des résultats après modification des chemins.

Avec le SDK .NET 10 installé, ou le SDK local dans `.tools/dotnet`, exécutez `./build.ps1 -Test` depuis PowerShell. Le script compile, teste et publie l’application et son archive ZIP dans `dist`. Les seuls téléchargements éventuels sont les composants d’exécution officiels .NET pour le paquet autonome.

Lancement facultatif avec chemins préremplis :

```powershell
.\dist\CheckerSan\CheckerSan.exe --source "D:\Dossier 1" --target "E:\Dossier 2"
```

Ajoutez `--compare` pour lancer directement le scan.

## Publication du projet

Le code source est hébergé dans [therealshulgin/checker-san](https://github.com/therealshulgin/checker-san). Les noms internes des projets et espaces de noms `FolderChecker.*` restent inchangés ; le nom de l’application distribuée est **Checker San** et son exécutable est **`CheckerSan.exe`**.

La compilation et les tests restent locaux ; `build.ps1` génère l’exécutable autonome et l’archive ZIP sans publier sur GitHub. Le dépôt conserve les sources, les tests et la documentation ; les SDK locaux, fichiers temporaires et distributions sont exclus par `.gitignore`.

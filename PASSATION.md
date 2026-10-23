# Checker San — Passation pour la prochaine session

Dernière mise à jour : **21 octobre 2026**. Ce document décrit les sources, la version performance/doublons déjà validée et l’ajout EN/FR en cours de validation. Les résultats historiques ci-dessous ne constituent pas une validation de la nouvelle localisation. Les chemins de code ci-dessous sont relatifs à la racine du projet.

## À lire en premier

- **Application existante et fonctionnelle en local**, avec comparaison, reprise du scan, copie des manquants et reprise de copie. La nouvelle évolution ajoute des optimisations de lecture et un mode de recherche/retrait des doublons dans un dossier. Il ne s’agit pas d’un projet à recommencer.
- Nom public et titre des fenêtres : **Checker San**, sans suffixe. Interface EN/FR en cours d’intégration, fonds noirs et gris neutres, boutons rose doux ; les fonds rose/violet ont été retirés. Le slogan sous le nom a été retiré ; les dossiers 1 et 2 ont des libellés neutres, sans copie à conserver prédéfinie. Employer « fichier », jamais un vocabulaire limité aux médias comme « rush ».
- Tous les types de fichiers sont concernés, avec les sous-dossiers et dossiers vides.
- **Publication initiale autorisée explicitement le 21 octobre 2026.** Dépôt : <https://github.com/therealshulgin/checker-san>, compte **therealshulgin**, branche `main`. Le dépôt distant était vide avant cette publication. L’autorisation concerne le code, les tests et les documents de cette version.
- L’identité du commit utilise le nom `therealshulgin` et son adresse GitHub privée fondée sur l’identifiant public du compte. Vérifier l’état et les commits avec `git status` et `git log` lors d’une reprise. Aucune licence n’a été choisie par l’utilisateur : ne pas en inventer.
- Le partage réseau sur l’autre ordinateur de l’utilisateur **n’est pas encore accessible**. Aucun test réel de bout en bout sur ce partage n’a été effectué.
- La comparaison ne modifie pas les dossiers, et la copie des manquants ne supprime ni ne remplace les originaux. **Le nouveau mode Doublons peut retirer les copies explicitement sélectionnées**, après aperçu/confirmation et revalidation du contenu. Ne pas lancer de copie, nettoyage ou suppression sur les vrais dossiers de l’utilisateur dans le cadre d’un simple test.

## Besoin utilisateur et état de livraison

L’utilisateur souhaite savoir si un dossier contient intégralement l’autre avant de se débarrasser d’une copie. Les collections peuvent contenir des centaines de fichiers volumineux, notamment audio/vidéo, mais le fonctionnement doit rester indépendant du format. Un des dossiers peut être sur un autre ordinateur, via UNC ou lecteur réseau mappé.

Demandes implémentées :

1. Inventaire récursif, comparaison des chemins, types, tailles et contenus SHA-256 complets.
2. Liste des fichiers identiques, absents d’un côté, différents, conflits fichier/dossier et fichiers retrouvés ailleurs sous un autre nom ou chemin.
3. Deux verdicts d’inclusion stricts calculés dans le même scan et affichés côte à côte : le dossier 1 contient-il tout le 2, et le dossier 2 contient-il tout le 1 ? Chaque réponse est Oui, Non ou Non vérifié. Le titre général distingue identité, inclusion dans un seul sens, absence d’inclusion et vérification incomplète.
4. Interface WPF noire/grise avec boutons rose doux, recherche, filtres, détails, ouverture des emplacements, inversion des dossiers et export CSV/JSON.
5. Chemins locaux, UNC et lecteurs mappés ; utilisation des accès de la session Windows, sans stockage de mots de passe.
6. Trois nouvelles tentatives automatiques après certaines erreurs réseau temporaires, espacées de 1, 2 puis 4 secondes.
7. Sauvegarde et reprise persistantes des vérifications terminées, y compris après redémarrage de l’application.
8. Copie des éléments manquants vers le dossier 1, vers le dossier 2 ou dans les deux sens en une seule opération, après aperçu avec volumes et destinations, reprise persistante et contrôle d’intégrité.
9. Réutilisation des vérifications précédentes activée par défaut après contrôle des identités et métadonnées ; présélection des gros fichiers par petits blocs pour écarter les contenus différents avant le SHA complet.
10. Recherche récursive des doublons dans un seul dossier, avec cache/reprise séparé, regroupement par contenu SHA-256 et retrait explicite des copies choisies : mise à l’écart réversible manuellement ou suppression définitive.
11. Localisation EN/FR en cours de validation : sélecteur mémorisé, interface et messages traduits, formats de nombres/tailles adaptés et README public succinct en anglais.

## Emplacement et livrables

Les chemins ci-dessous sont relatifs à la racine du checkout local de `checker-san`. Les livrables `dist` sont générés par `build.ps1` et ne sont pas versionnés.

| Élément | Chemin relatif |
|---|---|
| Application Windows x64 autonome | `dist/CheckerSan/CheckerSan.exe` |
| Archive de distribution | `dist/CheckerSan-Windows-x64.zip` |
| Lanceur à double-cliquer | `Lancer Checker San.cmd` |
| Présentation et démarrage en anglais | `README.md` |
| Mode d’emploi inclus dans la distribution | `dist/CheckerSan/Mode-emploi.md` |
| Compilation, tests et paquet | `build.ps1` |
| Présente passation | `PASSATION.md` |

L’exécutable n’exige pas d’installation de Python ou de .NET sur le poste utilisateur. Il est publié en fichier unique avec son environnement .NET. La première ouverture peut extraire des composants dans le répertoire temporaire Windows. La version déclarée du projet est `1.0.0`.

SHA-256 historique de l’exécutable livré le **21 octobre 2026**, avec optimisations et mode Doublons, **avant la localisation EN/FR** :

```text
F93E6DCEE699D3475869FC3B7A64B2A9EB337476E61BB679C0F1D4A46FCE6FDE
```

Cet exécutable et son archive ont été reconstruits après validation des tests et du rendu WPF de la version précédente. Ne pas attribuer cette empreinte à la version EN/FR ; elle devra être remplacée après sa publication locale vérifiée. Le ZIP contient uniquement `CheckerSan.exe` et `Mode-emploi.md` ; un fichier `CheckerSan.rar` déjà présent dans le répertoire de distribution a été laissé intact et exclu du paquet. Le script utilise désormais une liste explicite des fichiers à empaqueter. Les anciens livrables nommés `FolderCheck` ont été supprimés de `dist` lors d’une session précédente.

## Organisation du code

Ajustement visuel précédant la localisation : badge « Local & réseau » supprimé de l’en-tête à la demande de l’utilisateur. Le bouton « Doublons dans un dossier… » reste aligné à droite. Compilation et archive reconstruites avec succès ; pas de nouvelle batterie de tests pour cette suppression de texte décoratif. Les résultats de tests ci-dessous précèdent uniquement cet ajustement.

Application C# / **WPF .NET 10**, sans bibliothèque applicative tierce. Les noms internes `FolderChecker.*` sont conservés ; le nom distribué et les textes utilisateur sont **Checker San**.

| Fichier ou projet | Rôle |
|---|---|
| `FolderChecker.Core/L.cs` | Langue partagée fr/en, choix des textes et formatage localisé ; événement Changed. |
| `FolderChecker.App/AppLanguage.cs` | Ressources WPF, culture et préférence de langue persistante, initialisée au démarrage réel. |
| `FolderChecker.App/Strings.Main.cs` et `Strings.Duplicates.cs` | Catalogues de paires FR/EN pour les ressources dynamiques Main_* et Dup_*. |
| `FolderChecker.Core/Models.cs` | Options, lignes, états, progression et verdicts de comparaison. |
| `FolderChecker.Core/ComparisonEngine.cs` | Inventaire, présélection des gros fichiers, SHA-256, recherche indexée des déplacements, contrôle final, reprise et verdicts. |
| `FolderChecker.Core/CheckpointData.cs` | Lecture et format du journal de scan, schéma JSON 1. |
| `FolderChecker.Core/FileSystemRetryPolicy.cs` | Détection des erreurs Windows réseau temporaires, tentatives bornées et annulation des attentes. |
| `FolderChecker.Core/FileSafety.cs` | Chemins canoniques, appartenance à une racine, identités natives Windows et date de changement. |
| `FolderChecker.Core/DuplicateModels.cs` | Options, progression, fichiers/groupes et résultats du scan de doublons. |
| `FolderChecker.Core/DuplicateEngine.cs` | Inventaire d’un dossier, filtrage taille/échantillons, SHA-256, liens physiques, contrôle final et journal séparé. |
| `FolderChecker.Core/DuplicateRemoval.cs` | Validation de la sélection, protection de la copie conservée, quarantaine et suppression par handles Windows. |
| `FolderChecker.Core/DuplicateSelection.cs` | Règles de choix de la copie conservée dans chaque groupe (`KeepRule`). |
| `FolderChecker.Core/Csv.cs` | Lignes CSV `;` avec neutralisation des formules, partagées par rapports, exports et journal. |
| `FolderChecker.App/QuarantineRestoreDialog.cs` et `DuplicateReportWriter.cs` | Choix d’une mise à l’écart à restaurer ; export CSV/JSON de la liste des doublons. |
| `FolderChecker.Core/CopyEngine.cs` | Plans de copie unidirectionnels/bidirectionnels, journal incrémental (plan schéma 2 compatible schéma 1, puis états ajoutés), temporaires, reprise, SHA-256 et validation sans écrasement. |
| `FolderChecker.App/App.xaml` | Styles WPF : fonds noirs/gris neutres, contrôles, tableaux, menus et boutons roses. |
| `FolderChecker.App/MainWindow.xaml` | Structure de la fenêtre principale. |
| `FolderChecker.App/MainWindow.xaml.cs` | Lancement/arrêt/reprise du scan, résultats, sélection, export et aide. |
| `FolderChecker.App/MainWindow.Copy.cs` | Intégration de la copie, reprise, progression et invalidation du verdict précédent. |
| `FolderChecker.App/CopyDialog.cs` | Aperçu de la copie, choix du sens ou de la reprise, lancement explicite. |
| `FolderChecker.App/DuplicatesWindow.xaml` et `.xaml.cs` | Fenêtre doublons, choix du dossier, scan/reprise, sélection volontaire, retrait et invalidation des résultats. |
| `FolderChecker.App/DuplicateRowView.cs` | Cases à cocher et état conservé/à retirer de chaque copie. |
| `FolderChecker.App/DuplicateRemovalDialog.cs` | Aperçu des chemins à retirer/conserver et consentement explicite à la suppression définitive. |
| `FolderChecker.App/RowView.cs` | États/types localisés, tailles selon la culture et couleurs. |
| `FolderChecker.App/ReportWriter.cs` | Export complet CSV/JSON et protections des chemins de sortie. |
| `FolderChecker.App/Assets/CheckerSan.ico` | Icône distribuée. |
| `tools/generate_icon.py` | Génération de l’icône ; Pillow nécessaire uniquement pour la régénérer. |
| `FolderChecker.Tests` | Tests du moteur, de la reprise, de la copie, des préfiltrages (`PerfTests.cs`) et du mode Doublons. |
| `FolderChecker.App.Tests` | Programme de tests des rapports et de l’invalidation des résultats dans WPF. |

Le manifeste Windows active notamment les chemins longs et le DPI par moniteur. L’application s’exécute avec les droits ordinaires de l’utilisateur. La fenêtre et le dialogue de copie ont une barre de titre sombre.

## Localisation EN/FR — intégration en cours

L’utilisateur a demandé un sélecteur français/anglais, un README succinct en anglais et la publication de cette évolution. Cette section décrit les sources et le contrat d’intégration ; elle ne renseigne aucun nouveau résultat de tests, de compilation ou de push.

- `FolderChecker.Core/L.cs` conserve `Language` (`fr` ou `en`), `IsEnglish`, `Culture` (`fr-FR`/`en-US`) et l’événement `Changed`. `T(string french, string english)` choisit un texte ; `F(FormattableString french, FormattableString english)` formate dans la culture choisie. `SetLanguage` valide les deux codes et déclenche l’événement uniquement si la langue change. Le défaut du moteur reste français pour préserver les tests existants.
- `AppLanguage.Initialize(string? settingsPath=null, CultureInfo? systemCulture=null)` est appelé uniquement par `App.OnStartup`. Il charge `%LOCALAPPDATA%\CheckerSan\settings.json` (distinct des journaux), limité à 4 Kio, avec `Version=1` et `Language=fr/en`. Un fichier absent, incorrect, inaccessible ou une langue inconnue entraîne un choix français si la culture Windows commence par `fr`, anglais sinon.
- `AppLanguage.Apply(string language, bool persist=true, string? settingsPath=null)` met à jour la culture courante et celle des futurs threads, remplit les ressources puis appelle `L.SetLanguage`. Les abonnés à `Changed` voient donc les ressources et formats actualisés. La sauvegarde utilise un temporaire dans le même dossier, `Flush(true)` et un remplacement atomique. L’échec de sauvegarde ne bloque pas le changement ; `LastSaveError` conserve le message.
- `AppLanguage.InitializeResources(ResourceDictionary? resources=null)` remplit les ressources de la langue courante sans accès disque. Les tests ne doivent pas déclencher `OnStartup` ni lire les vraies préférences : injecter chemin/culture dans `Initialize`, utiliser `Apply(..., persist:false)` et initialiser les ressources explicitement après `Application.InitializeComponent`.
- `Strings.Main.cs` et `Strings.Duplicates.cs` exposent les catalogues `IReadOnlyDictionary<string,(string Fr,string En)>`. Les libellés XAML passent par `{DynamicResource Main_*}` ou `{DynamicResource Dup_*}`. Le nom du produit reste **Checker San**, les choix restent **English** et **Français**.
- `MainWindow.xaml` ajoute `LanguageBox`, `SelectedValuePath="Tag"`, avec `en` et `fr`, et `SelectionChanged="Language_Changed"`. Le code principal doit appliquer/synchroniser le choix et rafraîchir les textes générés en C#. Le sélecteur est à désactiver pendant une opération pour ne pas mélanger les langues dans le même scan ; les dialogues modaux bloquent déjà la fenêtre propriétaire.
- `RowView` et les écrans/dialogues de doublons/copie traduisent états, actions, prévisualisations et détails générés. Les tailles utilisent `B/KiB/MiB/...` en anglais, `o/Kio/Mio/...` en français. Les textes d’aide, progression et erreurs applicatives passent par `L.T`/`L.F`. Les messages natifs de Windows peuvent rester dans la langue du système.
- **Les diagnostics déjà produits par un scan conservent leur langue d’origine** : `ComparisonRow.Detail`, erreurs et avertissements sont des chaînes matérialisées. Changer la langue actualise les libellés de l’interface, mais ne réécrit pas rétroactivement ces preuves. Un nouveau scan produit de nouveaux diagnostics dans la langue choisie.
- `ReportWriter` localise les titres/états/verdicts CSV et les descriptions Algorithm/Scope du JSON. Les noms de champs JSON, enums, empreintes, chemins, tailles brutes et formats des journaux restent inchangés. Les diagnostics originaux sont conservés. Le CSV garde ses 19 colonnes, son BOM UTF-8 et ses protections contre les formules.
- `README.md` est une présentation anglaise très succincte (environ 140 mots, à la demande de l’utilisateur le 22 octobre 2026) : fonctions, construction, limites et choix de langue. Les détails d’usage restent dans l’aide de l’application. `build.ps1` doit en recopier la version courante dans la distribution lors de la publication locale.

**À compléter après intégration seulement :** résultats réels des tests EN/FR et des suites existantes, vérification des changements de langue sans scan/suppression accidentels, sérialisation des préférences dans des fixtures, rendu WPF des deux langues, nouveau SHA-256 de l’exécutable, distribution et statut réel du push demandé. Les résultats « 135 tests » et l’empreinte ci-dessus concernent la version précédente.

## Garanties à conserver lors des modifications

### Comparaison et verdicts

- Les tailles et les échantillons permettent uniquement d’écarter certains candidats différents. **Seul un SHA-256 de tous les octets**, fraîchement lu ou réutilisé après contrôle strict, permet de confirmer l’égalité. Des noms, tailles, dates ou échantillons égaux ne suffisent jamais.
- Lecture séquentielle par blocs d’environ 1 Mio, un fichier à la fois, adaptée notamment aux disques mécaniques et au réseau.
- `SourceFullyContained` signifie dossier 1 entièrement contenu dans le 2 ; `TargetFullyContained` signifie dossier 2 entièrement contenu dans le 1. Chaque direction exige les mêmes chemins relatifs, contenus et dossiers, même vides. `TreesIdentical` est la conjonction des deux. Les inclusions sont calculées sur les clés des inventaires : ne pas utiliser naïvement `TargetPath`, que les lignes `Moved` peuvent faire pointer vers un autre chemin.
- `ContentCovered` reste une information complémentaire du moteur pour les contenus du dossier 1 retrouvés dans le 2 ; elle ignore chemins, dossiers et multiplicité des doublons. Elle n’est plus un titre de verdict dans l’interface et ne doit jamais remplacer les deux inclusions strictes. La recherche complémentaire de déplacements reste orientée 1 vers 2 ; elle ne conditionne pas le calcul des inclusions.
- Un contenu retrouvé ailleurs ne masque pas un conflit au chemin d’origine. Sa ligne cible reste visible avec le libellé « Absent du dossier 1 », qui concerne ce chemin relatif précis.
- Racines identiques ou imbriquées refusées, avec contrôle des chemins canoniques pour les alias de jonction.
- Collisions ambiguës de casse signalées.
- **Liens et fichiers cloud (22 octobre 2026).** `FileSafety.Access` classe chaque entrée. Les liens symboliques, jonctions et points de montage (balise de réanalyse « name surrogate », lue avec `FindFirstFileExW` sans ouvrir l’entrée) ne sont jamais suivis. Ils produisent une ligne `ComparisonStatus.Skipped`, de même que les chemins situés sous un lien de l’autre côté. Ce ne sont pas des erreurs : un lien est exclu de l’inclusion de son propre dossier, mais un élément réel face à un lien empêche l’inclusion. Rien n’est jamais copié à travers un lien (`Skipped` bloque les descendants du plan). Les fichiers cloud locaux (OneDrive hydraté), dédupliqués ou compressés sont lus normalement.
- Les fichiers disponibles uniquement en ligne (`RECALL_ON_DATA_ACCESS`, `RECALL_ON_OPEN` ou `OFFLINE`) ne sont jamais lus, car la lecture les téléchargerait. À taille égale, la ligne reste `Unverified` avec `ComparisonRow.OnlineOnly` ; à tailles différentes, elle est `Different` ; sans vis-à-vis, `OnlySource`/`OnlyTarget`. Ces lignes ne sont pas des erreurs et n’empêchent pas la copie (`CopyEngine.CanCreatePlan`), mais elles ne sont jamais copiées. Les doublons les excluent avec un avertissement récapitulatif.
- Par défaut, Windows peut **déguiser** les fichiers cloud en fichiers ordinaires pour un processus .NET (mode 1 constaté le 22 octobre). L’ancienne version lisait donc et téléchargeait les fichiers en ligne sans le signaler. Le constructeur statique de `FileSafety`, déclenché par celui de chaque moteur, appelle `RtlSetProcessPlaceholderCompatibilityMode(PHCM_EXPOSE_PLACEHOLDERS)` pour tout le processus. Vérifié en lecture seule sur le vrai OneDrive de l’utilisateur : 0 erreur, aucun octet lu, et les 20 fichiers en ligne sont restés en ligne.
- Fichiers ouverts sans partage d’écriture pendant leur vérification ; contrôles de métadonnées et nouvel inventaire final. Une annulation, erreur de lecture ou mutation empêche un verdict positif.
- Modifier les chemins, inverser les dossiers ou commencer une opération invalide les deux verdicts affichés. `ClearContainmentVerdicts` est appelé lors des changements de chemins et par `SetBusy(true)` ; `ShowContainmentVerdicts` refuse Oui/Non en cas d’annulation, erreur, contrôle final incomplet ou ligne `Unverified`. Le filtre « Tous les écarts » inclut les manquants de chaque côté.

### Reprise du scan

Journal par défaut :

```text
%LOCALAPPDATA%\CheckerSan\Sessions\resume.json
```

Seules les empreintes SHA-256 de fichiers complètement lus sont sauvegardées dans ce journal de comparaison. La sauvegarde est temporisée pendant le scan, puis forcée à sa fin ou à son interruption. `Reprendre` refait les inventaires et revalide les fichiers avant toute réutilisation. Le bouton principal est désormais **Comparer** : il utilise le cache si **Réutiliser les vérifications précédentes** (`ReuseHashesCheck`) est coché, ce qui est le réglage par défaut. Décocher cette option impose le recalcul des SHA nécessaires. Le bouton **Reprendre** reste une demande explicite de réutilisation et restaure les racines enregistrées.

La réutilisation exige notamment taille, dates, attributs, identité du volume/fichier et `ChangeTime` obtenus sur le fichier ouvert via les API Windows. Une modification avec restauration de la date d’écriture est ainsi détectée. Si l’identité nécessaire n’est pas disponible, le fichier est relu.

**Reprise fichier par fichier :** le fichier dont le hachage était interrompu repart du début. Les fichiers déjà terminés et toujours valides peuvent être réutilisés. Ne pas promettre une reprise du hachage au milieu d’un fichier.

Les journaux doivent rester hors des arbres comparés, y compris via une jonction. Un journal incorrect ou d’autres racines ne doit pas apporter de preuve d’identité. Un échec de sauvegarde de session est signalé ; il ne transforme pas un fichier non vérifié en fichier identique.

### Optimisations de la comparaison

- `ComparisonOptions.UseSamplePrefilter` vaut `true` par défaut ; `SampleCheck` permet de désactiver l’option dans l’interface.
- Pour les fichiers d’au moins **4 Mio**, la présélection lit trois blocs de **64 Kio** (début/milieu/fin), soit 192 Kio par fichier. Une différence de signatures démontre une différence de contenu ; des signatures égales entraînent le SHA-256 intégral.
- Les hashes complets du journal ont priorité : la reprise de fichiers valides ne doit pas introduire des lectures d’échantillons inutiles.
- La recherche des fichiers déplacés utilise des groupes par taille/signature, puis un index SHA-256 et un curseur de candidats. Ne pas réintroduire un parcours de toutes les cibles pour chaque source ni un état mutable partagé entre tâches concurrentes.
- Contrat des compteurs conservé : `ComparisonResult.BytesRead` / `ComparisonProgress.BytesHashed` comptent les lectures pour SHA complets. `SampleBytesRead` compte **en plus** les échantillons. L’interface additionne les deux pour son volume réel lu et le débit ; le JSON expose les deux champs. Une différence détectée par échantillons peut donc avoir des hashes complets vides et `FilesVerified=0` sans être une erreur.
- Les échantillons de comparaison restent en mémoire pour le scan courant et ne constituent jamais un hash complet réutilisable. Le verrouillage en lecture, les métadonnées et l’identité sont contrôlés autour de leur lecture ; le contrôle final reste actif.
- Ne pas promettre d’accélération générale sur le premier scan de deux copies identiques, surtout via réseau : le SHA complet exige toujours leur lecture. Les gains concernent les fichiers inchangés déjà vérifiés et les gros candidats différents. La présélection ajoute même un petit surcoût lorsque tous les candidats sont identiques.

### Doublons dans un seul dossier

Le bouton **Doublons dans un dossier…** ouvre une recherche indépendante de la comparaison 1/2. Elle recherche les fichiers strictement identiques par contenu, tous formats et sous-dossiers inclus, pas les médias visuellement ou acoustiquement similaires.

- Journal distinct : `%LOCALAPPDATA%\CheckerSan\Sessions\duplicates.json`, hors du dossier analysé. `DuplicateScanOptions.ResumeFromCheckpoint` vaut `true` par défaut. Il peut conserver les étapes de présélection terminées et les hashes complets ; les signatures partielles ne deviennent jamais une preuve de doublon. Les identités natives et métadonnées sont revalidées avant réutilisation.
- Inventaire → groupes de même taille → échantillons → hashes complets des groupes encore candidats → contrôle final. Les fichiers de taille unique n’ont pas besoin de lecture complète. Le compteur `DuplicateResult.BytesRead` inclut les lectures d’échantillons et de SHA.
- Les liens physiques de même identité volume/fichier sont écartés des copies indépendantes. Cela évite de compter plusieurs noms d’un même stockage comme espace récupérable.
- Trois tentatives réseau, annulation et reprise persistante du scan. Une lecture inachevée recommence au début du fichier. Un résultat incomplet ou avec erreurs ne peut pas autoriser le retrait.
- **Filtres (22 octobre 2026).** `DuplicateScanOptions` comporte `MinimumSize`, `ExcludedNames` (motifs `*`/`?` comparés au nom de chaque fichier ou dossier, sans tenir compte de la casse ; un dossier exclu n’est pas parcouru) et `SkipHiddenAndSystem`. Les valeurs par défaut du moteur ne filtrent rien. L’interface propose « ignorer les fichiers vides », `Thumbs.db; desktop.ini; .DS_Store` et l’exclusion des éléments cachés ou système. Les éléments exclus ne sont pas inventoriés, donc leurs changements ne bloquent pas le contrôle final. `DuplicateResult.FilesFiltered` les compte, sauf le contenu des dossiers exclus.
- **Aucune présélection au chargement des résultats.** `DuplicateSelection.Extras` (moteur, testable) applique une règle `KeepRule` : premier chemin alphabétique, plus ancien ou plus récent (date de modification puis de création), chemin le plus court, ou copie d’un dossier choisi. Les égalités sont départagées par le chemin. Un groupe sans copie dans le dossier choisi reste sans sélection et est signalé. La sélection reste modifiable. L’aperçu liste les chemins/volumes, puis l’utilisateur choisit et confirme son action.
- **Corbeille** (`DuplicateRemovalMode.RecycleBin`, mode par défaut si disponible) : uniquement sur un disque local fixe (`GetDriveTypeW` = `DRIVE_FIXED`, jamais un chemin UNC). Ailleurs, l’option est désactivée et la mise à l’écart est choisie. Le fichier vérifié reste ouvert sans écriture possible, mais avec `FILE_SHARE_DELETE`, puis `SHFileOperationW` (`FOF_ALLOWUNDO`, `FOF_WANTNUKEWARNING`) le déplace depuis un thread STA. Le résultat est contrôlé sur ce même handle : chemin final sous `$Recycle.Bin` = recyclé ; suppression en attente = Windows l’a supprimé après sa propre confirmation (trop volumineux, avertissement retourné) ; inchangé = annulation de l’opération. Le test automatisé recycle réellement un fichier de fixture, puis efface ses fichiers `$R`/`$I` pour laisser la Corbeille intacte.
- **Mise à l’écart** : déplacement sur le même volume vers `<racine>/.CheckerSan-Quarantine/<guid>/<chemin relatif>`. Elle ne libère aucun espace. Un manifeste `<guid>.manifest.jsonl`, à côté de la session, reçoit avant chaque déplacement le chemin relatif, le SHA-256 et la taille (avec `Flush(true)`). « Restaurer des fichiers mis à l’écart… » (`QuarantineSessions`, `RestoreAsync`) remet un fichier en place seulement si son contenu correspond au manifeste et si rien n’occupe son chemin, sans aucun écrasement. Une session vidée est supprimée avec son manifeste. Les sessions antérieures au manifeste restent à restaurer manuellement.
- **Journal des actions** : `%LOCALAPPDATA%\CheckerSan\Logs\duplicate-actions.csv`, une ligne CSV par action (date, action, chemin, SHA-256, taille, copie conservée, destination). Il est ouvert avant la première modification : si l’écriture est impossible, rien n’est retiré. Une erreur d’écriture en cours arrête les actions suivantes. Le CSV partage `Core/Csv.cs` avec les rapports. La liste des doublons s’exporte en CSV ou JSON (`DuplicateReportWriter`), jamais dans le dossier analysé ni par-dessus un fichier existant.
- Marqueur de quarantaine `checker-san-quarantine.json`, schéma 1, application `Checker San`, type `DuplicateQuarantine`. Seule une quarantaine reconnue directement sous la racine est exclue du prochain scan **de doublons**. Un homonyme non reconnu reste inventorié et ne peut pas être réutilisé comme quarantaine. La comparaison à deux dossiers n’exclut pas la quarantaine.
- **Suppression définitive** : action séparée et confirmation explicite, sans passage par la Corbeille Windows. N’effectuer cette action que sur la sélection de l’utilisateur ; les essais automatisés restent dans leurs propres fixtures.
- `DuplicateRemoval` exige un scan complet, revalide le chemin canonique, la sélection, les identités et le SHA-256 des fichiers sélectionnés et de la copie conservée. Le fichier conservé reste ouvert sans partage d’écriture/suppression durant le groupe. Les suppressions/renommages utilisent les handles vérifiés et des parents protégés ; ne pas remplacer cela par une simple suppression de chemins après fermeture des handles.
- La sélection de toutes les copies d’un groupe, les identités absentes, un fichier changé, les chemins hors racine, liens/points de réanalyse et les doublons de sélection sont refusés. Les fichiers d’un groupe sont vérifiés avant sa première modification, mais une opération entière n’est pas une transaction : si elle s’arrête, certaines actions peuvent déjà avoir été effectuées.
- Après toute action, erreur ou interruption de retrait : invalider le résultat et relancer le scan. Ne pas promettre une reprise automatique des suppressions ni un retour arrière global. Le volume affiché correspond aux tailles logiques ; le gain disque effectif peut dépendre des liens extérieurs, fichiers compressés et autres particularités du stockage.

### Copie et reprise du transfert

Journal par défaut :

```text
%LOCALAPPDATA%\CheckerSan\Sessions\copy.json
```

- Préparation d’une nouvelle copie seulement depuis une comparaison complète sans erreur ; aperçu du sens, des chemins, du nombre de fichiers et du volume.
- Sens 1 → 2 : lignes `OnlySource` et `Moved` (leur chemin source manque bien dans le 2). Sens 2 → 1 : lignes `OnlyTarget`. Compléter les deux dossiers copie ces deux ensembles dans une seule opération. Les contenus déjà retrouvés ailleurs sont volontairement recopiés à leurs chemins manquants, ce qui peut conserver plusieurs noms du même contenu. Les éléments différents/conflits et les descendants des conflits de type sont exclus ; ils ne sont pas remplacés.
- `CreatePlan(result, bool)` conserve les plans unidirectionnels avec racines orientées. `CreateBidirectionalPlan(result)` garde les racines 1/2 et active `CopyPlan.Bidirectional`. Chaque `CopyItem.ReverseDirection` indique 2 → 1 (true) ou 1 → 2 (false). Les métadonnées et identités source sont revalidées avant transfert. Les nouveaux plans ont `Schema=2` ; les anciens schéma 1, sans directions inversées, restent lisibles et reprenables. Les combinaisons ambiguës de schéma/direction sont refusées avant écriture.
- Écriture dans `.checkersan-<GUID>.partial`, dans le dossier de destination. La destination finale existante n’est jamais écrasée.
- À la reprise : identification du temporaire, comparaison octet par octet de son préfixe avec la source, puis poursuite à l’offset déjà écrit. Cette vérification nécessite de relire la partie transférée.
- Vérification SHA-256 complète de la copie avant renommage sans écrasement. Le journal permet de traiter une interruption entre validation et finalisation.
- **Journal incrémental (22 octobre 2026).** `copy.json` est un fichier JSON Lines : la première ligne est le plan complet (schéma 1 ou 2, inchangé) ; chaque ligne suivante est le dernier état d’un élément (`Completed`, `VerifiedHash`, `PartialIdentity`, `DestinationIdentity`), identifié par son `PartialName`, désormais unique dans tout le plan. Le plan n’est réécrit (temporaire + `Flush(true)` + remplacement) qu’au démarrage, lorsque les états ajoutés atteignent un quart de sa taille (minimum 1 Mio), et à l’arrêt. Un arrêt normal, une annulation ou une erreur laissent donc une seule ligne, lisible par les versions précédentes. Auparavant, le plan entier était réécrit et forcé sur disque trois fois par fichier, d’où un coût quadratique : 2 000 petits fichiers prenaient 93,8 s, contre 9,0 s maintenant (environ 4,5 ms par fichier, constant jusqu’à 5 000 fichiers). Cette réécriture continuelle provoquait aussi des « Accès refusé » sporadiques, probablement dus à l’antivirus.
- Chaque état ajouté est transmis au cache système, ce qui survit à un plantage de l’application ; il n’est forcé sur disque qu’au plus une fois par seconde. Une coupure de courant peut donc perdre les derniers états : la reprise refuse alors les éléments concernés (destination existante sans empreinte, temporaire non identifié) au lieu de les accepter ou de les écraser. Une nouvelle comparaison les classe ensuite normalement. `ReadPlan` ignore une ligne illisible, tronquée, inconnue ou à l’empreinte invalide ; l’élément garde alors son état précédent. Les données de chaque copie restent forcées sur disque avant le renommage final.
- **Vérification, dates et espace (22 octobre 2026).** La relecture SHA-256 de la copie passe par un handle `FILE_FLAG_NO_BUFFERING`, avec un tampon aligné sur 64 Kio et des lectures arrêtées à la longueur du fichier. Elle lit donc le disque ou le partage, pas le cache de Windows. Si le périphérique refuse ces lectures (erreur 87), la relecture repasse par le cache. Depuis le lot vitesse, c’est le même handle qui écrit, vérifie et renomme le fichier (voir « Lot vitesse »).
- Les dates de création et de modification sont fixées sur le handle d’écriture avant `Flush(true)`. Les attributs lecture seule, caché, système, archive et non indexé (`CopyItem.Attributes`, null dans les anciens journaux) sont appliqués après le renommage, pour qu’un temporaire en lecture seule ne bloque pas une reprise. Les dossiers reçoivent leurs dates et attributs dans une passe finale, car l’ajout de fichiers modifie leurs dates.
- `CopyEngine.SpaceNeeds` additionne, par volume de destination, les fichiers non terminés moins leurs temporaires, arrondis à 4 Kio par fichier (`GetDiskFreeSpaceExW`, compatible avec les partages). Un manque bloque la copie avant toute écriture et désactive le bouton du dialogue. Un disque plein en cours de copie (erreurs 39/112) arrête la boucle au lieu d’échouer fichier par fichier. `GetVolumePathNameW` remonte tout le chemin : il n’est appelé qu’une fois par racine. L’appeler par fichier doublait le temps de copie.
- Coût mesuré de ces contrôles : environ 0,2 ms par petit fichier (4,6 ms contre 4,3 à 4,6 ms pour l’ancien moteur, en alternance sur 1 000 fichiers).
- Progression : chaque changement de phase ou d’élément est signalé ; à l’intérieur d’une même phase, les compteurs d’octets le sont au plus toutes les 100 ms (auparavant un rapport forcé par Mio et cinq par fichier). Les observateurs des tests, qui interrompent après le premier bloc ou le premier fichier terminé, restent déterministes.
- Les fichiers déjà terminés sont contrôlés avant d’être conservés. Une source modifiée ou un préfixe corrompu bloque la copie concernée.
- Une nouvelle copie peut remplacer le dernier journal ; une seule session de copie est proposée à la reprise. Des temporaires abandonnés peuvent rester sur la destination. Ne pas les supprimer automatiquement sans établir qu’ils appartiennent à une opération abandonnée.
- Relancer une comparaison après copie pour obtenir un verdict actualisé : un transfert terminé ne résout pas les contenus différents au même chemin. Le dialogue « Compléter les dossiers… » propose par défaut les deux sens, sauf si une reprise est disponible (prioritaire). Chaque ligne et chaque volume de la prévisualisation indiquent leur direction. Il s’agit d’une opération à la demande, sans surveillance permanente ni propagation des suppressions.

### Rapports

L’export inclut toutes les lignes, quel que soit le filtre affiché, ainsi que les erreurs, racines, dates et empreintes. CSV en UTF-8 avec BOM, séparateur `;`, protection contre les formules de tableur ; JSON conservant les valeurs originales. La sortie doit être un nouveau fichier hors des deux arbres, avec contrôle des alias. Aucun rapport existant n’est écrasé. Le CSV comporte désormais 19 colonnes : inclusion 1 dans 2 à l’index 16, identité à l’index 17 conservé, inclusion 2 dans 1 ajoutée à l’index 18. Les deux inclusions valent « Non vérifié » si le scan est incomplet. Le JSON expose `SourceFullyContained` et `TargetFullyContained`, avec les indicateurs de fiabilité existants.

## Compilation et tests

Environnement utilisé : Windows, PowerShell, SDK local **.NET 10.0.402** dans `.tools/dotnet`. Le SDK global présent lors du développement était en version 6 : utiliser le SDK local ou un SDK 10 compatible.

Depuis la racine :

```powershell
.\build.ps1 -Test
```

Le script sélectionne le SDK local s’il existe, lance les deux programmes de tests, restaure les composants officiels .NET nécessaires au paquet Windows x64, publie l’exécutable autonome, copie le mode d’emploi et recrée le ZIP. Une restauration peut nécessiter l’accès à NuGet. Le script ne publie rien sur GitHub.

Pour lancer seulement les tests avec le SDK local, sans recréer le paquet :

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD '.tools\cli'
$env:NUGET_PACKAGES = Join-Path $PWD '.tools\packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$previousAppData = $env:APPDATA
try {
    $env:APPDATA = Join-Path $PWD '.tools\appdata'
    foreach ($project in @('FolderChecker.Tests', 'FolderChecker.App.Tests')) {
        & .\.tools\dotnet\dotnet.exe restore ".\$project\$project.csproj" --configfile .\NuGet.Config
        if ($LASTEXITCODE -ne 0) { throw "Échec de restauration : $project" }
        & .\.tools\dotnet\dotnet.exe run --project ".\$project\$project.csproj" -c Release --no-restore
        if ($LASTEXITCODE -ne 0) { throw "Échec des tests : $project" }
    }
}
finally { $env:APPDATA = $previousAppData }
```

La redirection temporaire de `APPDATA` évite un problème d’accès au fichier NuGet utilisateur rencontré dans l’environnement restreint. Ne pas la rendre globale au système. `NuGet.Config` efface les sources pour les projets sans packages ; `build.ps1` indique explicitement la source officielle pour les composants du paquet autonome.

Les tests sont des exécutables de console internes, pas une suite xUnit/NUnit à lancer avec `dotnet test`. Les répertoires temporaires de tests sont isolés. `.gitignore` exclut `.tools`, `bin`, `obj`, `.test-data`, `artifacts` et `dist`.

Avant de remplacer un exécutable distribué actuellement ouvert, fermer proprement **Checker San**. Si une opération utilisateur est en cours, préserver sa progression ; ne pas tuer aveuglément le processus.

## Vérifications historiques — version antérieure au mode Doublons

Dernière compilation complète précédemment livrée : `build.ps1 -Test`, le 20 octobre 2026. Les tests n’ont signalé aucun échec. La restauration du paquet a signalé NU1900 (service NuGet de données de vulnérabilité inaccessible dans le réseau restreint) ; les composants nécessaires étaient déjà en cache. Aucun package applicatif tiers n’a été ajouté.

| Suite | Résultat constaté |
|---|---|
| `FolderChecker.Tests` | **66 réussis, 0 échec, 0 ignoré** |
| `FolderChecker.App.Tests` | **19 réussis, 0 échec** |
| Total | **85 tests réussis** |

Ces résultats ont été obtenus après les changements du 20 octobre, **avant les optimisations et le mode Doublons**. Ils ne valident pas les nouveaux changements. Aux 12 tests ajoutés pour les inclusions dans les deux sens s’ajoutent 13 tests de synchronisation et d’aperçu : union des fichiers/dossiers, restauration des chemins déplacés, conflits conservés, reprise du second sens, compatibilité des journaux schéma 1, directions ambiguës refusées, préfixe inverse corrompu, nouvelle destination non écrasée et routage des trois choix dans le dialogue.

Couverture significative : tous formats, fichiers cachés/sans extension, SHA-256, dossiers vides, doublons, déplacements, conflits, fichiers verrouillés, mutations pendant scan, annulation, jonctions, invalidation du cache même avec date restaurée, journaux incorrects, protections des chemins, erreurs réseau simulées, limites de tentatives, reprise de copie après redémarrage, absence d’écrasement, source modifiée, préfixe corrompu et conservation des fichiers déjà copiés. Rapports : contenu intégral, Unicode/échappement, formules CSV, refus des sorties internes/alias/écrasements et invalidation de l’interface après changement de chemin.

Vérifications manuelles du 19 octobre dans l’application compilée :

- Inspection visuelle du thème sombre/rose, des résultats et du dialogue d’aperçu de copie.
- Comparaison d’arborescences de démonstration avec des noms génériques et plusieurs extensions.
- Reprise du même scan : **6 empreintes réutilisées, 0 octet relu pour le hachage**, résultats cohérents après revalidation.
- Copie depuis l’aperçu : un fichier manquant de 31 octets et un dossier vide ; succès affiché, SHA-256 source/destination identiques, aucun temporaire restant.
- Redémarrage final sans chemins ni sessions de démonstration préchargés ; interface prête pour l’utilisateur.

Le 20 octobre, la nouvelle distribution a aussi été ouverte et inspectée : titre de fenêtre exactement « Checker San », slogan supprimé, libellés de dossiers neutres, deux verdicts côte à côte lisibles, catégories « Absents du dossier 1 » et « Absents du dossier 2 ». Les combinaisons Oui/Non et leur invalidation ont été vérifiées par les tests WPF. Aucun scan ni transfert sur les dossiers réels de l’utilisateur n’a été lancé pour cette validation visuelle.

Les fixtures restent sous `.test-data/ui-general`. Les deux journaux `%LOCALAPPDATA%\CheckerSan\Sessions\resume.json` et `copy.json` créés par ces tests ont été supprimés **après vérification qu’ils pointaient uniquement vers ces fixtures**. Ne pas répéter ce nettoyage à l’aveugle : la prochaine session peut contenir une vraie opération de l’utilisateur.

L’automatisation native de l’interface a utilisé le skill `computer-use` et `@oai/sky` via le REPL Node. Pour la reprendre, relire les instructions disponibles dans la nouvelle session et sélectionner une fenêtre actuelle ; les anciens identifiants de fenêtres et index de contrôles sont périmés.

La livraison précédente ajoutait la copie bidirectionnelle en une opération et la palette noire/grise. Ses 85 tests automatisés passaient, dont les trois choix du dialogue et la reprise dans le second sens. Le lancement pour contrôle visuel a été interrompu par l’utilisateur avec la touche Échap : aucune autre automatisation native n’a été effectuée ensuite et le contrôle visuel de cette dernière palette reste à faire. Les fixtures créées à cet effet se trouvent dans `.test-data/ui-sync-4065442190d649a7800d6f0ed5c76e18` ; aucun scan ni transfert n’a été lancé dessus depuis l’interface. Les journaux de session étaient absents avant cette tentative.

## Revue adverse et lot A « verdicts justes » — 23 octobre 2026 (non commité, au-dessus du lot automatisation)

Revue complète dans `revue/` :
- `REVUE-ROUGE.md` : 39 trouvailles prouvées ;
- `REVUE-BLEUE.md` : réfutations et avis sur les correctifs ;
- `ARBITRAGE.md` : gravité finale, correctif retenu et plan en trois lots.

Aucune trouvaille n'est réfutée sur le fond. Le lot A est fait ; les lots B (écritures et retraits au bon endroit) et C (robustesse) restent à faire.
- **CMP-01, élément réel face à un lien.** `ExitCodes.ForComparison` rend 2, et non 0, quand `TreesIdentical` est faux sans aucune ligne d'écart : un dossier réel face à une jonction, dans un sens comme dans l'autre. Des liens identiques des deux côtés restent à 0.
  - `PairRun.Links` compte les éléments derrière des liens, affichés par la liste des paires et la fenêtre de résultat.
  - Le CLI ajoute une ligne d'explication, et son aide décrit le code 2.
  - L'aide de l'app disait que les liens « n'empêchent pas le verdict » ; elle est corrigée en FR et en EN.
- **CMP-02, contrôle final.** Après le dernier inventaire, `VerifyReadIdentitiesAsync` réinterroge par handle (`FileSafety.CurrentIdentity` : `FILE_READ_ATTRIBUTES`, sans suivre de lien) chaque fichier qui fonde une ligne Identique ou Déplacé. Une identité (volume, identifiant, `ChangeTime`) différente ou devenue illisible donne une erreur et la ligne passe en `Unverified`.
  - Cas couverts : écriture en place avec date remise, et écriture par un autre lien physique, que l'entrée de répertoire ne reflète pas.
  - Même contrôle dans `SnapshotEngine`.
  - Une identité absente dès la lecture (système de fichiers sans identifiant) laisse le comportement inchangé.
  - **Coût mesuré** sur 10 000 fichiers de 1 Kio par côté, NVMe, trois passages en alternance : médiane 3,20 s → 3,34 s (+4 %) avec les lecteurs par disque. En séquentiel, c'était +43 % ; d'où la parallélisation.
- **AUT-02, `verify` et dossiers illisibles.**
  - Nouveau champ facultatif `FolderSnapshot.UnreadableFolders` ; le schéma 1 reste lisible.
  - `Compare` ne produit ni « absent » ni « ajouté » sous un dossier illisible, au moment de l'instantané comme au moment de la vérification.
  - `verify` place les erreurs avant les écarts (code 2), comme `compare` : un fichier seulement en ligne ne masque pas un écart prouvé (code 1).
  - Les erreurs sont affichées dans le CLI et dans la fenêtre d'instantanés.
- **Même dossier sous deux noms.** `ValidateSeparateRoots` compare aussi le volume et l'identifiant (`FileSafety.ObjectId`) des deux racines et de leurs ancêtres. Cela vise par exemple un NAS désigné par son nom puis par son adresse, que les chemins canoniques ne rapprochent pas. Non testable ici sur un vrai partage ; le mécanisme est testé avec une jonction, par réflexion.
- **DUP-07, noms d'un caractère.** `RenameByHandle` transmet au moins `sizeof(FILE_RENAME_INFORMATION)`, soit 24 octets en x64. Un fichier nommé `a` est de nouveau copié et mis à l'écart.
- **CMP-05, casse dans les instantanés.** Deux noms distincts seulement par la casse, dans un dossier sensible à la casse, donnent une erreur et un instantané incomplet. Le hachage passe par le nom énuméré.
- **Tests :** 6 nouveaux, dans `FolderChecker.Tests/VerdictFixTests.cs`, qui reprennent les scénarios prouvés par la revue. **140 tests moteur réussis trois fois de suite, 37 tests WPF réussis deux fois.**

### Lot B « écritures et retraits au bon endroit » — 23 octobre 2026 (non commité, au-dessus du lot A)
- **Une seule vérification des sorties** : `OutputFiles` (Core, publique). `EnsureOutside` vérifie, `CreateOutside` vérifie puis crée.
  - Refus : nom contenant `:` (flux NTFS d'un dossier), dossier parent absent, fichier existant, destination dans une racine. Le contrôle est textuel puis canonique ; il couvre les jonctions et le préfixe `\\?\`.
  - Utilisateurs : rapports, exports de doublons et aperçus de copie de l'app (`ReportWriter.CreateOutside` délègue), `SnapshotEngine.Save` et le `--json` du CLI.
  - Le CLI vérifie `--json` et la destination de l'instantané **avant** le scan ; la fenêtre d'instantanés aussi (`SnapshotEngine.ValidateDestination`). `ValidateCheckpointPath` refuse aussi `:`.
- **Rapports des paires** :
  - le dossier réellement écrit, `Reports\<id>`, est contrôlé de façon canonique contre les racines ;
  - le nom est en culture invariante, donc en chiffres grégoriens ;
  - deux vérifications dans la même seconde donnent `…-2.json` ;
  - la rotation ne touche que les noms `^\d{4}-\d{2}-\d{2} \d{6}(-\d{1,2})?\.json$` et épargne le rapport qui vient d'être écrit.
- **Doublons** :
  - **Quarantaine imbriquée.** Une quarantaine reconnue est exclue de l'inventaire à toute profondeur, avec un avertissement qui la nomme. `DuplicateSelection.Extras` ne garde jamais une copie située sous un dossier `.CheckerSan-Quarantine` (`DuplicateRemoval.IsInQuarantine`).
  - **Refus par groupe.** `BuildPlan` refuse le seul groupe concerné (sélection en quarantaine, ou seule copie conservée en quarantaine) au lieu de bloquer tout le plan. `ValidatePath` ne fait plus que vérifier la forme des chemins. L'aperçu marque une ligne CONSERVER située dans une quarantaine.
  - **Corbeille.** Pendant la vérification d'un groupe, aucun mode ne partage plus la suppression : un fichier vérifié ne peut plus être renommé puis remplacé. Avant l'appel au Shell, un second handle (attributs seuls, partage complet) doit désigner le même fichier au même endroit (`FileIdInfo` et chemin final). Le handle verrouillé n'est relâché qu'ensuite, quelques microsecondes avant l'appel. Le résultat est lu sur ce second handle, et un déplacement inattendu est inscrit au journal comme incident.
  - **Disponibilité de la Corbeille.** `RecycleBinAvailable` juge maintenant le volume réel (chemin canonique, puis `GetVolumePathNameW`), y compris derrière une jonction, un lecteur `subst` ou un point de montage. Un chemin pas encore accessible est jugé d'après sa lettre.
  - **Restauration par handle.** La session et la quarantaine sont épinglées (`DirectoryLocks`, liens refusés). Chaque fichier est ouvert sans suivre de lien : fichier simple, un seul nom, chemin final exact. Son SHA-256 est relu sur ce handle, puis il est renommé par handle vers son dossier d'origine, épinglé ou créé. Une session ou un manifeste qui sont des liens ne sont ni proposés ni restaurés, et `RemoveEmptyFolders` ne descend jamais dans un lien, racine comprise.
- **Copie** :
  - **COP-01.** Le chemin final est contrôlé sur le handle du temporaire juste après son ouverture ; un temporaire créé ailleurs est supprimé par son handle. Même contrôle sur la destination existante et sur le dossier du renommage. La passe finale des dossiers revalide les racines, épingle chaque dossier par un handle sans partage de suppression, contrôle son chemin final, puis applique les dates par handle et les attributs.
  - **Identité des racines.** `CopyPlan.SourceRootId`/`TargetRootId` contiennent le volume et l'identifiant ; nul dans les anciens journaux. Un disque de sauvegarde échangé sous la même lettre, ou un dossier recréé, est refusé.
  - **COP-08.** `CopyItem.CreatedByCopy` : un dossier qui existait déjà garde ses attributs et ses dates. Le drapeau est collant à la reprise ; nul dans les anciens journaux, qui gardent leur comportement.
  - **COP-09.** Une reprise applique les attributs d'une copie renommée mais jamais marquée terminée.
  - **COP-07.** Les temporaires `.checkersan-….partial` abandonnés sont écartés du plan, comptés dans `CopyPlan.AbandonedTemporaries` et annoncés dans l'aperçu. La comparaison les nomme dans le détail, sans les masquer.
  - **Source en ligne uniquement.** Une source devenue « en ligne uniquement » depuis le plan est refusée avant toute lecture.
- **Écart avec l'arbitrage, voulu.** DUP-02 prévoyait une ligne d'intention au journal avant l'appel au Shell. Le second handle réduit la fenêtre d'échange à quelques microsecondes. Je garde donc « une ligne par action » (même choix que pour DUP-04), avec une ligne d'incident en cas de déplacement inattendu.
- **Vérification par les reproductions de la revue.** Les programmes de l'équipe rouge ont été relancés sur les nouvelles DLL, avec des données temporaires depuis supprimées.
  - Copie : `oplock` (échange de la racine minuté par un oplock, que les tests ne savent pas reproduire), `folderpass`, `existingdir`, `resumeattrs`, `partials`.
  - Doublons : `nested`, `onechar`, `restorejunction`.
  - Rapports des paires : `reports-inside`, `culture`.
  - Comparaison : `linkexit`, `finalcheck`, `clijson`, `snapcase`, `ads`.
  - Résultat : tous montrent le refus ou le bon comportement, là où ils prouvaient le défaut.
- **Coût mesuré** : copie de 1 000 fichiers de 4 Kio, NVMe, trois passages en alternance : médiane 4,22 s avant, 4,09 s après. Aucune différence mesurable.
- **Tests :** 6 nouveaux dans `FolderChecker.Tests/WriteSafetyTests.cs`. Le test d'échange en mode Corbeille s'arrête avant l'appel au Shell : rien n'est mis dans la vraie Corbeille. **146 tests moteur réussis trois fois de suite, 37 tests WPF réussis deux fois.**

### Lot C « robustesse » — 23 octobre 2026 (non commité, au-dessus du lot B)
- **Ligne de commande** :
  - **AUT-10.** Liste blanche des options par commande ; les options communes (`--lang`, `--log`, `--readers`, `--pairs`) sont acceptées partout. Une option inconnue, ou propre à une autre commande, donne le code 3 avant tout scan. Une valeur qui commence par `--` est refusée. Les drapeaux ignorent la casse. `--help` avec une commande garde le code 0.
  - **AUT-04.** Ctrl+C donne le code 2, avec un message et une ligne d'activité. Un filet final garantit l'un des quatre codes pour toute exception imprévue.
  - **AUT-05.** `--log ""` est refusé (code 3). `ActivityLog.Write` est entièrement protégé : un chemin vide, une racine ou un dossier ne font jamais échouer l'opération.
  - **Chemins longs.** Le CLI a désormais un `app.manifest` `longPathAware`.
- **Paires** :
  - **AUT-06.** `PairStore.Modify(id, change)` modifie sous le verrou la paire telle qu'elle est dans le fichier. Planifier ou déplanifier garde donc un résultat enregistré entre-temps par une vérification.
  - **AUT-07.** Supprimer une paire interroge toujours la tâche Windows, puis la retire. Si l'enregistrement échoue après la création de la tâche, la tâche est retirée. « Ne plus planifier » est actif pour toute paire sélectionnée.
  - **Chemins et verrou.** `Save` refuse un chemin relatif, qu'une tâche résoudrait depuis System32. Le mutex de la liste porte le nom du chemin canonique, donc deux écritures du même fichier partagent le même verrou.
- **Mode planifié (AUT-01)** :
  - le processus `--scheduled` se termine toujours juste après la vérification, avec son code ;
  - s'il y a quelque chose à signaler, il lance `CheckerSan.exe --show-result <id>` dans un processus séparé, ramené à la priorité normale, avec une seule fenêtre par paire (mutex `Local\CheckerSan-result-<id>`) ;
  - « Ouvrir dans Checker San » lance un processus normal `--pair <id>`, qui charge la paire ; le lanceur est injectable pour les tests ;
  - une fenêtre laissée ouverte ne retient donc plus la tâche : Windows ne saute plus de vérification (`IgnoreNew`), la limite de 72 h ne ferme plus l'app, et le dernier résultat du Planificateur est le bon code ;
  - **vérifié en lançant l'exécutable Release à la main**, sans le Planificateur : avec un écart, code 1 en 0,44 s et fenêtre de résultat dans un processus séparé à priorité normale ; dossiers identiques, code 0 en 0,44 s et aucune fenêtre ;
  - **essai réel avec le Planificateur, fait le 23 octobre avec l'accord de l'utilisateur.** Tâche temporaire créée par `ScheduledTasks.Register`, sur deux dossiers d'essai qui diffèrent, avec une version autonome publiée hors de `dist`.
    - `schtasks /Run` : la tâche se termine en 2 s avec le dernier résultat 1 et revient à l'état « Prêt ».
    - Dix secondes après la fin de la tâche, la fenêtre de résultat est toujours ouverte dans son propre processus, à priorité normale. Elle ne prend pas le focus et affiche la paire, ses dossiers et « differences are proven ». L'objet job de la tâche ne la ferme donc pas.
    - Ensuite : fenêtre fermée, tâche supprimée par `ScheduledTasks.Unregister` (plus aucune tâche « Checker San »), dossiers d'essai supprimés.
- **Doublons** :
  - **DUP-08.** La suppression définitive passe par `FileDispositionInfoEx` avec `IGNORE_READONLY` (repli sur la classe 4). L'aperçu marque les fichiers en lecture seule.
  - **Refus par groupe.** Un refus propre à un groupe (contenu changé, liens physiques, lien sur le chemin, refus de Windows) n'arrête plus que ce groupe. Seuls le journal et la quarantaine arrêtent tout (`StopAll`).
  - **DUP-04.** Un retrait est compté avant sa ligne de journal. Si le journal ne peut plus être écrit, l'erreur « retiré sans ligne au journal : <chemin> » est inscrite et les actions suivantes ne sont pas faites. Manifeste et journal sont fermés séparément, sans jamais masquer le résultat ; même protection pour la restauration.
  - **DUP-06.** Le manifeste enregistre `CanonicalRoot`. Une mise à l'écart se restaure sous une autre écriture du même dossier (lecteur mappé, jonction plus haut), et `QuarantineSessions` n'offre que les sessions que `Restore` acceptera. Le message de refus donne la racine d'origine.
- **Réutilisation des empreintes (CMP-03, DUP-05).** Elle n'a lieu que sur NTFS ou ReFS (`FileSafety.KeepsChangeTime`, partage compris). Sur FAT32 ou exFAT tout est relu, avec un avertissement. L'aide dit maintenant que la réutilisation fait confiance aux métadonnées et ne détecte ni leur falsification délibérée ni une corruption silencieuse.
- **Copie** :
  - **COP-02.** La marque du Web (`Zone.Identifier`) est copiée au mieux. Le flux doit être ouvert en partageant la suppression, car le temporaire ouvert la détient. Les autres flux et les autorisations ne sont pas copiés : c'est dit dans l'aide et dans le dialogue.
  - **COP-03.** Après une copie 2 → 1, les champs Dossier 1 et Dossier 2 restent en place.
  - **COP-04.** L'espace est calculé avec la vraie taille de cluster du volume (`GetDiskFreeSpaceW`) plus un cluster par dossier à créer.
  - **COP-06.** Un nom que Windows modifierait (point ou espace final, nom réservé, demi-paire UTF-16) est écarté du plan avec un compteur (`UnsupportedNames`), au lieu de faire échouer tout le plan.
  - **COP-10.** Un journal impossible à rouvrir arrête la copie avec un message clair (`JournalUnavailable`), sans `NullReferenceException`. Le journal reste lisible et la copie reprenable : vérifié en rejouant la reproduction rouge `nre`.
  - **Erreur 87.** Si l'ouverture sans cache est refusée, le temporaire est rouvert avec cache. Non testable ici.
- **Comparaison** :
  - **CMP-06.** `FileSafety.Native` passe à Windows `\\?\…` (ou `\\?\UNC\…`) pour tous nos appels natifs, ainsi que pour les lectures de la comparaison et des instantanés. Il en résulte : pas de limite de 260 caractères même sans `LongPathsEnabled`, et un nom à point ou espace final conservé. Le chemin relatif est découpé tel qu'énuméré (`FileSafety.RelativeTo`), au lieu de `GetRelativePath` qui retirait le point ou l'espace final. Le moteur de doublons garde son ancienne lecture pour ces noms rares.
  - **CMP-07.** Une sauvegarde du fichier de reprise attend que le nombre d'empreintes ait crû d'un quart (ou 5 minutes au plus, ce que peut perdre un plantage), et au moins 20 fois sa propre durée. « Arrêter et garder » sauvegarde toujours aussitôt. La limite de lecture passe de 128 Mio à 1 Gio, et la fenêtre lit le fichier de reprise hors du thread d'interface. **Mesuré** avec la reproduction rouge `ckptcost` (2 × 10 000 fichiers, disque lent simulé à 10 ms par fichier, un lecteur) : 1,54 Go écrits pour un fichier final de 10,6 Mo (146 fois) avant, 59,8 Mo (5,7 fois) après. Durée 321 s avant, 311 s après. Une première version limitée à « 20 fois la durée » ne réduisait pas le volume, d'où cette règle.
  - **CMP-10.** `SnapshotEngine.Load` refuse un instantané qui se contredit : chemins en double, ou « complet » malgré des fichiers non vérifiés ou des erreurs.
  - **CMP-11.** Une différence de casse seule est écrite dans le détail de la ligne. Dans un dossier sensible à la casse, la ligne devient un conflit, donc les arborescences ne sont pas identiques.
- **Tests :** 6 nouveaux tests moteur (`FolderChecker.Tests/RobustnessTests.cs`), 1 nouveau test WPF, et deux tests WPF adaptés : bouton « Ne plus planifier », lanceur de la fenêtre de résultat, résultat conservé à la planification. **152 tests moteur réussis trois fois de suite, 38 tests WPF réussis deux fois.**

### Distribution reconstruite — 23 octobre 2026, 20:54
- `build.ps1 -Test` : 152 tests moteur et 38 tests WPF réussis, puis publication autonome de `dist\CheckerSan\CheckerSan.exe` et `checkersan-cli.exe`, avec `Mode-emploi.md` (copie du README) et `dist\CheckerSan-Windows-x64.zip`. Le fichier `CheckerSan.rar` de l'utilisateur, dans `dist\CheckerSan`, n'a pas été touché.
- SHA-256 :
  - `CheckerSan.exe` : `BEA85D7D8BC873BE62F8446BF93547C4CDFA05B7B28F562066648CD8CA0169E1` ;
  - `checkersan-cli.exe` : `A35E48E418095E43C04469C2FCFD760552020FF63C122AA2CE4C317727132EAE`.
- Contrôle : `checkersan-cli.exe` publié tourne sans le SDK local, et son aide s'affiche avec les accents.
- **Correctif découvert en route.** Au premier passage de `build.ps1`, le test « Resume invalidates only a changed same-size file » a échoué une fois : 1 empreinte réutilisée au lieu de 5. Il passe ensuite 15 fois sur 15 isolé. Cause la plus probable : la sauvegarde forcée de fin de scan échoue quand l'antivirus garde encore ouverte la copie précédente. Une seule sauvegarde avait alors eu lieu, la première, depuis que le lot C espace les sauvegardes. Le remplacement du fichier de reprise est désormais réessayé quatre fois (100 à 400 ms) lors de cette sauvegarde de fin. Le test affiche maintenant les avertissements de sauvegarde s'il échoue à nouveau.

## Lot automatisation — terminé le 23 octobre 2026 (non commité, au-dessus du lot vérification à distance)

Demande : « Lot suivant », après les quatre lots proposés. Choix : fin de la section architecture de la veille, paires enregistrées et planification (20), simulation (22). Restent hors lots : 8, 10, 11, 12, 15, 17.
- **Paires enregistrées** (`FolderChecker.Core/SavedPairs.cs`).
  - `SavedPair` : identifiant de 8 chiffres hexadécimaux, nom unique sans tenir compte de la casse, deux dossiers, option des déplacements, `PairSchedule` (chaque jour ou chaque semaine, à une heure locale) et `PairRun`, le dernier résultat. Ce résultat garde des compteurs et non du texte, pour s’afficher dans les deux langues.
  - `PairStore` : `%LOCALAPPDATA%\CheckerSan\pairs.json`, schéma 1. Chaque modification relit le fichier sous un mutex de session `Local\CheckerSan-pairs-…`, partagé par l’app, la ligne de commande et les vérifications planifiées, puis le remplace en une fois (fichier temporaire puis `File.Move` avec remplacement). Un fichier endommagé lève `InvalidDataException` et n’est jamais réécrit.
  - `PairCheck.RunAsync` : comparaison en lecture seule, puis rapport JSON compact dans `Reports\<id>\aaaa-mm-jj hhmmss.json`. Les fichiers identiques et les dossiers communs y sont seulement comptés. Seuls les 20 derniers rapports par paire sont gardés. Le rapport est refusé s’il tomberait dans un dossier comparé. Le résultat est enregistré avec la paire et dans le journal d’activité.
  - `ExitCodes` (Core) centralise les codes 0/1/2/3, utilisés par la ligne de commande et le mode planifié.
- **Tâche Windows** (`ScheduledTasks.cs`).
  - Format : XML Task Scheduler 1.2, enregistré par `schtasks /Create /TN "Checker San - <nom> (<id>)" /XML … /F`.
  - Exécution : `InteractiveToken` et `LeastPrivilege`, donc avec le compte de l’utilisateur quand il est connecté, sans mot de passe enregistré ni droits administrateur.
  - Réglages : `StartWhenAvailable` (une heure manquée est rattrapée), `IgnoreNew` (jamais deux vérifications à la fois), tâche lancée aussi sur batterie, limite de 72 h.
  - Action : `CheckerSan.exe --scheduled <id>`, avec `--pairs <fichier>` si la liste des paires n’est pas celle par défaut.
  - `Unregister` interroge la tâche avant de la supprimer. Les appels à schtasks passent par un délégué `Runner` remplaçable dans les tests.
- **Mode planifié de l’app** (`App.xaml.cs`).
  - `StartupUri` a été retiré : `OnStartup` ouvre la fenêtre principale lui-même, sauf avec `--scheduled`.
  - Avec `--scheduled`, aucune fenêtre ne s’ouvre pendant la vérification, ni après si les dossiers sont identiques : l’app quitte avec le code 0.
  - Sinon, `ScheduledResultWindow` s’ouvre sans prendre le focus (`ShowActivated = false`), avec « Ouvrir le rapport » et « Ouvrir dans Checker San ». La seconde remplit les dossiers et l’app quitte quand la fenêtre principale se ferme.
  - Le code de sortie est celui de la ligne de commande : le Planificateur l’affiche comme dernier résultat.
  - C’est l’app, et non la ligne de commande, qui est planifiée : c’est une application fenêtrée, donc aucune console n’apparaît.
- **Fenêtre « Paires… »** (`PairsWindow.cs`, bouton dans l’en-tête) : enregistrer les dossiers actuels (un nom existant demande confirmation), ouvrir une paire, voir le dernier rapport, supprimer (la tâche est retirée d’abord ; dossiers et rapports intacts), planifier (fréquence, jour, heure) ou ne plus planifier. `MainWindow.LoadPair` remplit les deux dossiers et l’option.
- **Simulation (22)** :
  - en ligne de commande, `compare … --simulate-copy both|1to2|2to1` liste ce que « Compléter les dossiers » copierait, avec l’espace nécessaire et les conflits non remplacés ;
  - en ligne de commande, `duplicates … --simulate-keep first|oldest|newest|shortest|folder [--keep-in d]` applique une règle de sélection et liste les copies qui seraient retirées et celles conservées ;
  - les deux simulations figurent aussi dans le rapport `--json`, et rien n’est modifié ;
  - nouvelles commandes `pairs` et `run <nom ou id>` (option `--pairs`) ;
  - dans l’app, « Exporter la liste… » dans la fenêtre de copie (`CopyPlanWriter`) enregistre en CSV ce qui serait copié, hors des deux dossiers et sans remplacer de fichier. L’aperçu de retrait des doublons existait déjà, et l’export de la liste des doublons indique déjà le choix « À retirer ».
- **Tests :**
  - Moteur :
    - « Saved pairs are unique, replaced in one step and never overwrite a damaged store » ;
    - « A scheduled task runs as the user, read-only, and Windows accepts its definition ». Il valide le XML auprès de Windows avec `TASK_VALIDATE_ONLY` (API COM `Schedule.Service`), qui n’enregistre rien. Un XML invalide est bien refusé, vérifié à part ;
    - « Saved pairs run from the command line with compact, rotated reports » ;
    - « Copy and removal simulations list what would happen and change nothing ».
  - WPF :
    - « The pairs window saves, schedules and unschedules a pair through the scheduler », avec un faux schtasks ;
    - « A scheduled result explains differences or failures without taking the focus » ;
    - « The copy preview exports what would be copied, outside both folders ».
  - Résultats : **134 tests moteur réussis trois fois de suite, 37 tests WPF réussis.**
  - Le mode `--scheduled` de l’exécutable Release a aussi été lancé à la main sur deux dossiers identiques : sortie silencieuse avec le code 0 en une seconde, résultat enregistré, rapport écrit. Avec une liste de paires endommagée, il affiche bien la fenêtre « Vérification planifiée impossible ».
- **Essai réel avec le Planificateur, fait le 23 octobre avec l’accord de l’utilisateur.** Tâche temporaire créée par `ScheduledTasks.Register`, sur deux dossiers d’essai du dossier temporaire de session, avec une version autonome publiée hors de `dist`.
  - Windows a enregistré la tâche avec le SID de l’utilisateur, `InteractiveToken`, `IgnoreNew` et `StartWhenAvailable`. `RunLevel` et `ExecutionTimeLimit` n’apparaissent pas dans le XML exporté parce qu’ils valent les valeurs par défaut.
  - Planifiée à 03:00 alors qu’il était 15:48, elle **ne s’est pas lancée d’elle-même** : prochaine exécution le lendemain à 03:00. Une heure déjà passée ne déclenche donc pas de vérification immédiate.
  - `schtasks /Run` sur deux dossiers identiques : aucune fenêtre, dernier résultat 0, résultat et rapport enregistrés une seconde après le lancement.
  - `schtasks /Run` après l’ajout d’un fichier : fenêtre de résultat ouverte dans la session sans prendre le focus (« 1 difference(s) », chemin du rapport, trois boutons), puis dernier résultat **1** après sa fermeture.
  - Tâche supprimée par `ScheduledTasks.Unregister` (plus aucune tâche « Checker San » ensuite), dossiers d’essai supprimés. Les deux vérifications ont laissé une ligne chacune dans le journal d’activité de l’utilisateur.
  - Attention : une version de développement dépendante du framework ne démarre pas depuis le Planificateur sur cette machine, car .NET 10 n’y est pas installé (seul le SDK local `.tools` l’est). Il faut planifier depuis la version autonome de `dist`.
- **Non fait :**
  - pas de vérification planifiée des instantanés ;
  - pas de notification Windows (toast), seulement la fenêtre de résultat ;
  - une tâche dont la paire a été supprimée hors de l’app affiche « la paire n’existe plus » : elle se supprime dans le Planificateur.

## Lot vérification à distance — terminé le 23 octobre 2026 (non commité, au-dessus du lot vitesse)

Demande : ligne de commande avec codes de sortie et rapport JSON (18), instantané exportable (19), journal d’activité (21).
- **Instantané** (`FolderChecker.Core/Snapshot.cs`, `SnapshotEngine`).
  - Contenu : chemins relatifs, tailles, date de modification et SHA-256 complet de chaque fichier, ainsi que les dossiers, y compris vides. Les liens et jonctions sont listés mais jamais suivis. Les fichiers en ligne uniquement ou illisibles vont dans `Unverified`.
  - Lecture avec `DeviceReaders`. Un nouvel inventaire final signale tout changement survenu pendant la prise ; `Complete` vaut faux s’il manque la moindre preuve.
  - `Compare(attendu, actuel)` donne `Missing`, `Changed`, `Added`, `MissingFolder` et `AddedFolder`. Un fichier non vérifié n’est pas compté comme absent, il rend seulement le résultat incertain.
  - `Save` refuse d’écrire dans le dossier décrit, y compris via une jonction, ou par-dessus un fichier existant. `Load` refuse un instantané endommagé (schéma, empreintes, chemins). Format JSON, schéma 1.
- **Ligne de commande** : nouveau projet `FolderChecker.Cli`, exécutable `checkersan-cli.exe`. Il ne s’appelle pas `checkersan.exe`, car Windows ignore la casse et `CheckerSan.exe` est dans le même dossier.
  - Commandes : `compare <1> <2> [--json f] [--no-moved]`, `snapshot <dossier> <fichier>`, `verify <dossier> <fichier> [--json f]`, `duplicates <dossier> [--json f] [--min-size n] [--exclude "a;b"] [--include-hidden]`.
  - Options communes : `--readers 1-16`, `--lang en|fr`, `--log fichier`.
  - Codes de sortie : 0 rien à signaler, 1 écarts ou doublons prouvés, 2 vérification incomplète, 3 commande invalide ou erreur.
  - Elle ne copie, ne déplace ni ne supprime jamais rien. Ctrl+C arrête proprement. La sortie console est en UTF-8, et la langue d’origine est rétablie après chaque appel (`CheckerSanCli.RunAsync` est utilisé directement par les tests).
- **Journal d’activité** (`ActivityLog`) : `%LOCALAPPDATA%\CheckerSan\Logs\activity.log`, une ligne tabulée par comparaison, copie, recherche ou retrait de doublons, instantané, vérification et commande. Il fonctionne au mieux : une ligne impossible à écrire ne fait jamais échouer l’opération. Le journal strict des retraits reste `duplicate-actions.csv`.
- **Interface** : bouton « Instantanés… » dans l’en-tête et fenêtre `SnapshotWindow` (créer, vérifier, arrêter, ouvrir le journal d’activité, liste des différences).
- **Publication** : `build.ps1` publie aussi `checkersan-cli.exe` (autonome) et l’ajoute au ZIP ; non exécuté depuis.
- **Tests** : « A snapshot detects every change, saves safely and rejects damage » et « Command-line exit codes report clean, different and incomplete results » (moteur) ; « The snapshot window opens from the header without reading anything » (WPF). **130 tests moteur réussis trois fois de suite, 34 tests WPF réussis.**
- **Non fait :** pas de planification intégrée (le Planificateur de tâches Windows peut lancer `checkersan-cli`), pas de rapport CSV en ligne de commande (JSON seulement), aucun essai réel de la ligne de commande publiée en fichier unique.

## Lot vitesse — terminé le 22 octobre 2026 (non commité, au-dessus du lot doublons)

**Résultat final contre la référence d’avant le lot (NVMe, deux passages en alternance) :**

| Mesure | Avant | Après |
|---|---|---|
| Comparaison de 2 × 2 000 fichiers de 64 Kio | 2,5–2,8 s | 0,63–0,65 s |
| Comparaison de 2 × 1 Go | 3,0–3,3 s | 0,71–0,95 s |
| Reprise de 10 000 fichiers | 2,8–2,9 s | 0,92–1,03 s |
| Doublons, 2 000 fichiers de 64 Kio | 1,8–2,0 s | 0,94–0,99 s |
| Doublons, 10 fichiers de 100 Mio | 0,81–0,90 s | 0,42–0,52 s |
| Copie de 2 000 fichiers de 64 Kio | ≈ 28 s | ≈ 9,5 s |

Sur le disque dur USB G:, aucune régression, et la copie de petits fichiers gagne 14 % (détails au point 5 ci-dessous). Nombre de lecteurs mesuré sur NVMe : 1 → 1,6 s, 2 → 0,9 s, 4 → 0,79 s, 8 → 0,77 s pour 2 × 1 Go ; 1,3–1,6 s, 1,0 s, 0,6–0,8 s, 0,5–0,6 s pour 2 × 2 000 petits fichiers. `SolidStateReads = 4` est conservé. La recherche de doublons lit maintenant en deux phases parallèles (`ReadAllAsync` : échantillons, puis empreintes complètes), avec `DuplicateScanOptions.ParallelReads`, son état partagé protégé par `_gate` et `_saving`. Le test « One reader or four give identical comparisons and duplicate groups » vérifie lignes, empreintes, octets lus et groupes. **128 tests moteur réussis six fois de suite, 33 tests WPF réussis.** Il n’y a aucune option d’interface pour le nombre de lecteurs, et les partages réseau, non mesurés, restent à un lecteur.

Lot demandé : lecture parallèle adaptée au disque (13), copie plus rapide (14), inventaire plus efficace (16), chaque gain **mesuré avant activation par défaut**. L’état des lots précédents est conservé sans commit dans deux arbres Git : lot fiabilité `d5c8620efa6531ec2fc41e6599dc10dca8c47149`, lot doublons `bde4878b195a44805f7f52f15d6f7ee786be1ea0`. Pour faire trois commits séparés : `git commit-tree <arbre> -p <parent> -m …`, puis un commit final de l’état courant, et `git update-ref`/push uniquement après accord de l’utilisateur, qui veut d’abord tester.

Machine de mesure : SSD NVMe C: (Micron 2450, seulement 19 Go libres), disque dur USB G: personnel (ne pas écrire dessus sans accord), Ryzen 9 6900HX 8 cœurs, Defender actif. Banc d’essai : application console qui écrit des données de test sans cache (lectures froides) puis mesure comparaison, doublons, copie et reprise. Elle était dans le dossier temporaire de session et est à recréer si besoin.

**Fait et mesuré (NVMe, références avant/après en alternance) :**
- **Lectures synchrones** dans la comparaison (`ReadHash`, `ReadSample`) et la copie. Avec `FileStream` asynchrone, lecture + SHA-256 tombait à environ 600 Mio/s, contre 1,2–1,36 Gio/s en synchrone (la lecture anticipée du cache chevauche le hachage). Comparaison de 2 × 1 Go : 3,2 s → 1,6 s ; 2 × 2 000 fichiers de 64 Kio : 2,55 s → 1,31 s. Le plafond atteint est celui du SHA-256 sur un cœur (≈ 1,35 Gio/s).
- **Moins d’appels système** : `FileSafety.Stat` (un seul `GetFileAttributesExW` au lieu de trois appels .NET) et `FileSafety.Describe` (dates et identité du fichier ouvert en deux requêtes au lieu de quatre). Tampon d’énumération de 64 Kio pour les partages. Reprise de 10 000 fichiers : 2,65 s → 1,42 s ; doublons de 2 000 fichiers : 1,82 s → 1,47 s.
- **Copie par un seul handle non mis en cache** (`OpenPartial`) : écriture alignée sur la taille de secteur réelle (`FileStorageInfo`, 4 Kio par défaut), `FlushToDisk`, relecture SHA-256 par le même handle, puis renommage par ce même handle (`FileSafety.RenameByHandle` + `OpenDirectory`, jamais d’écrasement). Partage en lecture seule : personne ne peut écrire, renommer ni supprimer le temporaire. Cause mesurée : rouvrir un fichier tout juste écrit coûtait ≈ 9 ms (analyse Defender), sur 14 ms par fichier de 64 Kio. 2 000 fichiers de 64 Kio : ≈ 28 s → ≈ 9,5 s (médianes de quatre passages).
- Une reprise de temporaire non aligné (ancienne version) repart de l’offset aligné inférieur, après vérification du préfixe.
- Les écritures non mises en cache se chevauchent avec la lecture du bloc suivant (deux tampons alignés). Ce point n’a pas pu être mesuré : les gros transferts sur ce SSD presque plein varient de 2,3 à 17 s.
- **Écarté après mesure :** `FILE_FLAG_WRITE_THROUGH` à la place de `Flush(true)` (1 Go en 8–10,7 s au lieu de 3–3,9 s, aucun gain sur les petits fichiers). Clonage ReFS/Dev Drive : aucun volume ReFS pour le tester.
- `FileSafety.Storage` identifie le disque (numéro via `IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS`, SSD via la pénalité de recherche). Un bus USB ou inconnu donne `Unknown`, c’est-à-dire une lecture à la fois. Les chemins UNC et lecteurs réseau donnent `Network`, une à la fois par défaut. Vérifié : C: → `disk:0 SolidState`, G: → `disk:1 Unknown`, `\\server\share` → `Network`.
- `DeviceReaders` (nouveau fichier) : un sémaphore par disque physique, 4 lecteurs sur SSD interne et 1 ailleurs (`ComparisonOptions.ParallelReads` permet de forcer la valeur, aucune interface pour l’instant). Deux dossiers sur deux disques différents sont lus en même temps ; deux dossiers sur le même disque dur, un fichier à la fois.
- Comparaison : `PrefetchPairsAsync` calcule à l’avance, en parallèle, les échantillons et empreintes des paires de même chemin. La boucle de verdict séquentielle trouve ensuite tout en cache : ses décisions ne changent pas. État partagé protégé par `_gate` (caches, compteurs, `MarkUnverified`, progression), compteurs d’octets via `Interlocked`, sauvegarde du journal de reprise avec le drapeau `_saving`. Sans gain possible (`Slots` = 1), le préchargement est ignoré et le comportement est identique à avant.
- Test de reprise après annulation adapté au parallélisme : un fichier inachevé est relu à la reprise ; seul un fichier terminé, dont l’empreinte est recontrôlée dans le journal, ne l’est jamais. 127 tests moteur réussis huit fois de suite.

**Étapes (toutes faites sauf la 6) :**
1. Nombre de lecteurs mesuré ; 4 conservé.
2. Doublons en parallèle.
3. Test 1 lecteur contre 4.
4. Deux suites relancées ; mesures finales dans le tableau ci-dessus.
5. ~~Validation sur disque dur~~ **faite le 22 octobre avec l’accord de l’utilisateur.** Le dossier `G:\CheckerSan-perf-test` a été vidé par chaque mesure, mais la suppression du dossier vide a été bloquée par un contrôle de sécurité de l’outil : l’utilisateur doit le supprimer lui-même. G: est le disque USB Seagate, traité comme `Unknown`, donc un lecteur. Référence (avant le lot) contre version actuelle :
   - copie de 1 Go de G: vers G: : 50,3/51,6 s contre 50,1/52,1 s, aucune régression des écritures non mises en cache ;
   - comparaison de 2 × 1 Go sur G: : 28,7/29,3 s contre 29,5/29,1 s, identique car le disque est lu un fichier à la fois ;
   - comparaison entre deux disques (dossier 1 sur le SSD, dossier 2 sur G:) : 14,9/17,0 s contre 15,5/15,3 s. Gain faible, car c’est le disque dur qui limite ;
   - copie de 2 000 fichiers de 64 Kio sur G: : 138,8 s contre 120,0 s (−14 %).
   Il n’existe toujours aucune mesure en réseau : les partages restent à un lecteur.
6. Plus tard, si l’utilisateur le demande : une option d’interface « lectures simultanées » pour essayer 2 à 4 lecteurs sur un partage réseau, puis la documenter dans l’aide.

## Validation du lot doublons (filtres, règles, Corbeille, restauration, journal, export) — 22 octobre 2026

**160 tests réussis, aucun échec** : 127 tests moteur et 33 tests WPF/rapports, en Release avec le SDK local, au-dessus du lot fiabilité. Rien n’est commité et `dist` n’a pas été reconstruit. L’état exact du lot fiabilité seul est conservé dans l’arbre Git `d5c8620efa6531ec2fc41e6599dc10dca8c47149` (sans commit), pour permettre deux commits séparés.
- Nouveaux tests moteur (`FolderChecker.Tests/DuplicateOptionsTests.cs`) : filtres, règles de sélection, vraie mise à la Corbeille avec journal, restauration sans écrasement.
- Nouveaux tests d’interface (`FolderChecker.App.Tests/DuplicateToolsUiTests.cs`) : règle choisie, Corbeille indisponible sur un partage, export CSV, aperçu Corbeille sans consentement de suppression définitive. Le mode par défaut attendu est maintenant la Corbeille.
- Aperçus WPF régénérés en français et en anglais, avec une nouvelle vue `duplicates-filters` (filtres dépliés).

## Validation du lot fiabilité (liens, cloud, vérification, dates, espace) — 22 octobre 2026

**152 tests réussis, aucun échec** : 123 tests moteur et 29 tests WPF/rapports, en Release avec le SDK local. Les sources ne sont pas encore commitées et `dist` n’a pas été reconstruit.
- Deux tests de jonctions ont été adaptés au nouveau comportement : signalement sans blocage, et dossier réel face à un lien jamais inclus ni copié.
- Nouveaux tests : `FolderChecker.Tests/CloudAndCopyMetadataTests.cs` (fichiers en ligne jamais lus en comparaison ni en doublons, dates et attributs conservés pour fichiers et dossiers, refus d’une copie trop grande avant toute écriture, besoins additionnés sur un même volume) et `FolderChecker.App.Tests/LinkCloudUiTests.cs` (verdict expliqué, libellés, copie toujours possible).

## Validation du journal de copie incrémental — 22 octobre 2026

**147 tests réussis, aucun échec** : 119 tests moteur et 28 tests WPF/rapports, en Release avec le SDK local, sur les sources EN/FR non commitées. La distribution `dist` n’a pas été reconstruite et l’empreinte de l’exécutable n’a pas changé. Trois tests ont été ajoutés dans `FolderChecker.Tests/CopyJournalTests.cs` :

- reprise d’un instantané du journal pris pendant la copie (états ajoutés), avec lignes invalides, inconnues et tronquée ignorées, et journal réduit à une ligne après l’arrêt ;
- performance : pour 400 petits fichiers, les octets écrits par le processus pour le journal (compteurs d’E/S Windows) restent inférieurs à 16 fois la taille finale du plan ;
- progression : un fichier de 24 Mio produit au plus 4 rapports plus un par tranche de 100 ms.

Les trois tests échouent avec l’ancien `CopyEngine.cs` ; l’ancien moteur a même laissé deux fichiers sur 400 en « Accès refusé » sur le journal.

## Validation historique performance/doublons — avant EN/FR, 21 octobre 2026

**135 tests réussis, aucun échec, aucun test ignoré** : 110 tests moteur et 25 tests WPF/rapports. Exécution en Release avec le SDK local, puis publication via `build.ps1` sans répéter les tests déjà passés. La compilation produit toujours un avertissement NU1900 : le service NuGet de données de vulnérabilités est inaccessible dans cet environnement ; la restauration et la publication ont réussi avec les paquets disponibles.

- Sept tests de présélection : écart dans les blocs, écart hors blocs exigeant un SHA complet, vrais déplacements, désactivation du filtre, cache et annulation. Le cas de deux fichiers de 4 Mio + 17 octets différents au début lit 393 216 octets au lieu de 8 388 642, soit environ 95,3 % de lectures en moins. Ce n’est pas une mesure de gain de temps sur le partage de l’utilisateur.
- Dix-sept tests du scanner doublons : groupes réels, petits fichiers sans extension/cachés/vides, absence de faux doublon à taille ou échantillons égaux, cache/reprise, modifications avec dates restaurées, verrouillage, jonctions et liens physiques, journaux et quarantaine.
- Vingt tests du retrait : suppression définitive et mise à l’écart réellement exécutées uniquement sur fixtures dédiées, préservation d’un exemplaire, relecture SHA, verrouillage de la copie conservée et des cibles, sélection totale refusée, chemins/identités/liens contrôlés, annulation avant et après une action, marqueur vide refusé.
- Six nouveaux tests WPF : aucune présélection, choix des copies supplémentaires, dernier exemplaire protégé, changement de dossier invalidant le résultat, résultats incomplets inexploitables et confirmation permanente obligatoire avec aperçu exact.
- Rendu WPF inspecté visuellement, sans automatiser le bureau : accueil, liste de doublons, petite fenêtre défilable et confirmation permanente. Images générées avec des données synthétiques dans `.test-data/previews/`. Reproduction : `dotnet run --project FolderChecker.App.Tests -c Release -- --render .test-data/previews`. Aucun dossier réel ni journal utilisateur touché.
- Exécutable autonome et ZIP reconstruits dans `dist`, README actualisé copié en `Mode-emploi.md`. SHA-256 de l’exécutable indiqué plus haut. Cette validation a précédé l’autorisation de publication GitHub reçue ensuite.

Corrections issues des tests : NTFS peut exposer des dates de dossiers retardées dans l’index d’énumération ; les deux moteurs rafraîchissent donc les dossiers, tout en réutilisant les métadonnées d’énumération des fichiers pour réduire les requêtes réseau. La mise à l’écart utilise `NtSetInformationFile(FileRenameInformation)` avec un handle du dossier destination verrouillé ; le wrapper Win32 refusait le nom relatif. La suppression définitive utilise `SetFileInformationByHandle(FileDispositionInfo)`. Ne pas remplacer ces opérations par une suppression ou un déplacement par chemin après validation, qui réintroduirait une course sur l’identité.

## Limites connues et validation restante

1. **Réseau réel non testé.** Les chemins UNC/mappés utilisent les API de fichiers Windows. Les tests de reconnexion injectent des erreurs simulées ; ils ne remplacent pas un essai SMB avec le vrai ordinateur. Valider accès, coupure, retour du partage et reprise du scan/de la copie lorsque le chemin sera disponible.
2. Windows peut attendre son propre timeout avant de rendre la main sur un accès réseau bloqué. Les attentes applicatives 1/2/4 s s’ajoutent à ce délai ; l’arrêt n’est pas garanti instantané.
3. Certains systèmes de fichiers/serveurs ne fournissent pas les identités natives nécessaires. Le scan relit alors le contenu ; la reprise d’un temporaire de copie peut être refusée de manière conservatrice.
4. La vérification couvre le flux principal des fichiers et l’arborescence. Elle ne compare pas les ACL, flux NTFS alternatifs ni l’égalité des dates entre copies. La copie ne reproduit pas ces métadonnées comme le ferait un outil complet de sauvegarde.
5. Il n’existe pas d’instantané VSS : ne pas modifier les dossiers pendant la vérification. Le verdict ne couvre pas les changements ultérieurs.
6. Les liens et jonctions sont signalés et exclus du verdict, sans être suivis. Les fichiers disponibles uniquement en ligne restent « non vérifiés » tant qu’ils ne sont pas rendus disponibles sur l’appareil. Les placeholders OneDrive réels n’ont pas de test automatisé : sans fournisseur de synchronisation, les tests simulent l’état « en ligne » avec l’attribut `Offline`.
7. Les fichiers de session et rapports doivent rester hors des dossiers comparés. Comparer une racine très large qui contient elle-même `%LOCALAPPDATA%\CheckerSan\Sessions` peut donc être refusé avec l’emplacement de journal par défaut.
8. La canonisation utilisée pour protéger les rapports nécessite l’accès aux racines ; une déconnexion peut empêcher l’export jusqu’au retour de l’accès.

Les points ci-dessus restent des limites explicites, pas une preuve de panne observée sur le partage de l’utilisateur. Le partage SMB réel, notamment la reconnexion et les autorisations de retrait, reste à valider lorsqu’il sera disponible.

## Reprise conseillée pour la prochaine session

1. Lire cette passation et `README.md`, puis prendre en compte la nouvelle demande utilisateur. Inspecter l’état actuel des sources et sessions avant d’agir.
2. Si la suite concerne le réseau, récupérer le chemin quand il est disponible et commencer par des vérifications en lecture seule. Utiliser des dossiers de test dédiés pour les essais de coupure/copie.
3. Si le code change, effectuer les tests pertinents puis reconstruire la distribution ; mettre à jour le mode d’emploi et cette passation en fonction des changements. Un simple changement documentaire ne nécessite pas une nouvelle batterie de tests.
4. Pour toute publication demandée, vérifier l’état local et distant sous `therealshulgin`. La publication initiale a été autorisée ; respecter les exclusions et ne pas inclure SDK, fixtures, journaux personnels ni sorties de compilation par inadvertance.
5. À défaut d’une nouvelle demande, conserver les livrables locaux tels quels ; ne déclencher ni push, ni transfert sur les vrais dossiers, ni suppression de copie.

# Checker San — Passation pour la prochaine session

Dernière mise à jour : **21 octobre 2026**. Ce document décrit l’état des sources, les nouvelles fonctionnalités de performance/doublons et les vérifications historiques. La validation finale de cette nouvelle version est distinguée des tests antérieurs ci-dessous. Les chemins de code ci-dessous sont relatifs à la racine du projet.

## À lire en premier

- **Application existante et fonctionnelle en local**, avec comparaison, reprise du scan, copie des manquants et reprise de copie. La nouvelle évolution ajoute des optimisations de lecture et un mode de recherche/retrait des doublons dans un dossier. Il ne s’agit pas d’un projet à recommencer.
- Nom public et titre des fenêtres : **Checker San**, sans suffixe. Interface française, fonds noirs et gris neutres, boutons rose doux ; les fonds rose/violet ont été retirés. Le slogan sous le nom a été retiré ; les dossiers 1 et 2 ont des libellés neutres, sans copie à conserver prédéfinie. Employer « fichier », jamais un vocabulaire limité aux médias comme « rush ».
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

## Emplacement et livrables

Les chemins ci-dessous sont relatifs à la racine du checkout local de `checker-san`. Les livrables `dist` sont générés par `build.ps1` et ne sont pas versionnés.

| Élément | Chemin relatif |
|---|---|
| Application Windows x64 autonome | `dist/CheckerSan/CheckerSan.exe` |
| Archive de distribution | `dist/CheckerSan-Windows-x64.zip` |
| Lanceur à double-cliquer | `Lancer Checker San.cmd` |
| Mode d’emploi et limites détaillées | `README.md` |
| Mode d’emploi inclus dans la distribution | `dist/CheckerSan/Mode-emploi.md` |
| Compilation, tests et paquet | `build.ps1` |
| Présente passation | `PASSATION.md` |

L’exécutable n’exige pas d’installation de Python ou de .NET sur le poste utilisateur. Il est publié en fichier unique avec son environnement .NET. La première ouverture peut extraire des composants dans le répertoire temporaire Windows. La version déclarée du projet est `1.0.0`.

SHA-256 de l’exécutable livré le **21 octobre 2026**, avec optimisations et mode Doublons :

```text
F93E6DCEE699D3475869FC3B7A64B2A9EB337476E61BB679C0F1D4A46FCE6FDE
```

L’exécutable et l’archive ont été reconstruits après validation des tests et du rendu WPF. Le ZIP contient uniquement `CheckerSan.exe` et `Mode-emploi.md` ; un fichier `CheckerSan.rar` déjà présent dans le répertoire de distribution a été laissé intact et exclu du paquet. Le script utilise désormais une liste explicite des fichiers à empaqueter. Les anciens livrables nommés `FolderCheck` ont été supprimés de `dist` lors d’une session précédente.

## Organisation du code

Dernier ajustement visuel : badge « Local & réseau » supprimé de l’en-tête à la demande de l’utilisateur. Le bouton « Doublons dans un dossier… » reste aligné à droite. Compilation et archive reconstruites avec succès ; pas de nouvelle batterie de tests pour cette suppression de texte décoratif. Les résultats de tests ci-dessous précèdent uniquement cet ajustement.

Application C# / **WPF .NET 10**, sans bibliothèque applicative tierce. Les noms internes `FolderChecker.*` sont conservés ; le nom distribué et les textes utilisateur sont **Checker San**.

| Fichier ou projet | Rôle |
|---|---|
| `FolderChecker.Core/Models.cs` | Options, lignes, états, progression et verdicts de comparaison. |
| `FolderChecker.Core/ComparisonEngine.cs` | Inventaire, présélection des gros fichiers, SHA-256, recherche indexée des déplacements, contrôle final, reprise et verdicts. |
| `FolderChecker.Core/CheckpointData.cs` | Lecture et format du journal de scan, schéma JSON 1. |
| `FolderChecker.Core/FileSystemRetryPolicy.cs` | Détection des erreurs Windows réseau temporaires, tentatives bornées et annulation des attentes. |
| `FolderChecker.Core/FileSafety.cs` | Chemins canoniques, appartenance à une racine, identités natives Windows et date de changement. |
| `FolderChecker.Core/DuplicateModels.cs` | Options, progression, fichiers/groupes et résultats du scan de doublons. |
| `FolderChecker.Core/DuplicateEngine.cs` | Inventaire d’un dossier, filtrage taille/échantillons, SHA-256, liens physiques, contrôle final et journal séparé. |
| `FolderChecker.Core/DuplicateRemoval.cs` | Validation de la sélection, protection de la copie conservée, quarantaine et suppression par handles Windows. |
| `FolderChecker.Core/CopyEngine.cs` | Plans de copie unidirectionnels/bidirectionnels, journal schéma 2 compatible schéma 1, temporaires, reprise, SHA-256 et validation sans écrasement. |
| `FolderChecker.App/App.xaml` | Styles WPF : fonds noirs/gris neutres, contrôles, tableaux, menus et boutons roses. |
| `FolderChecker.App/MainWindow.xaml` | Structure de la fenêtre principale. |
| `FolderChecker.App/MainWindow.xaml.cs` | Lancement/arrêt/reprise du scan, résultats, sélection, export et aide. |
| `FolderChecker.App/MainWindow.Copy.cs` | Intégration de la copie, reprise, progression et invalidation du verdict précédent. |
| `FolderChecker.App/CopyDialog.cs` | Aperçu de la copie, choix du sens ou de la reprise, lancement explicite. |
| `FolderChecker.App/DuplicatesWindow.xaml` et `.xaml.cs` | Fenêtre doublons, choix du dossier, scan/reprise, sélection volontaire, retrait et invalidation des résultats. |
| `FolderChecker.App/DuplicateRowView.cs` | Cases à cocher et état conservé/à retirer de chaque copie. |
| `FolderChecker.App/DuplicateRemovalDialog.cs` | Aperçu des chemins à retirer/conserver et consentement explicite à la suppression définitive. |
| `FolderChecker.App/RowView.cs` | Présentation française des états, tailles et couleurs. |
| `FolderChecker.App/ReportWriter.cs` | Export complet CSV/JSON et protections des chemins de sortie. |
| `FolderChecker.App/Assets/CheckerSan.ico` | Icône distribuée. |
| `tools/generate_icon.py` | Génération de l’icône ; Pillow nécessaire uniquement pour la régénérer. |
| `FolderChecker.Tests` | Tests du moteur, de la reprise, de la copie, des préfiltrages (`PerfTests.cs`) et du mode Doublons. |
| `FolderChecker.App.Tests` | Programme de tests des rapports et de l’invalidation des résultats dans WPF. |

Le manifeste Windows active notamment les chemins longs et le DPI par moniteur. L’application s’exécute avec les droits ordinaires de l’utilisateur. La fenêtre et le dialogue de copie ont une barre de titre sombre.

## Garanties à conserver lors des modifications

### Comparaison et verdicts

- Les tailles et les échantillons permettent uniquement d’écarter certains candidats différents. **Seul un SHA-256 de tous les octets**, fraîchement lu ou réutilisé après contrôle strict, permet de confirmer l’égalité. Des noms, tailles, dates ou échantillons égaux ne suffisent jamais.
- Lecture séquentielle par blocs d’environ 1 Mio, un fichier à la fois, adaptée notamment aux disques mécaniques et au réseau.
- `SourceFullyContained` signifie dossier 1 entièrement contenu dans le 2 ; `TargetFullyContained` signifie dossier 2 entièrement contenu dans le 1. Chaque direction exige les mêmes chemins relatifs, contenus et dossiers, même vides. `TreesIdentical` est la conjonction des deux. Les inclusions sont calculées sur les clés des inventaires : ne pas utiliser naïvement `TargetPath`, que les lignes `Moved` peuvent faire pointer vers un autre chemin.
- `ContentCovered` reste une information complémentaire du moteur pour les contenus du dossier 1 retrouvés dans le 2 ; elle ignore chemins, dossiers et multiplicité des doublons. Elle n’est plus un titre de verdict dans l’interface et ne doit jamais remplacer les deux inclusions strictes. La recherche complémentaire de déplacements reste orientée 1 vers 2 ; elle ne conditionne pas le calcul des inclusions.
- Un contenu retrouvé ailleurs ne masque pas un conflit au chemin d’origine. Sa ligne cible reste visible avec le libellé « Absent du dossier 1 », qui concerne ce chemin relatif précis.
- Racines identiques ou imbriquées refusées, avec contrôle des chemins canoniques pour les alias de jonction.
- Liens, jonctions et autres points de réanalyse signalés et non traversés ; collisions ambiguës de casse signalées.
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
- **Aucune présélection au chargement des résultats.** Un bouton distinct permet de sélectionner les copies supplémentaires, tout en gardant au moins un fichier de chaque groupe. La sélection reste modifiable. L’aperçu liste les chemins/volumes, puis l’utilisateur choisit et confirme son action.
- **Mise à l’écart** : déplacement sur le même volume vers `<racine>/.CheckerSan-Quarantine/<guid>/<chemin relatif>`. Elle ne libère aucun espace. La restauration est manuelle dans l’Explorateur ; aucun bouton de restauration n’est annoncé.
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

## Validation de la nouvelle version — 21 octobre 2026

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
6. Les points de réanalyse, dont certains fichiers cloud, restent non vérifiés. Ils empêchent un verdict positif plutôt que d’être silencieusement ignorés.
7. Les fichiers de session et rapports doivent rester hors des dossiers comparés. Comparer une racine très large qui contient elle-même `%LOCALAPPDATA%\CheckerSan\Sessions` peut donc être refusé avec l’emplacement de journal par défaut.
8. La canonisation utilisée pour protéger les rapports nécessite l’accès aux racines ; une déconnexion peut empêcher l’export jusqu’au retour de l’accès.

Les points ci-dessus restent des limites explicites, pas une preuve de panne observée sur le partage de l’utilisateur. Le partage SMB réel, notamment la reconnexion et les autorisations de retrait, reste à valider lorsqu’il sera disponible.

## Reprise conseillée pour la prochaine session

1. Lire cette passation et `README.md`, puis prendre en compte la nouvelle demande utilisateur. Inspecter l’état actuel des sources et sessions avant d’agir.
2. Si la suite concerne le réseau, récupérer le chemin quand il est disponible et commencer par des vérifications en lecture seule. Utiliser des dossiers de test dédiés pour les essais de coupure/copie.
3. Si le code change, effectuer les tests pertinents puis reconstruire la distribution ; mettre à jour le mode d’emploi et cette passation en fonction des changements. Un simple changement documentaire ne nécessite pas une nouvelle batterie de tests.
4. Pour toute publication demandée, vérifier l’état local et distant sous `therealshulgin`. La publication initiale a été autorisée ; respecter les exclusions et ne pas inclure SDK, fixtures, journaux personnels ni sorties de compilation par inadvertance.
5. À défaut d’une nouvelle demande, conserver les livrables locaux tels quels ; ne déclencher ni push, ni transfert sur les vrais dossiers, ni suppression de copie.

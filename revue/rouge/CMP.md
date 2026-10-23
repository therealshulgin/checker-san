# Équipe rouge — Comparaison, instantanés et rapports

Code revu : état non commité du 23 octobre 2026 (sources Core datées de 14:57 au plus tard). DLL utilisées : `FolderChecker.Core\bin\Release\net10.0\FolderChecker.Core.dll` et `FolderChecker.App\bin\Release\net10.0-windows\CheckerSan.dll`. Les essais de ligne de commande lancent le vrai `FolderChecker.Cli\bin\Release\net10.0\checkersan-cli.exe` avec `--log` dans le dossier de travail. Aucun projet du dépôt n'a été compilé ni modifié.

**Programme de reproduction :** `<dossier temporaire>\revue\cmp\Program.cs` (projet `cmp.csproj`, net10.0-windows, références HintPath uniquement). Les données sont créées sous `…\scratchpad\revue\cmp\data\<scénario>-<guid>`. Les sorties brutes sont dans `…\scratchpad\revue\cmp\run1.txt` à `run7.txt`.

- Compilation (bash) : `cd "<dépôt>" && export DOTNET_CLI_HOME="$PWD/.tools/cli" NUGET_PACKAGES="$PWD/.tools/packages" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 APPDATA="$PWD/.tools/appdata" DOTNET_ROOT="$PWD/.tools/dotnet" && ./.tools/dotnet/dotnet build "<W>/cmp.csproj" -c Release --configfile ./NuGet.Config -o "<W>/out"`
- Exécution : `DOTNET_ROOT=… "<W>/out/cmprepro.exe" <scénario…>`, où `<W>` = `<dossier temporaire>\revue\cmp`.

Dans les extraits, les caractères accentués et « · — » de la sortie console redirigée étaient altérés (`�`) : ils sont rétablis. Rien d'autre n'est modifié.

| ID | Gravité | Titre | Type de preuve |
|---|---|---|---|
| CMP-01 | Critique | La ligne de commande et les paires planifiées déclarent « identiques » (code 0) un dossier réel face à une jonction | (a) exécution du vrai CLI + PairCheck |
| CMP-02 | Critique | Le contrôle final ne voit pas une modification qui garde la taille et la date : faux « identiques » | (a) exécution |
| CMP-03 | Haute | Reprise : une empreinte périmée est réutilisée si la date de changement (ChangeTime) est restaurée | (a) exécution |
| CMP-04 | Haute | `checkersan-cli --json` écrit le rapport dans un dossier comparé via une jonction ou `\\?\` | (a) exécution du vrai CLI |
| CMP-05 | Haute | Instantané : deux noms distincts par la casse sont fusionnés sans erreur, une suppression passe inaperçue | (a) exécution du vrai CLI |
| CMP-06 | Moyenne | Chemins longs et noms finissant par un point ou une espace : comparaison impossible ou mal nommée | (a) exécution |
| CMP-07 | Moyenne | `resume.json` est réécrit en entier chaque seconde (coût quadratique) et ignoré au-delà de 128 Mio | (a) mesure |
| CMP-08 | Moyenne | Instantané : la destination n'est contrôlée qu'après la lecture de tous les fichiers | (a) exécution + (b) trace |
| CMP-09 | Basse | Un flux alternatif de la racine comparée (`dossier:rapport.csv`) est accepté par les quatre écrivains | (a) exécution |
| CMP-10 | Basse | `SnapshotEngine.Load` accepte un instantané contradictoire (Complete + Unverified, chemins en double) | (a) exécution |
| CMP-11 | Basse | Deux noms qui ne diffèrent que par la casse, un de chaque côté, sont « Identique » | (a) exécution |

---

## CMP-01 — La ligne de commande et les paires planifiées déclarent « identiques » un dossier réel face à une jonction

- **Gravité :** Critique (faux verdict « identiques » ; perte de données possible).
- **Emplacement :**
  - `FolderChecker.Core/ExitCodes.cs:13-23` : `DifferenceCount` ignore `Skipped`, et `ForComparison` rend `Clean` sans consulter `TreesIdentical` ;
  - `FolderChecker.Cli/CheckerSanCli.cs:79,84,247-249` : le code 0 affiche « OK — Les deux dossiers sont identiques. » ;
  - `FolderChecker.Core/SavedPairs.cs:34,175` : `LastRunText` affiche « identiques » ;
  - `FolderChecker.App/App.xaml.cs:47` : en mode `--scheduled`, le code 0 n'ouvre aucune fenêtre.
- **Scénario :** le dossier 2 (la sauvegarde) contient une jonction `Photos` à la place du vrai dossier `Photos` du dossier 1, par exemple une jonction vers l'original. Le moteur produit des lignes `Skipped` (`ComparisonEngine.cs:575-593`). Il calcule correctement `SourceFullyContained = false` et `TreesIdentical = false` (`ComparisonEngine.cs:250-255`), ce qui suit la règle de PASSATION : « un élément réel face à un lien empêche l'inclusion ». `ExitCodes.ForComparison` ne compte pourtant comme écart que `Different/OnlySource/OnlyTarget/Moved/Conflict`. Il rend donc 0.
- **Preuve :** scénario `linkexit` (`run1.txt`). Le dossier 1 contient `Photos\a.jpg`, `Photos\b.jpg` et `notes.txt`. Le dossier 2 contient `notes.txt` et la jonction `Photos` vers `folder1\Photos`.
  ```
  TreesIdentical=False SourceFullyContained=False TargetFullyContained=True
  row Identical       notes.txt
  row Skipped         Photos / Photos\a.jpg / Photos\b.jpg
  ExitCodes.ForComparison = 0  DifferenceCount = 0
  checkersan-cli exit code = 0
  | Identiques : 1 · absents du 2 : 0 · absents du 1 : 0 · différents : 0 · déplacés : 0 · non vérifiés : 0 · liens non suivis : 3
  | OK — Les deux dossiers sont identiques.
  PairCheck exit code = 0; LastRunText = 23/10/2026 16:15 — identiques
  ```
  L'interface, elle, affiche « Le dossier 1 contient tout le dossier 2 » (`MainWindow.xaml.cs:314`), car elle lit `TreesIdentical`.
- **Impact :** une vérification planifiée de la sauvegarde reste silencieuse et la paire est marquée « identiques ». L'utilisateur supprime alors l'original, alors que la sauvegarde ne contient qu'un lien, qui pointe peut-être vers ce même original : les photos sont perdues. Le rapport JSON du CLI se contredit aussi (`ExitCode: 0`, `TreesIdentical: false`). Les liens de fichiers produisent le même effet.
- **Correctif proposé :** dans `ExitCodes.ForComparison`, ne rendre `Clean` que si `certain && result.TreesIdentical`. Si le résultat est certain mais pas identique sans ligne d'écart, rendre `Incomplete` (2), car le contenu sous le lien n'a pas été comparé. Une autre option est un code dédié documenté. Aligner `CheckerSanCli.Verdict` et `PairRun.LastRunText` sur ce code. **Test :** dans `SnapshotAndCliTests.cs`, créer un dossier réel face à une jonction, puis vérifier que `compare` ne rend pas 0 et n'affiche pas « identical ». Vérifier aussi que `PairCheck.RunAsync(...).ExitCode != Clean`.

## CMP-02 — Le contrôle final ne voit pas une modification qui garde la taille et la date : faux « identiques »

- **Gravité :** Critique (faux verdict « identiques », perte de données possible).
- **Emplacement :**
  - `FolderChecker.Core/ComparisonEngine.cs:153-160` : contrôle final ;
  - `ComparisonEngine.cs:969-985` : `VerifySnapshot` ;
  - `ComparisonEngine.cs:1079-1082` : `SameMetadata` compare Kind, Size, LastWrite, Creation, Attributes et Access, mais ni l'identité ni ChangeTime ;
  - `ComparisonEngine.cs:914,942-949` : l'identité (avec ChangeTime) n'est contrôlée que pendant la lecture, puis elle est oubliée.
- **Scénario :** un fichier déjà haché est modifié en place pendant le reste du scan, à taille égale, puis sa date de modification est restaurée. C'est le cas d'un éditeur de tags audio réglé pour « conserver la date du fichier », avec un remplissage ID3 qui garde la taille. Le nouvel inventaire final ne compare que les métadonnées d'énumération, qui sont inchangées. L'empreinte de l'ancien contenu sert alors de preuve. PASSATION promet pourtant qu'une « mutation empêche un verdict positif ». Le code détecte la date restaurée pour la reprise grâce à ChangeTime, mais pas dans ce contrôle final.
- **Preuve :** scénario `finalcheck` (`run1.txt`, 2ᵉ bloc). Un `IProgress` synchrone modifie `folder1\album\track01.mp3` (9 octets à l'offset 10) quand la phase « Contrôle final » est annoncée, puis rétablit `LastWriteTimeUtc`.
  ```
  (file1 edited in place during the final-check phase, date restored)
  Cancelled=False SnapshotVerified=True Errors=0 FilesReused=0 BytesRead=4194304
  TreesIdentical=True SourceFullyContained=True TargetFullyContained=True
  row Identical       album\track01.mp3  h1=DAC7494485CF5BCE h2=DAC7494485CF5BCE
  actual SHA-256 now: folder1=FE51CADA823D870F folder2=DAC7494485CF5BCE
  ```
- **Impact :** l'app affiche « Les deux dossiers sont identiques… Résultat valable pour l'état contrôlé à la fin du scan », alors qu'à la fin du scan les contenus diffèrent. Si l'utilisateur supprime le dossier 1, la version modifiée est perdue. La fenêtre d'exposition couvre tout le temps entre le hachage d'un fichier et la fin du scan, soit des heures sur un gros arbre. Une modification survenue après le contrôle final reste, elle, hors garantie comme documenté.
- **Correctif proposé :** conserver par chemin l'identité `closing.Identity` (volume, identifiant et ChangeTime) obtenue à la fin de chaque `ReadHash` et `ReadSample`. Au contrôle final, la comparer pour chaque fichier haché ou réutilisé. Deux moyens possibles :
  - une ouverture en `FILE_READ_ATTRIBUTES` sans accès aux données, suivie de `Describe` ;
  - mieux, une énumération `NtQueryDirectoryFile(FileIdExtdDirectoryInformation)`, qui donne ChangeTime et l'identifiant de fichier par lot, sans ouvrir chaque fichier.

  Toute différence, ou une identité devenue indisponible, doit produire une erreur et `Unverified`. **Test :** reprendre ce scénario (modification en place avec date restaurée, déclenchée dans le rappel de la phase finale) et exiger `SnapshotVerified == false` et aucune ligne `Identical` pour ce fichier.

## CMP-03 — Reprise : une empreinte périmée est réutilisée si la date de changement est restaurée

- **Gravité :** Haute. L'effet est un faux « identiques », mais le déclencheur est un programme qui réécrit aussi ChangeTime.
- **Emplacement :** `FolderChecker.Core/ComparisonEngine.cs:917-925`, qui définit la condition de réutilisation. `FolderChecker.Core/FileSafety.cs:180-187` construit l'identité avec l'identifiant de fichier et `BasicInfo.ChangeTime`. `CheckpointData.cs:47` définit `FileIdentity`.
- **Scénario :** sans droits particuliers, un programme peut écrire dans un fichier puis remettre les quatre dates (création, accès, écriture, **changement**) avec `SetFileInformationByHandle(FileBasicInfo)` sur le même handle. Le fichier garde le même identifiant, la même taille et les mêmes attributs. Toutes les conditions de réutilisation sont remplies et l'ancien SHA-256 est repris sans relecture. C'est ce que font les outils qui « restaurent l'horodatage complet » et les outils de restauration de métadonnées NTFS. Le test existant `CheckpointDetectsRestoredTimestamps` ne restaure que la création et l'écriture.
- **Preuve :** scénario `changetime` (`run5.txt`). Première passe avec `CheckpointPath` dans le dossier de travail. `folder2\doc.bin` est modifié (64 octets à l'offset 1000), puis ses dates sont restaurées par `SetFileInformationByHandle`. `ctrl.bin`, modifié de la même façon mais avec la seule date d'écriture restaurée, sert de témoin.
  ```
  doc.bin ChangeTime before=134372386888485927 after=134372386888485927 LastWrite equal=True
  run 2 (resume): FilesReused=3 BytesRead=300000
  row Different       ctrl.bin  h1=10C5800F35ACF35B h2=D64CBF16C9721913   <- témoin détecté
  row Identical       doc.bin   h1=10C5800F35ACF35B h2=10C5800F35ACF35B   <- empreinte périmée
  actual SHA-256 doc.bin: folder1=10C5800F35ACF35B folder2=D64CBF16C9721913
  run 3 (resume, control files removed): FilesReused=2 BytesRead=0
  TreesIdentical=True SourceFullyContained=True TargetFullyContained=True
  checkersan-cli (no checkpoint) exit=1: ÉCARTS — Des différences sont prouvées.
  ```
- **Impact :** avec « Réutiliser les vérifications précédentes », qui est le réglage par défaut du bouton Comparer, l'interface déclare les dossiers identiques sans lire un octet, alors qu'un scan sans cache prouve des écarts. ChangeTime n'est pas une preuve infalsifiable de non-modification.
- **Correctif proposé :** ajouter à `FileIdentity` le dernier USN du fichier, lu par `FSCTL_READ_FILE_USN_DATA` sur le handle ouvert, avec l'identifiant du journal USN. Un processus utilisateur ne peut pas le choisir, et il change à chaque écriture de données. Ne réutiliser une empreinte que si cet USN et le journal sont identiques. Si le journal est absent (USN = 0, FAT, partage), soit relire, soit garder le comportement actuel en documentant explicitement la limite. **Test :** reprendre ce scénario et exiger que `doc.bin` soit relu (`FilesReused` sans lui, ligne `Different`).

## CMP-04 — `checkersan-cli --json` écrit le rapport dans un dossier comparé

- **Gravité :** Haute (écriture hors de l'endroit prévu).
- **Emplacement :** `FolderChecker.Cli/CheckerSanCli.cs:265-279` (`WriteJson`) se contente d'une comparaison de chaînes, `candidate.StartsWith(root + "\")`. Il n'y a ni canonisation, ni contrôle des jonctions ou du préfixe `\\?\`. L'app (`ReportWriter.CreateOutside`), `SnapshotEngine.Save` et `PairCheck.WriteReport` canonisent, eux.
- **Scénario :** `--json` désigne un chemin hors des dossiers, mais qui traverse une jonction menant dans le dossier 2. C'est le cas d'un raccourci de type « dernier rapport » vers un dossier de sauvegarde. Le chemin peut aussi être écrit sous la forme `\\?\C:\…\folder2\r.json`.
- **Preuve :** scénario `clijson` (`run2.txt`). `reports\latest` est une jonction vers `folder2`.
  ```
  first compare exit=0; report inside folder 2? True
  second compare exit=1
  | Identiques : 1 · absents du 2 : 0 · absents du 1 : 1 · …
  | ÉCARTS — Des différences sont prouvées.
  ReportWriter.CreateOutside refuses: Enregistrez le rapport à l'extérieur des dossiers analysés, y compris leurs liens et jonctions.
  ```
  Avec le préfixe, en PowerShell : `checkersan-cli.exe compare …\folder1 …\folder2 --json "\\?\…\folder2\prefixed.json" …` rend `exit=1`. Le contenu de `folder2` devient `a.txt, prefixed.json, report.json`.
- **Impact :** le rapport modifie l'arbre vérifié. Chaque vérification suivante, planifiée ou non, signale un écart fabriqué par l'outil lui-même : `report.json` absent du dossier 1, code 1. Cela viole la promesse « rapports jamais écrits dans un dossier comparé, y compris via une jonction ».
- **Correctif proposé :** déplacer `ReportWriter.CreateOutside` dans Core, par exemple sous le nom `FileSafety.CreateOutside(path, roots)`. L'utiliser dans le CLI (`WriteJson`), l'app, `DuplicateReportWriter` et `CopyPlanWriter`, avec une seule implémentation canonique. **Test :** lancer CLI `compare` avec `--json <jonction vers folder2>\r.json`, puis avec `\\?\<folder2>\r.json`. Exiger le code 3 et aucun fichier créé dans `folder2`.

## CMP-05 — Instantané : deux noms distincts par la casse sont fusionnés sans erreur

- **Gravité :** Haute. L'effet est un faux « correspond exactement », dans un dossier sensible à la casse avec deux fichiers de même taille et de mêmes dates.
- **Emplacement :** `FolderChecker.Core/Snapshot.cs:101,118` : `entries[relative] = …` dans un dictionnaire `OrdinalIgnoreCase`, sans détecter de collision. `Snapshot.cs:61,71,133` : le hachage ouvre `Path.Combine(root, clé)`, avec la casse de la première entrée énumérée, mais la contrôle avec les métadonnées de la dernière. Le moteur de comparaison, lui, signale la collision (`ComparisonEngine.cs:463-469`).
- **Scénario :** dans un dossier sensible à la casse (WSL, projet de développement ; `fsutil file setCaseSensitiveInfo` fonctionne sans droits administrateur sur ce poste), `Report.pdf` et `report.pdf` existent avec la même taille et les mêmes dates. L'instantané ne retient qu'un fichier et se déclare complet. `report.pdf` est ensuite supprimé. Si les tailles ou les dates diffèrent, l'instantané devient incomplet avec un message trompeur : « Le fichier a changé depuis l'inventaire ».
- **Preuve :** scénario `snapcase` (`run2.txt`).
  ```
  files on disk: Report.pdf, report.pdf
  snapshot Complete=True Files=1 [Report.pdf:C1DC79CC8B51] Errors=0
  deleted report.pdf; files on disk now: Report.pdf
  verify: Identical=True Certain=True Differences=0
  checkersan-cli verify exit=0
  | OK — Le dossier correspond exactement à l'instantané.
  control comparison: Errors=1 first=Chemins ambigus, distincts seulement par la casse : « …\Report.pdf » et « …\report.pdf ».
  ```
  Les dossiers `Dir` et `dir` sont fusionnés de la même façon, car un dossier est toujours enregistré avec des dates nulles. La disparition de l'un, s'il est vide, n'est pas signalée.
- **Impact :** un fichier entier peut disparaître sans que la vérification de l'instantané (fenêtre ou `checkersan-cli verify`, code 0) le signale. L'instantané se déclare complet alors qu'il omet un fichier.
- **Correctif proposé :**
  - dans `ListAsync`, si `entries.ContainsKey(relative)`, ajouter une erreur « Chemins ambigus… », comme dans `ComparisonEngine`, pour que `Complete = false` ;
  - garder `info.FullName` de chaque entrée et hacher ce chemin, au lieu de `Path.Combine(root, clé)`.

  **Test :** créer un dossier sensible à la casse avec `fsutil file setCaseSensitiveInfo <d> enable` et deux noms de même taille et de mêmes dates. Exiger `Complete == false` et une erreur citant les deux chemins.

## CMP-06 — Chemins longs et noms finissant par un point ou une espace : comparaison impossible ou mal nommée

- **Gravité :** Moyenne. Sur ce poste, la comparaison ne peut jamais aboutir pour ces arbres, mais elle ne donne jamais de faux « identique ».
- **Emplacement :**
  - `FolderChecker.Core/FileSafety.cs:193-198` : `Stat` appelle `GetFileAttributesExW` sur le chemin brut, sans préfixe `\\?\`. Il est appelé par `ReadEntry` (`ComparisonEngine.cs:541`) avant et après chaque lecture (lignes 835, 871, 903 et 946). Même chose pour `ReparseTag` (`FindFirstFileExW`) et `CanonicalRoot`/`CanonicalDirectory` (`CreateFileW`) ;
  - `ComparisonEngine.cs:500` : `Path.GetRelativePath` normalise le nom et retire le point ou l'espace final.
- **Scénario et preuve :**
  1. **Chemin de 426 caractères** (scénario `longpath`, `run3.txt`), deux arbres identiques :
     ```
     checkersan-cli exit=2
     | Identiques : 0 · … · non vérifiés : 1
     | INCOMPLET — la vérification n'a pas pu tout prouver.
     | ! Impossible de vérifier « …\ffff…\video.mp4 » : The system cannot find the path specified.
     checkersan-cli snapshot exit=0  | OK — Instantané complet.     <- l'instantané, en API .NET, y arrive
     ```
     Un processus déclarant `longPathAware`, comme le manifeste de `CheckerSan.exe` (projet `…\scratchpad\revue\cmp\lp`), réussit sur ce poste : `TreesIdentical=True Errors=0`. La stratégie système LongPathsEnabled y est donc active. L'app échouera comme le CLI sur un Windows à la configuration par défaut, où LongPathsEnabled vaut 0. Ce dernier point n'a pas été exécuté, car le registre est hors limites.
  2. **Noms finissant par un point ou une espace** (scénario `trailingdot`), courants pour des fichiers venus de macOS ou Linux par SMB ou par une archive :
     ```
     folder1 entries: 'contract.pdf' | 'contract.pdf.'
     row Unverified contract.pdf
     error: Chemins ambigus, distincts seulement par la casse : « …\contract.pdf » et « …\contract.pdf. ».
     lone 'draft ' on both sides, identical bytes:
     row Unverified draft
     error: Impossible de vérifier « …\lone1\draft  » : The system cannot find the file specified.
     ```
     Le message (« casse ») et le chemin relatif (`draft` au lieu de `draft `) sont faux. Le contenu réel n'est jamais lu.
- **Impact :**
  - `checkersan-cli compare` et les vérifications planifiées ne peuvent jamais conclure sur une arborescence profonde (vidéos rangées par projet, sauvegardes imbriquées) ;
  - l'app y échoue aussi sous la configuration Windows par défaut ;
  - le diagnostic des noms est trompeur, et les lignes et rapports donnent un chemin relatif inexistant.
- **Correctif proposé :** une fonction `Native(path)` qui renvoie `\\?\<chemin complet>` (ou `\\?\UNC\serveur\partage\…`), pour tous les appels P/Invoke et pour les `FileStream` ouverts sur des entrées énumérées. Calculer le chemin relatif par découpe de `info.FullName` après la racine, plutôt que par `Path.GetRelativePath`. **Tests :** dans `FolderChecker.Tests`, dont l'exécutable n'a pas de manifeste, comparer deux arbres avec un chemin de plus de 300 caractères et exiger `TreesIdentical`. Comparer deux arbres contenant `x.` et `y ` et exiger `Identical`, avec le bon chemin relatif.

## CMP-07 — `resume.json` est réécrit en entier chaque seconde et ignoré au-delà de 128 Mio

- **Gravité :** Moyenne. Il n'y a pas de faux verdict, mais une écriture massive, un ralentissement, et une reprise qui ne fonctionne plus pour les grands arbres.
- **Emplacement :**
  - `FolderChecker.Core/ComparisonEngine.cs:959` : `SaveCheckpoint()` après chaque fichier haché ;
  - `ComparisonEngine.cs:365` : limite d'une sauvegarde par seconde ;
  - `ComparisonEngine.cs:374-388` : sérialisation de **toutes** les entrées, puis `Flush(true)`, sur le thread de lecture ;
  - `FolderChecker.Core/CheckpointData.cs:22` : un fichier de plus de 128 Mio est rejeté ;
  - `MainWindow.xaml.cs:169` : `ReadCheckpointInfo` relit tout le fichier sur le thread de l'interface.
- **Preuve :** scénario `ckptcost` (`run6.txt`, `run7.txt`).
  ```
  20,000 files: final resume.json = 10,555,561 bytes (528 bytes per file); written during the scan 23,379,847 (2.2x)
  128 MiB limit of CheckpointData.Read reached at about 254,307 files (both folders together)
  slow device (10 ms per file, 1 reader): 161469 ms; final resume.json 5,278,822 bytes; written during the scan 413,614,998 bytes (78.4x the final file)
  ```
  La seconde mesure simule un disque dur ou un partage à environ 10 ms par fichier, par une pause dans le rappel de progression après chaque fichier. Pour 20 Mo de données lus, 414 Mo sont écrits dans le journal. Le volume écrit croît comme le carré du nombre de fichiers : c'est le défaut corrigé le 22 octobre pour `copy.json`, mais pas ici.
- **Impact :**
  - environ 127 000 fichiers par côté, par exemple une photothèque, suffisent à dépasser 128 Mio. « Comparer » avec réutilisation, qui est le réglage par défaut, et « Reprendre » rejettent alors le journal (« illisible ou invalide ») et relisent tout ;
  - l'app réécrit pourtant ce journal de plus de 128 Mio environ une fois par seconde pendant tout le scan. Par extrapolation, 250 000 fichiers à 10 ms produiraient plus de 100 Go écrits sur le SSD système, avec un hachage ralenti d'autant.
- **Correctif proposé :** reprendre le format JSON Lines de `copy.json` :
  - une ligne d'en-tête (racines, canoniques, options), puis une ligne ajoutée par empreinte terminée ;
  - un compactage seulement au démarrage, à l'arrêt, ou quand les ajouts dépassent une fraction du fichier ;
  - une lecture en continu, sans limite fixe de 128 Mio, ou avec une limite proportionnelle au nombre d'entrées ;
  - `ReadCheckpointInfo` hors du thread de l'interface, en lisant seulement l'en-tête.

  **Test :** reprendre le test des compteurs d'E/S de `CopyJournalTests`. Pour N fichiers hachés avec un rappel lent, exiger que les octets écrits restent inférieurs à k fois la taille finale du journal. Vérifier aussi la reprise d'un journal de plus de 128 Mio, synthétique.

## CMP-08 — Instantané : la destination n'est contrôlée qu'après la lecture de tous les fichiers

- **Gravité :** Moyenne (échec sans perte de données, mais des heures de lecture perdues dans un cas réaliste).
- **Emplacement :**
  - `FolderChecker.App/SnapshotWindow.cs:75-85` : `SaveFileDialog` avec `OverwritePrompt = false` et sans contrôle préalable, puis `CreateAsync` (lecture complète), puis `Save` ;
  - `FolderChecker.Cli/CheckerSanCli.cs:125-126` : même ordre ;
  - `FolderChecker.Core/Snapshot.cs:184-186` : les seuls contrôles ;
  - `SnapshotWindow.cs:128-130` : en cas d'échec, l'instantané calculé est abandonné.
- **Scénario :** l'utilisateur choisit, ou laisse proposer, un emplacement dans le dossier à décrire, ou un nom déjà existant. Le dialogue ne prévient pas, puisque `OverwritePrompt=false`. Tout le dossier est lu, puis `Save` refuse, et le travail est perdu.
- **Preuve :** scénario `snapsave` (`run6.txt`), même séquence que la fenêtre et le CLI :
  ```
  snapshot.json: refused after reading 67,108,864 bytes in 132 ms: Enregistrez l'instantané à l'extérieur du dossier qu'il décrit.
  existing.json: refused after reading 67,108,864 bytes in 44 ms: The file '…\existing.json' already exists.
  ```
  À 150 Mo/s, un disque de sauvegarde de 2 To est lu pendant environ 3 h 40 avant le refus. Le second message est le texte brut de .NET, en anglais, au lieu d'un message de l'application.
- **Impact :** perte de temps importante pour l'usage visé par la fonction (« sauvegarde débranchée »), et message incohérent.
- **Correctif proposé :** ajouter `SnapshotEngine.ValidateDestination(root, path)`, qui reprend les contrôles de `Save` plus le refus d'un fichier existant. L'appeler avant `CreateAsync`, dans la fenêtre et dans le CLI, en gardant la revalidation dans `Save`. En cas d'échec de `Save` dans la fenêtre, garder l'instantané en mémoire et proposer une autre destination. **Test :** lancer CLI `snapshot <d> <d>\s.json` et exiger le code 3 avec 0 octet lu (compteur de progression) ; refaire le cas d'un fichier existant.

## CMP-09 — Un flux alternatif de la racine comparée est accepté comme rapport ou instantané

- **Gravité :** Basse. L'écriture se fait sur l'objet dossier de la racine, pas dans son arborescence ; le verdict en cours n'est pas faussé.
- **Emplacement :**
  - `FolderChecker.App/ReportWriter.cs:64-78` : le parent de `C:\…\folder1:report.csv` est `C:\…`, donc considéré comme hors racine ;
  - `FolderChecker.Core/Snapshot.cs:184` ;
  - `FolderChecker.Cli/CheckerSanCli.cs:268-273`.

  Aucun ne refuse un `:` dans le nom final. L'interface est probablement protégée par la validation des noms du dialogue Windows : seuls l'API et le CLI sont concernés.
- **Preuve :** scénario `ads` (`run4.txt`) :
  ```
  folder1 LastWriteUtc before=2026-10-23T14:16:15.8486991Z
  ReportWriter.CreateOutside accepted 'folder1:report.csv'
  ReportWriter.Write accepted ':full.json'
  SnapshotEngine.Save accepted ':snap.json'
  CLI --json folder1:cli.json exit=0
  folder1 LastWriteUtc after=2026-10-23T14:16:16.3977826Z
  | 2,005 folder1:cli.json:$DATA | 2,137 folder1:full.json:$DATA | 3 folder1:report.csv:$DATA | 638 folder1:snap.json:$DATA
  ```
- **Impact :** les écrivains modifient la racine comparée : un flux est ajouté et sa date de modification change. Cela contredit « jamais écrit dans un dossier comparé ». La même faille permettrait de placer un `CheckpointPath` sur `racine:resume.json`. La date de la racine changerait alors pendant le scan, et le contrôle final échouerait à chaque fois.
- **Correctif proposé :** dans la future fonction commune `CreateOutside`, et dans `ValidateCheckpointPath`, refuser tout chemin dont `Path.GetFileName(GetFullPath(path))` contient `:`. Une autre option est d'ôter le nom de flux et de contrôler aussi le chemin sans flux comme une racine. **Test :** `CreateOutside(root + ":r.csv")`, `Save(snapshot, root + ":s.json")` et le CLI `--json root:r.json` doivent tous échouer sans créer de flux, ce qui se vérifie par `dir /r`.

## CMP-10 — `SnapshotEngine.Load` accepte un instantané contradictoire

- **Gravité :** Basse. Il faut un fichier fabriqué ou corrompu de manière cohérente.
- **Emplacement :** `FolderChecker.Core/Snapshot.cs:191-202` : seuls le schéma, les chemins de `Files`/`Directories` et le format des empreintes sont contrôlés. `Snapshot.cs:171` : `expected.Unverified` neutralise « Added », et `Certain` fait confiance à `expected.Complete` (ligne 177).
- **Preuve :** scénario `snapload` (`run4.txt`) :
  ```
  Load accepted: Files=2 (duplicate path, sizes 10/999) Complete=True Unverified=1
  verify (Complete=true + Unverified=[intruder.exe]): Identical=True Differences=0
  ```
  Le dossier contient un `intruder.exe` absent de l'instantané d'origine. L'instantané modifié déclare `Complete: true` tout en listant ce fichier dans `Unverified`, et la vérification conclut « correspond exactement ».
- **Impact :** la promesse « Load refuse un fichier endommagé » n'est pas tenue pour les incohérences internes : chemins en double avec des tailles différentes, `Complete` vrai avec des fichiers non vérifiés ou des erreurs, chemins de `Unverified`/`Links` non validés.
- **Correctif proposé :** dans `Load`, refuser :
  - les doublons (sans tenir compte de la casse) dans et entre `Files`, `Directories`, `Unverified` et `Links` ;
  - `Complete == true` quand `Unverified` ou `Errors` ne sont pas vides ;
  - un chemin à la fois fichier et dossier ;
  - des chemins de `Unverified`/`Links` invalides au sens de `Relative()`.

  Une empreinte HMAC n'aurait de sens qu'avec une clé, donc ce n'est pas proposé. **Test :** chacun de ces cas doit lever `InvalidDataException`.

## CMP-11 — Deux noms qui ne diffèrent que par la casse, un de chaque côté, sont « Identique »

- **Gravité :** Basse. Le contenu est identique, seul le nom diffère.
- **Emplacement :** `FolderChecker.Core/ComparisonEngine.cs:46,563-629` : appariement `OrdinalIgnoreCase`. La ligne prend `first.RelativePath` sans comparer la casse de `second.RelativePath`.
- **Preuve :** scénario `casecross` (`run4.txt`). Le dossier 1 contient `Makefile`. Le dossier 2, sensible à la casse, contient `makefile`.
  ```
  TreesIdentical=True SourceFullyContained=True TargetFullyContained=True
  row Identical       Makefile
  folder2 holds 'Makefile'? False
  ```
- **Impact :** « mêmes chemins relatifs » est faux dans un dossier sensible à la casse, ou sur un partage Linux où les deux noms désignent deux fichiers différents. Les programmes qui attendent `Makefile` ne le trouvent pas dans la copie déclarée identique.
- **Correctif proposé :** quand `string.Equals(first.RelativePath, second.RelativePath, StringComparison.Ordinal)` est faux, ne pas classer la ligne `Identical`. Il faut au minimum un détail « noms différents par la casse » et un avertissement qui retire `TreesIdentical`, au moins si l'un des dossiers parents est sensible à la casse (`FileCaseSensitiveInfo`). **Test :** ce scénario doit donner un avertissement et ne pas conclure à des arborescences identiques.

---

## Pistes non prouvées

1. **FAT32/exFAT (disques USB).** Sur ces systèmes, la date de changement n'existe pas ou n'évolue pas. Si `FileIdInfo` y renvoie quand même un identifiant stable, l'identité de reprise (`FileSafety.cs:184-185`) ne change plus lors d'une modification en place avec date d'écriture restaurée. L'empreinte périmée serait alors réutilisée, comme dans CMP-03, mais avec un simple éditeur de tags. Non vérifié : aucun volume FAT n'est disponible sans droits administrateur. Pour le vérifier, interroger `FileIdInfo` et ChangeTime avant et après une écriture sur une clé exFAT.
2. **Même dossier par un chemin UNC local.** Par exemple `C:\Photos` contre `\\localhost\C$\Photos`, ou contre un lecteur mappé vers un partage du même PC. `ValidateSeparateRoots` (`ComparisonEngine.cs:1107-1127`) ne compare que les chaînes canoniques. `CanonicalRoot` (lignes 1129-1146) rend `C:\Photos` d'un côté et `\\localhost\c$\Photos` de l'autre. Le dossier serait comparé à lui-même et déclaré « identique » ; supprimer « une des copies » supprimerait l'unique exemplaire. Non vérifié : les partages sont hors limites. Correctif : comparer aussi le numéro de série du volume et l'identifiant de fichier des deux racines (`FILE_ID_INFO`), et des ancêtres de chacune.
3. **Caches de métadonnées du client SMB** (`FileInfoCacheLifetime`/`DirectoryCacheLifetime`, de l'ordre de 10 s). Sur un partage, l'inventaire final pourrait lire des métadonnées en cache et manquer une modification faite sur le serveur juste avant. Non vérifié, faute de partage.

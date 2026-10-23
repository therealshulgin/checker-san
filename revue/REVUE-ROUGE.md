# Revue adverse — rapport de l'équipe rouge

Revue du code actuel de Checker San (lots 1 à 5, non commités), menée le 23 octobre 2026 par quatre agents « équipe rouge », un par domaine où l'application touche aux fichiers et aux données. Chaque trouvaille devait être prouvée, par une reproduction exécutée ou une trace de code, et venir avec un correctif proposé. Les reproductions ont tourné uniquement dans des dossiers d'essai temporaires, sans toucher aux données de l'utilisateur, à la Corbeille ni au Planificateur.

**Gravités** : Critique = perte, écrasement ou suppression de données, ou faux verdict « identique » ; Haute = écriture ou suppression hors de l'endroit prévu, verdict trompeur, journal qui empêche une reprise ou une restauration ; Moyenne = échec ou blocage sans perte, garantie non tenue dans un cas réaliste ; Basse = robustesse, message.

Les réfutations sont dans `REVUE-BLEUE.md`, et la décision finale dans `ARBITRAGE.md`.

## Synthèse des 39 trouvailles

| ID | Gravité | Titre | Preuve |
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
| COP-01 | Haute | La racine de destination n’est jamais revérifiée : une jonction posée à sa place fait écrire et renommer la copie vérifiée hors du dossier 2, et la passe finale modifie des dossiers étrangers | Reproduction exécutée (oplock + rappel de progression) |
| COP-02 | Moyenne | Les flux de données alternatifs, dont la marque du Web (`Zone.Identifier`), sont perdus sans avertissement ; la comparaison suivante conclut « identiques » | Reproduction exécutée |
| COP-03 | Moyenne | Après une copie 2 → 1, les champs Dossier 1 et Dossier 2 de la fenêtre principale sont inversés | Trace de code |
| COP-04 | Moyenne | Contrôle d’espace calculé sur des clusters de 4 Kio, sans compter les dossiers : sur exFAT/FAT, il laisse démarrer une copie qui remplit le disque | Trace de code + calcul |
| COP-05 | Moyenne | Un dossier parent de destination d’environ 207 à 257 caractères fait échouer chaque fichier : le nom temporaire de 52 caractères dépasse MAX_PATH dans un `CreateFileW` sans préfixe `\\?\` | Reproduction exécutée |
| COP-06 | Moyenne | Le journal enregistre un nom NTFS non conforme à UTF-16 avec U+FFFD : la reprise de cet élément devient impossible | Reproduction exécutée |
| COP-07 | Basse | Un `.checkersan-….partial` abandonné est recopié comme un fichier ordinaire par « Compléter les deux dossiers » | Reproduction exécutée |
| COP-08 | Basse | Un dossier créé dans la destination après la comparaison reçoit les attributs (caché, lecture seule) et les dates du dossier source | Reproduction exécutée |
| COP-09 | Basse | Si la copie a été renommée sans que l’attribut ait été appliqué (plantage, erreur, nouvelle tentative réseau), la reprise la déclare terminée sans attributs | Reproduction exécutée (état de plantage reconstitué) |
| COP-10 | Basse | Si le compactage du journal et sa réouverture échouent tous les deux, `Journal.Record` lève une NullReferenceException : la copie s’arrête au lieu de reporter le compactage | Reproduction exécutée |
| DUP-01 | Haute | Une quarantaine située sous la racine analysée devient la « copie conservée » ; la règle par défaut retire l'exemplaire vivant | (a) exécution `nested` |
| DUP-02 | Haute | Mode Corbeille : le fichier vérifié reste renommable (`FILE_SHARE_DELETE`) et le Shell agit sur le chemin, donc un fichier non vérifié peut partir à la Corbeille, sans trace ou avec une trace fausse | (a) exécution `recycleswap` jusqu'à l'appel Shell + (b) trace |
| DUP-03 | Haute | « Restaurer » suit les jonctions de la quarantaine : un manifeste falsifié déplace des fichiers extérieurs à la racine et supprime des dossiers vides extérieurs | (a) exécution `restorejunction` |
| DUP-04 | Moyenne | Journal écrit après l'action : en cas d'échec d'écriture, la suppression définitive n'est pas journalisée et `DeleteAsync` lève une exception au lieu de rendre son résultat | (a) exécution `logfail` |
| DUP-05 | Moyenne | Cache de reprise : restaurer les quatre dates (dont `ChangeTime`) après une modification produit un faux groupe de doublons | (a) exécution `ctimeforge` |
| DUP-06 | Moyenne | Quarantaine non restaurable si le dossier est ouvert sous un autre nom (alias, lecteur mappé, UNC, nom court), alors que le bouton « Restaurer » est proposé | (a) exécution `restorealias` + (b) trace |
| DUP-07 | Moyenne | Mise à l'écart impossible pour un nom d'un seul caractère (`FILE_RENAME_INFORMATION` trop court, erreur 24) | (a) exécution `onechar` |
| DUP-08 | Basse | Un doublon en lecture seule bloque la suppression définitive et arrête tous les groupes suivants, avec un message qui dit le contraire | (a) exécution `readonly` |
| AUT-01 | Haute | La fenêtre de résultat et « Ouvrir dans Checker San » vivent dans le processus de la tâche : vérifications suivantes ignorées, session arrêtée par le Planificateur au bout de 72 h, code de retour remplacé par 0 | Programme exécuté (code de sortie) et trace de code |
| AUT-02 | Haute | `verify` annonce des fichiers « ABSENT » et des « différences prouvées » (code 1) quand un sous-dossier est seulement illisible ; l'erreur n'est pas affichée | Exécutable réel |
| AUT-03 | Haute | Le rapport `--json` de la ligne de commande s'écrit dans le dossier vérifié via une jonction ou le préfixe `\\?\` ; le contrôle n'a lieu qu'après le scan | Exécutable réel |
| AUT-04 | Moyenne | Ctrl+C pendant `snapshot` ou `verify` : exception non gérée, code de sortie 0xE0434352 au lieu de 2, aucune ligne de journal | Programme exécuté |
| AUT-05 | Moyenne | `--log ""` (ou une racine comme `C:\`) fait planter la commande après la comparaison : le journal d'activité fait échouer l'opération | Exécutable réel |
| AUT-06 | Moyenne | La fenêtre « Paires » réenregistre une copie périmée de la paire : « Planifier » ou « Ne plus planifier » efface le dernier résultat d'une vérification planifiée | Programme exécuté (vraie fenêtre WPF) |
| AUT-07 | Moyenne | Supprimer une paire depuis un état périmé laisse la tâche Windows en place (aucun appel à schtasks) ; tâche impossible à retirer depuis l'app si l'enregistrement échoue après sa création | Programme exécuté et trace de code |
| AUT-08 | Moyenne | Le contrôle « rapport hors des dossiers comparés » ne regarde que le dossier de la liste : rapport écrit dans un dossier comparé et 6 fichiers de l'utilisateur supprimés par la rotation | Programme exécuté |
| AUT-09 | Basse | Le nom des rapports suit le calendrier de Windows : la rotation supprime aussitôt le rapport qu'elle vient d'écrire, et `LastRun.ReportPath` désigne un fichier absent | Programme exécuté |
| AUT-10 | Basse | Analyse des options : une option inconnue ou mal orthographiée est ignorée, un drapeau devient une valeur, `compare … --help` renvoie 0 sans rien comparer | Exécutable réel |


## Équipe rouge — Comparaison, instantanés et rapports

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

### CMP-01 — La ligne de commande et les paires planifiées déclarent « identiques » un dossier réel face à une jonction

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

### CMP-02 — Le contrôle final ne voit pas une modification qui garde la taille et la date : faux « identiques »

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

### CMP-03 — Reprise : une empreinte périmée est réutilisée si la date de changement est restaurée

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

### CMP-04 — `checkersan-cli --json` écrit le rapport dans un dossier comparé

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

### CMP-05 — Instantané : deux noms distincts par la casse sont fusionnés sans erreur

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

### CMP-06 — Chemins longs et noms finissant par un point ou une espace : comparaison impossible ou mal nommée

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

### CMP-07 — `resume.json` est réécrit en entier chaque seconde et ignoré au-delà de 128 Mio

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

### CMP-08 — Instantané : la destination n'est contrôlée qu'après la lecture de tous les fichiers

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

### CMP-09 — Un flux alternatif de la racine comparée est accepté comme rapport ou instantané

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

### CMP-10 — `SnapshotEngine.Load` accepte un instantané contradictoire

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

### CMP-11 — Deux noms qui ne diffèrent que par la casse, un de chaque côté, sont « Identique »

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

### Pistes non prouvées

1. **FAT32/exFAT (disques USB).** Sur ces systèmes, la date de changement n'existe pas ou n'évolue pas. Si `FileIdInfo` y renvoie quand même un identifiant stable, l'identité de reprise (`FileSafety.cs:184-185`) ne change plus lors d'une modification en place avec date d'écriture restaurée. L'empreinte périmée serait alors réutilisée, comme dans CMP-03, mais avec un simple éditeur de tags. Non vérifié : aucun volume FAT n'est disponible sans droits administrateur. Pour le vérifier, interroger `FileIdInfo` et ChangeTime avant et après une écriture sur une clé exFAT.
2. **Même dossier par un chemin UNC local.** Par exemple `C:\Photos` contre `\\localhost\C$\Photos`, ou contre un lecteur mappé vers un partage du même PC. `ValidateSeparateRoots` (`ComparisonEngine.cs:1107-1127`) ne compare que les chaînes canoniques. `CanonicalRoot` (lignes 1129-1146) rend `C:\Photos` d'un côté et `\\localhost\c$\Photos` de l'autre. Le dossier serait comparé à lui-même et déclaré « identique » ; supprimer « une des copies » supprimerait l'unique exemplaire. Non vérifié : les partages sont hors limites. Correctif : comparer aussi le numéro de série du volume et l'identifiant de fichier des deux racines (`FILE_ID_INFO`), et des ancêtres de chacune.
3. **Caches de métadonnées du client SMB** (`FileInfoCacheLifetime`/`DirectoryCacheLifetime`, de l'ordre de 10 s). Sur un partage, l'inventaire final pourrait lire des métadonnées en cache et manquer une modification faite sur le serveur juste avant. Non vérifié, faute de partage.


## Équipe rouge — Copie et écritures

Code revu : arbre de travail non commité (CopyEngine.cs du 22/10 22:21), DLL `FolderChecker.Core\bin\Release\net10.0\FolderChecker.Core.dll` du 23/10 15:47.

Programme de reproduction (aucun fichier du dépôt modifié, tout est écrit sous `work\`) :
`<dossier temporaire>\revue\cop\Program.cs`
- `CopRepro.csproj` : même `app.manifest` que CheckerSan.exe (`longPathAware=true`), référence la DLL compilée.
- `nolp\CopReproNoLP.csproj` : même source, sans manifeste, pour reproduire un poste où les chemins longs ne sont pas activés.

Compilation (bash) :
```
cd "<dépôt>" && export DOTNET_CLI_HOME="$PWD/.tools/cli" NUGET_PACKAGES="$PWD/.tools/packages" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 APPDATA="$PWD/.tools/appdata" DOTNET_ROOT="$PWD/.tools/dotnet"
S="<dossier temporaire>/revue/cop"
./.tools/dotnet/dotnet build "$S/CopRepro.csproj" -c Release --configfile ./NuGet.Config -o "$S/out"
./.tools/dotnet/dotnet build "$S/nolp/CopReproNoLP.csproj" -c Release --configfile ./NuGet.Config -o "$S/out-nolp"
```
Exécution : `"$S/out/CopRepro.exe" <scénario>`, avec `DOTNET_ROOT` défini comme ci-dessus.

| ID | Gravité | Titre | Type de preuve |
|---|---|---|---|
| COP-01 | Haute | La racine de destination n’est jamais revérifiée : une jonction posée à sa place fait écrire et renommer la copie vérifiée hors du dossier 2, et la passe finale modifie des dossiers étrangers | Reproduction exécutée (oplock + rappel de progression) |
| COP-02 | Moyenne | Les flux de données alternatifs, dont la marque du Web (`Zone.Identifier`), sont perdus sans avertissement ; la comparaison suivante conclut « identiques » | Reproduction exécutée |
| COP-03 | Moyenne | Après une copie 2 → 1, les champs Dossier 1 et Dossier 2 de la fenêtre principale sont inversés | Trace de code |
| COP-04 | Moyenne | Contrôle d’espace calculé sur des clusters de 4 Kio, sans compter les dossiers : sur exFAT/FAT, il laisse démarrer une copie qui remplit le disque | Trace de code + calcul |
| COP-05 | Moyenne | Un dossier parent de destination d’environ 207 à 257 caractères fait échouer chaque fichier : le nom temporaire de 52 caractères dépasse MAX_PATH dans un `CreateFileW` sans préfixe `\\?\` | Reproduction exécutée |
| COP-06 | Moyenne | Le journal enregistre un nom NTFS non conforme à UTF-16 avec U+FFFD : la reprise de cet élément devient impossible | Reproduction exécutée |
| COP-07 | Basse | Un `.checkersan-….partial` abandonné est recopié comme un fichier ordinaire par « Compléter les deux dossiers » | Reproduction exécutée |
| COP-08 | Basse | Un dossier créé dans la destination après la comparaison reçoit les attributs (caché, lecture seule) et les dates du dossier source | Reproduction exécutée |
| COP-09 | Basse | Si la copie a été renommée sans que l’attribut ait été appliqué (plantage, erreur, nouvelle tentative réseau), la reprise la déclare terminée sans attributs | Reproduction exécutée (état de plantage reconstitué) |
| COP-10 | Basse | Si le compactage du journal et sa réouverture échouent tous les deux, `Journal.Record` lève une NullReferenceException : la copie s’arrête au lieu de reporter le compactage | Reproduction exécutée |

### COP-01 — La racine de destination n’est jamais revérifiée : une jonction posée à sa place fait écrire et renommer la copie vérifiée hors du dossier 2, et la passe finale modifie des dossiers étrangers
- **Gravité :** Haute (écriture hors de l’endroit prévu, et la copie se dit terminée et vérifiée).
- **Emplacement :**
  - `FolderChecker.Core/CopyEngine.cs:561-572` : `AssertNoLinks` part de `current = root` et ne contrôle que les composants **sous** la racine, jamais la racine elle-même.
  - `CopyEngine.cs:330` : `ValidateRoots` n’est appelé qu’au début de chaque `Transfer()`. Ensuite, tout est résolu par chemin à travers la racine : `EnsureDirectory` (341), `Exists(target)` (344), `OpenPartial(partial)` (368), puis `FileSafety.OpenDirectory(folder)` (439). `OpenDirectory` (`FileSafety.cs:121-136`) n’applique `FILE_FLAG_OPEN_REPARSE_POINT` qu’au dernier composant : une racine devenue jonction est traversée.
  - `CopyEngine.cs:463-478` : la passe finale sur les dossiers n’appelle jamais `ValidateRoots`, ne tient aucun handle et applique `File.SetAttributes`, `Directory.SetCreationTimeUtc` et `Directory.SetLastWriteTimeUtc` par chemin.
- **Scénario :** pendant une copie, le dossier 2 est déplacé et remplacé par une jonction de même nom qui pointe vers un autre dossier du même volume. Ce peut être un outil de déplacement de dossiers, une synchronisation ou un processus hostile. La promesse « Rien n’est jamais copié à travers un lien » n’est plus tenue, alors que le code renomme par handle précisément pour empêcher ce type d’échange (« Neither path can be swapped between a verification and this rename »). Deux fenêtres sont exploitables :
  1. Entre la ligne 330 et `OpenPartial` (368). Pendant l’écriture, le handle du temporaire ouvert sans `FILE_SHARE_DELETE` empêche de renommer ses dossiers parents (scénario `dirrename`/`filedata` : « Access to the path … is denied »).
  2. Toute la passe finale sur les dossiers, sans aucun handle ouvert.
- **Preuve :** reproductions exécutées.
  - Données. `CopRepro.exe oplock` pose un oplock batch sur le fichier source une fois le plan créé. L’ouverture de la source par `Transfer()` (ligne 342, après `ValidateRoots`) déclenche la rupture de l’oplock. À ce moment, le dossier 2 est renommé et une jonction vers `outside` prend sa place ; l’oplock est ensuite libéré.
    ```
    plan items: File:docs\big.bin
    batch oplock requested: immediate=False error=997 (997 = pending, granted)
    oplock break: folder 2 root replaced by a junction to 'outside'
    CopyResult: Complete=True Cancelled=False Completed=1/1 Errors=[]
    outside\docs: [big.bin]
    dst-moved\docs: []
    outside copy identical to source: True
    ```
  - Métadonnées. `CopRepro.exe folderpass` : la source contient `Album` et `Album\2001` (caché, système, dates 2000/2001). Lors du rappel de progression « Élément terminé » du dernier fichier, la racine est remplacée par une jonction vers `outside`.
    ```
    before: outside\Album attrs=Directory created=2026-10-23 14:19 written=2026-10-23 14:19
    CopyResult: Complete=True Cancelled=False Completed=1/1 Errors=[]
    after : outside\Album      attrs=Hidden, System, Directory created=2000-01-01 00:00 written=2001-01-01 00:00
    after : outside\Album\2001 attrs=Hidden, System, Directory created=2000-01-01 00:00 written=2001-01-01 00:00
    after : dst-moved\Album    attrs=Directory created=2026-10-23 14:19 written=2026-10-23 14:19
    ```
- **Impact sur les données :** un fichier complet est créé hors des deux dossiers comparés, sans aucune erreur, et le verdict affiché est « Copie terminée et vérifiée ». Des dossiers existants hors périmètre deviennent cachés/système (ou lecture seule) et perdent leurs dates d’origine. Rien n’est écrasé, car le renommage reste non destructif. Dans le dossier réellement visé, le fichier manque et les dossiers copiés ne reçoivent pas leurs métadonnées.
- **Correctif proposé :**
  1. Dans `RunAsync`, ouvrir chaque racine de destination une fois pour toutes avec `CreateFileW(root, FILE_LIST_DIRECTORY|FILE_TRAVERSE|SYNCHRONIZE, FILE_SHARE_READ|FILE_SHARE_WRITE /* sans DELETE */, OPEN_EXISTING, BACKUP_SEMANTICS|OPEN_REPARSE_POINT)`. Vérifier sur ce handle `FileSafety.Access(handle) != Link` et `GetFinalPathNameByHandle == TargetCanonical`, puis le conserver jusqu’à la fin de la passe des dossiers : la racine ne peut alors être ni renommée ni remplacée.
  2. Faire contrôler aussi la racine par `AssertNoLinks`.
  3. Dans la passe finale, appeler `ValidateRoots`, puis ouvrir chaque dossier en mode sans suivi (`OpenDirectory`) et appliquer attributs et dates sur ce handle (`File.SetAttributes(SafeFileHandle…)`, `File.SetLastWriteTime(SafeFileHandle…)`).
  4. À terme, créer les dossiers intermédiaires et le temporaire relativement au handle du parent (`NtCreateFile` avec `RootDirectory` et `OBJ_DONT_REPARSE`), comme c’est déjà le cas pour le renommage.

  **Tests à ajouter :**
  - (a) Reprendre le scénario `oplock` : oplock batch sur la source après `CreatePlan`, remplacement de la racine lors de la rupture. Attendu : une erreur, aucun fichier dans la cible de la jonction.
  - (b) Reprendre le scénario `folderpass` : remplacement de la racine lors du dernier « Élément terminé ». Attendu : attributs et dates de `outside` inchangés, et une erreur.

### COP-02 — Les flux de données alternatifs, dont la marque du Web (`Zone.Identifier`), sont perdus sans avertissement ; la comparaison suivante conclut « identiques »
- **Gravité :** Moyenne (perte silencieuse de données secondaires et suppression d’une protection de sécurité ; le périmètre « hors flux alternatifs » de la comparaison est documenté, mais rien ne le signale pour la copie).
- **Emplacement :** `CopyEngine.cs:342` et `368-449`. Seul le flux principal est lu (`FileStream`) et écrit (`OpenPartial`/`RandomAccess.Write`) ; aucune énumération de flux (`FindFirstStreamW` / `FileStreamInfo`). L’aperçu de `CopyDialog.cs:25` et le verdict final de `MainWindow.Copy.cs:56-57` n’en disent rien.
- **Scénario :** l’utilisateur complète une sauvegarde contenant des fichiers téléchargés, porteurs de la marque du Web, ou des métadonnées stockées en flux alternatif (balises, données SMB de macOS, etc.).
- **Preuve :** `CopRepro.exe ads`
  ```
  CopyResult: Complete=True Cancelled=False Completed=2/2 Errors=[]
  installer.exe:Zone.Identifier  folder1="[ZoneTransfer] / ZoneId=3 / HostUrl=https://example.invalid/installer.exe / "  folder2="<absent: FileNotFoundException>"
  notes.txt:user.metadata  folder1="data kept only in an alternate stream"  folder2="<absent: FileNotFoundException>"
  comparison after copy: TreesIdentical=True SnapshotVerified=True errors=0
  ```
- **Impact sur les données :** le contenu des flux alternatifs est absent de la copie. Un exécutable ou un document Office copié perd sa marque du Web, donc SmartScreen et le mode protégé ne s’appliquent plus. L’Explorateur et `CopyFileEx`, eux, conservent ces flux. Le verdict « Copie terminée et vérifiée », puis la comparaison qui conclut à des arborescences identiques, laissent croire à une copie fidèle.
- **Correctif proposé :** énumérer les flux nommés de la source sur le handle ouvert (`GetFileInformationByHandleEx(FileStreamInfo)`). Les écrire dans `partial:nom` avant le renommage (ils suivent le fichier), les inclure dans la vérification, et au minimum conserver `Zone.Identifier`. À défaut, l’annoncer clairement dans l’aperçu et dans le verdict final.

  **Test :** une source avec `Zone.Identifier` et un autre flux nommé. Attendu après copie : flux présents et identiques dans la destination.

### COP-03 — Après une copie 2 → 1, les champs Dossier 1 et Dossier 2 de la fenêtre principale sont inversés
- **Gravité :** Moyenne (pas de perte directe, mais le « 1 » et le « 2 » des verdicts suivants changent de sens sans avertissement).
- **Emplacement :** `FolderChecker.App/MainWindow.Copy.cs:32`.
- **Preuve (trace) :**
  1. `CopyDialog.cs:33` propose « Compléter le dossier 1 (2 → 1) ». La ligne 83 appelle alors `CopyEngine.CreatePlan(result!, false)`.
  2. `CopyEngine.cs:73-75` : `source = comparison.TargetRoot` (dossier 2), `target = comparison.SourceRoot` (dossier 1), et `plan.SourceRoot = source`. Les plans unidirectionnels ont des racines orientées (PASSATION, ligne 186).
  3. `MainWindow.Copy.cs:32` : `SourceBox.Text = plan.SourceRoot; TargetBox.Text = plan.TargetRoot;`. Le champ « Dossier 1 » reçoit donc le dossier 2, et inversement.
  4. `Paths_Changed` (`MainWindow.xaml.cs:133-135`) sort immédiatement, car `_result` a été remis à null en `MainWindow.Copy.cs:29`. Aucun message ne signale le changement.
  5. Le « Comparer » suivant, recommandé par le verdict de fin de copie, compare `(dossier 2, dossier 1)`. Les libellés des lignes 278-279 (« Le dossier 2 contient tout le dossier 1 ») portent alors sur les dossiers inversés.

  La reprise d’un plan 2 → 1 enregistré produit le même effet. Aucun test App ne contrôle les champs après une copie (`grep SourceBox FolderChecker.App.Tests` : aucun résultat).
- **Impact sur les données :** l’utilisateur qui vient de compléter « Photos » (1) depuis « Sauvegarde » (2) lit ensuite « Le dossier 2 contient tout le dossier 1 : Oui ». Il comprend « la sauvegarde contient tout Photos », alors que le verdict dit l’inverse ; il risque alors de supprimer ou formater le mauvais côté. S’il choisit de nouveau « Compléter le dossier 1 », la copie part dans l’autre sens et écrit dans le dossier qu’il voulait garder intact. Une paire enregistrée ou une tâche planifiée créée ensuite hérite de l’inversion.
- **Correctif proposé :** conserver l’orientation 1/2 de la comparaison. Pour un plan unidirectionnel 2 → 1, affecter `SourceBox = plan.TargetRoot` et `TargetBox = plan.SourceRoot`. Il suffit pour cela de mémoriser le choix ou d’ajouter un champ `CopyPlan.FromFolder2` persisté. Pour une reprise de schéma 1 sans orientation connue, ne pas modifier les champs.

  **Test App :** comparer A/B, lancer « Compléter le dossier 1 (2 → 1) », puis vérifier `SourceBox.Text == A` et `TargetBox.Text == B`.

### COP-04 — Contrôle d’espace calculé sur des clusters de 4 Kio, sans compter les dossiers : sur exFAT/FAT, il laisse démarrer une copie qui remplit le disque
- **Gravité :** Moyenne (garantie « Un manque bloque la copie avant toute écriture », PASSATION ligne 194, non tenue sur des disques externes courants ; pas de perte).
- **Emplacement :**
  - `CopyEngine.cs:165` : `(remaining + 4095) / 4096 * 4096`, en dur.
  - `CopyEngine.cs:155` : seuls les fichiers sont comptés, les dossiers jamais.
  - `CopyDialog.cs:95-97` : le bouton s’active sur ce résultat.
- **Preuve (trace + calcul) :**
  - Pour les volumes de 32 Gio à 256 Tio, Windows formate en exFAT avec des clusters de 128 Kio par défaut ; c’est le cas de la plupart des disques et clés USB multiplateformes. Chaque fichier y occupe un nombre entier de clusters de 128 Kio, et chaque dossier au moins un cluster.
  - Exemple : 20 000 photos (≈ 3 Mo) et 800 dossiers. Perte moyenne par fichier ≈ 64 Kio − 2 Kio = 62 Kio, soit ≈ 1,18 Gio, plus 800 × 128 Kio = 100 Mio pour les dossiers : environ 1,3 Gio non comptés.
  - Si l’espace libre se situe dans cet écart, `SpaceNeeds` renvoie `Sufficient = true` et la copie démarre. Elle s’arrête ensuite sur l’erreur 112 (`CopyEngine.cs:457-458`), en laissant un temporaire et un disque plein.
  - Sur le C: de test (NTFS, 4096 octets, scénario `cluster`), le calcul est exact : l’écart n’apparaît que sur les volumes à grands clusters.
- **Impact sur les données :** aucune perte. La copie échoue après un long transfert, le disque cible se retrouve saturé, ce qui gêne les autres programmes qui l’utilisent, et le dialogue a affiché un espace suffisant.
- **Correctif proposé :** pour chaque volume de destination, lire la taille de cluster avec `GetDiskFreeSpaceW(root, out spc, out bps, …)` (fonctionne aussi sur UNC), arrondir chaque fichier à ce cluster, ajouter au moins un cluster par dossier à créer et une petite marge. Afficher la taille de cluster dans l’aperçu.

  **Test :** surcharge interne de `SpaceNeeds` avec une taille de cluster injectée. Attendu : 1 000 fichiers d’un octet et 10 dossiers sur des clusters de 128 Kio exigent 1 010 × 128 Kio.

### COP-05 — Un dossier parent de destination d’environ 207 à 257 caractères fait échouer chaque fichier : le nom temporaire de 52 caractères dépasse MAX_PATH dans un `CreateFileW` sans préfixe `\\?\`
- **Gravité :** Moyenne (échec systématique sans perte ; chemin final inférieur à 260 caractères, que .NET et l’Explorateur écrivent sans difficulté).
- **Emplacement :** `CopyEngine.cs:194-202` (`OpenPartial` → `CreateFileW(path, …)` avec un chemin brut) et `CopyEngine.cs:362` (`partial = dossier + ".checkersan-<32 hex>.partial"`, 52 caractères). Même construction dans `FileSafety.OpenDirectory`/`CanonicalDirectory`/`ReparseTag`/`Stat`. Le manifeste `longPathAware` ne suffit pas : Windows exige aussi `LongPathsEnabled=1`, qui vaut 0 par défaut. Bornes : le temporaire dépasse 259 caractères dès que le dossier parent en compte 207 (207 + 1 + 52), alors qu’un fichier final au nom court y tient encore sous 260.
- **Preuve :** `CopReproNoLP.exe longpath` (le processus voit les chemins longs désactivés, `RtlAreLongPathsEnabled()=0`, comme CheckerSan.exe sur un poste Windows par défaut).
  ```
  RtlAreLongPathsEnabled() = 0
  destination folder length=236, final file length=251, partial length=289
  comparison: errors=0 canPlan=True
  CopyResult: Complete=False Cancelled=False Completed=0/1 Errors=[sub\photo-2024.jpg : The system cannot find the path specified. (0x80070003)]
  final file exists=False
  control File.Copy to a 259-char path: exists=True
  ```
  Témoin : sur ce poste, où `LongPathsEnabled=1`, `CopRepro.exe longpath` réussit (`RtlAreLongPathsEnabled() = 1 … Complete=True`).
- **Impact sur les données :** aucun écrasement. Tous les fichiers d’un dossier profond échouent avec un message trompeur (« chemin introuvable »), alors que la comparaison était propre et que l’aperçu proposait la copie.
- **Correctif proposé :** ajouter un utilitaire `Extended(path)` qui ajoute `\\?\` ou `\\?\UNC\` et l’utiliser pour tous les appels natifs prenant un chemin (`OpenPartial`, `OpenDirectory`, `CanonicalDirectory`, `ReparseTag`, `Stat`, `ReportWriter.CanonicalDirectory`). On peut aussi créer le temporaire relativement au handle du dossier parent, et raccourcir son nom.

  **Test dans FolderChecker.Tests :** ce programme n’a pas de manifeste, donc les chemins longs y sont toujours désactivés. Copier vers un dossier de destination d’environ 236 caractères ; attendu : `Complete`.

### COP-06 — Le journal enregistre un nom NTFS non conforme à UTF-16 avec U+FFFD : la reprise de cet élément devient impossible
- **Gravité :** Moyenne (le journal est altéré et la reprise impossible, mais un seul élément est touché, avec une erreur explicite et sans écriture au mauvais endroit).
- **Emplacement :** `CopyEngine.cs:668` (`JsonSerializer.Serialize(stream, _plan)` dans `Journal.Compact`). System.Text.Json remplace une demi-paire de substitution isolée par U+FFFD. Aucun contrôle d’aller-retour n’est fait, ni dans `AddMissingItems` (106-142) ni dans `ReadPlan` (252-281).
- **Scénario :** NTFS accepte tout nom en UTF-16, y compris une demi-paire isolée, ce que produit par exemple un logiciel qui tronque un nom au milieu d’un émoji. La comparaison et la première copie fonctionnent, car le plan en mémoire reste correct. Après une interruption, le plan relu porte un autre nom.
- **Preuve :** `CopRepro.exe surrogate` (fichier `photo\uD83D.jpg`, interruption après le premier fichier, puis reprise).
  ```
  comparison errors=0 rows=OnlySource:10, OnlySource:10
  CopyResult: Complete=False Cancelled=True Completed=1/2 Errors=[]
  journal readable=True; names in journal: … 0070 0068 006F 0074 006F FFFD 002E 006A 0070 0067
  CopyResult: Complete=False Cancelled=False Completed=1/2 Errors=[photo�.jpg : Could not find file '…\src\photo�.jpg'.]
  ```
  Contenu brut du journal : `"RelativePath":"photo\uFFFD.jpg"`.
- **Impact sur les données :** l’élément ne peut plus être repris ; son temporaire éventuel est abandonné, et l’utilisateur doit refaire comparaison et copie. Aucune écriture au mauvais endroit : la source de nom U+FFFD est introuvable avant toute écriture, et l’identité source protégerait un homonyme.
- **Observation connexe :** un nom réservé ou terminé par un point ou une espace (`sub\NUL`, `sub\report.`, `sub\notes `, créés via `\\?\`) ressort de la comparaison déjà déformé (`'\\.\NUL'`, `'sub\report'`). `CreatePlan` lève alors une exception pour tout le plan (scénario `odd` : « CreatePlan THREW ArgumentException: Chemin relatif de copie invalide »), et plus rien n’est copiable. La cause se trouve dans `ComparisonEngine.cs:500` ; côté copie, `AddMissingItems` abandonne le plan entier au lieu d’écarter la ligne.
- **Correctif proposé :** à la création du plan, détecter les chemins mal formés en UTF-16 (`Rune.DecodeFromUtf16` en boucle) ou non réversibles (`Path.GetFullPath` modifie le dernier composant). Les écarter avec un avertissement par ligne, au lieu d’abandonner le plan ou de les journaliser de façon destructive. Ou les journaliser sous une forme sans perte, par exemple un champ base64 des unités UTF-16. Dans `Compact`, vérifier l’aller-retour sérialisation/désérialisation des `RelativePath` avant de remplacer le journal.

  **Test :** nom avec demi-paire isolée, interruption, reprise. Attendu : reprise complète, ou refus explicite dès l’aperçu.

### COP-07 — Un `.checkersan-….partial` abandonné est recopié comme un fichier ordinaire par « Compléter les deux dossiers »
- **Gravité :** Basse (pas de perte : un fragment tronqué se propage dans l’autre dossier et le verdict final « identiques » l’englobe).
- **Emplacement :** `CopyEngine.cs:114-116` : le filtre ne connaît que `OnlineOnly`, aucun nom qui satisfait `ValidPartialName` (497) n’est écarté. Le chemin réaliste passe par `CopyEngine.cs:371`, qui conseille « Conservez-le et démarrez une nouvelle copie depuis un nouveau scan ». Or sans reprise disponible, le dialogue propose « Les deux sens » par défaut (`CopyDialog.cs:103`).
- **Preuve :** `CopRepro.exe partials` (vidéo de 3 Mio, copie 1 → 2 interrompue à 1 Mio, nouvelle comparaison, complétion dans les deux sens).
  ```
  folder 2 after interruption: [.checkersan-306d8a22acc8499b848fa0cd777a5002.partial (1048576 B, Archive)]
  row OnlyTarget .checkersan-306d8a22acc8499b848fa0cd777a5002.partial
  two-way plan: 1->2 video.mp4 (3145733 B), 2->1 .checkersan-306d8a22acc8499b848fa0cd777a5002.partial (1048576 B)
  CopyResult: Complete=True …
  folder 1 now: [.checkersan-306d8a22acc8499b848fa0cd777a5002.partial (1048576 B), video.mp4 (3145733 B)]
  comparison after: TreesIdentical=True
  ```
- **Impact sur les données :** le fragment, qui peut peser des dizaines de Gio pour une grosse vidéo interrompue tard, est dupliqué dans le dossier 1. Il est compté dans le besoin d’espace et ne pourra plus jamais être repris, puisque le journal a été remplacé. Les deux dossiers sont ensuite déclarés identiques.
- **Correctif proposé :** dans `AddMissingItems`, et de préférence dès la comparaison, écarter les noms qui satisfont `ValidPartialName` et les signaler dans l’aperçu (« n temporaire(s) de copie abandonné(s) »).

  **Test :** copie interrompue, nouvelle comparaison. Attendu : `CreateBidirectionalPlan` ne contient aucun `.checkersan-*.partial`.

### COP-08 — Un dossier créé dans la destination après la comparaison reçoit les attributs (caché, lecture seule) et les dates du dossier source
- **Gravité :** Basse (métadonnées d’un dossier existant de l’utilisateur modifiées ; contenu intact).
- **Emplacement :** `CopyEngine.cs:334-339` : l’élément de type Dossier passe `Completed = true` que `EnsureDirectory` l’ait créé ou qu’il ait déjà existé. `CopyEngine.cs:472-474` : la passe finale applique attributs et dates à tous les dossiers terminés.
- **Preuve :** `CopRepro.exe existingdir` (`Projects` caché + lecture seule, daté 2005 dans la source ; l’utilisateur crée son propre `Projects` dans le dossier 2 après le plan).
  ```
  before copy: folder2\Projects attrs=Directory created=2026-10-23 14:21 written=2026-10-23 14:21
  CopyResult: Complete=True Cancelled=False Completed=1/1 Errors=[]
  after copy : folder2\Projects attrs=ReadOnly, Hidden, Directory created=2005-05-05 00:00 written=2005-05-05 00:00
  ```
- **Impact sur les données :** le dossier de travail de l’utilisateur disparaît de l’Explorateur (caché) et ses dates deviennent fausses. Cela contredit l’esprit de « destination existante jamais écrasée ».
- **Correctif proposé :** journaliser si le dossier a été créé par la copie (nouveau champ d’état, par exemple `CreatedByCopy`, à fixer quand `EnsureDirectory` crée réellement le dossier), et n’appliquer attributs et dates qu’à ces dossiers.

  **Test :** reprendre le scénario `existingdir`. Attendu : attributs et dates du dossier préexistant inchangés.

### COP-09 — Si la copie a été renommée sans que l’attribut ait été appliqué (plantage, erreur, nouvelle tentative réseau), la reprise la déclare terminée sans attributs
- **Gravité :** Basse (garantie « attributs conservés » non tenue, dans une fenêtre étroite et sans aucun signalement).
- **Emplacement :**
  - `CopyEngine.cs:442-447` : renommage, puis `File.SetAttributes`.
  - `CopyEngine.cs:344-359` : quand la destination existe avec une empreinte `VerifiedHash` connue, l’élément est marqué terminé sans appliquer `item.Attributes`.
  - La fenêtre se rouvre aussi sans plantage : une erreur transitoire (64, 53…) levée par `SetAttributes` ou par `Identity` sur un partage relance `Transfer()` par `Retry` (ligne 326), ce qui aboutit dans cette même branche.
- **Preuve :** `CopRepro.exe resumeattrs`. L’état laissé par un plantage entre 442 et 447 est reconstitué : fichier renommé avec les attributs par défaut, journal avec `VerifiedHash` et `Completed=false`.
  ```
  normal copy: attrs=ReadOnly, Hidden …
  crash-state journal: Completed=False VerifiedHash=set
  CopyResult: Complete=True Cancelled=False Completed=1/1 Errors=[]
  after resume: attrs=Archive …   (source: attrs=ReadOnly, Hidden …)
  ```
- **Impact sur les données :** la copie est déclarée « terminée et vérifiée » sans ses attributs (lecture seule, caché, système). Une erreur signalée au premier passage disparaît au suivant.
- **Correctif proposé :** dans la branche `Exists(target)`, si l’élément n’était pas journalisé comme terminé avec cette `DestinationIdentity`, appliquer `item.Attributes` (et contrôler les dates) sur le handle vérifié avant de le marquer terminé. Variante : journaliser un état intermédiaire « renommé » avant les métadonnées.

  **Test :** reprendre le scénario `resumeattrs`. Attendu : attributs de la source après la reprise.

### COP-10 — Si le compactage du journal et sa réouverture échouent tous les deux, `Journal.Record` lève une NullReferenceException : la copie s’arrête au lieu de reporter le compactage
- **Gravité :** Basse (robustesse : ni perte ni journal illisible).
- **Emplacement :**
  - `CopyEngine.cs:669` : `_stream = null` avant `File.Move`.
  - `CopyEngine.cs:677-681` : la réouverture peut lever une exception dans le `finally`, et `_stream` reste alors null.
  - `CopyEngine.cs:656` : `catch { _compactedLength = _stream.Length; }` déréférence ce null. La `NullReferenceException` n’est interceptée ni par le `catch` de l’élément (455-460) ni par celui de `RunAsync` (480-481) : `CopyAsync` échoue (tâche en faute) au lieu de renvoyer un `CopyResult`.
- **Preuve :** `CopRepro.exe nre` (1 700 petits fichiers ; le journal passe en lecture seule après le premier fichier, comme pourrait le faire un outil tiers ; le compactage s’enclenche au-delà de 1 Mio d’états).
  ```
  journal marked read-only after 1 file(s); size=720448
  CopyAsync THREW System.NullReferenceException: Object reference not set to an instance of an object.
  at FolderChecker.Core.CopyEngine.Journal.Record(CopyItem item)
  files in folder 2: 1296/1700, partials: 1
  journal readable=True completed=1296 lines=3890
  ```
- **Impact sur les données :** la copie s’arrête sur « Copie non aboutie : Object reference not set… », au lieu de continuer comme l’annonce le commentaire « postpone compaction rather than failing this item ». Le journal reste lisible et reprenable : aucune perte.
- **Correctif proposé :** ne fermer `_stream` qu’après un remplacement réussi. Pour cela, ouvrir le flux avec `FileShare.Read|FileShare.Delete` afin que `File.Move` puisse remplacer le fichier ouvert, puis rouvrir. Si la réouverture échoue, lever une `IOException` explicite (« journal de copie inaccessible ») que la boucle traite comme une interruption. Dans `Record`, ne jamais déréférencer un `_stream` null.

  **Test :** reprendre le scénario `nre`. Attendu : `CopyAsync` renvoie un `CopyResult` avec une erreur claire, et la reprise fonctionne une fois l’attribut retiré.

### Pistes non prouvées
1. **Fichier devenu « en ligne uniquement » entre le plan et la copie.**
   - Constat : `Transfer()` ne consulte jamais `FileSafety.Access` sur la source ouverte (`CopyEngine.cs:342-343`) ; seule l’identité source (`ChangeTime`) protège. En simulation avec l’attribut Offline (scénario `online`), la copie est bloquée tant que `SourceIdentity` est présent (`bytes … = 0`, « La source a changé »). Sans identité, cas d’un volume ou d’un partage qui ne répond pas à `FileIdInfo`, le fichier est lu et copié (`bytes … = 2097152`, `Complete=True`).
   - Reste à vérifier : qu’un vrai déshydratage OneDrive, ou une hiérarchisation HSM sur un partage, modifie bien `ChangeTime`, et quels volumes renvoient une identité nulle.
2. **Reprise après échange de disque.**
   - Constat : `ValidateRoots` (`CopyEngine.cs:553-560`) ne compare que des chemins canoniques avec lettre de lecteur (`GetFinalPathNameByHandleW`, `FileSafety.cs:73-83`). Aucun numéro de série de volume ni identifiant de la racine n’est enregistré dans `CopyPlan`.
   - Hypothèse : en rotation de disques de sauvegarde (même lettre, même dossier), « Reprendre » écrirait dans un disque qui n’a jamais été comparé. Non reproduit faute de second volume.
3. **Périphérique qui refuse les E/S non mises en cache (erreur 87).**
   - Constat : le repli vers une lecture mise en cache n’existe que pour la relecture (`HashFromDisk`, 217-226). `OpenPartial` (197) et `RandomAccess.Write` (410) restent toujours en `FILE_FLAG_NO_BUFFERING`.
   - Hypothèse : sur un tel système de fichiers (pilotes tiers, WebDAV), chaque fichier échouerait au lieu de se replier. Non testé faute de périphérique de ce type.


## Équipe rouge — Doublons et retrait

Code revu : version non commitée du 23 octobre 2026 (sources `FolderChecker.Core` du 22/10, DLL `FolderChecker.Core\bin\Release\net10.0\FolderChecker.Core.dll` du 23/10 à 15:47, plus récente que les sources).

**Programme de reproduction** (un seul projet, un scénario par argument, tout se passe dans le dossier de travail) :

- source : `<dossier temporaire>\revue\dup\proj\Program.cs` (+ `DupRepro.csproj`, `app.manifest` identique à celui de l'application pour `longPathAware`)
- sortie complète de la dernière exécution : `<dossier temporaire>\revue\dup\sortie-all.txt`
- commande (bash, `S` = le dossier `...\scratchpad\revue\dup` ci-dessus) :

```bash
cd "<dépôt>" && export DOTNET_CLI_HOME="$PWD/.tools/cli" NUGET_PACKAGES="$PWD/.tools/packages" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 APPDATA="$PWD/.tools/appdata" DOTNET_ROOT="$PWD/.tools/dotnet" \
 && ./.tools/dotnet/dotnet build "$S/proj/DupRepro.csproj" -c Release --configfile ./NuGet.Config -o "$S/out" \
 && "$S/out/DupRepro.exe" all        # ou : nested | recycleswap | restorejunction | logfail | ctimeforge | restorealias | readonly | onechar
```

Dans les extraits, `<W>` remplace `...\scratchpad\revue\dup\work`. Le mode Corbeille n'a jamais été exécuté : son chemin est reproduit jusqu'à l'appel Shell exclu (réflexion sur `OpenVerified`, `DirectoryLocks` et `RecycledOutcome`), puis tracé dans le code. Aucun journal ni session réels de `%LOCALAPPDATA%\CheckerSan` n'ont été utilisés : `CheckpointPath` et `LogPath` pointent toujours dans `<W>`.

| ID | Gravité | Titre | Type de preuve |
|---|---|---|---|
| DUP-01 | Haute | Une quarantaine située sous la racine analysée devient la « copie conservée » ; la règle par défaut retire l'exemplaire vivant | (a) exécution `nested` |
| DUP-02 | Haute | Mode Corbeille : le fichier vérifié reste renommable (`FILE_SHARE_DELETE`) et le Shell agit sur le chemin, donc un fichier non vérifié peut partir à la Corbeille, sans trace ou avec une trace fausse | (a) exécution `recycleswap` jusqu'à l'appel Shell + (b) trace |
| DUP-03 | Haute | « Restaurer » suit les jonctions de la quarantaine : un manifeste falsifié déplace des fichiers extérieurs à la racine et supprime des dossiers vides extérieurs | (a) exécution `restorejunction` |
| DUP-04 | Moyenne | Journal écrit après l'action : en cas d'échec d'écriture, la suppression définitive n'est pas journalisée et `DeleteAsync` lève une exception au lieu de rendre son résultat | (a) exécution `logfail` |
| DUP-05 | Moyenne | Cache de reprise : restaurer les quatre dates (dont `ChangeTime`) après une modification produit un faux groupe de doublons | (a) exécution `ctimeforge` |
| DUP-06 | Moyenne | Quarantaine non restaurable si le dossier est ouvert sous un autre nom (alias, lecteur mappé, UNC, nom court), alors que le bouton « Restaurer » est proposé | (a) exécution `restorealias` + (b) trace |
| DUP-07 | Moyenne | Mise à l'écart impossible pour un nom d'un seul caractère (`FILE_RENAME_INFORMATION` trop court, erreur 24) | (a) exécution `onechar` |
| DUP-08 | Basse | Un doublon en lecture seule bloque la suppression définitive et arrête tous les groupes suivants, avec un message qui dit le contraire | (a) exécution `readonly` |

---

### DUP-01 — Une quarantaine sous la racine analysée devient la « copie conservée » ; la règle par défaut retire l'exemplaire vivant

- **Gravité :** Haute. La sélection automatique est trompeuse et retire l'exemplaire en place. Le seul exemplaire restant se trouve alors dans un dossier que l'application présente comme une mise à l'écart temporaire. Une fois ce dossier vidé, la donnée est perdue. Il n'y a pas de perte immédiate.
- **Emplacement :**
  - `FolderChecker.Core/DuplicateEngine.cs:294-301` : la quarantaine n'est exclue que si `directory == _root` ;
  - `FolderChecker.Core/DuplicateRemoval.cs:311` : `ValidatePath` ne refuse `.CheckerSan-Quarantine` qu'en premier composant ;
  - `FolderChecker.Core/DuplicateSelection.cs:28-35` : rien n'empêche un « gardien » placé dans une quarantaine ;
  - `FolderChecker.App/DuplicatesWindow.xaml:57` : règle par défaut `FirstPath` (`SelectedIndex="0"`).
- **Scénario :**
  1. L'utilisateur analyse `Photos\2020`, garde `2020\IMG_001.jpg` et met à l'écart `2020\Backup\IMG_001.jpg`. Le fichier part dans `2020\.CheckerSan-Quarantine\<guid>\Backup\IMG_001.jpg`.
  2. Plus tard, il analyse `Photos`. La fenêtre peut s'ouvrir directement sur le dossier 1 de la comparaison (`MainWindow.xaml.cs:101`). La quarantaine imbriquée est alors inventoriée comme des données ordinaires. Elle n'est ni cachée ni système, donc le filtre par défaut ne l'écarte pas.
  3. Le groupe contient la copie en quarantaine et l'exemplaire vivant. En ordre alphabétique, `.` (0x2E) précède les lettres et les chiffres. « Garder le premier chemin » conserve donc la copie en quarantaine et sélectionne `2020\IMG_001.jpg` pour le retrait. `BuildPlan` accepte ce plan.
- **Preuve :** `DupRepro.exe nested`
  ```
  [scan of Photos (parent)] verified=True errors=0 groups=1
     group E3AD6880ED73 size=50000: 2020\.CheckerSan-Quarantine\62fbeef5...\Backup\IMG_001.jpg | 2020\IMG_001.jpg
     rule FirstPath    -> remove: 2020\IMG_001.jpg
     rule Oldest       -> remove: 2020\.CheckerSan-Quarantine\...\Backup\IMG_001.jpg
     rule Newest       -> remove: 2020\IMG_001.jpg
     rule ShortestPath -> remove: 2020\.CheckerSan-Quarantine\...\Backup\IMG_001.jpg
  [permanent removal with default rule (FirstPath)] complete=True ... deleted=1 bytesRemoved=50000
     live copy 2020\IMG_001.jpg exists: False
  ```
  L'arborescence finale ne contient plus que la copie en quarantaine.
- **Impact sur les données :**
  - L'exemplaire de travail disparaît : il est supprimé définitivement, ou mis à la Corbeille avec le mode par défaut.
  - Le contenu ne subsiste que dans la quarantaine. Celle-ci « ne libère aucun espace » : l'utilisateur sera donc tenté de la vider.
  - « Restaurer » remet le fichier à son ancien chemin `Backup\IMG_001.jpg`, et non au chemin retiré.
  - Avec « Garder le plus récent », la copie en quarantaine gagne aussi si elle a été créée après l'original.
- **Correctif proposé :**
  1. Dans `InventoryAsync`, exclure toute quarantaine reconnue par `IsManagedQuarantine`, à n'importe quelle profondeur, avec un avertissement qui la nomme.
  2. Dans `BuildPlan`/`ValidatePath`, refuser un groupe dont le gardien ou un fichier sélectionné possède un composant `.CheckerSan-Quarantine`, à n'importe quel niveau.
  3. Dans `DuplicateSelection.Extras`, ne jamais choisir de gardien sous une quarantaine.
  4. En défense supplémentaire, créer `.CheckerSan-Quarantine` avec l'attribut caché.

  Test à ajouter, `DuplicateNestedQuarantineNeverKeeps` : mettre à l'écart dans `root\sub`, analyser `root`, puis vérifier deux points. Aucun groupe ne contient de chemin sous `sub\.CheckerSan-Quarantine`. `DeleteAsync` refuse un plan forgé dont le gardien est en quarantaine.

### DUP-02 — Mode Corbeille : le fichier vérifié reste renommable et le Shell agit sur le chemin

- **Gravité :** Haute. Le fichier retiré n'est pas celui dont le SHA-256 a été relu. Un fichier non vérifié, éventuellement unique, part à la Corbeille. Selon le cas, rien n'est inscrit au journal, ou le journal affirme à tort une suppression définitive. La Corbeille limite la perte, sauf si Windows la contourne pour un fichier trop volumineux : l'utilisateur confirme alors sur la foi du nom.
- **Emplacement :** `FolderChecker.Core/DuplicateRemoval.cs` :
  - `182-184` et `320-321` : en mode Corbeille, le handle est ouvert sans accès DELETE, avec `FileShare.Read | FileShare.Delete` ;
  - `212` et `430-437` : `Recycle(item.File.FullPath)` transmet un **chemin** à `SHFileOperationW` ;
  - `213-215` et `440-447` : `RecycledOutcome` n'est vérifié qu'après coup ;
  - `220` et `226` : écritures au journal ;
  - `222-227` : branche « trop volumineux ».
  - Mode par défaut : `DuplicatesWindow.xaml:62`.
- **Scénario :**
  - **Fenêtre d'exposition.** Tous les fichiers sélectionnés d'un groupe sont hachés (178-188) avant le premier appel Shell (190-245). Pour un fichier k, cette fenêtre couvre donc le hachage complet des fichiers suivants et les mises à la Corbeille des précédents, soit des minutes pour des vidéos. Le dossier parent épinglé par `DirectoryLocks` n'empêche pas le renommage de ses enfants.
  - **Variante A.** Un autre programme (client de synchronisation, éditeur, script) renomme le fichier vérifié et en écrit un autre au même chemin. Le Shell met à la Corbeille ce nouveau fichier. `RecycledOutcome` voit ensuite le handle ailleurs et lève une `IOException`. Aucune ligne n'est écrite au journal : le fichier non vérifié est dans la Corbeille sans aucune trace.
  - **Variante B, enregistrement « sûr » d'un éditeur (Office, etc.).** Il renomme l'original, écrit la nouvelle version sous le nom d'origine, puis supprime l'ancien. Le handle vérifié passe en *delete pending* et `RecycledOutcome` renvoie `""`. Le code inscrit alors « Supprimé définitivement (trop volumineux pour la Corbeille) » et l'avertissement correspondant, alors que la **nouvelle version** de l'utilisateur est partie dans la Corbeille.
  - On peut aussi substituer un **dossier** au fichier. Le Shell mettrait alors toute son arborescence à la Corbeille.
- **Preuve :** `DupRepro.exe recycleswap`. Le programme reproduit `Remove()` à l'identique jusqu'à l'appel Shell exclu : `DirectoryLocks.PinParent`, puis `OpenVerified(..., shellMayMove: true)` par réflexion.
  ```
  verified handle open (SHA-256 checked, write denied, FILE_SHARE_DELETE granted)
  concurrent rename of the verified file + new file at the same path: succeeded
  path now holds: "UNIQUE USER DATA, never verified"
  RecycledOutcome throws IOException: Le fichier vérifié a été déplacé vers un emplacement inattendu : <W>\...\copy.bin.old
  folder created at the verified path while the handle is open: True
  safe-save sequence (rename away, write new, delete old) while verified handle open: succeeded
  RecycledOutcome -> "" (= code path 'too large for the Recycle Bin, deleted permanently', DuplicateRemoval.cs:222-227)
  path now holds: "NEW VERSION saved by the editor" (this is what SHFileOperationW would have recycled)
  ```
  Suite par la trace du code. La ligne 212 passe ce chemin à `SHFileOperationW` (`FO_DELETE`, `pFrom` = chemin), qui agit sur l'objet présent à ce chemin. En variante A, la ligne 215 n'est pas atteinte et l'`IOException` part en 254 : les lignes 220 et 226 ne sont pas exécutées. En variante B, les lignes 224-226 inscrivent une suppression définitive.
- **Impact sur les données :**
  - Retrait d'un contenu jamais vérifié, qui peut être la dernière version d'un document.
  - Journal absent ou faux.
  - Les modes Quarantaine et Définitif ne sont pas concernés : leur handle refuse `FILE_SHARE_DELETE`.
- **Correctif proposé :**
  1. Pendant la vérification du groupe, garder chaque fichier sélectionné ouvert **sans** `FILE_SHARE_DELETE`, comme dans les autres modes.
  2. Juste avant `Recycle`, ouvrir un second handle avec `FILE_SHARE_DELETE` et `OPEN_REPARSE_POINT`. Comparer son `FileIdInfo` et son volume à ceux du handle vérifié, refuser s'ils diffèrent, puis fermer le premier handle et appeler le Shell immédiatement. La fenêtre passe ainsi de plusieurs minutes à quelques microsecondes.
  3. Inscrire une ligne d'intention au journal avant l'appel Shell.
  4. Si `RecycledOutcome` signale un emplacement inattendu ou un *delete pending* sans dossier `$Recycle.Bin`, écrire au journal « élément non vérifié possiblement mis à la Corbeille : chemin », au lieu de « trop volumineux ».

  Test à ajouter : prévoir un point d'injection interne pour remplacer `SHFileOperationW` par un délégué factice, afin de ne jamais toucher la vraie Corbeille. Échanger le fichier entre la vérification et l'appel, puis vérifier deux points. Le délégué n'est jamais appelé avec un fichier d'identité différente. Le journal contient l'incident.

### DUP-03 — « Restaurer » suit les jonctions de la quarantaine (manifeste falsifié)

- **Gravité :** Haute. La restauration écrit et supprime hors de l'endroit prévu : un fichier situé **hors de la racine** est déplacé dans le dossier analysé, et des dossiers vides extérieurs sont supprimés. La garantie « liens et jonctions jamais suivis » n'est pas tenue. **Précondition :** quelqu'un a pu écrire dans le dossier analysé, par exemple un dossier partagé entre comptes ou un dossier de collaboration. Il faut y créer une jonction, ce qui ne demande aucun droit d'administrateur, et une ligne de manifeste.
- **Emplacement :** `FolderChecker.Core/DuplicateRemoval.cs` :
  - `486` : seule la quarantaine est contrôlée, ni la session ni le chemin stocké ;
  - `493-504` : `File.Exists(stored)` et `new FileStream(stored)` suivent les liens, puis `File.Move(stored, …)` déplace par chemin ;
  - `510` et `533-543` : `RemoveEmptyFolders` vérifie les enfants, mais pas la racine de récursion ;
  - `450-462` : `QuarantineSessions` liste une session-jonction.
- **Scénario :**
  - **(a) Session réelle.** On ajoute dans la session une jonction `evil` vers `Private`, extérieur à la racine, et une ligne `{"RelativePath":"evil\\victim.docx","Hash":…,"Size":…}`. `ValidRelative` l'accepte, car elle ne contient pas `..`. « Restaurer » déplace alors `Private\victim.docx` dans `Shared\evil\victim.docx`.
  - **(b) Session forgée.** Le dossier `<guid>` est lui-même une jonction vers `Private2`, avec un manifeste dont la racine est correcte. La session apparaît dans la liste. La restauration déplace `Private2\salary.xlsx` dans `Shared`, puis `RemoveEmptyFolders(session)` parcourt la cible et supprime `Private2\EmptyProjectFolder`. Pour ce seul effacement de dossiers vides, connaître un nom de fichier suffit : le hachage n'est pas nécessaire, car `RemoveEmptyFolders` s'exécute même si toutes les entrées échouent.
- **Preuve :** `DupRepro.exe restorejunction`
  ```
  restore (a): restored=2 errors=
  victim still in Private: False ; now inside Shared\evil: True
  sessions listed for Restore: ffffffffffffffffffffffffffffffff(1)
  restore (b): restored=1 errors=
  salary.xlsx moved into Shared: True ; still in Private2: False
  Private2\EmptyProjectFolder still exists: False
  ```
  Le journal inscrit d'ailleurs ces déplacements comme des « Restauré » légitimes.
- **Impact sur les données :** un fichier quitte un emplacement privé et arrive dans un dossier où le tiers peut le supprimer : un simple accès en lecture devient ainsi une suppression. Des dossiers vides extérieurs sont aussi effacés.
- **Correctif proposé :**
  1. Dans `Restore`, refuser une session qui est un lien (`FileSafety.Access`), et contrôler chaque composant de `entry.RelativePath` sous la session, comme le fait `DuplicateEngine.ValidatePath`.
  2. Ouvrir `stored` avec `CreateFileW(OPEN_REPARSE_POINT)`. Sur ce handle, vérifier `Access(handle) == Normal`, un seul lien physique, et `FinalPath` égal à `canonical(session)\rel`. Calculer le SHA-256 sur ce même handle, puis faire `RenameByHandle` vers un parent d'origine épinglé, à la place de `File.Move`.
  3. Dans `RemoveEmptyFolders`, ne pas descendre si `directory` est lui-même un point d'analyse.
  4. `QuarantineSessions` doit ignorer les sessions-liens.

  Tests à ajouter : les deux cas (a) et (b) ci-dessus. Les fichiers et dossiers extérieurs doivent rester intacts, et une erreur doit être signalée.

### DUP-04 — Journal écrit après l'action : une suppression définitive peut n'être ni journalisée ni rapportée

- **Gravité :** Moyenne. La garantie documentée « chaque action est inscrite au journal » n'est pas tenue dans un cas réaliste : disque C: plein, comme sur la machine de test, verrou d'antivirus. Les données ne sont pas perdues davantage, mais la seule preuve du retrait disparaît.
- **Emplacement :**
  - `FolderChecker.Core/DuplicateRemoval.cs` :
    - `232-236` : suppression, puis seulement ensuite `log.Write` ; même ordre en `205/208` et `212/220/226` ;
    - `238` : `completed.Add` après le journal ;
    - `254-256` puis `258-262` : `log?.Dispose()` relance l'erreur depuis le `finally` ;
    - `621-627` : `StreamWriter` tamponné ;
    - même schéma pour la restauration en `489/504/506`.
  - `FolderChecker.App/DuplicatesWindow.xaml.cs:235-256` : sur exception, seul le message s'affiche et `ActivityLog.Write` (236) est sauté.
- **Scénario :**
  1. Le journal s'ouvre normalement (160).
  2. Le fichier est supprimé (232-234).
  3. L'écriture de sa ligne échoue (236) : l'erreur est rattrapée en 254, et le chemin n'est pas ajouté à `completed`.
  4. Dans le `finally`, `StreamWriter.Dispose()` tente de vider la même ligne, échoue de nouveau et lève une exception qui sort de `Remove`. `DeleteAsync` échoue alors au lieu de rendre `DuplicateRemovalResult`.
- **Preuve :** `DupRepro.exe logfail`. Une plage d'octets est verrouillée au-delà de la fin du journal, ce qui simule tout échec d'écriture survenu après l'ouverture.
  ```
  DeleteAsync THREW (no DuplicateRemovalResult): IOException: The process cannot access the file because another process has locked a portion of the file.
  copy.bin still exists: False ; keep.bin exists: True
  journal lines: 1      (en-tête seul)
  ```
- **Impact sur les données :**
  - Le fichier est supprimé définitivement sans aucune ligne au journal, ni dans le journal d'activité.
  - L'interface n'affiche que « Opération interrompue » et le message d'E/S, sans liste de ce qui a été retiré.
  - En mode Quarantaine, le manifeste permet encore la restauration. En mode Définitif, plus rien ne prouve ni ne nomme ce qui a disparu.
- **Correctif proposé :**
  1. Écrire et vider (`Flush`) une ligne « intention : chemin, SHA-256, gardien » **avant** chaque mutation. Si cette écriture échoue, ne pas agir. Écrire ensuite la confirmation.
  2. Mettre `completed.Add` et les compteurs à jour immédiatement après la mutation, avant toute écriture au journal.
  3. Entourer `log?.Dispose()` et `manifest?.Dispose()` d'un `try/catch` qui ajoute l'erreur à `errors`, afin que `DeleteAsync` et `RestoreAsync` rendent toujours leur résultat.

  Test à ajouter : reprendre le verrou de plage de `logfail`, ou un point d'injection `TextWriter` qui échoue au deuxième `Write`. Vérifier que `DeleteAsync` ne lève pas d'exception et que de deux choses l'une : soit `DeletedPaths` contient le fichier et le journal contient sa ligne d'intention, soit le fichier est intact.

### DUP-05 — Cache de reprise : un faux groupe de doublons si les quatre dates sont restaurées

- **Gravité :** Moyenne. Formellement, c'est un faux verdict « identique », mais limité :
  - il faut un outil qui remet aussi `ChangeTime`, via `SetFileInformationByHandle(FileBasicInfo)`, comme certains outils de restauration de métadonnées ou d'anti-forensique ;
  - le retrait reste protégé, car `OpenVerified` relit le SHA-256 (`DuplicateRemoval.cs:347-363`) ;
  - la CLI n'est pas exposée, car elle force `ResumeFromCheckpoint = false` (`CheckerSanCli.cs:169-171`).

  La liste, les statistiques et l'export CSV/JSON affichent toutefois des fichiers différents comme des doublons « confirmés par SHA-256 ». Un utilisateur qui agit sur l'export avec un autre outil perd des données. La promesse de `PASSATION.md:141,161` (« une modification avec restauration de la date d'écriture est ainsi détectée ») ne tient que si `ChangeTime` n'est pas restauré.
- **Emplacement :** `FolderChecker.Core/DuplicateEngine.cs:391-404` réutilise `prior` si les métadonnées et l'identité (volume, FileId, `ChangeTime`, lignes 47-48) sont égales. Aucune preuve liée au contenu n'est exigée.
- **Scénario :**
  1. Premier scan avec cache : a.bin et b.bin sont identiques.
  2. b.bin est modifié sur place, à taille égale, puis ses dates de création, d'accès, d'écriture et de changement sont remises.
  3. Le second scan, avec « Réutiliser les vérifications précédentes » coché par défaut, réutilise l'ancien hachage.
- **Preuve :** `DupRepro.exe ctimeforge`
  ```
  real SHA-256: a=222A9840D76D b=F38DBE0AF408 (different: True)
  [second scan with 'Réutiliser' (reused=2)] verified=True errors=0 groups=1
     group 222A9840D76D size=100000: a.bin | b.bin
  [scan without cache] verified=True errors=0 groups=0
  [removal based on the cached result] complete=False ... error: Le contenu de « b.bin » a changé.
  ```
- **Impact sur les données :** le verdict affiché et exporté est faux. Aucune perte n'a lieu dans l'application elle-même.
- **Correctif proposé :**
  - Sur NTFS, ajouter à l'identité mise en cache l'USN du fichier (`FSCTL_READ_FILE_USN_DATA`). Toute écriture le change et `SetFileInformationByHandle` ne peut pas le restaurer. Sans USN disponible, ne réutiliser que les échantillons, jamais les hachages complets.
  - Marquer dans `DuplicateResult` les groupes fondés sur des hachages réutilisés.

  Test à ajouter : le scénario `ctimeforge`. Le second scan ne doit pas réutiliser b.bin et ne doit former aucun groupe.

### DUP-06 — Quarantaine non restaurable si le dossier est rouvert sous un autre nom

- **Gravité :** Moyenne. La garantie « quarantaine restaurable par Restaurer » n'est pas tenue : le bouton apparaît, puis la restauration refuse avec un message trompeur. Aucune perte, et une restauration manuelle reste possible.
- **Emplacement :** `FolderChecker.Core/DuplicateRemoval.cs` :
  - `548` : le manifeste ne stocke que la racine **textuelle** (`root`, ligne 153) ;
  - `486-488` : comparaison textuelle à la racine déduite du chemin de session ;
  - `450-467` : `QuarantineSessions` ne fait pas cette comparaison, d'où `RestoreButton` visible (`DuplicatesWindow.xaml.cs:59-64`).
- **Scénario :** la mise à l'écart a été faite via `Z:\Photos`, un lecteur mappé, ou via un alias de jonction ou un nom court 8.3. La restauration est ensuite demandée via `\\nas\photos` ou via le chemin réel, ou inversement. Le cas des lecteurs mappés et de l'UNC est tracé dans le code, car les partages étaient interdits pendant le test : la comparaison en 487 est purement textuelle.
- **Preuve :** `DupRepro.exe restorealias` (jonction `AliasToData` → `Data`)
  ```
  [quarantine through alias] complete=True ... deleted=1
  sessions shown for the real path: 1
  restore throws InvalidOperationException: Cette mise à l'écart ne peut pas être restaurée : son manifeste est absent ou concerne un autre dossier.
  copy.bin back in place: False
  ```
- **Impact sur les données :** les fichiers restent bloqués dans la quarantaine. L'utilisateur, qui ne sait pas quelle orthographe du chemin utiliser, risque de vider la quarantaine en croyant qu'elle ne sert plus.
- **Correctif proposé :**
  - Ajouter `CanonicalRoot = FileSafety.CanonicalDirectory(root)` dans `ManifestHeader`.
  - Dans `Restore` et dans `QuarantineSessions`, accepter si `CanonicalDirectory(root)` est égal à `header.CanonicalRoot`, en gardant la comparaison textuelle pour les anciens manifestes.
  - Afficher la racine d'origine dans le message de refus.

  Test à ajouter : le scénario `restorealias`. La restauration via le chemin réel doit réussir.

### DUP-07 — Mise à l'écart impossible pour un nom d'un seul caractère

- **Gravité :** Moyenne. La mise à l'écart échoue de façon déterministe pour une classe de noms valides : un caractère, sans extension. L'échec intervient sans perte, mais :
  - l'opération s'arrête ;
  - une entrée fantôme reste dans le manifeste ;
  - la même cause empêche `CopyEngine` de finaliser la copie d'un tel fichier (`CopyEngine.cs:442`, hors de mon périmètre, signalé pour l'équipe Copie).
- **Emplacement :** `FolderChecker.Core/FileSafety.cs:99-113`. La taille allouée et transmise vaut `nameOffset + bytes.Length` (20 + 2 = 22 octets en x64). Or `NtSetInformationFile(FileRenameInformation)` exige au moins `sizeof(FILE_RENAME_INFORMATION)`, soit 24 octets en x64, et répond `STATUS_INFO_LENGTH_MISMATCH`, c'est-à-dire l'erreur Win32 24. Appel concerné : `DuplicateRemoval.cs:205`, après l'écriture du manifeste en 204.
- **Preuve :** `DupRepro.exe onechar`
  ```
  [quarantine ab + a] complete=False ... deleted=1
     error: Windows a refusé la mise à l'écart (code 24) ; aucune suppression définitive n'a été tentée.
     'a' still in place: True, 'ab' still in place: False
     manifest: ... {"RelativePath":"a","Hash":"23336D08...","Size":1000}   <- entrée sans fichier
  RenameByHandle(name="x", 2 bytes) -> Win32 error 24
  RenameByHandle(name="xy", 4 bytes) -> Win32 error 0
  ```
- **Impact sur les données :** aucune perte. La fonction échoue, et tout lot qui contient un tel nom est interrompu.
- **Correctif proposé :** dans `RenameByHandle`, allouer et transmettre `Math.Max(nameOffset + bytes.Length, IntPtr.Size == 8 ? 24 : 16)` octets, avec un remplissage à zéro. On peut aussi déclarer la structure et utiliser `Marshal.SizeOf`.

  Test à ajouter : mettre à l'écart un doublon nommé `a`, et copier un fichier nommé `a` (test côté Copie).

### DUP-08 — Un doublon en lecture seule bloque la suppression définitive et arrête tous les groupes suivants

- **Gravité :** Basse. Il s'agit de robustesse et de message, sans perte. Le cas est réaliste : fichiers importés de CD ou DVD, photos « protégées » sur l'appareil, puis copiées.
- **Emplacement :**
  - `FolderChecker.Core/DuplicateRemoval.cs:231-233` : `FileDispositionInfo` (classe 4) est refusé sur un fichier en lecture seule ;
  - `166` et `254` : l'exception sort de la boucle des groupes ;
  - `336` et `363` : les messages des autres refus ne parlent que de « ce groupe », alors que tous les groupes suivants sont eux aussi abandonnés.
- **Preuve :** `DupRepro.exe readonly`. Deux groupes ; le premier, traité en premier, contient la copie en lecture seule.
  ```
  [permanent removal] complete=False ... deleted=0
     error: Windows a refusé la suppression du fichier vérifié.
     read-only copy still exists: True ; other group copy still exists: True
  [quarantine of the read-only copy] complete=True ... deleted=1
  ```
- **Impact sur les données :** aucune perte. La suppression définitive est impossible pour ces fichiers, et le second groupe, pourtant valide, n'est pas traité. Le message n'indique pas la cause (lecture seule).
- **Correctif proposé :**
  - Utiliser `FileDispositionInfoEx` (classe 21) avec `FILE_DISPOSITION_FLAG_DELETE | FILE_DISPOSITION_FLAG_IGNORE_READONLY_ATTRIBUTE` (0x1 | 0x10) sur le handle vérifié. Sinon, ouvrir avec `FILE_WRITE_ATTRIBUTES` et effacer l'attribut par handle juste avant.
  - Signaler la lecture seule dans l'aperçu.
  - Corriger les messages, ou bien poursuivre avec les groupes suivants quand l'erreur est propre à un groupe.

  Test à ajouter : suppression définitive d'une copie en lecture seule, qui doit réussir ou refuser ce seul groupe avec un message explicite pendant que l'autre groupe est traité.

### Pistes non prouvées

1. **`RecycleBinAvailable` se fonde sur la lettre du chemin textuel** (`DuplicateRemoval.cs:84-91`, `Path.GetPathRoot` puis `GetDriveTypeW`), pas sur le volume qui contient vraiment les fichiers. Trois cas peuvent rendre la Corbeille disponible alors que le volume physique n'en a pas :
   - une racine située sous un dossier de montage (clé USB montée dans `C:\mnt`) ;
   - un lecteur `subst` ;
   - une jonction ancêtre vers un volume amovible.

   Windows supprimerait alors définitivement, après sa propre alerte (`FOF_WANTNUKEWARNING`), et le journal dirait « trop volumineux pour la Corbeille » (222-227). Non testé : `subst`, les points de montage et l'exécution réelle du mode Corbeille sont interdits ici.
2. **Volumes FAT32/exFAT (cartes SD, clés USB).** Si `GetFileInformationByHandleEx(FileIdInfo)` n'y est pas pris en charge, `Identity` vaut null, `SameIdentity` vaut false (`DuplicateRemoval.cs:341,374-383`), et tout retrait est refusé sur ces supports. L'échec est sans risque, mais la fonction devient inutilisable. Non testé : aucun volume FAT n'était disponible.
3. **Racine atteinte par une jonction vers un autre volume fixe, en mode Corbeille.** Le Shell choisit peut-être la Corbeille d'après le chemin (`C:\$Recycle.Bin`) et procède par copie puis suppression. `RecycledOutcome` verrait alors le handle en *delete pending* et inscrirait « Supprimé définitivement (trop volumineux) » alors que le fichier est dans la Corbeille. Non testé, car le mode Corbeille réel est interdit.


## Équipe rouge — Automatisation, ligne de commande et journaux

Code revu : version de travail non commitée (DLL Release du 23 octobre : `FolderChecker.Core.dll` 15:47, `checkersan-cli.dll` 14:58, `CheckerSan.dll` 15:08, toutes postérieures aux sources revues). Aucun fichier du dépôt n'a été modifié, aucun projet du dépôt n'a été compilé, aucune tâche planifiée n'a été créée. Toutes les reproductions passent `--log`, `--pairs` et des chemins de rapport situés dans le dossier de travail ; `TMP`/`TEMP` y étaient redirigés.

Dossier des reproductions (abrégé `AUT\` ci-dessous) : `<dossier temporaire>\revue\aut`
- `AUT\repro-cli.ps1 -Work <dossier>` : scénarios AUT-02, 03, 05 et 10 avec le vrai `FolderChecker.Cli\bin\Release\net10.0\checkersan-cli.exe` ;
- `AUT\cli\Program.cs` (→ `AUT\out-cli\autcli.exe <mode> <dossier>`) : projet console qui référence `FolderChecker.Core.dll` et `checkersan-cli.dll` (AUT-04, 08, 09) ;
- `AUT\wpfpairs\Program.cs` (→ `AUT\out-wpfpairs\wpfpairs.exe <dossier>`) : vraie `PairsWindow` de `CheckerSan.dll`, avec le même démarrage que `FolderChecker.App.Tests` et un faux schtasks (AUT-06, 07) ;
- `AUT\wpfexit\App.xaml.cs` (→ `AUT\out-wpfexit\wpfexit.exe close|open`) : copie de la logique de fenêtres d'`App.RunScheduledAsync` et de `ScheduledResultWindow`, avec le même `void Main()` généré que `CheckerSan.dll` (AUT-01).
- Compilation : la commande indiquée dans la consigne (SDK local), avec `-o AUT\out-…`.

| ID | Gravité | Titre | Type de preuve |
|---|---|---|---|
| AUT-01 | Haute | La fenêtre de résultat et « Ouvrir dans Checker San » vivent dans le processus de la tâche : vérifications suivantes ignorées, session arrêtée par le Planificateur au bout de 72 h, code de retour remplacé par 0 | Programme exécuté (code de sortie) et trace de code |
| AUT-02 | Haute | `verify` annonce des fichiers « ABSENT » et des « différences prouvées » (code 1) quand un sous-dossier est seulement illisible ; l'erreur n'est pas affichée | Exécutable réel |
| AUT-03 | Haute | Le rapport `--json` de la ligne de commande s'écrit dans le dossier vérifié via une jonction ou le préfixe `\\?\` ; le contrôle n'a lieu qu'après le scan | Exécutable réel |
| AUT-04 | Moyenne | Ctrl+C pendant `snapshot` ou `verify` : exception non gérée, code de sortie 0xE0434352 au lieu de 2, aucune ligne de journal | Programme exécuté |
| AUT-05 | Moyenne | `--log ""` (ou une racine comme `C:\`) fait planter la commande après la comparaison : le journal d'activité fait échouer l'opération | Exécutable réel |
| AUT-06 | Moyenne | La fenêtre « Paires » réenregistre une copie périmée de la paire : « Planifier » ou « Ne plus planifier » efface le dernier résultat d'une vérification planifiée | Programme exécuté (vraie fenêtre WPF) |
| AUT-07 | Moyenne | Supprimer une paire depuis un état périmé laisse la tâche Windows en place (aucun appel à schtasks) ; tâche impossible à retirer depuis l'app si l'enregistrement échoue après sa création | Programme exécuté et trace de code |
| AUT-08 | Moyenne | Le contrôle « rapport hors des dossiers comparés » ne regarde que le dossier de la liste : rapport écrit dans un dossier comparé et 6 fichiers de l'utilisateur supprimés par la rotation | Programme exécuté |
| AUT-09 | Basse | Le nom des rapports suit le calendrier de Windows : la rotation supprime aussitôt le rapport qu'elle vient d'écrire, et `LastRun.ReportPath` désigne un fichier absent | Programme exécuté |
| AUT-10 | Basse | Analyse des options : une option inconnue ou mal orthographiée est ignorée, un drapeau devient une valeur, `compare … --help` renvoie 0 sans rien comparer | Exécutable réel |

### AUT-01 — La fenêtre de résultat et « Ouvrir dans Checker San » vivent dans le processus de la tâche
- **Gravité :** Haute (la tâche fait autre chose que prévu, et le Planificateur affiche un résultat trompeur).
- **Emplacement :** `FolderChecker.App/App.xaml.cs:35`, `:47-50` ; `FolderChecker.App/ScheduledResultWindow.cs:62-70` ; `FolderChecker.Core/ScheduledTasks.cs:45` (`IgnoreNew`) et `:49` (`ExecutionTimeLimit PT72H`).
- **Scénario :**
  1. Paire planifiée chaque jour. La vérification de lundi 03:00 trouve un écart. `RunScheduledAsync` ne quitte pas (`:47` ne le fait que pour le code 0) : la fenêtre s'ouvre et le processus reste vivant, en `OnExplicitShutdown` (`:35`), jusqu'à sa fermeture (`:49`). PASSATION.md:315 le confirme pour l'essai réel : « dernier résultat 1 **après sa fermeture** ».
  2. L'utilisateur est absent, ou laisse la fenêtre. Mardi 03:00, l'instance de lundi est encore « en cours d'exécution ». Avec `MultipleInstancesPolicy=IgnoreNew` (`ScheduledTasks.cs:45`), Windows ne lance pas la nouvelle vérification, sans rien afficher. Ce comportement est documenté par Microsoft : la nouvelle instance n'est pas exécutée. Aucune vérification n'a lieu tant que la fenêtre reste ouverte.
  3. Il clique « Ouvrir dans Checker San » : `ScheduledResultWindow.cs:65-69` crée la fenêtre principale **dans le même processus** (celui de la tâche) et passe en `OnMainWindowClose`. L'application complète, avec copie, retrait de doublons ou suppression définitive, tourne désormais comme instance de la tâche. Les vérifications de la paire restent ignorées tant qu'elle est ouverte. Au bout de 72 h comptées depuis le démarrage de la tâche (`ScheduledTasks.cs:49`), le Planificateur arrête la tâche, donc ce processus, éventuellement au milieu d'une copie ou d'un retrait de doublons. PASSATION.md:173 rappelle que ce retrait n'est pas transactionnel.
  4. Quand l'utilisateur ferme la fenêtre principale, WPF quitte avec le code 0. Le Planificateur affiche alors « L'opération a réussi » pour une vérification qui avait prouvé des écarts. C'est contraire à PASSATION.md:290 : « Le code de sortie est celui de la ligne de commande ».
- **Preuve :**
  - Code de sortie : `AUT\out-wpfexit\wpfexit.exe` reproduit à l'identique `App.xaml.cs:35/49/50` et `ScheduledResultWindow.cs:65-70` ; `CheckerSan.dll` a bien pour point d'entrée `Void Main()` (affiché par `wpfpairs.exe`). Commande : `Start-Process wpfexit.exe -ArgumentList close|open -PassThru`, puis `.ExitCode`. Sortie :
    ```
    mode close -> code de sortie du processus = 1
    mode open -> code de sortie du processus = 0
    ```
  - Durée de vie, `IgnoreNew` et 72 h : trace ci-dessus, ainsi que le XML vérifié par le test existant `ScheduledTaskIsReadOnlyAndValid` (`IgnoreNew`, `PT72H`).
- **Impact :**
  - une fenêtre laissée ouverte suspend sans bruit toutes les vérifications suivantes de la paire ;
  - l'application interactive peut être arrêtée de force par le Planificateur ;
  - l'historique du Planificateur montre un succès après un écart prouvé.
- **Correctif proposé :**
  - Le processus planifié doit toujours se terminer juste après la vérification, avec `Shutdown(run.ExitCode)`.
  - Le résultat doit s'afficher hors de la tâche : notification Windows, ou processus séparé `CheckerSan.exe --show-result <id>` lancé en `UseShellExecute`, à vérifier en dehors de l'objet job de la tâche.
  - « Ouvrir dans Checker San » doit démarrer un nouveau processus normal. `MainWindow` accepte déjà `--source`/`--target` (`MainWindow.xaml.cs:85-90`).
  - Tests à ajouter :
    - test WPF : le bouton `ScheduledOpenButton` appelle un lanceur injectable avec les deux dossiers, et le code de sortie reste `run.ExitCode` ;
    - test de l'app : en mode `--scheduled`, le processus se termine même quand le résultat n'est pas « identique ».

### AUT-02 — `verify` annonce des fichiers « ABSENT » quand un sous-dossier est seulement illisible
- **Gravité :** Haute (résultat trompeur : « différences prouvées » pour des fichiers présents, sans mention de l'erreur).
- **Emplacement :** `FolderChecker.Cli/CheckerSanCli.cs:142` (le code 1 l'emporte même si `Certain == false`) et `:146` (seuls les `Unverified` sont affichés, jamais `actual.Errors`). Cause dans le moteur : `FolderChecker.Core/Snapshot.cs:123-126`, où un dossier illisible n'ajoute qu'une erreur, de sorte que ses fichiers ne sont ni listés ni « non vérifiés » ; puis `:169`, qui les déclare alors `Missing`. Même affichage dans `SnapshotWindow.cs:103-114`, hors de mon périmètre.
- **Scénario :** instantané d'un dossier, puis un sous-dossier devient illisible (ACL d'un disque venant d'un autre PC, dossier verrouillé, partage partiellement accessible). `checkersan-cli verify` répond « ABSENT » pour chaque fichier, « ÉCARTS — N différence(s) prouvée(s) » et code 1. Avec la même incertitude, `compare` renvoie 2 (`ExitCodes.ForComparison`) : les deux commandes ne suivent pas la même règle.
- **Preuve :** `AUT\repro-cli.ps1` (section AUT-02). Il crée `f1\Photos\img1-3.jpg`, prend l'instantané, puis `icacls f1\Photos /deny %USERNAME%:(RD)`, lance `verify`, et enfin rétablit les droits avec `/remove:d`. Sortie :
  ```
  ABSENT      Photos\img1.jpg
  ABSENT      Photos\img2.jpg
  ABSENT      Photos\img3.jpg
  ÉCARTS — 3 différence(s) prouvée(s).
    verify : exit=1
    JSON : Certain=False ; Errors=Dossier non entièrement lisible « Photos » : Access to the path '…\f1\Photos' is denied.
  OK — Le dossier correspond exactement à l’instantané.
    droits rétablis : exit=0 ; photos présentes : 3
  ```
- **Impact :** un script ou une tâche qui teste le code 1 conclut à une perte de fichiers, et l'utilisateur peut « restaurer » des fichiers qui n'ont jamais disparu. Rien n'indique, à l'écran, que la vérification était incomplète.
- **Correctif proposé :**
  - `CheckerSanCli.cs:142` : `var code = !verification.Certain ? Incomplete : verification.Differences.Count > 0 ? Differences : Clean;` (même priorité que `ForComparison`) ;
  - afficher `actual.Errors.Take(20)` comme le fait `compare` (`:85`) ;
  - dans `SnapshotEngine.ListAsync`, mémoriser les dossiers illisibles, et dans `Compare`, classer en non vérifiés (et non en `Missing`) les fichiers attendus qui s'y trouvent.
  - Test : refuser la lecture d'un sous-dossier de fixture, puis vérifier le code 2, l'absence de « MISSING » et l'affichage de l'erreur.

### AUT-03 — Le rapport `--json` s'écrit dans le dossier vérifié via une jonction ou `\\?\`
- **Gravité :** Haute (écriture dans un dossier vérifié, malgré la promesse ; la commande « ne modifie jamais rien » modifie l'arbre comparé).
- **Emplacement :** `FolderChecker.Cli/CheckerSanCli.cs:265-279`. `Within` (`:268-272`) ne fait qu'une comparaison de texte après `Path.GetFullPath`, sans chemin canonique. Comparer avec `SnapshotEngine.Save` (`Snapshot.cs:184`) et avec les rédacteurs de l'app (PASSATION.md:203, « avec contrôle des alias »). Appels situés **après** le scan : `:87`, `:148`, `:184`.
- **Scénario :**
  - `compare D:\Data E:\Backup --json C:\Rapports\jour.json`, où `C:\Rapports` est une jonction vers `D:\Data\Rapports`. Ou bien `--json \\?\D:\Data\r.json`.
  - Le rapport tombe dans le dossier 1. La vérification suivante, planifiée par l'utilisateur avec `checkersan-cli`, trouve donc un écart à chaque passage.
  - Quand le refus a lieu (chemin écrit en clair), il n'intervient qu'après une comparaison qui a pu durer des heures : le résultat est perdu et le code devient 3.
- **Preuve :** `AUT\repro-cli.ps1` (section AUT-03), `f1` et `f2` identiques, `j` étant une jonction vers `f1` :
  ```
  OK — Les deux dossiers sont identiques.
    avant : exit=0
  Erreur : Enregistrez le rapport à l’extérieur des dossiers vérifiés.
    --json f1\direct.json : exit=3
  OK — Les deux dossiers sont identiques.
    --json j\via-jonction.json : exit=0
  ÉCARTS — Des différences sont prouvées.
    --json \\?\...\f1\via-prefixe.json : exit=1
    contenu de f1 : a.txt, via-jonction.json, via-prefixe.json
  Identiques : 1 · absents du 2 : 2 · …
    après : exit=1
  ```
  Le nom court 8.3 est, lui, bien refusé, car `GetFullPath` développe les `~`.
- **Impact :** écriture dans l'arbre vérifié (comparaison, instantané `verify`, dossier des `duplicates`). Il en résulte des écarts fabriqués par l'outil lui-même aux vérifications suivantes.
- **Correctif proposé :**
  - Valider la sortie **avant** le scan, puis de nouveau à l'écriture.
  - Pour chaque racine : `FileSafety.IsWithin(root, path)` **ou** `IsWithin(CanonicalDirectory(root), Combine(CanonicalDirectory(ancêtre existant), reste))`, comme `Snapshot.cs:184` et `ComparisonEngine.ValidateCheckpointPath` (`ComparisonEngine.cs:326-348`). Exposer cette aide de manière publique dans Core, car `FileSafety` est `internal`.
  - Refuser aussi un fichier existant avant le scan.
  - Test : jonction vers `f1` et préfixe `\\?\` refusés avec le code 3, aucun fichier créé dans `f1`, aucun scan lancé.

### AUT-04 — Ctrl+C pendant `snapshot` ou `verify` : exception non gérée, code 0xE0434352
- **Gravité :** Moyenne (garantie documentée non tenue : « Ctrl+C arrête proprement », code 2, PASSATION.md:334-335 et `CheckerSanCli.cs:11`).
- **Emplacement :** `FolderChecker.Core/Snapshot.cs:48` (`Task.Run(..., cancellationToken)`), `:66` (`Parallel.ForEachAsync`) et `:107`, qui lèvent `OperationCanceledException`. `FolderChecker.Cli/CheckerSanCli.cs:65` : le filtre n'attrape pas `OperationCanceledException`. `FolderChecker.Cli/Program.cs:8` la laisse s'échapper. `compare` et `duplicates` attrapent l'annulation dans le moteur (`ComparisonEngine.cs:164`, `DuplicateEngine.cs:221`).
- **Scénario :** un script lance `checkersan-cli verify D:\Photos photos.json`, et l'utilisateur fait Ctrl+C (ou le script annule). Au lieu de 2, le processus plante avec une pile .NET, et aucune ligne n'est ajoutée au journal d'activité.
- **Preuve :** `AUT\out-cli\autcli.exe cancel <dossier>`, qui appelle `CheckerSanCli.RunAsync` avec un jeton annulé :
  ```
  compare    Ctrl+C -> code 2
  duplicates Ctrl+C -> code 2
  snapshot   Ctrl+C -> EXCEPTION NON ATTRAPÉE par RunAsync : System.Threading.Tasks.TaskCanceledException: A task was canceled.
  verify     Ctrl+C -> EXCEPTION NON ATTRAPÉE par RunAsync : System.Threading.Tasks.TaskCanceledException: A task was canceled.
  Lignes du journal d'activité : CLI snapshot (avant), CLI compare, CLI duplicates   ← rien pour snapshot/verify annulés
  ```
  Code de sortie du processus : `autcli.exe host 60 verify <f1 de 3000 fichiers> snap.json --log …` reprend les trois lignes de `Program.cs` et annule après 60 ms, au lieu de Ctrl+C dont le gestionnaire se contente d'appeler `stop.Cancel()`. Sortie : `verify Ctrl+C exit code = -532462766 (0xE0434352)`, identique pour `snapshot`, et `Unhandled exception. System.Threading.Tasks.TaskCanceledException … CheckerSanCli.cs:line 140`.
- **Impact :** les codes stables 0/1/2/3 ne le sont plus ; un appelant qui teste `-ge 3` ou `-eq 2` se trompe ; la trace est perdue.
- **Correctif proposé :**
  - Faire renvoyer par `SnapshotEngine.Create` un instantané `Complete=false` avec une erreur « annulé », comme le font les autres moteurs ; refuser alors `Save` d'un instantané annulé.
  - À défaut, ajouter dans `RunCoreAsync` `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)` → message, ligne de journal, `return Incomplete`.
  - Test : `RunAsync(["snapshot"|"verify", …], jeton annulé)` renvoie 2 et écrit une ligne de journal.

### AUT-05 — `--log ""` ou `--log C:\` fait planter la commande après la comparaison
- **Gravité :** Moyenne (garantie documentée non tenue : « une ligne impossible à écrire ne fait jamais échouer l'opération », PASSATION.md:336 et `ActivityLog.cs:7-8`).
- **Emplacement :**
  - `FolderChecker.Core/ActivityLog.cs:19` : `""` n'est pas remplacé par le chemin par défaut ;
  - `:25` : `Path.GetFullPath("")` lève `ArgumentException`, et `GetDirectoryName("C:\")` vaut `null`, ce qui mène à `ArgumentNullException` ;
  - `:29` : le filtre n'attrape ni l'une ni l'autre ;
  - `FolderChecker.Cli/CheckerSanCli.cs:68` : `ActivityLog.Write` est appelé de nouveau **dans** le bloc `catch`, et l'exception s'échappe.
- **Scénario :** script `checkersan-cli compare A B --log "$LOG"` avec une variable vide. La comparaison complète s'exécute, le verdict s'affiche, puis le processus plante.
- **Preuve :** `AUT\repro-cli.ps1` (section AUT-05) :
  ```
  --log "" -> exit code -532462766 (0xE0434352) ; stdout : ÉCARTS — Des différences sont prouvées. ; stderr : Erreur : The path is empty. (Parameter 'path') / Unhandled exception. System.ArgumentException: The path is empty.
  --log C:\ -> exit code -532462766 (0xE0434352) ; stdout : ÉCARTS — Des différences sont prouvées. ; stderr : Erreur : Value cannot be null. (Parameter 'path') / Unhandled exception. System.ArgumentNullException
  ```
- **Impact :** un verdict prouvé (1) devient un plantage (0xE0434352) ; c'est le journal « au mieux » qui fait échouer l'opération.
- **Correctif proposé :**
  - Dans `ActivityLog.Write`, mettre **tout** le corps, y compris `GetFullPath` et `GetDirectoryName`, dans `try { … } catch (Exception e) when (e is not OutOfMemoryException) { }`, et traiter un répertoire `null`.
  - Dans le CLI, refuser `--log` vide à l'analyse, avant tout scan, avec le code 3.
  - Tests : `--log ""`, `--log C:\` et `--log <dossier existant>` renvoient le code de la comparaison.

### AUT-06 — « Planifier » / « Ne plus planifier » efface le dernier résultat d'une vérification planifiée
- **Gravité :** Moyenne (résultat trompeur dans la liste des paires : un écart récent disparaît).
- **Emplacement :** `FolderChecker.App/PairsWindow.cs:115-118` et `:126`. La paire réenregistrée est `Selected`, lue lors du dernier `Refresh` (`:142`), puis copiée avec `with { Schedule = … }`. `FolderChecker.Core/SavedPairs.cs:85-86` : `Save` remplace l'enregistrement entier, `LastRun` compris. `RecordRun` (`:93-98`) protège le cas inverse, mais pas celui-ci.
- **Scénario :** la fenêtre « Paires… » est ouverte, ou l'a été avant la nuit. À 03:00, la vérification planifiée (autre processus) enregistre « 42 écarts ». Le matin, l'utilisateur change l'heure et clique « Planifier ». Le résultat de la nuit est remplacé par celui que la fenêtre avait chargé (« Jamais vérifiée » ou un ancien « identiques »). Un nom ou des dossiers modifiés depuis une autre instance sont écrasés de la même façon.
- **Preuve :** `AUT\out-wpfpairs\wpfpairs.exe <dossier>` construit la vraie `PairsWindow`, appelle `PairStore.RecordRun` comme le fait la tâche, puis déclenche `SchedulePairButton`. Sortie :
  ```
  [Planifier] Avant le clic, pairs.json : 23/10/2026 16:17 — 42 écart(s)
  [Planifier] Appels schtasks : /Create · statut : « Photos » : chaque dimanche à 03:00. …
  [Planifier] Après le clic, pairs.json : Jamais vérifiée
  ```
  On obtient la même chose au niveau du moteur avec `autcli.exe stale`.
- **Impact :** l'utilisateur ne voit plus qu'un écart a été prouvé, alors que la fenêtre de résultat a pu être fermée sans être lue.
- **Correctif proposé :**
  - Ajouter à `PairStore` des modifications ciblées sous le verrou : `SetSchedule(id, schedule?)` et `UpdateFolders(id, name, f1, f2, moved)`. Elles relisent la paire et ne changent que ces champs, comme `RecordRun`. Les utiliser dans `PairsWindow` (`:90`, `:118`, `:126`).
  - Test WPF : fenêtre ouverte, puis `RecordRun`, puis clic sur Planifier, Ne plus planifier ou Enregistrer : `LastRun` est conservé.

### AUT-07 — Suppression depuis un état périmé : la tâche Windows reste orpheline
- **Gravité :** Moyenne (la tâche continue d'ouvrir chaque jour « Vérification planifiée impossible — la paire n'existe plus », et l'app ne peut plus la retirer).
- **Emplacement :** `FolderChecker.App/PairsWindow.cs:104`. `Unregister` n'est appelé que si la copie **en mémoire** a un `Schedule`, alors qu'`Unregister` interroge déjà la tâche (`ScheduledTasks.cs:73-77`). `:117-118` : `Register` est appelé avant `PairStore.Save`, sans retour arrière si `Save` échoue. `:153` : « Ne plus planifier » n'est actif que si `pair.Schedule` est renseigné.
- **Scénario :**
  - (a) Deux instances : la fenêtre « Paires » de l'instance A est ouverte, la paire n'y est pas planifiée. Dans l'instance B (par exemple celle ouverte depuis un résultat planifié), l'utilisateur la planifie, puis il la supprime dans A. Aucun appel à schtasks n'a lieu : la paire disparaît, la tâche reste.
  - (b) `Register` réussit mais `Save` échoue (fichier occupé ou verrouillé par un antivirus, nom pris entre-temps). La paire apparaît « Non planifiée », « Ne plus planifier » est grisé et « Supprimer » ne retire pas la tâche.
- **Preuve :** (a) `wpfpairs.exe` : vraie `PairsWindow`, faux schtasks qui garde la liste des tâches, réponse « Oui » automatique à la confirmation. Sortie :
  ```
  [Supprimer] Tâche « Checker San - Sauvegarde (78326391) » présente : True; paire planifiée dans pairs.json : True
  [Supprimer] Appels schtasks pendant la suppression : aucun
  [Supprimer] Paires restantes : 0; tâche Windows encore présente : True (Checker San - Sauvegarde (78326391))
  [Supprimer] Statut : Paire « Sauvegarde » supprimée.
  ```
  (b) Trace pas à pas : `:117` crée la tâche, `:118` lève une exception attrapée par `Act` (`:163-165`) ; le magasin garde `Schedule = null`, d'où `:153` (bouton grisé) et `:104` (aucun `Unregister`).
- **Impact :** la tâche d'une paire supprimée s'exécute indéfiniment (code 3 chaque jour) jusqu'à une intervention manuelle dans le Planificateur.
- **Correctif proposé :**
  - À la suppression, relire la paire et **toujours** appeler `ScheduledTasks.Unregister(pair)`.
  - Si `PairStore.Save` échoue après `Register`, appeler `Unregister` (retour arrière).
  - Activer « Ne plus planifier » selon `ScheduledTasks.Exists(pair)`.
  - Tests WPF : suppression avec une copie périmée → `/Query,/Delete` ; échec de `Save` après `/Create` → `/Delete`.

### AUT-08 — Le contrôle « rapport hors des dossiers comparés » ne regarde que le dossier de la liste
- **Gravité :** Moyenne (écriture et suppression dans un dossier comparé, contrairement à PASSATION.md:278, dans un cas peu fréquent : comparer le dossier `Reports`).
- **Emplacement :** `FolderChecker.Core/SavedPairs.cs:197-201`. On teste seulement si le **dossier de la liste** se trouve dans une racine, pas si le dossier réellement écrit, `Reports\<id>` (`:202`), est dans une racine. La racine `…\Reports` ou `…\Reports\<id>` passe donc le contrôle. Ensuite, `:219-220` supprime tout fichier de `Reports\<id>` correspondant au motif, au-delà des 20 plus récents.
- **Scénario :** paire « Archives » : `%LOCALAPPDATA%\CheckerSan\Reports` ⇄ `G:\Sauvegarde\Reports`. Avec `--pairs`, toute liste dont le dossier voisin `Reports` est comparé convient aussi. Chaque vérification écrit son rapport dans le dossier 1, donc un écart perpétuel, et la rotation y supprime des fichiers.
- **Preuve :** `AUT\out-cli\autcli.exe reports-inside <dossier>`, qui passe par `CheckerSanCli.RunAsync(["run","Archives","--pairs",…])`. Le dossier 1 est `store\Reports`, avec 25 fichiers de l'utilisateur dans `0a1b2c3d\`. Sortie :
  ```
  Avant : 25 fichier(s) dans le dossier 1 comparé (…\inside-…\store\Reports)
  checkersan-cli run -> code 1
  Rapport : …\store\Reports\0a1b2c3d\2026-10-23 161609.json
  Rapport écrit DANS le dossier 1 comparé : True
  Fichiers de l'utilisateur restants dans le dossier 1 : 19 sur 25 (supprimés : 6)
  ```
- **Impact :** la vérification « en lecture seule » modifie et supprime des fichiers d'un dossier comparé.
- **Correctif proposé :**
  - Contrôler le dossier de destination réel `Reports\<id>` dans les deux sens (destination dans une racine, ou racine dans la destination), de façon canonique, en remontant au premier ancêtre existant comme `ComparisonEngine.ValidateCheckpointPath`.
  - Pour la rotation, ne supprimer que des fichiers reconnus comme nos rapports : nom strict `^\d{4}-\d{2}-\d{2} \d{6}\.json$` **et** contenu `"Application": "Checker San"` avec le bon `Pair` (ou un manifeste des rapports écrits).
  - Test : une paire dont le dossier 1 est `Reports` → rapport refusé, aucun fichier supprimé.

### AUT-09 — Le nom des rapports suit le calendrier de Windows ; la rotation supprime le rapport qu'elle vient d'écrire
- **Gravité :** Basse (cas d'un Windows au calendrier thaï, persan ou hégirien, ou d'une horloge qui recule).
- **Emplacement :** `FolderChecker.Core/SavedPairs.cs:204`. `$"{DateTime.Now:yyyy-MM-dd HHmmss}"` utilise `CurrentCulture`. L'app impose fr-FR ou en-US, mais la ligne de commande ne change jamais `CurrentCulture` (`CheckerSanCli.cs:37-39` ne règle que `L`). `:219-220` trie par nom et supprime, sans épargner le fichier qui vient d'être écrit.
- **Scénario :** sur un Windows en th-TH, `checkersan-cli run` écrit des rapports « 2569-… », tandis que la vérification planifiée de l'app écrit « 2026-… ». Le rapport de l'app est classé le plus ancien et supprimé immédiatement, alors que `PairRun.ReportPath` le désigne. « Ouvrir le rapport » ne fait alors rien (`ScheduledResultWindow.cs:59`). Même effet si l'horloge recule : pile de la carte mère, correction NTP.
- **Preuve :** `AUT\out-cli\autcli.exe culture <dossier>` :
  ```
  CLI run sous th-TH -> code 0, rapport : 2569-10-23 161610.json
  Vérification sous fr-FR -> code 0, ReportPath = 2026-10-23 161610.json, existe : False
  LastRun.ReportPath enregistré existe : False
  Rapports restants : 20, le plus récent par nom : 2569-10-23 161610.json
  ```
- **Impact :** perte du rapport de la dernière vérification et lien mort dans la paire.
- **Correctif proposé :**
  - Écrire le nom avec `DateTime.Now.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture)`.
  - Exclure `path` de la rotation, et trier sur la date analysée en culture invariante (ou `CreationTimeUtc`).
  - Test : sous th-TH, le nom commence par l'année grégorienne ; avec 20 rapports « futurs », le nouveau rapport est conservé.

### AUT-10 — Analyse des options : options inconnues ignorées, drapeau pris pour une valeur, `--help` → 0
- **Gravité :** Basse (robustesse et code de sortie).
- **Emplacement :** `FolderChecker.Cli/CheckerSanCli.cs:30-36` et `:40`.
  - Toute option `--x` prend l'argument suivant, sans liste d'options connues (`:33`).
  - Les drapeaux sont comparés à la casse près (`:32`), alors que le dictionnaire l'ignore.
  - `--help` combiné à une commande renvoie `Clean` (`:40`), alors que 0 signifie « rien à signaler ».
- **Scénario :** `checkersan-cli compare A B --jsno rapport.json` dans une tâche : aucune erreur, aucun rapport (le script relit éventuellement un ancien rapport, jamais remplacé). `--json --no-moved` crée un fichier `--no-moved` dans le dossier courant, et la recherche des déplacements reste active.
- **Preuve :** `AUT\repro-cli.ps1` (section AUT-10) :
  ```
  --jsno : exit=1 ; rapport écrit : False
  --json --no-moved : exit=1 ; fichier créé : --no-moved
  compare ... --help : exit=0 (aucune comparaison)
  ```
- **Impact :** une faute de frappe dans une commande planifiée passe inaperçue.
- **Correctif proposé :**
  - Liste blanche des options par commande ; refus (code 3, avant tout scan) d'une option inconnue, d'une valeur commençant par `--` et d'une option propre à une autre commande.
  - Drapeaux comparés sans tenir compte de la casse.
  - Code 3 lorsque l'aide remplace une vérification demandée.
  - Tests : les trois commandes ci-dessus renvoient 3, et aucun fichier n'est créé.

### Pistes non prouvées
1. **Chemins relatifs dans une paire.** `PairStore.Save` (`SavedPairs.cs:77`) et `PairsWindow` (`:90`) enregistrent le texte saisi tel quel. `NormalizeRoot` le résout ensuite avec le dossier courant du processus (`ComparisonEngine.cs:1094`). La tâche n'a pas de `WorkingDirectory` (`ScheduledTasks.cs:50`), et le Planificateur utilise alors son dossier par défaut (System32), si bien que la tâche comparerait d'autres dossiers que l'app ou le CLI. Non exécuté avec le vrai Planificateur.
2. **Priorité de la tâche.** `Priority 7` (`ScheduledTasks.cs:49`) abaisse la priorité processeur, et probablement d'E/S, de la tâche. La fenêtre principale ouverte depuis un résultat planifié (AUT-01) en hériterait pour ses copies. Non mesuré.
3. **Verrou de `PairStore` lié à l'écriture du chemin** (`SavedPairs.cs:140`). Les alias 8.3 sont normalisés par `GetFullPath`, et l'essai `autcli.exe alias` n'a perdu aucune mise à jour sur 81. En revanche, une liste désignée via une jonction ou un lecteur `subst` (`--pairs`) obtiendrait un autre mutex, donc des mises à jour perdues entre l'app et le CLI. Non testé, pour ne pas créer de lecteur `subst`.


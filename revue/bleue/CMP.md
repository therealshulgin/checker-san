# Équipe bleue — Comparaison, instantanés et rapports

Code examiné : état non commité du 23 octobre 2026, avec les mêmes DLL que l'équipe rouge (`FolderChecker.Core\bin\Release\net10.0\FolderChecker.Core.dll`, `FolderChecker.App\bin\Release\net10.0-windows\CheckerSan.dll`, `FolderChecker.Cli\bin\Release\net10.0\checkersan-cli.exe`). Aucun fichier du dépôt n'a été modifié ni compilé.

**Contre-preuves :** `<dossier temporaire>\revue\cmp-bleu\Program.cs` (projet `bleu.csproj`, net10.0-windows + WPF, références HintPath uniquement). Les données sont créées sous `…\cmp-bleu\data\<scénario>-<guid>`. Les sorties brutes sont dans `…\cmp-bleu\run-*.txt`.

- Compilation (bash) : `cd "<dépôt>" && export DOTNET_CLI_HOME="$PWD/.tools/cli" NUGET_PACKAGES="$PWD/.tools/packages" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 APPDATA="$PWD/.tools/appdata" DOTNET_ROOT="$PWD/.tools/dotnet" && ./.tools/dotnet/dotnet build "<B>/bleu.csproj" -c Release --configfile ./NuGet.Config -o "<B>/out"`
- Exécution : `DOTNET_ROOT=… "<B>/out/bleu.exe" <scénarios…>`, avec `<B>` = `…\scratchpad\revue\cmp-bleu`.
- Le verdict de l'application est obtenu sans fenêtre : thread STA, `new App().InitializeComponent()`, `AppLanguage.Apply("fr", persist: false)`, puis `new MainWindow()`, affectation de `_result` et appel de `ShowResult` par réflexion, comme dans `FolderChecker.App.Tests/Program.cs:492-501`. Les textes lus sont `VerdictTitle`, `VerdictDetail`, `ContainsSourceVerdict` et `ContainsTargetVerdict`.
- La ligne de commande est le vrai `checkersan-cli.exe`, avec `--log` dans le dossier de travail. `PairCheck` utilise un `pairs.json` dans le dossier de travail. Aucune reprise ne touche `%LOCALAPPDATA%\CheckerSan` : soit `CheckpointPath` est explicite, soit il est absent.

| ID | Gravité rouge | Verdict bleu | Gravité proposée | Correctif rouge : avis |
|---|---|---|---|---|
| CMP-01 | Critique | Concédé | Critique | Correct sur le fond, incomplet : messages des paires, de la ligne de commande et de l'aide |
| CMP-02 | Critique | Partiellement réfuté (gravité). La portée est en revanche **plus large** que décrite | Haute | Correct sur le principe. L'option « énumération », présentée comme meilleure, est insuffisante (liens physiques). À étendre aux instantanés |
| CMP-03 | Haute | Partiellement réfuté | Basse sur NTFS (à réévaluer si la piste exFAT se confirme) | De faible valeur : `resume.json` est lui-même falsifiable. Alternative plus simple proposée |
| CMP-04 | Haute | Partiellement réfuté (gravité) | Moyenne | Correct. Ajouter le refus des flux (CMP-09) et une validation avant le scan |
| CMP-05 | Haute | Partiellement réfuté (gravité) | Moyenne | Correct. Garder le refus en cas de collision : `Compare` ne supporte pas les doublons |
| CMP-06 | Moyenne | Concédé | Moyenne | Correct. Étape immédiate peu coûteuse : manifeste `longPathAware` pour le CLI |
| CMP-07 | Moyenne | Concédé | Moyenne | Correct. Alternative plus simple sans changer le format |
| CMP-08 | Moyenne | Partiellement réfuté (gravité) | Basse | Correct. Contrôler aussi l'existence du dossier parent |
| CMP-09 | Basse | Concédé | Basse | Correct |
| CMP-10 | Basse | Partiellement réfuté | Info (robustesse) | Correct, mais sans valeur de sécurité |
| CMP-11 | Basse | Concédé | Basse | Risqué s'il est appliqué sans condition : le limiter aux dossiers sensibles à la casse |

---

## CMP-01 — La ligne de commande et les paires planifiées déclarent « identiques » un dossier réel face à une jonction

- **Verdict :** Concédé.
- **Vérification de la preuve rouge :** code relu.
  - `ExitCodes.cs:13-23` : `DifferenceCount` ignore `Skipped`, et `ForComparison` ne lit jamais `TreesIdentical`.
  - `CheckerSanCli.cs:79,84,247-253`, `SavedPairs.cs:31-38,175` et `App.xaml.cs:47` : tous suivent ce code.

  Scénario relancé (`bleu.exe links`, `run-links-final.txt`) :
  ```
  (a) dossier réel 1\Photos face à une jonction 2\Photos
    TreesIdentical=False SourceFullyContained=False TargetFullyContained=True
    ExitCodes.ForComparison = 0 ; formule du correctif rouge = 2
    | Identiques : 1 · … · liens non suivis : 3
    | OK — Les deux dossiers sont identiques.
    PairCheck.ExitCode = 0 ; LastRunText (fenêtre Paires) = 23/10/2026 16:40 — identiques
    rapport de paire : ExitCode=0 TreesIdentical=False
  ```
- **Arguments et preuve :** aucune réfutation n'est possible.
  - **Droits :** `mklink /J` fonctionne sans droits administrateur. Un lien symbolique de *fichier* exige en revanche un privilège : `mklink` (fichier) a répondu « You do not have sufficient privilege ». La remarque « les liens de fichiers produisent le même effet » vaut donc seulement pour des liens déjà présents (créés par un administrateur, en mode développeur ou par WSL). Les jonctions suffisent au scénario.
  - **Ce n'est pas un comportement voulu :** PASSATION.md:125 pose la règle « un élément réel face à un lien empêche l'inclusion ». Le moteur l'applique (`ComparisonEngine.cs:250-255`), et le test `JunctionSkipped` la vérifie (`FolderChecker.Tests/Program.cs:566-571`). Aucun test ne vérifie le code de sortie dans ce cas : le test CLI (`SnapshotAndCliTests.cs:60`) ne couvre que des dossiers sans lien. Le texte de l'aide (« ils n'empêchent pas le verdict », `MainWindow.xaml.cs:425`) contredit cette règle et doit être corrigé.
  - **Verdict de l'application :** la fenêtre principale est **correcte** :
    ```
    [a: jonction d'un seul côté (2)]
       titre   : Le dossier 1 contient tout le dossier 2
       détail  : … 3 élément(s) concerné(s) par des liens ou jonctions non suivis, exclus du verdict.
       inclus. : Le dossier 2 contient tout le dossier 1 : Non | Le dossier 1 contient tout le dossier 2 : Oui
    ```
    Le faux « identiques » touche tous les consommateurs d'`ExitCodes` :
    - `checkersan-cli compare` et `run` ;
    - le mode `--scheduled`, qui quitte avec 0 sans fenêtre (`App.xaml.cs:47`) ;
    - le texte « dernière vérification » de la fenêtre Paires (`PairsWindow.cs:143`) ;
    - le rapport JSON de la paire, qui se contredit (`ExitCode=0`, `TreesIdentical=False`).
  - **Le cas inverse est plus courant que celui du rouge.** Une jonction dans le dossier 1 face à un dossier réel dans le dossier 2 rend aussi 0 (scénario (c) : `TreesIdentical=False Source=True Target=False`, `ForComparison = 0`). C'est exactement l'arborescence que produit `robocopy` (qui suit les jonctions par défaut) en sauvegardant un profil où l'utilisateur a « déplacé » un dossier vers un autre disque en laissant une jonction. La vérification planifiée reste alors muette, alors que le contenu de la jonction n'est jamais comparé à la sauvegarde. Aucun droit particulier, aucune modification pendant le scan, et un résultat entièrement reproductible. Je maintiens donc la gravité Critique pour le canal automatisé, dont le but est justement d'alerter.
  - **Contrôle :** des jonctions des deux côtés donnent `TreesIdentical=True`, avec le code 0 aujourd'hui comme avec le correctif. L'app affiche alors « Les deux dossiers sont identiques… 1 élément(s) concerné(s) par des liens… exclus du verdict ». Ce cas est cohérent et ne doit pas changer.
- **Gravité proposée :** Critique.
- **Avis sur le correctif :** correct sur le fond. Rendre `Clean` seulement si `certain && TreesIdentical`, sinon `Incomplete`, donne le résultat attendu dans les trois cas mesurés : jonction d'un seul côté → 2, des deux côtés → 0, cas inverse → 2. Aucun test existant ne devrait casser, d'après la lecture du code (je n'ai pas lancé les suites du dépôt). Le correctif reste **incomplet** sur les messages :
  - avec le correctif, `LastRunText` affiche « incomplète (0 erreur(s), 0 non vérifié(s)) », ce que j'ai calculé avec `pair with { LastRun = run with { ExitCode = 2 } }`. C'est incompréhensible : `PairRun` doit compter ces lignes, par exemple avec un champ `Links` ou en les ajoutant à `Unverified`. `ScheduledResultWindow` doit aussi les nommer ;
  - `checkersan-cli compare` doit écrire une ligne d'explication, par exemple « ! N élément(s) réel(s) face à un lien : contenu non comparé » ;
  - l'aide (`MainWindow.xaml.cs:425`, et la phrase anglaise à la ligne 434) et l'aide du CLI (« 2 vérification incomplète ») doivent mentionner ce cas.

  Le code 2 (et non 1) est cohérent avec « contenu non comparé ». Pour les utilisateurs qui gardent volontairement cette arborescence, il y aura une fenêtre à chaque passage planifié. C'est le comportement juste, mais il faut l'annoncer.

## CMP-02 — Le contrôle final ne voit pas une modification qui garde la taille et la date : faux « identiques »

- **Verdict :** Partiellement réfuté (gravité). Le défaut est réel, et même **plus large** que décrit.
- **Vérification de la preuve rouge :**
  - `VerifySnapshot` (`ComparisonEngine.cs:969-985`) ne compare que `SameMetadata` (`:1079-1082`), sans `ChangeTime` ni identité ;
  - les entrées de fichiers du dernier inventaire gardent les métadonnées d'énumération (`ReadEnumeratedEntry`, `:515-535`).

  Scénario relancé (`bleu.exe final`, `run-2.txt`) :
  ```
  (a) écriture en place, date de modification remise
    SnapshotVerified=True Errors=0  TreesIdentical=True   row Identical album\track01.mp3
    ChangeTime du fichier : avant=134372401364397855 après=134372401365671313 (différent=True)
    SHA réel : folder1=FE51CADA823D870F folder2=DAC7494485CF5BCE
  [APP] titre : Les deux dossiers sont identiques
        détail : … confirmés par SHA-256. Résultat valable pour l'état contrôlé à la fin du scan.
  ```
- **Arguments et preuve :**
  - **Contre le rouge (conditions étroites) :**
    - Une écriture qui change la taille ou la date est détectée, même si l'éditeur reste ouvert pendant le contrôle final. Scénario (b), ajout de 1 Mio, et scénario (b2), écriture en place sans date remise : `SnapshotVerified=False`, ligne `Unverified`, « Élément ajouté, supprimé ou modifié pendant la comparaison ». `bleu.exe probe` montre que, sur ce NTFS, l'entrée de répertoire est à jour même pendant qu'un éditeur garde le fichier ouvert.
    - Si l'attribut Archive avait été effacé, l'écriture le remet et la modification est détectée. Scénario (c) : `attributs après l'édition = Archive`, `SnapshotVerified=False`.
    - Le cas du rouge exige donc les quatre conditions suivantes :
      1. une écriture en place, à taille égale ;
      2. une date de modification remise ;
      3. un attribut Archive déjà présent, ce qui est le cas courant ;
      4. une écriture placée entre le hachage du fichier et le dernier inventaire.
    - Enfin, la documentation demande de ne pas modifier les dossiers pendant la vérification : PASSATION.md:428 (limite 5) et l'aide, `MainWindow.xaml.cs:425`.
  - **Pour le rouge (portée plus large) :**
    - Un **lien physique** suffit, sans aucune date remise (`bleu.exe hardlink`, `run-hardlink.txt`). Le fichier du dossier 1 a un second nom hors des deux arbres (`mklink /H`, sans droits administrateur). Après son hachage, il est modifié par ce second nom : écriture en place et ajout de 1 Mio. NTFS ne met à jour que l'entrée du nom utilisé, donc le dernier inventaire lit une entrée périmée :
      ```
      énumération du dossier 1 : taille=2097152 écriture=…14:44:06.8247833Z | handle (par l'autre nom) : taille=3145728 écriture=…14:44:07.0060967Z
      SnapshotVerified=True Errors=0 TreesIdentical=True   row Identical video.mp4
      SHA réel : folder1=826C82147C60DB70 folder2=DAC7494485CF5BCE
      [APP] titre : Les deux dossiers sont identiques
      ```
      Les tailles diffèrent même de 1 Mio. Pour être transparent : un premier essai (`run-3.txt`) a été détecté, parce que ma sonde avait ouvert le fichier par son nom du dossier 1. La fermeture de ce handle rafraîchit l'entrée de répertoire. L'essai corrigé n'ouvre le fichier que par l'autre nom.
    - Le moteur d'**instantanés** a la même faiblesse (`Snapshot.cs:81-84` : seul l'enregistrement `Listed` est comparé). `bleu.exe snapfinal` (`run-snapfinal.txt`) modifie un fichier pendant une vérification, après son hachage, et remet sa date :
      ```
      vérification : Complete=True Identical=True Differences=0
      SHA réel maintenant = 10C64D64D36E341E ; SHA enregistré = 4FFA6AC78EC49A62
      ```
  - **Canaux touchés :** tous, car la faiblesse est dans le moteur : app, CLI, paires planifiées et fenêtre d'instantanés.
  - **Gravité :** il y a bien un faux « identiques », que la limite documentée ne suffit pas à excuser, puisque PASSATION.md:128 promet qu'une mutation empêche un verdict positif. Mais tout déclencheur exige une modification *pendant* le scan, que la documentation demande d'éviter. La perte se limite alors à cette modification concurrente, et seulement si l'utilisateur supprime ensuite la copie modifiée. Exemples réalistes :
    - éditeur de tags réglé pour conserver la date ;
    - conteneur VeraCrypt, dont l'option de conservation de la date est à ma connaissance cochée par défaut (non vérifié ici) ;
    - modification par un autre nom d'un fichier à liens physiques.

    Ces cas existent mais sont moins probables que l'arborescence statique de CMP-01. Je propose Haute.
- **Gravité proposée :** Haute.
- **Avis sur le correctif :**
  - **Principe correct :** comparer au contrôle final l'identité (volume, identifiant, `ChangeTime`) relevée en fin de lecture, ou à l'ouverture en cas de réutilisation, pour chaque fichier qui fonde une ligne `Identical` ou `Moved`, des deux côtés. Dans tous les cas détectables sans falsification, `ChangeTime` change : dans le cas (a), il passe de 134372401364397855 à 134372401365671313.
  - **L'option « mieux, une énumération » est insuffisante.** Une entrée de répertoire est périmée pour un fichier à liens physiques modifié par un autre nom (preuve ci-dessus). Un `ChangeTime` lu par énumération serait périmé de la même façon. Il faut une **requête par handle** : `CreateFileW(…, FILE_READ_ATTRIBUTES, partage RWD)` puis `Describe`, déjà disponible dans `FileSafety.cs:180-187`. Une variante acceptable : l'énumération `FileFullDirectoryInfo`, plus une requête par handle pour les fichiers dont `NumberOfLinks > 1`. Ce nombre se lit sur le handle déjà ouvert dans `ReadHash`.
  - **Coût mesuré** (`bleu.exe fixcost`, 10 000 fichiers sur NVMe) :
    - ouverture 0x80 et deux requêtes : 514 à 553 ms, soit 51 à 55 µs par fichier ;
    - énumération : 8 à 20 ms.

    Le coût est du même ordre que les deux `Stat` déjà faits par fichier haché (`ComparisonEngine.cs:903,946`). Il est acceptable. Sur SMB, cela ajoute un aller-retour par fichier, non mesuré faute de partage.
  - **Risques de régression :**
    - si l'identité était absente dès le départ (système de fichiers sans `FileIdInfo`), garder le comportement actuel : seule une identité *devenue* indisponible doit être une erreur. Le rouge le dit bien ;
    - un changement d'ACL pendant le scan rendra le verdict incomplet. C'est prudent et acceptable ;
    - la lecture elle-même ne change pas `ChangeTime` : la réutilisation fonctionne après lecture (`FilesReused=2`, `run-resume.txt`).
  - **À étendre :** appliquer la même correction à `SnapshotEngine.Create` (`Snapshot.cs:131-153`, puis `:81-84`). Le test proposé est bon. Y ajouter le cas du lien physique modifié par un autre nom, et le même scénario pour une vérification d'instantané.

## CMP-03 — Reprise : une empreinte périmée est réutilisée si la date de changement est restaurée

- **Verdict :** Partiellement réfuté.
- **Vérification de la preuve rouge :** la condition de réutilisation est en `ComparisonEngine.cs:917-925`, l'identité en `FileSafety.cs:184-185`. Scénario relancé (`bleu.exe resume`, `run-resume.txt`) :
  ```
  doc.bin : ChangeTime avant=134372404817715246 après=134372404817715246 ; USN avant=406080198008 après=406080201200
  passe 2 : FilesReused=2 BytesRead=0 TreesIdentical=True   row Identical doc.bin
  SHA réel doc.bin : folder1=10C5800F35ACF35B folder2=D64CBF16C9721913
  [APP] titre : Les deux dossiers sont identiques
        détail : … Reprise : 2 empreintes réutilisées après contrôle de l'identité et des métadonnées des fichiers.
  ```
- **Arguments et preuve :**
  - **Portée :**
    - seule la comparaison interactive de l'app est concernée. Elle utilise la reprise par défaut : `ReuseHashesCheck IsChecked="True"`, `MainWindow.xaml:26`, et `MainWindow.xaml.cs:221-222` ;
    - la ligne de commande et les paires planifiées n'utilisent jamais de `CheckpointPath` (`CheckerSanCli.cs:76`, `SavedPairs.cs:173-174`) : elles relisent tout ;
    - le verdict de l'app signale la reprise (« Reprise : 2 empreintes réutilisées… »).
  - **Déclencheur :**
    - les outils ordinaires ne remettent pas `ChangeTime`. `SetFileTime` et `File.SetLastWriteTimeUtc` le laissent avancer : c'est le témoin `ctrl.bin` du rouge, et le test `CheckpointDetectsRestoredTimestamps` ;
    - j'ai vérifié les copieurs. `File.Copy(…, overwrite)` (CopyFileEx) et `robocopy /COPY:DAT` écrasent la destination **en place** (même identifiant) et **lui donnent le `ChangeTime` de la source** (`bleu.exe copytools`, `run-2.txt`) :
      ```
      File.Copy(overwrite) : même identifiant=True ; ChangeTime dest avant=…69241057 après=…67156028 source=…67156028 ; contenu = source ? True
      robocopy /COPY:DAT : même identifiant=True ; ChangeTime dest avant=…71470680 après=…67156028 source=…67156028 ; contenu = source ? True
      ```
      `ChangeTime` voyage donc avec le contenu. Une copie ne produit jamais un contenu différent sous le `ChangeTime` enregistré, sauf si la source avait elle-même un `ChangeTime` falsifié. Je n'ai trouvé aucun outil courant qui réécrit le `ChangeTime` *propre* d'un fichier après l'avoir modifié. Il faut un appel explicite à `SetFileInformationByHandle(FileBasicInfo)`, typique des outils anti-forensiques.
  - **Le scénario adverse ne relève pas de ce défaut.** Un processus de l'utilisateur obtient le même faux « identiques » sans toucher au fichier, en réécrivant `resume.json`, qui est un JSON en clair dans le profil (`bleu.exe resume`, partie (b)). Après une modification ordinaire de `folder2\doc.bin`, il suffit de recopier sa date d'écriture et son `ChangeTime` actuels dans l'entrée du journal :
    ```
    après réécriture de resume.json : TreesIdentical=True FilesReused=2 BytesRead=0 ; SHA réel folder1=10C5800F35ACF35B folder2=D64CBF16C9721913
    ```
    `CheckpointData.Read` (`CheckpointData.cs:24-27`) accepte toute empreinte de 64 chiffres hexadécimaux. La réutilisation fait donc, par construction, confiance à tous les processus de la session. Falsifier `ChangeTime` ne donne rien de plus que réécrire le journal.
  - **Ce qui reste :** un outil *non malveillant* qui remet les quatre dates, et la piste exFAT/FAT (voir plus bas), non vérifiée, qui en ferait un cas ordinaire sur clé USB.
  - **Remarque connexe, non signalée par le rouge :** la réutilisation ne détecte pas non plus une corruption silencieuse du disque depuis le scan précédent, car aucune métadonnée ne change. C'est inhérent au cache, mais l'aide promet que la réutilisation « revalide chaque fichier » (`MainWindow.xaml.cs:427`). Il s'agit de métadonnées, pas d'une relecture : l'aide doit le dire.
- **Gravité proposée :** Basse sur NTFS, faute de déclencheur courant non malveillant. Le cas malveillant est déjà ouvert par le journal lui-même. À relever en Haute pour les volumes FAT/exFAT si la piste 1 se confirme.
- **Avis sur le correctif :**
  - **Faisable ici :** sur C:, `FSCTL_READ_FILE_USN_DATA` fonctionne sans droits administrateur (`bleu.exe usn`) :
    - lecture complète et requêtes de métadonnées : USN inchangé (406075820456) ;
    - écriture puis remise des quatre dates : USN changé (406075820672).
  - **Incomplet face à un adversaire :** l'USN attendu serait stocké dans `resume.json`, que le même processus peut réécrire. Sans le contrôle de l'état du journal, un administrateur qui le désactive (`fsutil usn deletejournal`) fige les USN des fichiers. Le rouge cite l'identifiant du journal, mais `FSCTL_QUERY_USN_JOURNAL` exige une poignée de volume, probablement réservée aux administrateurs (non vérifié). Il faut alors prévoir un repli. L'USN est enfin indisponible sur SMB et FAT, c'est-à-dire dans les cas où la reprise sert le plus (lot vérification à distance).
  - **Alternative plus simple et plus sûre :**
    1. Ne réutiliser une empreinte que sur un système de fichiers qui tient `ChangeTime` (NTFS, ReFS, SMB), ce que donne `GetVolumeInformationByHandleW`. Sur FAT32, exFAT ou un système inconnu : relire. Cela neutralise la piste 1.
    2. Écrire la limite dans l'aide et dans PASSATION : « la réutilisation fait confiance aux métadonnées du système de fichiers et ne détecte ni leur falsification délibérée ni une corruption silencieuse du disque ; décochez-la pour une preuve complète ».

    L'USN peut s'ajouter ensuite comme défense en profondeur, sans être présenté comme une garantie.

## CMP-04 — `checkersan-cli --json` écrit le rapport dans un dossier comparé

- **Verdict :** Partiellement réfuté (gravité). Ce point recoupe AUT-03, que l'autre équipe bleue classe aussi Moyenne.
- **Vérification de la preuve rouge :** `CheckerSanCli.cs:265-279` ne fait qu'une comparaison de chaînes. Scénario relancé (`bleu.exe clijson`, `run-2.txt`) :
  ```
  via jonction exit=0 ; préfixe \\?\ exit=1 ; chemin direct exit=3 (Erreur : Enregistrez le rapport à l'extérieur des dossiers vérifiés.)
  contenu de folder2 : a.txt, prefixe.json, via-jonction.json
  comparaison suivante exit=1 : ÉCARTS — Des différences sont prouvées.
  ```
- **Arguments et preuve :** le défaut est réel, mais son effet reste borné :
  - `FileMode.CreateNew` (`:277`) n'écrase jamais rien ;
  - le rapport est écrit *après* le verdict ;
  - l'effet est un faux *écart* visible lors des vérifications suivantes, jamais un faux « identiques » ;
  - il faut que l'utilisateur désigne lui-même un chemin qui aboutit dans un dossier comparé.

  `verify` et `duplicates` passent par la même fonction (`:148,184`).
- **Gravité proposée :** Moyenne.
- **Avis sur le correctif :** correct. Une seule fonction canonique dans Core est la bonne solution. Deux ajouts :
  - y intégrer le refus des flux alternatifs (CMP-09) ;
  - valider la destination `--json` **avant** la comparaison, comme pour CMP-08, pour ne pas perdre un long scan sur un refus final.

  `CanonicalDirectory` exige que le dossier parent existe : refuser explicitement un parent absent.

## CMP-05 — Instantané : deux noms distincts par la casse sont fusionnés sans erreur

- **Verdict :** Partiellement réfuté (gravité).
- **Vérification de la preuve rouge :** `Snapshot.cs:101,118` remplace silencieusement l'entrée, et `:133` hache `Path.Combine(root, clé)`. Scénario relancé (`bleu.exe snapcase`, `run-2.txt`), avec `fsutil … enable` sans droits administrateur :
  ```
  dates égales      : instantané Complete=True Files=1 [Report.pdf] Unverified=0 Errors=
  dates différentes : instantané Complete=False Files=0 [] Unverified=1 Errors=« Report.pdf » : Le fichier a changé depuis l'inventaire.
  ```
- **Arguments et preuve :** le faux « complet » exige :
  - un dossier sensible à la casse ;
  - deux noms qui ne diffèrent que par la casse ;
  - la même taille ;
  - la même date d'écriture au 100 ns près, puisque `Hash` ne contrôle que la longueur et cette date (`:136`).

  Si une seule de ces conditions manque, l'instantané est incomplet, sans faux verdict, mais avec un message trompeur. Le cas existe surtout pour une archive extraite dans un arbre WSL, dont les dates sont à la seconde. C'est rare. La fenêtre d'instantanés (`SnapshotWindow.cs:103`) et `verify` (code 0) concluent alors « correspond exactement ».
- **Gravité proposée :** Moyenne.
- **Avis sur le correctif :** correct : une erreur en cas de collision, donc `Complete = false`, et le hachage de `info.FullName`. Il ne faut pas conserver les deux entrées : `SnapshotEngine.Compare` construit `actual.Files.ToDictionary(…, OrdinalIgnoreCase)` (`:159`), qui lèverait `ArgumentException` en cas de doublon. Le second `ListAsync` jette ses erreurs (`:81`), mais l'erreur du premier suffit.

## CMP-06 — Chemins longs et noms finissant par un point ou une espace

- **Verdict :** Concédé.
- **Vérification de la preuve rouge :**
  - `FileSafety.Stat` (`:193-198`), `ReparseTag` (`:58-65`) et `CanonicalRoot` (`ComparisonEngine.cs:1129-1146`) passent le chemin brut ;
  - le CLI n'a pas de manifeste : `FolderChecker.Cli.csproj` n'en déclare aucun, et `longPathAware` est absent de `checkersan-cli.exe`, alors qu'il est présent dans `CheckerSan.exe` (`app.manifest`).

  Je n'ai pas relancé ces scénarios. Leur sortie (`run3.txt`) est cohérente avec ce code.
- **Arguments et preuve :** aucun faux « identique » : l'échec produit `Unverified` et un code 2. Le défaut empêche toutefois toute conclusion en ligne de commande sur des arbres profonds, et les messages sont faux (« casse », `draft` au lieu de `draft `).
- **Gravité proposée :** Moyenne.
- **Avis sur le correctif :** correct. Avec `\\?\`, la normalisation Win32 disparaît : appliquer d'abord `GetFullPath` sur la racine, puis ajouter uniquement des noms énumérés. Les `FileStream` doivent recevoir le même chemin préfixé, sinon .NET retire le point ou l'espace final. Étape immédiate et peu coûteuse : ajouter au CLI le même `app.manifest` `longPathAware` que l'app. Cela ne règle que les chemins longs, et seulement si `LongPathsEnabled` vaut 1.

## CMP-07 — `resume.json` est réécrit en entier chaque seconde et ignoré au-delà de 128 Mio

- **Verdict :** Concédé.
- **Vérification de la preuve rouge :**
  - `SaveCheckpoint` est appelé après chaque fichier haché (`ComparisonEngine.cs:959`) ;
  - le minuteur repart *après* chaque sauvegarde (`:365,393`) ;
  - la sérialisation de toutes les entrées se fait sur le thread lecteur (`:368-386`) ;
  - la limite de 128 Mio est en `CheckpointData.cs:22`.

  Mesure complémentaire (`bleu.exe ckpt`, `run-3.txt`), avec des chemins d'environ 95 caractères :
  ```
  250,000 empreintes : 90.2 Mio (378 octets/entrée), sérialisation 808 / 326 / 188 ms (sans l'écriture disque ni Flush(true))
  ```
- **Arguments et preuve :**
  - en fin de grand scan, chaque sauvegarde coûte 0,2 à 0,3 s de processeur, plus l'écriture et `Flush(true)` de 90 Mio ;
  - avec un seul lecteur (disque dur, réseau), le hachage est donc arrêté environ 20 à 25 % du temps, rien que pour la sérialisation ;
  - avec ces longueurs de chemin, la limite de 128 Mio est atteinte vers 355 000 entrées, soit environ 177 000 fichiers par côté. Le rouge l'estime à 254 000 entrées avec ses chemins plus longs.

  Il n'y a aucun faux verdict.
- **Gravité proposée :** Moyenne.
- **Avis sur le correctif :** correct. Le format JSON Lines a déjà fait ses preuves avec `copy.json`. Il faut garder la lecture de l'ancien schéma 1.

  Alternative plus simple, sans changer de format :
  - n'autoriser une sauvegarde que si le temps écoulé dépasse `max(1 s, 20 × durée de la dernière sauvegarde)`, ou si les nouvelles entrées dépassent 1 % du total. Cela borne le coût à environ 5 % du temps ;
  - remplacer la limite fixe de 128 Mio par une limite proportionnelle, ou par une désérialisation en continu ;
  - lire `ReadCheckpointInfo` hors du thread de l'interface (`MainWindow.xaml.cs:169`).

## CMP-08 — Instantané : la destination n'est contrôlée qu'après la lecture de tous les fichiers

- **Verdict :** Partiellement réfuté (gravité).
- **Vérification de la preuve rouge :** `SnapshotWindow.cs:75-86` et `CheckerSanCli.cs:125-126` lisent tout le dossier avant `Save` (`Snapshot.cs:184-186`). En cas d'échec, `RunAsync` affiche seulement le message (`SnapshotWindow.cs:128-130`). Le défaut est réel.
- **Arguments et preuve :** aucune donnée n'est en jeu, ni aucun verdict : seul du temps est perdu, et le message est clair, sauf le texte anglais de .NET pour un fichier existant. Le cas réaliste est un second instantané du même dossier, le même jour, au même endroit : le nom par défaut contient la date (`:78`), d'où une collision. Ce cas est occasionnel.
- **Gravité proposée :** Basse.
- **Avis sur le correctif :** correct. Garder la revalidation dans `Save`, pour couvrir le délai entre le contrôle et l'écriture. Contrôler aussi l'existence du dossier parent, que `CanonicalDirectory` exige. En ligne de commande, un parent absent échoue aussi après la lecture complète.

## CMP-09 — Un flux alternatif de la racine comparée est accepté comme rapport ou instantané

- **Verdict :** Concédé.
- **Vérification de la preuve rouge :**
  - pour `C:\…\folder1:r.csv`, le parent calculé est `C:\…` (`ReportWriter.cs:64-65`), donc hors racine ;
  - `FileSafety.IsWithin` (`FileSafety.cs:67-72`) et `WriteJson` (`CheckerSanCli.cs:268-273`) ne cherchent pas de `:` ;
  - aucune réfutation par le code.
- **Arguments et preuve :** le dialogue Windows refuse `:` dans un nom, donc seuls l'API et le CLI sont concernés. Le défaut ne fausse aucun verdict : la date de la racine n'est comparée qu'à l'intérieur d'un même scan (`ComparisonEngine.cs:972-974`).
- **Gravité proposée :** Basse.
- **Avis sur le correctif :** correct, sans risque : `:` n'est jamais légitime dans le dernier composant d'un chemin Windows, hors nom de flux. L'appliquer aussi à `ValidateCheckpointPath`.

## CMP-10 — `SnapshotEngine.Load` accepte un instantané contradictoire

- **Verdict :** Partiellement réfuté.
- **Vérification de la preuve rouge :** `Snapshot.cs:191-202` ne contrôle que le schéma, les chemins et les empreintes. C'est exactement ce que promet PASSATION.md:330 : « `Load` refuse un instantané endommagé (schéma, empreintes, chemins) ». La promesse documentée est donc tenue. Le rouge la lit comme plus large qu'elle n'est.
- **Arguments et preuve :** le seul chemin vers ce fichier est une modification délibérée, et une corruption accidentelle casse presque toujours le JSON. Or quiconque peut écrire l'instantané peut fabriquer un instantané parfaitement cohérent, avec les empreintes réelles de n'importe quel contenu. Les contrôles proposés n'apportent donc aucune sécurité, seulement de la robustesse. Le rouge le reconnaît en écartant le HMAC.
- **Gravité proposée :** Info (robustesse).
- **Avis sur le correctif :** correct, peu coûteux et sans risque. Il peut être adopté tel quel.

## CMP-11 — Deux noms qui ne diffèrent que par la casse, un de chaque côté, sont « Identique »

- **Verdict :** Concédé.
- **Vérification de la preuve rouge :** l'appariement se fait en `OrdinalIgnoreCase` (`ComparisonEngine.cs:46,563`), et la ligne prend `first.RelativePath` (`:629`).
- **Arguments et preuve :** sur un NTFS ordinaire, insensible à la casse, `Makefile` et `makefile` désignent le même chemin pour tous les programmes Windows. Le verdict y est donc juste. Il n'est faux que dans un dossier ou sur un partage sensible à la casse.
- **Gravité proposée :** Basse.
- **Avis sur le correctif :** **risqué s'il est appliqué sans condition.** Les sauvegardes passées par FAT, par macOS ou par certains outils changent la casse des noms. Elles cesseraient d'être « identiques » sur des volumes où la casse ne compte pas : c'est une régression. Je propose :
  - un détail « casse différente » toujours affiché sur la ligne ;
  - un refus de `TreesIdentical` seulement si le dossier parent, d'un côté ou de l'autre, est sensible à la casse. Cela se lit avec `GetFileInformationByHandleEx(FileCaseSensitiveInfo)` sur la poignée du dossier, une seule requête par dossier.

---

## Pistes non prouvées du rouge

1. **FAT32/exFAT.** Plausible, non vérifiable ici : aucun volume FAT n'est accessible sans droits administrateur, et G: est exclu. L'issue dépend de deux comportements des pilotes, à mesurer sur une clé :
   - si `GetFileInformationByHandleEx(FileIdInfo)` échoue sur ces systèmes, l'identité est nulle, le fichier est toujours relu (`ComparisonEngine.cs:917`), et la piste est réfutée ;
   - si cet appel réussit et que `ChangeTime` ne bouge pas de lui-même sur FAT, un simple éditeur de tags qui conserve la date suffit à produire une empreinte périmée.

   Le correctif que je propose pour CMP-03 (réutiliser seulement sur NTFS, ReFS et SMB) couvre la piste dans les deux cas.
2. **Même dossier par deux chemins.**
   - **Alias locaux : réfutée.** `bleu.exe alias` (`run-2.txt`) montre que `ValidateSeparateRoots` refuse les quatre formes suivantes : `\\?\Volume{…}\…`, `\\?\GLOBALROOT\Device\HarddiskVolume3\…`, `\\.\C:\…` et le nom court 8.3. Message : « Choisissez deux dossiers distincts ».
   - **Alias réseau : plausible par le code, non testé**, puisque les partages sont exclus. Exemples : `\\NAS\Photos` et `\\192.168.1.10\Photos`, ou `\\localhost\partage` et le dossier local. `CanonicalRoot` rend `\\serveur\partage\…` (`ComparisonEngine.cs:1144`), et `RelatedRoots` ne compare que des chaînes (`:1120-1127`). Le cas du NAS désigné par son nom puis par son adresse est réaliste.
   - **Gravité s'il se confirme :** Critique, car un dossier comparé à lui-même est déclaré « identique ».
   - **Correctif :** celui du rouge est le bon. Comparer le numéro de série du volume et l'identifiant (`FileIdInfo`) des deux racines et de leurs ancêtres. Si l'information manque, afficher un avertissement.
3. **Caches de métadonnées SMB.** Plausible et de faible portée, non vérifié faute de partage. Le dernier inventaire passe par l'énumération (`ReadDirectoryBatch`), que le redirecteur peut servir depuis son cache de répertoires. Les baux de répertoire des serveurs Windows récents invalident ce cache. Sans eux, le risque se limite à une modification faite sur le serveur dans les secondes qui précèdent le dernier inventaire. La correction de CMP-02 par requête de handle réduirait aussi ce risque pour les fichiers qui fondent un verdict.

## Constats complémentaires (hors rapport rouge)

- **CMP-02 élargi aux liens physiques** (voir plus haut) : une modification ordinaire, sans date remise, faite par un autre nom pendant le scan, donne « identiques » dans l'app (`run-hardlink.txt`).
- **Même faiblesse dans la vérification d'instantané** (`run-snapfinal.txt`) : `Complete=True Identical=True` alors que l'empreinte réelle diffère.
- **`resume.json` falsifiable** par tout processus de l'utilisateur, avec un faux « identiques » sans lecture (`run-resume.txt`, partie (b)). C'est le modèle de confiance actuel de la reprise. Il faut le documenter plutôt que le présenter comme un contrôle strict.

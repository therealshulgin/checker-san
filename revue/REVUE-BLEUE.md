# Revue adverse — rapport de l'équipe bleue

Réponse aux 39 trouvailles de `REVUE-ROUGE.md`, menée le 23 octobre 2026 par quatre agents « équipe bleue », un par domaine. Chaque trouvaille a été revérifiée : la reproduction rouge a été relancée ou le code relu. Chacune a ensuite été réfutée si possible, preuve à l'appui, et son correctif a été jugé. Les contre-preuves ont tourné uniquement dans des dossiers d'essai temporaires.

**Verdicts** : Réfuté = le problème n'existe pas ; Partiellement réfuté = réel, mais gravité ou portée surestimée ; Concédé = réel tel que décrit.

Aucune trouvaille n'est entièrement réfutée. La décision finale est dans `ARBITRAGE.md`.

## Synthèse

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
| COP-01 | Haute | Partiellement réfuté | Basse (Moyenne seulement si l'exécution en administrateur devient un usage pris en charge) | Incomplet et risqué sur réseau (point 1) ; point 3 correct ; alternative plus simple ci-dessous |
| COP-02 | Moyenne | Partiellement réfuté | Basse | Excessif comme exigence ; documenter, et éventuellement conserver `Zone.Identifier` au mieux |
| COP-03 | Moyenne | Partiellement réfuté | Basse | Correct ; une variante sans champ persistant suffit |
| COP-04 | Moyenne | Partiellement réfuté | Basse | Correct, avec réserves sur la marge |
| COP-05 | Moyenne | Concédé | Moyenne | Correct |
| COP-06 | Moyenne | Partiellement réfuté | Basse | Correct ; préférer l'exclusion à l'encodage base64 |
| COP-07 | Basse | Concédé | Basse | Correct pour le plan ; déconseillé dans la comparaison |
| COP-08 | Basse | Concédé | Basse | Correct, à condition que l'indicateur soit « collant » et nullable |
| COP-09 | Basse | Concédé | Basse | Correct sur le fond ; le handle actuel est en lecture seule |
| COP-10 | Basse | Concédé | Basse | En partie faux : `FileShare.Delete` ne permet pas à `File.Move` de remplacer le fichier (prouvé) |
| DUP-07 (côté copie) | — | Confirmé | Moyenne | Correct |
| DUP-01 | Haute | Partiellement réfuté | Moyenne | Point 1 correct. Point 2 risqué tel que formulé (bloque tout le plan). Point 3 correct. Point 4 facultatif |
| DUP-02 | Haute | Partiellement réfuté | Moyenne | Correct et faisable (vérifié), avec une fenêtre résiduelle ; à appliquer élément par élément |
| DUP-03 | Haute | Partiellement réfuté | Moyenne | Correct. Les seuls contrôles de chemin (point 1) laissent une course ; réutiliser `DirectoryLocks` et `RenameByHandle` |
| DUP-04 | Moyenne | Partiellement réfuté | Basse | Correct. Le point 1 change le format du journal (test et passation à adapter) ; une alternative minimale suffit |
| DUP-05 | Moyenne | Partiellement réfuté | Basse | USN utile en contrôle supplémentaire. Le repli « jamais de hachage complet sans USN » est risqué (régression de la reprise sur partage) |
| DUP-06 | Moyenne | Partiellement réfuté | Basse | Correct. Réutiliser `scan.CanonicalRoot` ; appliquer la même règle dans `QuarantineSessions` |
| DUP-07 | Moyenne | Partiellement réfuté | Basse | Correct et complet (vérifié). Il corrige aussi `CopyEngine` |
| DUP-08 | Basse | Concédé | Basse | Correct (vérifié) ; ignorer la lecture seule est un choix de produit à signaler dans l'aperçu |
| AUT-01 | Haute | Partiellement réfuté | Moyenne | Bon principe, mais incomplet : l'enfant hérite de la priorité « inférieure à la normale », il faut une seule fenêtre de résultat, et un essai réel reste à faire |
| AUT-02 | Haute | Concédé, et même plus large (cas symétrique « AJOUTÉ ») | Haute | Code de sortie : **risqué**, il masquerait des écarts prouvés ; partie moteur incomplète |
| AUT-03 | Haute | Partiellement réfuté (gravité) | Moyenne | Correct ; reprendre le motif de `Snapshot.Save` |
| AUT-04 | Moyenne | Partiellement réfuté (impact) | Basse | Correctif principal **risqué** (`SnapshotWindow`) ; retenir la variante « à défaut » |
| AUT-05 | Moyenne | Concédé, et plus grave que décrit (`if errorlevel 1` faux) | Moyenne | Correct |
| AUT-06 | Moyenne | Partiellement réfuté (portée) | Basse | Correct ; la ligne `:90` n'utilise pas de copie périmée |
| AUT-07 | Moyenne | Partiellement réfuté | Basse | Points 1 et 2 corrects ; point 3 **risqué** : garder le bouton toujours actif |
| AUT-08 | Moyenne | Partiellement réfuté | Basse | Contrôle de la destination : correct ; filtrage de la rotation par contenu : superflu |
| AUT-09 | Basse | Concédé | Basse | Correct |
| AUT-10 | Basse | Partiellement réfuté (`--help`, casse) | Basse | Liste blanche : correcte, mais garder les options communes ; refuser le passage de `--help` à 3 |


## Équipe bleue — Comparaison, instantanés et rapports

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

### CMP-01 — La ligne de commande et les paires planifiées déclarent « identiques » un dossier réel face à une jonction

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

### CMP-02 — Le contrôle final ne voit pas une modification qui garde la taille et la date : faux « identiques »

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

### CMP-03 — Reprise : une empreinte périmée est réutilisée si la date de changement est restaurée

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

### CMP-04 — `checkersan-cli --json` écrit le rapport dans un dossier comparé

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

### CMP-05 — Instantané : deux noms distincts par la casse sont fusionnés sans erreur

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

### CMP-06 — Chemins longs et noms finissant par un point ou une espace

- **Verdict :** Concédé.
- **Vérification de la preuve rouge :**
  - `FileSafety.Stat` (`:193-198`), `ReparseTag` (`:58-65`) et `CanonicalRoot` (`ComparisonEngine.cs:1129-1146`) passent le chemin brut ;
  - le CLI n'a pas de manifeste : `FolderChecker.Cli.csproj` n'en déclare aucun, et `longPathAware` est absent de `checkersan-cli.exe`, alors qu'il est présent dans `CheckerSan.exe` (`app.manifest`).

  Je n'ai pas relancé ces scénarios. Leur sortie (`run3.txt`) est cohérente avec ce code.
- **Arguments et preuve :** aucun faux « identique » : l'échec produit `Unverified` et un code 2. Le défaut empêche toutefois toute conclusion en ligne de commande sur des arbres profonds, et les messages sont faux (« casse », `draft` au lieu de `draft `).
- **Gravité proposée :** Moyenne.
- **Avis sur le correctif :** correct. Avec `\\?\`, la normalisation Win32 disparaît : appliquer d'abord `GetFullPath` sur la racine, puis ajouter uniquement des noms énumérés. Les `FileStream` doivent recevoir le même chemin préfixé, sinon .NET retire le point ou l'espace final. Étape immédiate et peu coûteuse : ajouter au CLI le même `app.manifest` `longPathAware` que l'app. Cela ne règle que les chemins longs, et seulement si `LongPathsEnabled` vaut 1.

### CMP-07 — `resume.json` est réécrit en entier chaque seconde et ignoré au-delà de 128 Mio

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

### CMP-08 — Instantané : la destination n'est contrôlée qu'après la lecture de tous les fichiers

- **Verdict :** Partiellement réfuté (gravité).
- **Vérification de la preuve rouge :** `SnapshotWindow.cs:75-86` et `CheckerSanCli.cs:125-126` lisent tout le dossier avant `Save` (`Snapshot.cs:184-186`). En cas d'échec, `RunAsync` affiche seulement le message (`SnapshotWindow.cs:128-130`). Le défaut est réel.
- **Arguments et preuve :** aucune donnée n'est en jeu, ni aucun verdict : seul du temps est perdu, et le message est clair, sauf le texte anglais de .NET pour un fichier existant. Le cas réaliste est un second instantané du même dossier, le même jour, au même endroit : le nom par défaut contient la date (`:78`), d'où une collision. Ce cas est occasionnel.
- **Gravité proposée :** Basse.
- **Avis sur le correctif :** correct. Garder la revalidation dans `Save`, pour couvrir le délai entre le contrôle et l'écriture. Contrôler aussi l'existence du dossier parent, que `CanonicalDirectory` exige. En ligne de commande, un parent absent échoue aussi après la lecture complète.

### CMP-09 — Un flux alternatif de la racine comparée est accepté comme rapport ou instantané

- **Verdict :** Concédé.
- **Vérification de la preuve rouge :**
  - pour `C:\…\folder1:r.csv`, le parent calculé est `C:\…` (`ReportWriter.cs:64-65`), donc hors racine ;
  - `FileSafety.IsWithin` (`FileSafety.cs:67-72`) et `WriteJson` (`CheckerSanCli.cs:268-273`) ne cherchent pas de `:` ;
  - aucune réfutation par le code.
- **Arguments et preuve :** le dialogue Windows refuse `:` dans un nom, donc seuls l'API et le CLI sont concernés. Le défaut ne fausse aucun verdict : la date de la racine n'est comparée qu'à l'intérieur d'un même scan (`ComparisonEngine.cs:972-974`).
- **Gravité proposée :** Basse.
- **Avis sur le correctif :** correct, sans risque : `:` n'est jamais légitime dans le dernier composant d'un chemin Windows, hors nom de flux. L'appliquer aussi à `ValidateCheckpointPath`.

### CMP-10 — `SnapshotEngine.Load` accepte un instantané contradictoire

- **Verdict :** Partiellement réfuté.
- **Vérification de la preuve rouge :** `Snapshot.cs:191-202` ne contrôle que le schéma, les chemins et les empreintes. C'est exactement ce que promet PASSATION.md:330 : « `Load` refuse un instantané endommagé (schéma, empreintes, chemins) ». La promesse documentée est donc tenue. Le rouge la lit comme plus large qu'elle n'est.
- **Arguments et preuve :** le seul chemin vers ce fichier est une modification délibérée, et une corruption accidentelle casse presque toujours le JSON. Or quiconque peut écrire l'instantané peut fabriquer un instantané parfaitement cohérent, avec les empreintes réelles de n'importe quel contenu. Les contrôles proposés n'apportent donc aucune sécurité, seulement de la robustesse. Le rouge le reconnaît en écartant le HMAC.
- **Gravité proposée :** Info (robustesse).
- **Avis sur le correctif :** correct, peu coûteux et sans risque. Il peut être adopté tel quel.

### CMP-11 — Deux noms qui ne diffèrent que par la casse, un de chaque côté, sont « Identique »

- **Verdict :** Concédé.
- **Vérification de la preuve rouge :** l'appariement se fait en `OrdinalIgnoreCase` (`ComparisonEngine.cs:46,563`), et la ligne prend `first.RelativePath` (`:629`).
- **Arguments et preuve :** sur un NTFS ordinaire, insensible à la casse, `Makefile` et `makefile` désignent le même chemin pour tous les programmes Windows. Le verdict y est donc juste. Il n'est faux que dans un dossier ou sur un partage sensible à la casse.
- **Gravité proposée :** Basse.
- **Avis sur le correctif :** **risqué s'il est appliqué sans condition.** Les sauvegardes passées par FAT, par macOS ou par certains outils changent la casse des noms. Elles cesseraient d'être « identiques » sur des volumes où la casse ne compte pas : c'est une régression. Je propose :
  - un détail « casse différente » toujours affiché sur la ligne ;
  - un refus de `TreesIdentical` seulement si le dossier parent, d'un côté ou de l'autre, est sensible à la casse. Cela se lit avec `GetFileInformationByHandleEx(FileCaseSensitiveInfo)` sur la poignée du dossier, une seule requête par dossier.

---

### Pistes non prouvées du rouge

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

### Constats complémentaires (hors rapport rouge)

- **CMP-02 élargi aux liens physiques** (voir plus haut) : une modification ordinaire, sans date remise, faite par un autre nom pendant le scan, donne « identiques » dans l'app (`run-hardlink.txt`).
- **Même faiblesse dans la vérification d'instantané** (`run-snapfinal.txt`) : `Complete=True Identical=True` alors que l'empreinte réelle diffère.
- **`resume.json` falsifiable** par tout processus de l'utilisateur, avec un faux « identiques » sans lecture (`run-resume.txt`, partie (b)). C'est le modèle de confiance actuel de la reprise. Il faut le documenter plutôt que le présenter comme un contrôle strict.


## Équipe bleue — Copie et écritures

Code examiné : arbre de travail non commité (`CopyEngine.cs` du 22/10 22:21) et DLL `FolderChecker.Core\bin\Release\net10.0\FolderChecker.Core.dll` du 23/10 15:47, celle utilisée par le rouge.

Programme de vérification bleu (aucun fichier du dépôt modifié ; toutes les écritures restent sous `cop-bleu\work\`) :
`<dossier temporaire>\revue\cop-bleu\proj\Program.cs`
- Le projet `CopBleu.csproj` n'a **pas de manifeste**. Le processus voit donc les chemins longs désactivés (`RtlAreLongPathsEnabled() = 0`), comme CheckerSan.exe sur un Windows réglé par défaut.
- Compilation : la commande du SDK local de la consigne, avec `-o cop-bleu/out`.
- Exécution : `cop-bleu/out/CopBleu.exe <scénario…>`, avec `DOTNET_ROOT` défini.
- Scénarios : `onechar`, `renamecodes`, `staticswap`, `betweenitems`, `pinning`, `ads`, `existingdir`, `longpath`, `surrogate`, `partialplan`, `moveopen`.

Je n'ai pas relancé les scénarios du rouge qui créent une concurrence volontaire (oplock, crochet de progression) : la trace de code suffit à confirmer les faits qu'ils établissent. Les autres faits ont été revérifiés par mes propres exécutions.

| ID | Gravité rouge | Verdict bleu | Gravité proposée | Correctif rouge : avis |
|---|---|---|---|---|
| COP-01 | Haute | Partiellement réfuté | Basse (Moyenne seulement si l'exécution en administrateur devient un usage pris en charge) | Incomplet et risqué sur réseau (point 1) ; point 3 correct ; alternative plus simple ci-dessous |
| COP-02 | Moyenne | Partiellement réfuté | Basse | Excessif comme exigence ; documenter, et éventuellement conserver `Zone.Identifier` au mieux |
| COP-03 | Moyenne | Partiellement réfuté | Basse | Correct ; une variante sans champ persistant suffit |
| COP-04 | Moyenne | Partiellement réfuté | Basse | Correct, avec réserves sur la marge |
| COP-05 | Moyenne | Concédé | Moyenne | Correct |
| COP-06 | Moyenne | Partiellement réfuté | Basse | Correct ; préférer l'exclusion à l'encodage base64 |
| COP-07 | Basse | Concédé | Basse | Correct pour le plan ; déconseillé dans la comparaison |
| COP-08 | Basse | Concédé | Basse | Correct, à condition que l'indicateur soit « collant » et nullable |
| COP-09 | Basse | Concédé | Basse | Correct sur le fond ; le handle actuel est en lecture seule |
| COP-10 | Basse | Concédé | Basse | En partie faux : `FileShare.Delete` ne permet pas à `File.Move` de remplacer le fichier (prouvé) |
| DUP-07 (côté copie) | — | Confirmé | Moyenne | Correct |

---

### COP-01 — La racine de destination n'est jamais revérifiée : une jonction posée à sa place fait écrire et renommer la copie vérifiée hors du dossier 2, et la passe finale modifie des dossiers étrangers
- **Verdict :** Partiellement réfuté. Les faits de code sont exacts. La gravité et le réalisme du scénario sont surestimés.

- **Vérification de la preuve rouge :** faits confirmés par la lecture du code.
  - `AssertNoLinks` (`CopyEngine.cs:561-572`) commence à `current = root` et ne teste que les composants situés sous la racine.
  - Pendant un élément, la seule revalidation de la racine se trouve en `CopyEngine.cs:330`. Tout ce qui suit est résolu par chemin : 341, 342, 344, 368, 439.
  - `OpenDirectory` (`FileSafety.cs:123`) n'applique `FILE_FLAG_OPEN_REPARSE_POINT` (0x00200000) qu'au dernier composant, ce qui est le comportement Win32.
  - La passe finale (`CopyEngine.cs:463-478`) n'appelle pas `ValidateRoots` et applique attributs et dates par chemin.
  - Le commentaire de `RenameByHandle` (`FileSafety.cs:94`), « Neither path can be swapped… », est donc trop affirmatif : le handle du dossier est lui-même obtenu par un chemin qui traverse la racine.

- **Arguments et preuve :**
  1. **Les protections existantes couvrent le cas réaliste.** Un remplacement de la racine *avant* la copie ou *entre deux éléments* est détecté et bloqué. `ValidateRoots` (308 et 330) appelle `IsFolder` (548-552), qui classe une jonction comme `Link` (`FileSafety.cs:42`, bit « name surrogate »). L'appel lève alors l'erreur de `CopyEngine.cs:556`, interceptée par élément en 459-460. Exécution :
     ```
     CopBleu.exe staticswap betweenitems
     === staticswap ===
        folder 2 is now a junction: True
        CopyResult: Complete=False Completed=0/1 Errors=[Les racines doivent être des dossiers réels accessibles.]
        outside: []
     === betweenitems ===
        CopyResult: Complete=False Completed=1/3 Errors=[f2.bin : Les racines doivent être des dossiers réels accessibles. | f3.bin : Les racines doivent être des dossiers réels accessibles.]
        outside: []  real folder 2: [f1.bin]
     ```
     Conséquence : dans une copie de plusieurs fichiers, un échange réussi pendant un élément autre que le dernier fait échouer tous les éléments suivants de façon visible (« Copie incomplète »). Le verdict faussement « terminée et vérifiée » n'est possible que si l'échange frappe le **dernier** fichier, ou s'il a lieu pendant la passe des dossiers. Dans ce second cas, les fichiers sont correctement dans le vrai dossier 2, et seules les métadonnées de dossiers sont mal dirigées.
  2. **Pendant l'écriture, la fenêtre est fermée.** Le temporaire est ouvert sans `FILE_SHARE_DELETE`, ce qui verrouille tous ses dossiers parents réels. Mon exécution le reproduit indépendamment du rouge :
     ```
     CopBleu.exe pinning
        rename of dst with the partial open: refused (IOException)
        rename of sub with the partial open: refused (IOException)
     ```
     La fenêtre exploitable se limite donc :
     - à l'intervalle entre la ligne 330 et la ligne 368 de chaque fichier, soit l'ouverture de la source et quelques métadonnées, de l'ordre de la microseconde à la dizaine de millisecondes ;
     - à la passe finale, brève.

     Le rouge n'a pu viser la première fenêtre qu'avec un oplock posé délibérément sur la source. Pour la seconde, il a utilisé un `IProgress` **synchrone**, injecté dans le processus. Dans l'application, la progression est un `Progress<CopyProgress>` (`MainWindow.Copy.cs:41`), qui publie de façon asynchrone vers l'interface : le moteur ne s'arrête pas. Aucun outil légitime ne « déplace le dossier 2 et le remplace par une jonction » précisément dans ces intervalles. Un déplacement légitime survient presque toujours pendant l'écriture, où il est refusé, ou entre deux éléments, où il est détecté (point 1).
  3. **Qui peut le faire ?** Il faut un processus qui :
     - dispose du droit d'écriture et de suppression sur le **parent** du dossier 2, pour le renommer et y créer une jonction ;
     - agit à un instant choisi.

     L'application est `asInvoker` (`FolderChecker.App/app.manifest:4`), et PASSATION.md:95 dit « L'application s'exécute avec les droits ordinaires de l'utilisateur ». La copie n'existe qu'en mode interactif. La CLI ne fait que simuler (`CheckerSanCli.cs:98-104` : `CreatePlan` et `SpaceNeeds`, aucun `CopyAsync`), et les tâches planifiées sont `LeastPrivilege` et ne font que comparer (`ScheduledTasks.cs:43`). Un tel processus tourne donc avec les mêmes droits que l'utilisateur. Il peut déjà lire la source et écrire où il veut : l'application ne lui donne aucun droit supplémentaire. Il s'agit bien d'une attaque locale par un processus hostile déjà présent, pas d'une situation réaliste d'utilisation.
  4. **Conséquences réelles sur les données.**
     - Rien n'est écrasé : `CREATE_NEW` pour le temporaire (`CopyEngine.cs:197`) et renommage sans remplacement (`FileSafety.cs:105`).
     - Ce qui reste est réel, mais limité :
       - une copie supplémentaire d'un fichier de l'utilisateur apparaît dans un dossier où l'attaquant pouvait déjà écrire ;
       - le dernier fichier manque dans le dossier 2 alors que le verdict annonce « terminée et vérifiée » ; une nouvelle comparaison, recommandée par ce même verdict, le révèle ;
       - des dossiers situés aux mêmes chemins relatifs sous la cible de la jonction reçoivent attributs et dates, un dommage réversible.
  5. **Ce que l'application promet.** « Rien n'est jamais copié à travers un lien » (PASSATION.md:125) vise les liens **présents dans l'arborescence lors de la comparaison** (« `Skipped` bloque les descendants du plan »), et cette promesse est tenue. PASSATION.md:428 demande par ailleurs de ne pas modifier les dossiers pendant l'opération. Le seul écart réel avec une promesse est le commentaire de `FileSafety.cs:94`.
  6. **Seul cas réellement grave :** l'utilisateur lance lui-même CheckerSan en administrateur, alors qu'un processus non élevé contrôle le parent du dossier 2. Une écriture privilégiée, sans écrasement, pourrait alors être détournée vers un dossier protégé. Ce n'est pas un usage prévu : le manifeste est `asInvoker` et rien ne demande l'élévation. Ce cas justifie néanmoins un correctif.
  7. **Test existant :** aucun test ne couvre un remplacement de racine pendant la copie. La recherche de `ValidateRoots`, « réels accessibles » et « emplacement réel » dans `FolderChecker.Tests` ne donne rien. Le point est réel pour la couverture.

- **Gravité proposée :** Basse. L'écart est réel, mais il faut un processus hostile de même niveau de droits, agissant à un instant précis, sans élévation de privilèges, sans écrasement, et seulement en fin de copie pour que le verdict soit trompeur. Passer à Moyenne si l'exécution en administrateur est un jour revendiquée.

- **Avis sur le correctif :** incomplet pour le point 1, correct pour le point 3, et le point 4 est trop lourd.
  - **Point 1 (garder un handle de racine sans `FILE_SHARE_DELETE`) : incomplet et risqué.**
    - Incomplet : il n'empêche pas de remplacer par une jonction un dossier **intermédiaire** (`docs`) entre `AssertNoLinks` (363) et `OpenPartial` (368). C'est la même classe de problème, que ce point ne couvre pas.
    - Risqué sur un partage : une reconnexion SMB invalide le handle (hors handles durables). Il faudrait le rouvrir et le revalider dans le chemin de `Retry`, sinon la reprise réseau, déjà délicate (Limites 1-2), régresse.
    - Effet de bord accepté : il empêche aussi l'utilisateur de renommer le dossier 2 pendant la copie. C'est acceptable, mais à signaler.
  - **Point 2 (contrôler la racine dans `AssertNoLinks`) :** redondant avec `IsFolder` dans `ValidateRoots`, sauf dans la passe finale. Il ne protège pas pour autant contre un échange postérieur au contrôle.
  - **Point 3 (passe finale) : correct.** Appeler `ValidateRoots`, ouvrir chaque dossier par handle, puis appliquer attributs et dates sur ce handle. Les surcharges `File.SetAttributes(SafeFileHandle…)` et `File.SetCreationTimeUtc(SafeFileHandle…)` sont déjà utilisées en 425-447. Le handle doit demander `FILE_WRITE_ATTRIBUTES` avec `BACKUP_SEMANTICS|OPEN_REPARSE_POINT`.
  - **Point 4 (`NtCreateFile` relatif, `OBJ_DONT_REPARSE`) :** complet, mais c'est une réécriture de `EnsureDirectory`, `Exists`, `OpenPartial` et des ouvertures de lecture. Le risque de régression est élevé, notamment sur les partages et DFS, pour un gain que l'alternative ci-dessous obtient plus simplement.
  - **Alternative plus simple et complète pour les données :** vérifier *après* l'ouverture, sur le handle, où l'objet se trouve réellement.
    1. Juste après `OpenPartial` (368), comparer `GetFinalPathNameByHandleW(output)`, normalisé comme `CanonicalDirectory` (`FileSafety.cs:80-82`), à `TargetCanonical + dossier relatif + PartialName`, sans tenir compte de la casse. En cas d'écart, supprimer le temporaire par son handle (le handle a l'accès DELETE ; `FileDispositionInfo`), puis lever une `IOException`.
       - Ce contrôle couvre la racine **et** les dossiers intermédiaires.
       - Une fois le handle ouvert, les dossiers parents réels ne peuvent plus être renommés (preuve `pinning`). L'emplacement vérifié reste donc valable jusqu'au renommage.
       - Le chemin final désigne bien l'emplacement réel : `pinning` affiche `opened through 'alias\sub\file.bin', final path = ...\outside\sub\file.bin`.
    2. Même contrôle sur le handle du dossier en 439, et sur `existing` en 348.
    3. Passe finale : appliquer le point 3, avec le même contrôle du chemin final sur chaque handle de dossier.

    Aucun handle n'est conservé entre deux éléments, donc la reconnexion réseau n'est pas touchée. Coût : un `GetFinalPathNameByHandleW` par fichier. Il est négligeable en local, mais représente un aller-retour sur SMB : à **mesurer avant d'activer**, conformément à la règle du projet.
    Complément utile pour le point 2 des pistes : enregistrer dans `CopyPlan` l'identité (`FILE_ID_INFO`) de chaque racine et la comparer dans `ValidateRoots`. Cela détecte aussi un dossier 2 remplacé par un **autre dossier réel** de même nom, cas que ni `ValidateRoots` ni le point 1 du rouge ne voient, puisque le chemin canonique reste identique.
  - **Tests :** le test (b) du rouge (passe finale, observateur synchrone à « Élément terminé ») est adapté à la suite actuelle, dont les observateurs sont déjà synchrones. Pour (a), un test sans oplock suffit si le contrôle porte sur le handle : par exemple un plan dont `TargetCanonical` ne correspond plus au chemin final du temporaire. Le comportement de détection peut ainsi être vérifié de façon déterministe.

### COP-02 — Les flux de données alternatifs, dont la marque du Web (`Zone.Identifier`), sont perdus sans avertissement ; la comparaison suivante conclut « identiques »
- **Verdict :** Partiellement réfuté.

- **Vérification de la preuve rouge :** reproduit.
  ```
  CopBleu.exe ads
     CopyResult: Complete=True Completed=1/1 Errors=[]
     Zone.Identifier folder1="[ZoneTransfer] / ZoneId=3 / " folder2="<absent: FileNotFoundException>"
     comparison after: TreesIdentical=True
  ```

- **Arguments et preuve :**
  - **Limite documentée, en interne et en partie pour l'utilisateur.**
    - PASSATION.md:427 (Limites 4) : « La copie ne reproduit pas ces métadonnées comme le ferait un outil complet de sauvegarde. »
    - Le verdict « identiques » après la copie correspond exactement au périmètre annoncé à l'utilisateur :
      - aide FR (`MainWindow.xaml.cs:426`) : « Il ne compare pas les autorisations, dates, flux NTFS alternatifs… » ;
      - aide EN (`MainWindow.xaml.cs:434`) ;
      - README.md:25 : « alternate data streams are not compared » ;
      - champ « Scope » du rapport (`ReportWriter.cs:26`).
    - Ce n'est donc pas un faux verdict.
  - **Ce qui reste réel :** la section Copie de l'aide (`MainWindow.xaml.cs:430` : « Les dates et attributs des fichiers et dossiers sont conservés ») et le dialogue (`CopyDialog.cs:25`) ne disent pas que les flux alternatifs ne sont pas copiés. La perte de la marque du Web supprime un avertissement SmartScreen sur un exécutable copié, alors que l'Explorateur la conserve.
  - Les fichiers concernés appartiennent à l'utilisateur et passent d'un de ses dossiers à un autre. Ce n'est pas une perte du contenu principal.

- **Gravité proposée :** Basse. C'est un oubli de documentation dans la section Copie, sur une limite déjà assumée.

- **Avis sur le correctif :** excessif comme exigence, et risqué s'il est appliqué tel quel.
  - Écrire les flux dans `partial:nom` échouera sur exFAT/FAT, sur ReFS au-delà de 128 Kio par flux, et sur de nombreux NAS SMB où les flux sont désactivés. Cela ne doit **jamais** faire échouer la copie.
  - Les « inclure dans la vérification » rendrait la copie plus stricte que la comparaison, qui ne les lit pas. Ce serait incohérent.
  - Alternative minimale :
    1. ajouter une phrase dans l'aide Copie et dans le dialogue : « Les flux NTFS alternatifs, dont la marque du Web, et les autorisations ne sont pas copiés » ;
    2. en option, copier au mieux le seul `Zone.Identifier`, de quelques octets, en ignorant silencieusement un refus du système de fichiers de destination.

### COP-03 — Après une copie 2 → 1, les champs Dossier 1 et Dossier 2 de la fenêtre principale sont inversés
- **Verdict :** Partiellement réfuté.

- **Vérification de la preuve rouge :** trace confirmée.
  - `CopyDialog.cs:83` appelle `CreatePlan(result!, false)`.
  - `CopyEngine.cs:73-75` : `SourceRoot` = dossier 2.
  - `MainWindow.Copy.cs:32` : `SourceBox.Text = plan.SourceRoot`.
  - `MainWindow.xaml.cs:135` sort de la fonction, car `_result` vaut null (`MainWindow.Copy.cs:29`).
  - `SourceBox` est bien le champ « Dossier 1 » (`MainWindow.xaml:18`).

- **Arguments et preuve :**
  - Les verdicts suivants sont **exacts au regard des champs affichés**. Les champs montrent les chemins réels, et l'aide dit explicitement « Choose folders 1 and 2 in either order… Neither folder is automatically designated as the copy to keep » (`MainWindow.xaml.cs:434`). Un bouton d'inversion existe d'ailleurs (`MainWindow.xaml.cs:132`).
  - Le scénario « il risque de supprimer le mauvais côté » suppose que l'utilisateur ne lise pas les chemins affichés.
  - « Écrire dans le dossier qu'il voulait garder intact » est atténué : avant toute écriture, le dialogue affiche « Source : … / Destination : … » avec les chemins (`CopyDialog.cs:88-90`), et la copie n'écrase jamais rien.
  - Il reste une incohérence d'ergonomie réelle, qui peut se propager à une paire enregistrée (`MainWindow.xaml.cs:114`).

- **Gravité proposée :** Basse.

- **Avis sur le correctif :** correct. Une variante sans nouveau champ persistant suffit :
  - dans `Transfer_Click`, ne modifier les champs que s'ils ne désignent pas déjà les deux racines du plan, dans un sens ou dans l'autre, en comparant avec `Path.GetFullPath` sans tenir compte de la casse ;
  - pour une reprise venue d'une autre session, garder le comportement actuel.

  Cela évite de changer le format du journal.

### COP-04 — Contrôle d'espace calculé sur des clusters de 4 Kio, sans compter les dossiers : sur exFAT/FAT, il laisse démarrer une copie qui remplit le disque
- **Verdict :** Partiellement réfuté.

- **Vérification de la preuve rouge :** trace confirmée. Arrondi à 4096 en dur (`CopyEngine.cs:165`), seuls les fichiers sont comptés (155). Les tailles de cluster exFAT par défaut citées (128 Kio de 32 Gio à 256 Tio) sont exactes.

- **Arguments et preuve :**
  - L'hypothèse des 4 Kio est **documentée** (PASSATION.md:194 : « arrondis à 4 Kio par fichier »).
  - L'échec est **géré proprement** : les erreurs 39 et 112 arrêtent la boucle avec un message clair (« espace disque insuffisant. Copie arrêtée ; libérez de la place puis reprenez », `CopyEngine.cs:457-458`), et la copie reste reprenable.
  - Aucune perte de données.
  - Le contrôle ne peut de toute façon pas être une garantie absolue : d'autres programmes écrivent sur le même volume pendant la copie.
  - La promesse « Un manque bloque la copie avant toute écriture » est donc approximative sur les volumes à grands clusters, sans conséquence sur les données.

- **Gravité proposée :** Basse.

- **Avis sur le correctif :** correct, avec des réserves.
  - `GetDiskFreeSpaceW` exige une racine ; pour un chemin UNC, une barre oblique inverse finale. La valeur renvoyée par `VolumeOf` convient : elle est déjà calculée par racine en 151-152.
  - Ne compter que les dossiers non terminés.
  - Limiter la « petite marge » à un ordre de grandeur proportionnel, par exemple quelques clusters, pour ne pas refuser des copies justes qui passent aujourd'hui sur NTFS.
  - Mettre à jour PASSATION.md:194.

### COP-05 — Un dossier parent de destination d'environ 207 à 257 caractères fait échouer chaque fichier : le nom temporaire de 52 caractères dépasse MAX_PATH dans un `CreateFileW` sans préfixe `\\?\`
- **Verdict :** Concédé.

- **Vérification de la preuve rouge :** reproduit, y compris la borne exacte.
  ```
  CopBleu.exe longpath
     RtlAreLongPathsEnabled() = 0 (no manifest)
     parent length=206 partial length=259 final length=212
     CopyResult: Complete=True Completed=1/1 Errors=[]
     parent length=207 partial length=260 final length=213
     CopyResult: Complete=False Completed=0/1 Errors=[sub\p.jpg : The system cannot find the path specified. (0x80070003)]
     parent length=230 partial length=283 final length=236
     CopyResult: Complete=False Completed=0/1 Errors=[sub\p.jpg : The system cannot find the path specified. (0x80070003)]
  ```

- **Arguments et preuve :**
  - Aucune réfutation possible : `OpenPartial` (`CopyEngine.cs:197`) passe un chemin brut à `CreateFileW`, alors que les API .NET ajoutent elles-mêmes le préfixe.
  - PASSATION.md:95 affirme que « le manifeste Windows active notamment les chemins longs », mais `LongPathsEnabled` vaut 0 par défaut sur Windows 11.
  - Un chemin parent de plus de 207 caractères est courant avec OneDrive Entreprise ou des arborescences profondes.
  - L'échec est visible et sans perte, aucun temporaire n'étant créé. Le message (« chemin introuvable ») reste trompeur.

- **Gravité proposée :** Moyenne, au bas de l'échelle.

- **Avis sur le correctif :** correct.
  - Les chemins sont déjà normalisés par `Path.GetFullPath` (`SafePath`, 544). Le préfixe `\\?\`, qui désactive la normalisation Win32, ne change donc pas la cible.
  - Penser au cas UNC (`\\?\UNC\`).
  - `File.OpenHandle` ne peut pas remplacer l'appel natif, car il ne demande pas l'accès DELETE nécessaire au renommage par handle.
  - Mettre à jour PASSATION.md:95.

### COP-06 — Le journal enregistre un nom NTFS non conforme à UTF-16 avec U+FFFD : la reprise de cet élément devient impossible
- **Verdict :** Partiellement réfuté.

- **Vérification de la preuve rouge :** reproduit.
  ```
  CopBleu.exe surrogate
     CopyResult: Complete=False Completed=1/2 Errors=[]
     journal contains \uFFFD: True, contains \uD83D: False
     CopyResult: Complete=False Completed=1/2 Errors=[photo�.jpg : Could not find file '…\src\photo�.jpg'.]
  ```

- **Arguments et preuve :**
  - Le mécanisme est réel : System.Text.Json écrit en UTF-8 et remplace une demi-paire isolée.
  - Les noms contenant une demi-paire de substitution isolée sont exceptionnels. Ils ne peuvent pas être saisis dans l'Explorateur et ne proviennent que de logiciels défaillants.
  - Un seul élément est touché, avec une erreur explicite, sans écriture au mauvais endroit (`ValidateSource` protégerait un homonyme). La première copie fonctionne, et une nouvelle comparaison suivie d'une copie règle le cas.
  - L'« observation connexe » (`NUL`, nom terminé par un point ou une espace) relève de la normalisation de `ComparisonEngine`, donc du domaine Comparaison. Côté copie, il est vrai que `SafePath` lève l'exception pour tout le plan et que le dialogue n'affiche alors que le message (`CopyDialog.cs:101`). Ces noms ne se créent qu'avec `\\?\`, WSL ou SMB.

- **Gravité proposée :** Basse.

- **Avis sur le correctif :** correct sur le principe.
  - Préférer l'exclusion de la ligne avec un avertissement dans l'aperçu.
  - Détection simple : `new UTF8Encoding(false, throwOnInvalidBytes: true).GetByteCount(path)` lève une exception sur une demi-paire.
  - Un champ base64 change le format du journal et casse la lecture par les versions antérieures, que PASSATION veut préserver : à éviter.

### COP-07 — Un `.checkersan-….partial` abandonné est recopié comme un fichier ordinaire par « Compléter les deux dossiers »
- **Verdict :** Concédé.

- **Vérification de la preuve rouge :** reproduit.
  ```
  CopBleu.exe partialplan
     rows: OnlyTarget:.checkersan-0123456789abcdef0123456789abcdef.partial, OnlySource:video.mp4
     two-way plan: 1->2 video.mp4, 2->1 .checkersan-0123456789abcdef0123456789abcdef.partial
  ```
  Aucune référence à `.checkersan-`/`.partial` n'existe hors de `CopyEngine.cs`, ni dans la comparaison, ni dans `AddMissingItems` (114-116). Le dialogue propose « Les deux sens » par défaut (`CopyDialog.cs:103`).

- **Arguments et preuve :** aucune réfutation. Aggravation constatée avec DUP-07 (scénario `onechar`, voir plus bas) : le temporaire complet laissé par l'échec de renommage fait classer le fichier source comme `Moved`, son contenu étant retrouvé dans le temporaire, et apparaît lui-même en `OnlyTarget`.

- **Gravité proposée :** Basse.

- **Avis sur le correctif :**
  - Correct pour le **plan** : écarter `ValidPartialName` et compter ces fichiers dans l'aperçu.
  - Je déconseille de les écarter « dès la comparaison ». Ce sont des fichiers réellement présents : les masquer rendrait faux un verdict « identiques », car un dossier contenant des Gio de temporaires serait déclaré identique. Mieux vaut les laisser en `OnlyTarget`/`OnlySource` avec un libellé « temporaire de copie abandonné ».

### COP-08 — Un dossier créé dans la destination après la comparaison reçoit les attributs (caché, lecture seule) et les dates du dossier source
- **Verdict :** Concédé.

- **Vérification de la preuve rouge :** reproduit.
  ```
  CopBleu.exe existingdir
     bidirectional=False before: attrs=Directory written=2026-10-23
     CopyResult: Complete=True Completed=1/1 Errors=[]
     after: attrs=Hidden, Directory written=2005-05-05
  ```

- **Arguments et preuve :**
  - La fenêtre commence **à la comparaison**, et non à la création du plan : `AddMissingItems` (133-139) ne lit que la source.
  - Dans une copie bidirectionnelle, elle dure jusqu'au traitement des dossiers 2 → 1, placés après tout le sens 1 → 2 (`FinishPlan`, 246).
  - Il faut que l'utilisateur crée lui-même un dossier de même chemin relatif entre-temps.
  - Seules les métadonnées changent ; le contenu reste intact.

- **Gravité proposée :** Basse.

- **Avis sur le correctif :** correct, avec deux précisions indispensables.
  1. Chaque reprise rejoue tous les éléments de type dossier, terminés ou non (boucle 315 sans filtre ; 337-338). L'indicateur doit donc être **collant** (`CreatedByCopy |= !Exists(target)` avant `EnsureDirectory`), sinon une reprise le remettrait à faux pour les dossiers créés lors du passage précédent.
  2. Il doit être **nullable** et journalisé dans `ItemState`. La valeur null, issue d'un ancien journal, conserve le comportement actuel, pour ne pas priver de métadonnées les dossiers créés avant la mise à jour. Un plantage entre la création et l'enregistrement donne un résultat prudent : métadonnées non appliquées.

  Variante écartée : remettre `LastWriteUtc` à `default` lorsque le dossier existe déjà. Ce champ n'est persisté qu'au compactage, et une reprise effacerait les métadonnées des dossiers créés par la copie elle-même.

### COP-09 — Si la copie a été renommée sans que l'attribut ait été appliqué (plantage, erreur, nouvelle tentative réseau), la reprise la déclare terminée sans attributs
- **Verdict :** Concédé.

- **Vérification de la preuve rouge :** trace confirmée, sans relancer la reconstitution du rouge.
  - Si `File.SetAttributes(output…)` (447) lève une exception après le renommage (442), l'élément garde `Completed=false` avec `VerifiedHash` (journalisé en 435).
  - Au passage suivant, `Exists(target)` (344), l'empreinte (347) et la vérification (351-356) mènent à `Completed=true` en 357, sans aucun attribut.

- **Arguments et preuve :**
  - La fenêtre de plantage se compte en microsecondes.
  - Le chemin réaliste est une **erreur réseau transitoire** levée par `SetAttributes` : `Retry` (326) relance `Transfer()`, qui aboutit dans cette branche, et le résultat est `Complete` **sans aucune erreur**.
  - `Identity` (448) ne lève pas d'exception : elle renvoie null (`FileSafety.cs:87-88`). Le rouge la cite à tort comme source possible.

- **Gravité proposée :** Basse.

- **Avis sur le correctif :** correct sur le fond, avec deux précisions.
  - Le handle `existing` (348) est ouvert en `FileAccess.Read` : il faut l'ouvrir avec `FILE_WRITE_ATTRIBUTES` pour appliquer les attributs dessus.
  - Calculer `DestinationIdentity` **après** `SetAttributes`, car l'identité inclut `ChangeTime` (`IdentityText`, 496), comme l'ordre actuel 447-448.

### COP-10 — Si le compactage du journal et sa réouverture échouent tous les deux, `Journal.Record` lève une NullReferenceException : la copie s'arrête au lieu de reporter le compactage
- **Verdict :** Concédé.

- **Vérification de la preuve rouge :** trace confirmée.
  1. `_stream = null` (669).
  2. `File.Move` échoue sur un journal en lecture seule.
  3. Le `new FileStream(..., FileAccess.Write)` du `finally` (679) lève `UnauthorizedAccessException`.
  4. Le `catch` de `Record` (656) déréférence `_stream`, qui vaut null.
  5. L'exception `NullReferenceException` n'est filtrée ni en 455-460 ni en 480-481.
  6. Elle remonte jusqu'à `MainWindow.Copy.cs:64`, qui affiche « Copie non aboutie » avec le message.

  Aucun plantage de l'application. La CLI n'appelle pas `CopyAsync`.

- **Gravité proposée :** Basse.

- **Avis sur le correctif :** en partie faux. La proposition d'ouvrir le flux avec `FileShare.Read|FileShare.Delete` « afin que `File.Move` puisse remplacer le fichier ouvert » **ne fonctionne pas** :
  ```
  CopBleu.exe moveopen
     journal open with Read: File.Move(overwrite) FAILED UnauthorizedAccessException: Access to the path is denied.
     journal open with Read, Delete: File.Move(overwrite) FAILED UnauthorizedAccessException: Access to the path is denied.
  ```
  Appliqué tel quel, chaque compactage échouerait, et le journal grossirait indéfiniment.

  Alternative minimale : garder l'ordre actuel (fermer, puis remplacer, puis rouvrir) et seulement :
  1. dans le `catch` de `Record`, lever une `IOException` explicite (« journal de copie inaccessible ») si `_stream` vaut null ;
  2. en tête de `Record`, si `_stream` vaut null, tenter une réouverture, sinon lever cette même `IOException` ;
  3. faire traiter cette exception comme une interruption (`interrupted = true; break`).

  Aucun octet n'est alors écrit sans journal, car l'enregistrement de la ligne 373 précède l'écriture des données.

### Pistes non prouvées du rouge
1. **Fichier devenu « en ligne uniquement » entre le plan et la copie — plausible, faible.**
   - Avec `SourceIdentity`, le rouge montre lui-même que la copie est bloquée. Une déshydratation pose un attribut et un point d'analyse, ce qui modifie `ChangeTime`.
   - Sans identité, il faut un volume ou un partage qui ne fournit pas `FileIdInfo`. OneDrive, toujours sur NTFS, en fournit une. Le cas se limite donc à une hiérarchisation de stockage (HSM) sur un partage exotique.
   - Conséquence : un téléchargement, sans corruption, puisque la copie est vérifiée contre ce qui a été lu.
   - Correctif simple : appeler `FileSafety.Access(input.SafeFileHandle)` juste après l'ouverture en 342 et refuser `OnlineOnly`. L'ouverture seule ne déclenche pas le rappel ; seule la lecture le fait.
2. **Reprise après échange de disque — plausible, faible.**
   - Exact : `CopyPlan` ne contient que des chemins canoniques avec lettre de lecteur.
   - Sur le second disque, aucun écrasement n'est possible :
     - un fichier existant sans empreinte est refusé (347) ;
     - un fichier présent mais différent l'est aussi (355) ;
     - `PartialIdentity` et `DestinationIdentity` incluent le numéro de série du volume (`FileIdInfo.Volume`).
   - Seul effet : des fichiers manquants sont ajoutés à un disque jamais comparé, avec un verdict « terminée », qui recommande lui-même une nouvelle comparaison.
   - Correctif : enregistrer l'identité de chaque racine (volume et identifiant de fichier). Voir COP-01.
3. **Périphérique qui refuse les E/S non mises en cache (erreur 87) — plausible, non vérifiable ici.**
   - Exact : `OpenPartial` (197) impose toujours `FILE_FLAG_NO_BUFFERING`, alors que le repli n'existe que pour la relecture (217-226).
   - Sur un système de fichiers tiers qui refuse ce mode, chaque fichier échouerait avec une erreur explicite, sans perte.
   - Aucun périphérique de ce type n'est disponible pour le vérifier.
   - Correctif naturel : sur l'erreur 87 levée par `OpenPartial`, rouvrir sans `NO_BUFFERING` (alignement inutile), comme le fait déjà `HashFromDisk`.

### Point transversal DUP-07
- **Verdict : confirmé pour la COPIE.** Un fichier nommé d'un seul caractère, sans extension, n'est **jamais** copié. Gravité proposée côté copie : **Moyenne**.

- **Cause :** `FileSafety.RenameByHandle` alloue et transmet `nameOffset + bytes.Length` octets (`FileSafety.cs:102` et 113). En x64, cela fait 20 + 2 = 22 octets, alors que `sizeof(FILE_RENAME_INFORMATION)` vaut 24. `NtSetInformationFile` répond `STATUS_INFO_LENGTH_MISMATCH`, c'est-à-dire l'erreur Win32 24. Appel concerné : `CopyEngine.cs:442`.

- **Preuve (exécution) :**
  ```
  CopBleu.exe renamecodes
     IntPtr.Size=8
     RenameByHandle(name="q", 2 bytes, struct length 22) -> Win32 24; exists=False
     RenameByHandle(name="qr", 4 bytes, struct length 24) -> Win32 0; exists=True
     RenameByHandle(name="qrs", 6 bytes, struct length 26) -> Win32 0; exists=True

  CopBleu.exe onechar
     plan: sub, a, ab, b.txt, sub\x, sub\xy
     CopyResult: Complete=False Completed=3/5 Errors=[a : The program issued a command but the command length is incorrect. | sub\x : The program issued a command but the command length is incorrect.]
     folder 2: [.checkersan-0408f5d6….partial, ab, b.txt, sub\.checkersan-01d522a0….partial, sub\xy]
     resume: journal readable=True, unfinished=2
     CopyResult: Complete=False Completed=3/5 Errors=[a : … command length is incorrect. | sub\x : … command length is incorrect.]
     comparison after: TreesIdentical=False rows=OnlyTarget:.checkersan-0408f5d6….partial, Moved:a, CommonDirectory:sub, OnlyTarget:sub\.checkersan-01d522a0….partial, Moved:sub\x
  ```

- **Conséquences :**
  - L'échec est déterministe, aussi bien à la première copie qu'à chaque reprise.
  - Le temporaire, complet et vérifié, reste à côté de la destination. Il est réutilisé à la reprise, puisque le journal le retrouve. Chaque nouvelle comparaison suivie d'une copie en laisse un **nouveau**, avec un nouveau GUID.
  - La comparaison suivante classe le fichier `Moved`, son contenu étant retrouvé dans le temporaire, et le temporaire en `OnlyTarget`. Une copie dans les deux sens le recopie alors dans le dossier 1 (COP-07).
  - L'erreur est visible et sans perte, avec un message incompréhensible pour l'utilisateur.
  - Les dossiers d'un seul caractère ne sont pas touchés : ils sont créés par `Directory.CreateDirectory`.

- **Correctif DUP-07 :** correct. Transmettre au moins `Marshal.SizeOf` d'une structure déclarée, ou `Math.Max(nameOffset + bytes.Length, IntPtr.Size == 8 ? 24 : 16)`, avec le reste mis à zéro. La correction se fait à un seul endroit et règle aussi la copie.

- **Test à ajouter dans FolderChecker.Tests :** copier `a` et `sub\x`. Attendu : `Complete=True` et aucun `*.partial` restant.


## Équipe bleue — Doublons et retrait

Code examiné : sources non commitées actuelles et DLL `FolderChecker.Core\bin\Release\net10.0\FolderChecker.Core.dll` (23/10, 15:47, inchangée pendant toute la revue).

**Reproductions bleues.** Elles se trouvent dans `<dossier temporaire>\revue\dup-bleu` (noté `B` ci-dessous) :

- `B\red\` : copie à l'identique du projet rouge (`Program.cs`, `DupRepro.csproj`, `app.manifest`), compilée dans `B\red-out`. Sortie de la nouvelle exécution : `B\red-rerun.txt`.
- `B\blue\Program.cs` et `DupBleu.csproj` : contre-expériences bleues, compilées dans `B\out`. Sortie : `B\sortie-bleu.txt`.

Commandes (bash) :

```bash
B="<dossier temporaire>/revue/dup-bleu"
cd "<dépôt>" && export DOTNET_CLI_HOME="$PWD/.tools/cli" NUGET_PACKAGES="$PWD/.tools/packages" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 APPDATA="$PWD/.tools/appdata" DOTNET_ROOT="$PWD/.tools/dotnet"
./.tools/dotnet/dotnet build "$B/red/DupRepro.csproj" -c Release --configfile ./NuGet.Config -o "$B/red-out" && "$B/red-out/DupRepro.exe" all
./.tools/dotnet/dotnet build "$B/blue/DupBleu.csproj"  -c Release --configfile ./NuGet.Config -o "$B/out"     && "$B/out/DupBleu.exe" all   # ou b01 | b01wide | b02 | b03 | b04 | b05 | b05usn | b06 | b07 | b08
```

**Conditions d'essai :**

- Tout a été créé dans `B\work` et `B\work-bleu`.
- Le mode Corbeille réel n'a jamais été exécuté.
- Aucun chemin réel de `%LOCALAPPDATA%\CheckerSan` n'a été utilisé.
- Le seul changement d'ACL porte sur le dossier d'essai `B\work-bleu\b03-…\Private` : héritage retiré et accès réduit à l'utilisateur courant.

**Résultat de la nouvelle exécution rouge.** Les neuf scénarios rouges se reproduisent à l'identique (`B\red-rerun.txt`). Aucune trouvaille n'est donc réfutée sur le fond. Les désaccords portent sur la gravité, la portée et plusieurs correctifs.

| ID | Gravité rouge | Verdict bleu | Gravité proposée | Correctif rouge : avis |
|---|---|---|---|---|
| DUP-01 | Haute | Partiellement réfuté | Moyenne | Point 1 correct. Point 2 risqué tel que formulé (bloque tout le plan). Point 3 correct. Point 4 facultatif |
| DUP-02 | Haute | Partiellement réfuté | Moyenne | Correct et faisable (vérifié), avec une fenêtre résiduelle ; à appliquer élément par élément |
| DUP-03 | Haute | Partiellement réfuté | Moyenne | Correct. Les seuls contrôles de chemin (point 1) laissent une course ; réutiliser `DirectoryLocks` et `RenameByHandle` |
| DUP-04 | Moyenne | Partiellement réfuté | Basse | Correct. Le point 1 change le format du journal (test et passation à adapter) ; une alternative minimale suffit |
| DUP-05 | Moyenne | Partiellement réfuté | Basse | USN utile en contrôle supplémentaire. Le repli « jamais de hachage complet sans USN » est risqué (régression de la reprise sur partage) |
| DUP-06 | Moyenne | Partiellement réfuté | Basse | Correct. Réutiliser `scan.CanonicalRoot` ; appliquer la même règle dans `QuarantineSessions` |
| DUP-07 | Moyenne | Partiellement réfuté | Basse | Correct et complet (vérifié). Il corrige aussi `CopyEngine` |
| DUP-08 | Basse | Concédé | Basse | Correct (vérifié) ; ignorer la lecture seule est un choix de produit à signaler dans l'aperçu |

---

### DUP-01 — Une quarantaine sous la racine analysée devient la « copie conservée » ; la règle par défaut retire l'exemplaire vivant

- **Verdict :** Partiellement réfuté.

- **Vérification de la preuve rouge :** relancé (`red-rerun.txt`, scénario `nested`). Le résultat est identique : le groupe contient `2020\.CheckerSan-Quarantine\<guid>\Backup\IMG_001.jpg | 2020\IMG_001.jpg`, et `FirstPath` retire `2020\IMG_001.jpg`. Code relu :
  - `DuplicateEngine.cs:294-301` : exclusion seulement si `directory == _root` ;
  - `DuplicateRemoval.cs:311` : premier composant seulement ;
  - `DuplicateSelection.cs:28-35`.

- **Arguments et preuve :**
  1. **Comportement documenté.** `PASSATION.md:170` dit : « Seule une quarantaine reconnue directement sous la racine est exclue du prochain scan de doublons. » L'inventaire d'une quarantaine imbriquée est donc un choix assumé. Le défaut réel est ailleurs : rien n'empêche cette copie de devenir le gardien.
  2. **Aucune perte : l'application restaure elle-même le contenu.** `DupBleu.exe b01` reprend le scénario rouge jusqu'à la suppression définitive de l'exemplaire vivant, puis utilise le bouton « Restaurer » ouvert sur `Photos\2020` :
     ```
     [permanent removal, FirstPath] complete=True deleted=1 bytesRemoved=50000
        live 2020\IMG_001.jpg exists: False
        sessions offered by Restore for Photos\2020: 1 (waiting=1)
        restore: restored=1 errors=0
        content back at 2020\Backup\IMG_001.jpg: True ; identical to original photo: True
     ```
  3. **L'aperçu obligatoire montre le choix.** Les lignes construites par `DuplicateRemovalDialog.cs:43-49`, reproduites par `b01`, affichent :
     ```
     preview: RETIRER   Photos\2020\IMG_001.jpg
     preview: CONSERVER   Photos\2020\.CheckerSan-Quarantine\a05a…\Backup\IMG_001.jpg
     ```
     La grille affiche aussi le chemin relatif complet (`DuplicatesWindow.xaml:49`). La protection reste faible sur une longue liste, mais l'information est visible.
  4. **Aucune présélection automatique.** L'utilisateur doit choisir la règle puis cliquer sur « Sélectionner » (`DuplicatesWindow.xaml.cs:173-188`).
  5. **Le mode par défaut laisse l'exemplaire vivant récupérable.** C'est la Corbeille (`DuplicatesWindow.xaml:62`). Le mode définitif exige une case de consentement (`DuplicateRemovalDialog.cs:33, 54-56`).
  6. **Une perte demande une action manuelle en dehors de l'application.** Il faudrait vider ou supprimer la quarantaine à la main (et la Corbeille, en mode par défaut). L'application n'a aucune fonction de purge : aucune occurrence de `Directory.Delete`, ni de « Vider », dans `FolderChecker.App`.
  7. **Ce qui reste réel :**
     - le piège de sélection (3 règles sur 4 peuvent garder la copie en quarantaine) ;
     - aucun avertissement au scan du parent (`b01` : « warnings of parent scan: (none) ») ;
     - l'exemplaire vivant est remplacé par une copie que l'interface présente comme provisoire.

- **Gravité proposée :** Moyenne. La sélection est trompeuse, mais il n'y a pas de perte immédiate. L'application sait restaurer le contenu, et une perte exige ensuite une suppression manuelle de la quarantaine.

- **Avis sur le correctif :**
  - **Point 1** (exclure toute quarantaine reconnue, à toute profondeur, avec un avertissement) : correct et peu risqué, car `IsManagedQuarantine` n'est appelé que sur les dossiers portant ce nom. Il faut mettre à jour `PASSATION.md:170`.
  - **Point 2, risqué tel que formulé.** `BuildPlan` appelle `ValidatePath` sur **tous les fichiers de tous les groupes** (`DuplicateRemoval.cs:285-287`), pas seulement sur les groupes sélectionnés. Un refus « à n'importe quel niveau » placé dans `ValidatePath` bloquerait donc tout retrait dès qu'un dossier homonyme non reconnu existerait n'importe où. `DupBleu.exe b01wide` montre que c'est déjà le cas au premier niveau :
    ```
    group 98D0BAEA1C23: x.bin | y.bin
    group 40815D36BCA7: .CheckerSan-Quarantine\other.bin | ordinary.bin
    [permanent removal of y.bin only (unrelated group)] complete=False deleted=0
       error: Le chemin d'un doublon est invalide ou sort du dossier analysé.
    ```
    Alternative précise : dans `BuildPlan`, après le calcul de `selected` et `keeper` (lignes 291-296), refuser **ce seul groupe** si le gardien ou un fichier sélectionné a un composant `.CheckerSan-Quarantine`. Il faut aussi choisir comme `keeper` le premier fichier non sélectionné **hors quarantaine** : aujourd'hui, c'est simplement le premier non sélectionné (ligne 293). Enfin, laisser `ValidatePath` vérifier seulement la forme des chemins.
  - **Point 3** (`Extras` ne garde jamais une copie en quarantaine) : correct. Si aucune copie n'est hors quarantaine, le groupe doit être compté dans `UndecidedGroups`.
  - **Point 4** (attribut caché) : facultatif si les points 1 et 3 sont faits. Il rend la quarantaine invisible dans l'Explorateur par défaut, et n'a aucun effet quand l'utilisateur décoche « ignorer cachés/système ».
  - **Complément utile :** dans l'aperçu, marquer toute ligne CONSERVER située dans une quarantaine.

### DUP-02 — Mode Corbeille : le fichier vérifié reste renommable et le Shell agit sur le chemin

- **Verdict :** Partiellement réfuté.

- **Vérification de la preuve rouge :** relancé (`recycleswap`) avec un résultat identique :
  - le renommage puis la recréation au même chemin réussissent pendant que le handle vérifié est ouvert ;
  - `RecycledOutcome` lève une `IOException` en variante A et renvoie `""` en variante B.

  Code relu :
  - `DuplicateRemoval.cs:170-188` : tout le groupe est haché avant la première mutation ;
  - `320-321` : `FileShare.Read | FileShare.Delete` quand `shellMayMove` est vrai ;
  - `212`, `430-437` : `pFrom` est un chemin ;
  - `440-447`.

  La fenêtre est réelle et peut durer : elle va de l'ouverture du handle (avant le hachage du fichier lui-même) jusqu'à l'appel Shell. Le dossier parent épinglé (`DirectoryLocks.Pin`, ligne 655, partage `ReadWrite` sans `Delete`) protège le dossier, pas ses enfants.

- **Arguments et preuve :**
  1. **Choix documenté.** `PASSATION.md:167` décrit exactement ce fonctionnement : handle avec `FILE_SHARE_DELETE`, appel Shell, puis contrôle sur le même handle. Le défaut tient à la classification après coup et à l'absence de journalisation, pas à une suppression par chemin cachée.
  2. **La variante A n'est pas « sans aucune trace ».** L'`IOException` de `RecycledOutcome` est rattrapée en `DuplicateRemoval.cs:254-257` et placée dans `Errors`. La fenêtre l'affiche (`DuplicatesWindow.xaml.cs:245`) avec l'emplacement du fichier vérifié (sortie rouge : « …a été déplacé vers un emplacement inattendu : …\copy.bin.old »). Les deux contenus restent intacts :
     - le fichier vérifié est à `copy.bin.old` ;
     - le fichier substitué est dans la Corbeille, que Windows sait restaurer à son chemin d'origine grâce à son fichier `$I`.

     Il manque seulement la ligne du journal CSV, et le message ne dit pas qu'un autre élément est parti à la Corbeille.
  3. **La perte définitive exige un cumul de conditions :** un échange concurrent sur le fichier précis sélectionné, pendant l'opération, **et** un fichier trop volumineux pour la Corbeille, **et** une confirmation de l'utilisateur dans la fenêtre de Windows (`FOF_WANTNUKEWARNING`, drapeaux `0x4454`, ligne 433). Dans tous les autres cas, l'élément est dans la Corbeille.
  4. **La variante B suppose que l'utilisateur, ou un client de synchronisation, réenregistre la copie qu'il vient de choisir de retirer, pendant le retrait.** Les éditeurs qui gardent le document ouvert sans partage de suppression (Office garde un verrou sur le fichier ouvert) feraient plutôt échouer l'appel Shell (ligne 435), avec une erreur : ce point est tiré du comportement de Windows, non testé ici. Le faux message « supprimé définitivement (trop volumineux) » reste un vrai défaut.
  5. **Ce qui reste réel :**
     - un élément non vérifié peut partir à la Corbeille ;
     - le journal est absent (A) ou faux (B) ;
     - la promesse « le résultat est contrôlé sur ce même handle » ne distingue pas l'action d'un tiers.

- **Gravité proposée :** Moyenne. Le scénario exige une modification concurrente ciblée pendant l'opération. Ce qui est retiré par erreur reste dans la Corbeille, sauf cumul improbable avec une confirmation de Windows. La variante A affiche une erreur.

- **Avis sur le correctif :** correct, et sa faisabilité est vérifiée par `DupBleu.exe b02` :
  ```
  handle 1 (read, share=Read): open
  concurrent rename refused: IOException: …being used by another process.
  concurrent delete refused: IOException: …being used by another process
  handle 2 (read, share=Read|Delete) while handle 1 open: open
  same FileIdInfo on both handles: True
  rename after closing handle 1: succeeded (residual window)
  ```
  Précisions :
  - Le deuxième handle doit être ouvert **élément par élément**, juste avant son propre appel Shell, et non pour tout le groupe d'un coup. Sinon, la fenêtre redevient celle des appels Shell précédents.
  - La fenêtre résiduelle va de la fermeture du handle 1 à l'ouverture interne du Shell. Elle ne peut pas être fermée avec l'API publique de la Corbeille, qui travaille par chemin.
  - Comparer aussi `FinalPath` du handle 2 avec `canonicalRoot\RelativePath`, comme dans `OpenVerified` (lignes 343-345).
  - Points 3 et 4 (ligne d'intention, classement « non vérifié possiblement mis à la Corbeille ») : pertinents.
  - Point d'injection du Shell pour les tests : indispensable, car le seul test actuel (`DuplicateOptionsTests.cs:66-100`) utilise la vraie Corbeille.

### DUP-03 — « Restaurer » suit les jonctions de la quarantaine (manifeste falsifié)

- **Verdict :** Partiellement réfuté.

- **Vérification de la preuve rouge :** relancé (`restorejunction`) avec un résultat identique :
  - (a) `victim.docx` est déplacé de `Private` vers `Shared\evil` ;
  - (b) la session-jonction est listée, `salary.xlsx` est déplacé, et `Private2\EmptyProjectFolder` est supprimé.

  Code relu :
  - `DuplicateRemoval.cs:486-488` : seule la quarantaine est contrôlée ;
  - `493-504` : `File.Exists`, `FileStream` et `File.Move` par chemin ;
  - `533-543` : la racine de `RemoveEmptyFolders` n'est pas contrôlée ;
  - `450-462` : la session-jonction est listée.

  La garantie « liens jamais suivis » (`PASSATION.md:125, 173`) n'est donc pas tenue du côté source de la restauration.

- **Arguments et preuve :**
  1. **Précondition forte : un autre compte malveillant qui peut écrire dans le dossier analysé et dans sa quarantaine.** Un tel compte peut déjà détruire tout le contenu du dossier analysé. Le surcroît de dégâts se limite à ce qui se trouve **hors de la racine**. Une jonction n'apparaît jamais d'elle-même dans une session : la mise à l'écart ne déplace que des fichiers (`RenameByHandle`, ligne 205), et `DirectoryLocks.Pin` refuse les liens (lignes 663-668).
  2. **Pour déplacer un fichier, l'attaquant doit en connaître le SHA-256 et la taille,** donc pouvoir déjà le lire. La restauration vérifie le contenu (lignes 500-502). `DupBleu.exe b03` montre qu'une entrée dont le hachage est inconnu ne bouge pas :
     ```
     restore: restored=2 errors=« evil\unknown.docx » a changé depuis sa mise à l'écart : il n'est pas restauré.
     unknown.docx (hash not known) still in Private: True
     ```
  3. **Le déplacement ne donne aucun nouvel accès en lecture.** Sur le même volume, le fichier garde sa liste d'accès d'origine et n'hérite pas de celle du dossier partagé. Extrait de `b03`, où `Private` n'accorde l'accès qu'à l'utilisateur courant :
     ```
     ACL of victim before: <PC>\<utilisateur>:(I)(F)
     ACL of moved victim : <PC>\<utilisateur>:(I)(F)
     ACL of a new file created in Shared\evil: <SID>:(I)(M) ; …\<groupe local>:(I)(M) ; … ; NT AUTHORITY\SYSTEM:(I)(F) ; BUILTIN\Administrators:(I)(F) ; …<utilisateur>:(I)(F)
     ```
     L'affirmation rouge « un simple accès en lecture devient une suppression » ne tient que si l'attaquant a le **contrôle total** du dossier partagé : `FILE_DELETE_CHILD` n'est pas inclus dans « Modification ». Avec une jonction vers un **autre volume**, `File.Move` copie, et la copie hériterait de la liste d'accès de la destination ; ce cas n'a pas été testé, car le disque G: est exclu.
  4. **Le fichier déplacé n'est pas perdu.** Le journal inscrit chaque « Restauré », avec la source et la destination (sortie rouge, dernières lignes), ce qui permet de le remettre en place.
  5. **Ce qui reste réel :**
     - l'effacement de dossiers **vides** extérieurs, possible sans connaître aucun hachage dans le cas (b) ;
     - le déplacement hors d'un emplacement privé de fichiers que le tiers peut lire ;
     - la session falsifiée, dont le `CreatedAt` est choisi par le tiers, peut être la première proposée (`QuarantineRestoreDialog.cs:34`, `SelectedIndex = 0` sur une liste triée de la plus récente à la plus ancienne).

- **Gravité proposée :** Moyenne. La garantie de non-suivi des liens est violée. L'exploitation suppose un co-utilisateur hostile qui a déjà des droits d'écriture sur le dossier et de lecture sur la cible. L'effet se limite à un déplacement sans nouvelle exposition du contenu, visible dans le journal, et à l'effacement de dossiers vides.

- **Avis sur le correctif :**
  - **Correct et complet** si les points 2 à 4 sont appliqués.
  - **Le point 1 seul** (contrôle des composants par chemin) laisse une course : le tiers peut remplacer un dossier par une jonction entre le contrôle et `File.Move`.
  - **Alternative plus simple qui réutilise le code existant :**
    - `using var locks = new DirectoryLocks(root)` ;
    - `locks.Pin(session)`, puis `locks.PinParent(stored)`. Chaque composant est ouvert avec `OPEN_REPARSE_POINT`, les liens sont refusés, et le partage sans `Delete` empêche tout renommage pendant l'opération (lignes 648-672) ;
    - ouvrir `stored` comme dans `OpenVerified`, avec `OPEN_REPARSE_POINT`, `Access == Normal` et `FinalPath == canonical(session)\rel`. Calculer le SHA-256 sur ce handle ;
    - le déplacer avec `FileSafety.RenameByHandle` vers `locks.CreateAndPinParent(original)`.
  - **Contrôle « un seul lien physique » :** facultatif. Déplacer un lien physique ne retire qu'un nom, et le fichier privé garde son chemin d'origine.
  - **Ajouts :**
    - refuser un manifeste qui serait lui-même un lien ;
    - dans `RemoveEmptyFolders`, partir du handle épinglé de la session.

### DUP-04 — Journal écrit après l'action : une suppression définitive peut n'être ni journalisée ni rapportée

- **Verdict :** Partiellement réfuté.

- **Vérification de la preuve rouge :** relancé (`logfail`) avec un résultat identique : `DeleteAsync` lève une exception, `copy.bin` est supprimé et le journal ne contient que l'en-tête. `DupBleu.exe b04` confirme l'origine de l'exception. Elle vient du `finally` (`DuplicateRemoval.cs:258-262`), quand `StreamWriter.Dispose` tente de vider le tampon de `FileStream`, laissé plein par l'échec de `Flush` en ligne 626 :
  ```
  [Permanent] DeleteAsync threw IOException; frames:
     at System.IO.Strategies.BufferedFileStreamStrategy.Dispose(Boolean disposing)
     at System.IO.StreamWriter.CloseStreamFromDispose(Boolean disposing)
     at System.IO.StreamWriter.Dispose(Boolean disposing)
     at FolderChecker.Core.DuplicateRemoval.Remove(…)
  [Permanent] copy.bin still in place: False ; keep.bin: True ; journal lines: 1
  ```

- **Arguments et preuve :**
  1. **Aucune perte de contenu.** Le fichier supprimé est un doublon dont le SHA-256 a été relu. La copie conservée a été relue elle aussi et verrouillée pendant tout le groupe (`DuplicateRemoval.cs:173-177`). Elle est intacte (`keep.bin: True`).
  2. **La passation n'a jamais promis davantage.** `PASSATION.md:169` dit : « ouvert avant la première modification : si l'écriture est impossible, rien n'est retiré. Une erreur d'écriture en cours arrête les actions suivantes. » Ces deux points sont tenus : l'ouverture précède toute mutation (ligne 160), et l'exception arrête la boucle. La passation admet aussi que l'opération n'est pas une transaction (`PASSATION.md:173`).
  3. **En mode Quarantaine, rien ne se perd.** Le manifeste, écrit et forcé sur disque avant le déplacement (lignes 203-204), suffit à restaurer. Extrait de `b04` :
     ```
     [Quarantine] sessions offered by Restore: 1 waiting=1
     [Quarantine] restore: restored=1 ; copy.bin back: True
     ```
  4. **La condition est rare.** Il faut un échec d'écriture **après** une ouverture réussie, sur `%LOCALAPPDATA%` : disque plein, par exemple. Un tableur qui ouvre le CSV ne peut pas provoquer ce cas pendant l'opération, car le fichier est déjà ouvert en écriture ; s'il était ouvert avant, `ActionLog.Open` échoue et rien n'est retiré.
  5. **Ce qui reste réel :**
     - l'exception qui sort du `finally` fait perdre `DeletedPaths` et `Errors`. L'interface n'affiche alors que le message (`DuplicatesWindow.xaml.cs:256`) et saute `ActivityLog.Write` (ligne 236) ;
     - en mode Définitif, le chemin retiré n'est consigné nulle part.

     Aucun test ne couvre ce cas.

- **Gravité proposée :** Basse. Le défaut touche la traçabilité et la robustesse, dans un cas rare. Le contenu retiré existe toujours dans la copie conservée, qui a été vérifiée.

- **Avis sur le correctif :** correct.
  - **Point 1** (ligne d'intention, puis confirmation) : donne la garantie la plus forte. En cas de disque plein, l'intention échoue et rien n'est retiré. En revanche, il passe à deux lignes par action, ce qui casse `DuplicateOptionsTests.cs:79` (« a header and one line per action ») et contredit `PASSATION.md:169` (« une ligne CSV par action »). Il faut adapter les deux.
  - **Alternative minimale sans changement de format :** les points 2 et 3 du rouge, plus deux ajouts :
    - inscrire dans `errors` « retiré sans ligne au journal : <chemin> » ;
    - entourer séparément `manifest?.Dispose()` et `log?.Dispose()`. Aujourd'hui, une exception dans le premier empêche le second (lignes 260-261).

    L'interface liste alors les fichiers retirés (« Retiré : … », ligne 245), et le journal d'activité est écrit.
  - **`Restore` :** appliquer la même protection au `using var log` (ligne 489).

### DUP-05 — Cache de reprise : un faux groupe de doublons si les quatre dates sont restaurées

- **Verdict :** Partiellement réfuté.

- **Vérification de la preuve rouge :** relancé (`ctimeforge`) avec un résultat identique : `reused=2`, un groupe `a.bin | b.bin` alors que les SHA-256 diffèrent, et un retrait refusé par la relecture. Code relu : `DuplicateEngine.cs:391-404` et `47-48`.

- **Arguments et preuve :**
  1. **Les API usuelles de dates ne trompent pas le cache.** `DupBleu.exe b05` modifie `b.bin` sur place, puis remet ses dates de création, d'écriture et d'accès avec `File.Set*TimeUtc` (`SetFileTime`) :
     ```
     b.bin edited in place, creation/write/access restored with SetFileTime; real SHA differ: True
     [second scan with cache] verified=True errors=0 groups=0 reused=1
     ```
     Le test existant `DuplicateCacheDetectsRestoredTimestamps` (`DuplicateTests.cs:112-132`) couvre le même cas. Seul un appel délibéré à `SetFileInformationByHandle(FileBasicInfo)`, qui réécrit `ChangeTime`, trompe le cache. C'est une technique de falsification, pas le comportement d'un outil ordinaire.
  2. **La promesse documentée est tenue.** `PASSATION.md:141` promet de détecter une modification « avec restauration de la date d'écriture ». `PASSATION.md:118` exige un « contrôle strict », qui inclut ici `ChangeTime`.
  3. **Aucune perte dans l'application,** comme le rouge le reconnaît : la relecture refuse le retrait. Un tiers capable de falsifier `ChangeTime` sur ces fichiers peut de toute façon les modifier ou les détruire directement.

- **Gravité proposée :** Basse. Le faux verdict n'est produit que par une falsification délibérée de `ChangeTime`, et le retrait reste protégé par la relecture.

- **Avis sur le correctif :**
  - **USN.** C'est un bon signal supplémentaire. `DupBleu.exe b05usn` montre qu'une simple lecture ne le change pas, et que la falsification des quatre dates ne l'empêche pas de changer :
    ```
    ChangeTime restored: True
    USN initial=5E8BD5FF28 after plain read=5E8BD5FF28 (unchanged: True) after edit+forged times=5E8BD60000 (changed: True)
    ```
  - **Repli risqué : « sans USN, ne réutiliser que les échantillons ».** Sur les partages SMB (cas d'usage principal), FAT et exFAT, et les volumes sans journal USN, la réutilisation des hachages complets disparaîtrait. Or c'est elle qui rend la reprise utile (`PASSATION.md:161, 164`). Je n'ai pas pu tester `FSCTL_READ_FILE_USN_DATA` sur un partage, faute de partage disponible.

    Alternative : ajouter l'USN à l'identité quand il est disponible, et garder sinon le comportement actuel. Documenter dans `PASSATION.md:141` qu'une falsification délibérée de `ChangeTime` n'est pas détectée hors USN.
  - **Marquer les groupes fondés sur des hachages réutilisés,** dans le résultat et dans l'export : pertinent et sans risque.

### DUP-06 — Quarantaine non restaurable si le dossier est rouvert sous un autre nom

- **Verdict :** Partiellement réfuté.

- **Vérification de la preuve rouge :** relancé (`restorealias`) avec un résultat identique : la session est listée pour le chemin réel, et la restauration refuse avec une `InvalidOperationException`. Code relu :
  - `DuplicateRemoval.cs:548` : la racine est stockée sous forme textuelle ;
  - `486-488` : la comparaison est textuelle ;
  - `450-467` : `QuarantineSessions` ne fait pas cette comparaison.

  Le cas lecteur mappé / UNC est plausible : sur un partage, la Corbeille est indisponible et l'interface choisit d'office la mise à l'écart (`DuplicatesWindow.xaml.cs:134-135`).

- **Arguments et preuve :**
  1. **La session reste restaurable par le bouton « Restaurer », à condition de rouvrir le dossier avec l'orthographe d'origine.** `DupBleu.exe b06` :
     ```
     restore via real path: InvalidOperationException
     sessions offered when the folder is reopened with the original spelling: 1
     restore via original spelling: restored=1 errors=0 ; copy.bin back (real path): True
     ```
  2. **L'orthographe d'origine est écrite à deux endroits :** sur la première ligne du manifeste (`"Root":…`) et dans chaque ligne « Mis à l'écart » du journal, colonnes Chemin et Destination. Le message de refus dit d'ailleurs que le manifeste « concerne un autre dossier ».
  3. **Aucun fichier n'est déplacé, écrasé ni perdu.** Le refus survient avant toute action.

- **Gravité proposée :** Basse. La fonction échoue avec un message peu clair, mais un contournement existe dans l'application et les données ne courent aucun risque.

- **Avis sur le correctif :** correct. Précisions :
  - **Faisabilité.** `b06` le confirme : « CanonicalDirectory(alias) == CanonicalDirectory(real): True ».
  - **`Remove` connaît déjà la racine canonique.** Elle est calculée et vérifiée en `DuplicateRemoval.cs:162-164`. La passer à `CreateManifest` au lieu de la recalculer.
  - **`QuarantineSessions` doit appliquer la même règle d'acceptation.** Sinon le bouton continue d'apparaître pour une session refusée.
  - **Limite de la canonisation.** Elle ne rapproche pas deux noms de serveur différents (`\\nas` et `\\192.168.x.x`). Afficher la racine d'origine dans le message de refus, comme le propose le rouge, couvre ce reste.

### DUP-07 — Mise à l'écart impossible pour un nom d'un seul caractère

- **Verdict :** Partiellement réfuté. Le défaut est réel tel que décrit ; seule la gravité est surestimée.

- **Vérification de la preuve rouge :** relancé (`onechar`) avec un résultat identique : code 24 avec 22 octets, et succès dès 2 caractères. Code relu : `FileSafety.cs:99-113`.

- **Arguments et preuve :**
  1. **L'échec est sûr et explicite.** Le fichier `a` reste en place, et le message indique qu'aucune suppression définitive n'a été tentée.
  2. **L'entrée « fantôme » du manifeste est inoffensive.** La restauration ignore toute entrée dont le fichier n'existe pas (`DuplicateRemoval.cs:495`), et `Waiting` ne la compte pas. `DupBleu.exe b07` :
     ```
     Restore offers 1 session(s): files=2 waiting=1
     restore: restored=1 errors=0 ; ab back: True ; a untouched: True
     quarantine session folder removed after restore: True
     ```
  3. **Seuls les noms d'exactement un caractère UTF-16, sans extension, sont touchés.** `a.txt` fonctionne. Ces noms sont rares dans des données d'utilisateur.
  4. **Ce qui reste réel :** l'opération s'arrête, les groupes suivants ne sont pas traités, et le même défaut touche la finalisation dans `CopyEngine.cs:442` (hors de mon périmètre).

- **Gravité proposée :** Basse dans le domaine Doublons : fonction indisponible pour une classe de noms rare, échec sûr. L'équipe Copie doit évaluer à part l'effet sur `CopyEngine`.

- **Avis sur le correctif :** correct et complet, vérifié par `b07` :
  ```
  rename to "q" (1 char) with length=22: Win32 error 24; exists now: False
  rename to "p" (1 char) with length=max(22,24)=24: Win32 error 0; exists now: True
  ```
  La correction se fait dans `FileSafety.RenameByHandle`, utilisée aussi par `CopyEngine` : elle répare les deux. L'allocation doit utiliser la même taille complétée, et `FileNameLength` doit rester égal à `bytes.Length`.

### DUP-08 — Un doublon en lecture seule bloque la suppression définitive et arrête tous les groupes suivants

- **Verdict :** Concédé.

- **Vérification de la preuve rouge :** relancé (`readonly`) avec un résultat identique : « Windows a refusé la suppression du fichier vérifié. », et le second groupe n'est pas traité. Code relu :
  - `DuplicateRemoval.cs:231-233` ;
  - les exceptions sortent de la boucle des groupes (`166`, `254`) ;
  - les messages des lignes `336` et `363` ne parlent que de « ce groupe ».

  Aucun test ne couvre les fichiers en lecture seule dans le retrait.

- **Arguments et preuve :** il n'y a pas de perte. La mise à l'écart fonctionne sur ce fichier, comme le montre la sortie rouge. `DupBleu.exe b08` confirme la cause, l'erreur 5 sur `FileDispositionInfo`, et la solution :
  ```
  FileDispositionInfo(DeleteFile=1) on read-only file: ok=False error=5
  FileDispositionInfoEx(DELETE|IGNORE_READONLY) on read-only file: ok=True error=0
  file still exists after handle close: False
  ```

- **Gravité proposée :** Basse (accord avec le rouge).

- **Avis sur le correctif :**
  - **`FileDispositionInfoEx`** (classe 21, drapeaux `0x1 | 0x10`) : correct sur le handle vérifié, qui a déjà l'accès DELETE, et disponible sur Windows 11.
  - **Choix de produit.** L'attribut lecture seule peut être une protection voulue par l'utilisateur. Il faut le signaler dans l'aperçu, avant de l'ignorer.
  - **Poursuivre avec les groupes suivants** : acceptable pour les refus propres à un groupe (lecture seule, contenu changé, liens physiques). Les erreurs globales doivent rester bloquantes : journal, racine déplacée, quarantaine non reconnue. À défaut, corriger les messages des lignes 336, 342 et 363 pour dire que l'opération entière s'arrête.

### Pistes non prouvées du rouge

1. **`RecycleBinAvailable` se fonde sur la lettre du chemin textuel :** plausible, gravité Basse.
   - **Trace :** `DuplicateRemoval.cs:84-91` appelle `GetDriveTypeW(Path.GetPathRoot(...))`. Le volume physique n'est pas résolu.
   - **Effet :** si Windows ne peut pas recycler, `FOF_WANTNUKEWARNING` (drapeaux `0x4454`, ligne 433) affiche sa propre demande de confirmation avant toute suppression définitive. Après coup, `RecycledOutcome` ne trouve qu'une suppression en attente sur le handle vérifié (ligne 442) : le fichier supprimé est bien celui qui a été vérifié, sauf échange concurrent (DUP-02).
   - **Ce qui reste :** le motif journalisé (« trop volumineux ») est faux, et la décision repose sur la confirmation de Windows.
   - **Non testable ici :** points de montage (droits d'administrateur), `subst` interdit, Corbeille réelle interdite.

2. **Volumes FAT32/exFAT :** il ne s'agit pas d'un risque pour les données. C'est un refus sûr, déjà documenté.
   - **Trace :** `IdentityString` renvoie null si `ChangeTime <= 0` (`DuplicateEngine.cs:47-48`). `SameIdentity(…, null)` renvoie false (`DuplicateRemoval.cs:376-377`), puis `OpenVerified` refuse (ligne 342).
   - **La passation l'annonce :** `PASSATION.md:173` (« les identités absentes … sont refusés ») et `PASSATION.md:426` (« Certains systèmes de fichiers … ne fournissent pas les identités natives »).
   - **Ce qui reste plausible :** la fonction de retrait pourrait être inutilisable sur ces supports. Non vérifié, faute de volume FAT. À mentionner dans l'aide si cela se confirme.

3. **Racine atteinte par une jonction vers un autre volume fixe, en mode Corbeille :** plausible, gravité Basse.
   - **Trace :** `Remove` accepte cette racine. Les contrôles canoniques passent (`DuplicateRemoval.cs:162-164, 343-345`), et `RecycleBinAvailable` teste la lettre du chemin textuel.
   - **Deux issues possibles :**
     - le Shell recycle par copie puis suppression : le journal dit à tort « supprimé définitivement », mais la copie est dans la Corbeille ;
     - le Shell supprime définitivement, après sa propre confirmation.

     Aucune des deux ne retire un fichier non vérifié.
   - **Non testable ici :** Corbeille réelle interdite, et aucun second volume autorisé.


## Équipe bleue — Automatisation, ligne de commande et journaux

Code examiné : version de travail non commitée. Les DLL Release utilisées (`FolderChecker.Core.dll` 15:47, `checkersan-cli.exe/.dll` 14:58, `CheckerSan.dll` 15:08) sont postérieures aux dernières modifications des sources concernées (Core 14:57, Cli 14:58, App 15:08). Aucun projet du dépôt n'a été compilé. Aucune tâche planifiée n'a été créée, modifiée, lancée ni supprimée : la définition de tâche n'a été inspectée qu'**en mémoire** (`Schedule.Service.NewTask(0)` puis `XmlText`, sans `RegisterTaskDefinition`). Les droits NTFS modifiés pour AUT-02 ont été rétablis (`icacls /remove:d`, vérifié par la relecture des fichiers).

**Incident à signaler.** Un premier essai cmd d'AUT-05 (`cmd /c "set LOG=& … --log "%LOG%""`) a développé `%LOG%` avant le `set` et créé à la racine du dépôt un fichier `%LOG%` de 421 octets, qui ne contenait qu'une ligne d'activité de mes dossiers d'essai. Je l'ai déplacé dans `aut-bleu\work\percent-LOG-cree-par-erreur.log`. Après coup, aucun fichier du dépôt n'a été modifié depuis 45 minutes (hors `bin`, `obj`, `.git`, `.tools`, `revue`, `.test-data`).

Dossier bleu (abrégé `BLEU\`) : `<dossier temporaire>\revue\aut-bleu`
- `BLEU\bleu-cli.ps1 -Work BLEU\work -Only 02,03,04,05,10` : vrai `checkersan-cli.exe` ;
- `BLEU\cli\Program.cs` → `BLEU\out-cli\bleucli.exe xml|reports|lock|cancel|host|relative` (Core + checkersan-cli.dll) ;
- `BLEU\wpfexit\Program.cs` → `BLEU\out-wpfexit\bleuexit.exe <mode> <journal>` : **vraie** `ScheduledResultWindow` de `CheckerSan.dll`, logique d'`App.RunScheduledAsync` recopiée, `void Main` comme l'app ;
- `BLEU\wpfpairs\Program.cs` → `BLEU\out-wpfpairs\bleupairs.exe BLEU\work` : vraie `PairsWindow`, faux schtasks ;
- `BLEU\work\log-vide.cmd` : script cmd d'AUT-05 ;
- les exécutables rouges (`autcli`, `wpfpairs`, `wpfexit`) ont été relancés tels quels sur `BLEU\work`, avec `TMP`/`TEMP` redirigés vers `BLEU\tmp`.

| ID | Gravité rouge | Verdict bleu | Gravité proposée | Correctif rouge : avis |
|---|---|---|---|---|
| AUT-01 | Haute | Partiellement réfuté | Moyenne | Bon principe, mais incomplet : l'enfant hérite de la priorité « inférieure à la normale », il faut une seule fenêtre de résultat, et un essai réel reste à faire |
| AUT-02 | Haute | Concédé, et même plus large (cas symétrique « AJOUTÉ ») | Haute | Code de sortie : **risqué**, il masquerait des écarts prouvés ; partie moteur incomplète |
| AUT-03 | Haute | Partiellement réfuté (gravité) | Moyenne | Correct ; reprendre le motif de `Snapshot.Save` |
| AUT-04 | Moyenne | Partiellement réfuté (impact) | Basse | Correctif principal **risqué** (`SnapshotWindow`) ; retenir la variante « à défaut » |
| AUT-05 | Moyenne | Concédé, et plus grave que décrit (`if errorlevel 1` faux) | Moyenne | Correct |
| AUT-06 | Moyenne | Partiellement réfuté (portée) | Basse | Correct ; la ligne `:90` n'utilise pas de copie périmée |
| AUT-07 | Moyenne | Partiellement réfuté | Basse | Points 1 et 2 corrects ; point 3 **risqué** : garder le bouton toujours actif |
| AUT-08 | Moyenne | Partiellement réfuté | Basse | Contrôle de la destination : correct ; filtrage de la rotation par contenu : superflu |
| AUT-09 | Basse | Concédé | Basse | Correct |
| AUT-10 | Basse | Partiellement réfuté (`--help`, casse) | Basse | Liste blanche : correcte, mais garder les options communes ; refuser le passage de `--help` à 3 |

### AUT-01 — La fenêtre de résultat et « Ouvrir dans Checker San » vivent dans le processus de la tâche
- **Verdict :** Partiellement réfuté.
- **Vérification de la preuve rouge :**
  - Trace exacte. `App.xaml.cs:35` passe en `OnExplicitShutdown`. `:47` ne quitte que pour le code 0, et `:49` seulement quand la fenêtre de résultat se ferme sans `OpenedMain`. `ScheduledResultWindow.cs:65-70` crée `MainWindow` dans le même processus et passe en `OnMainWindowClose`. PASSATION.md:315 (« dernier résultat 1 après sa fermeture ») confirme que l'instance de la tâche dure tant que la fenêtre est ouverte.
  - Réglages de la tâche, lus sur l'objet COM **non enregistré** construit avec le XML exact de `ScheduledTasks.Xml` (`bleucli xml`, puis `NewTask(0).XmlText`) :
    ```
    MultipleInstances (…2=IgnoreNew…) : 2
    ExecutionTimeLimit : PT72H
    AllowHardTerminate : True
    Priority : 7
    Action : C:\Programme\CheckerSan.exe --scheduled 0a1b2c3d ; WorkingDirectory=''
    ```
  - Documentation Microsoft (TaskSettings.MultipleInstances) pour `IGNORE_NEW` : « Does not start a new instance if an existing instance of the task is running. » Le rouge a raison sur ce point.
  - Exécutables rouges relancés : `wpfexit close -> 1`, `wpfexit open -> 0`. Le code 0 après « Ouvrir dans Checker San » est donc confirmé.
- **Arguments et preuve :**
  1. **La suspension des vérifications est bornée, et nulle pour une planification hebdomadaire.**
     - D'après TaskSettings.AllowHardTerminate, à la limite de durée le service envoie d'abord `WM_CLOSE`, et n'utilise `TerminateProcess` que si la tâche ne répond pas.
     - `bleuexit.exe result-wmclose` utilise la vraie `ScheduledResultWindow` (`ShowActivated=False`), qui reçoit `WM_CLOSE`. Résultat :
       ```
       16:39:02.172 WM_CLOSE envoyé à la fenêtre de résultat
       16:39:02.183 fenêtre de résultat fermée
       16:39:02.186 OnExit : ApplicationExitCode=1        → code de sortie du processus = 1
       ```
       Une fenêtre oubliée ne bloque donc la paire que **72 h au plus** après le début de la vérification. Le processus quitte ensuite proprement, avec le bon code.
     - Avec la fréquence par défaut, « Chaque semaine » (`PairsWindow.cs:64`, `:154`), aucune exécution n'est jamais perdue : 72 h < 7 jours. Avec « Tous les jours », on perd au plus 2 exécutions (J+1 et J+2), éventuellement 3 si celle de J+3 tombe exactement à la limite des 72 h. Le scénario rouge (« aucune vérification n'a lieu tant que la fenêtre reste ouverte ») est donc surestimé.
     - Il suppose en outre un PC laissé allumé et en session pendant des jours (`InteractiveToken`). Pendant ce temps, une fenêtre d'écart reste affichée, ce qui constitue déjà une alerte.
  2. **L'arrêt à 72 h n'est pas une coupure brutale « au milieu d'une copie ».**
     - `MainWindow.Window_Closing` (`MainWindow.xaml.cs:446-450`) annule l'opération en cours et ferme la fenêtre après l'annulation (`:251` pour la comparaison, `MainWindow.Copy.cs:70` pour la copie). `DuplicatesWindow.xaml.cs:339-344` fait de même.
     - Essai `bleuexit.exe main-busy-wmclose`, avec la même logique de fermeture :
       ```
       16:39:06.574 WM_CLOSE envoyé à la fenêtre principale occupée
       16:39:06.575 Closing pendant une opération : annulation demandée, fermeture différée
       16:39:06.992 opération arrêtée proprement
       16:39:07.003 OnExit : ApplicationExitCode=0
       ```
     - `TerminateProcess` n'intervient que si le processus ne s'est pas terminé à la fin du délai de grâce, dont la durée n'a pas été mesurée. Même dans ce cas, le journal de copie survit à un plantage (PASSATION.md:191), et le manifeste de quarantaine est forcé sur disque avant chaque déplacement (PASSATION.md:168-169).
     - Il reste une gêne réelle mais rare : l'app se ferme d'elle-même 72 h après 03:00. Le cas d'un dialogue modal de doublons ouvert au moment de `WM_CLOSE` n'a pas été prouvé, ni dans un sens ni dans l'autre.
  3. **Le code 0 est réel, mais n'a qu'un effet limité.**
     - `bleuexit.exe main-idle-wmclose` donne 0, comme `wpfexit open`. La promesse de PASSATION.md:290 n'est donc pas tenue.
     - Mais le vrai résultat est déjà enregistré avant l'ouverture de la fenêtre (`PairCheck.RunAsync` → `RecordRun`, `SavedPairs.cs:187`). La liste des paires affiche donc « N écart(s) ».
     - Seule la colonne « Résultat de la dernière exécution » du Planificateur est fausse, et ce après que l'utilisateur a vu la fenêtre et cliqué sur « Ouvrir ».
  4. Ce qui reste réel : une conception où l'application interactive tourne dans l'instance de la tâche, avec trois conséquences modérées (exécutions quotidiennes ignorées pendant 72 h au plus, fermeture automatique à 72 h, code 0), et une promesse documentée non tenue.
- **Gravité proposée :** Moyenne.
- **Avis sur le correctif :** juste sur le principe (quitter avec `Shutdown(run.ExitCode)` et afficher le résultat hors de la tâche), mais **incomplet** sur deux points prouvés.
  - **Priorité héritée.**
    - Documentation TaskSettings.Priority : la priorité 7 donne `BELOW_NORMAL_PRIORITY_CLASS`.
    - Essai : un processus mis en `BelowNormal` lance deux enfants. Sortie : `processus parent : BelowNormal ; enfant Start-Process : BelowNormal ; enfant UseShellExecute=true : BelowNormal`.
    - Un `CheckerSan.exe` lancé depuis la tâche hériterait donc de la priorité basse. Il faut fixer `Process.PriorityClass = Normal` après le lancement, ou faire `SetPriorityClass(Normal)` au démarrage hors `--scheduled`.
  - **Une seule fenêtre de résultat.** Si le résultat s'affiche dans un processus séparé, les fenêtres s'empilent, une par vérification. Il faut une seule fenêtre par paire (mutex nommé d'après l'id), qui relit `LastRun`.
  - **Survie de l'enfant.** Le fait que l'enfant survive à la fin de l'instance (objet job) doit être vérifié lors d'un essai réel avec le Planificateur, sur accord de l'utilisateur comme le 23 octobre.
  - **Arguments.** `MainWindow` ne lit que `--source`, `--target` et `--compare` (`MainWindow.xaml.cs:85-91`) : l'option des déplacements de la paire serait perdue. Mieux vaut un `--pair <id> [--pairs f]`, qui appelle `LoadPair`.
  - **Correctif minimal immédiat pour le code** (si la refonte est différée) : dans `open.Click`, garder `OnExplicitShutdown` et ajouter `OpenedMain.Closed += (_, _) => Application.Current.Shutdown(run.ExitCode);`.

### AUT-02 — `verify` annonce des fichiers « ABSENT » quand un sous-dossier est seulement illisible
- **Verdict :** Concédé, et le problème est plus large que décrit.
- **Vérification de la preuve rouge :** la trace est exacte. `Snapshot.cs:123-126` n'ajoute qu'une erreur : les fichiers du dossier illisible ne sont ni dans `Files` ni dans `Unverified`. `:169` les déclare alors `Missing`. `CheckerSanCli.cs:142` fait passer les différences avant `Certain`, et `:146` n'affiche jamais `actual.Errors`. Repro rejouée (`bleu-cli.ps1`, section 02 a) :
  ```
  ABSENT      Photos\img1.jpg … img3.jpg
  ÉCARTS — 3 différence(s) prouvée(s).
    verify : exit=1
  ```
- **Arguments et preuve :**
  - Je n'ai trouvé aucune protection ailleurs. Le test existant `CliExitCodesReportResults` (`SnapshotAndCliTests.cs:71-76`) ne couvre que des instantanés complets.
  - Le scénario est réaliste sans ACL : `ListAsync` ne retente rien, donc une erreur réseau passagère lors de l'énumération d'un sous-dossier d'un NAS suffit.
  - **Cas symétrique, non signalé par le rouge** (section 02 b) : un dossier illisible **pendant** l'instantané, puis lisible ensuite.
    ```
    ! Dossier non entièrement lisible « Photos » : Access … is denied.
      snapshot : exit=2
    AJOUTÉ      Photos\img1.jpg … img3.jpg
    ÉCARTS — 3 différence(s) prouvée(s).
      verify (droits rétablis) : exit=1
    ```
    `Snapshot.cs:171-172` ne retire de « Ajouté » que `expected.Unverified`, pas les fichiers des dossiers illisibles à l'époque.
- **Gravité proposée :** Haute. C'est une fausse **preuve**, affichée à l'écran et transmise par le code de sortie.
- **Avis sur le correctif :**
  - **Code de sortie : risqué.** Le rouge propose `!Certain ? Incomplete : …` « même priorité que ForComparison ». Ce n'est **pas** la règle de `ForComparison` : celle-ci ne met « incomplet » avant les écarts que pour les **erreurs**, tandis que les lignes `Unverified` passent après les écarts (`ExitCodes.cs:18-22`). Or `Certain` devient faux dès qu'un fichier est seulement en ligne (`Snapshot.cs:93`, `:177`). Preuve (section 02 c), avec un fichier `Offline` et un contenu réellement changé :
    ```
    snapshot avec un fichier en ligne : exit=2
    MODIFIÉ     a.txt
      verify : exit=1      JSON : Certain=False ; Differences=a.txt:Changed
    compare (même situation, ForComparison) : exit=1
    ```
    Avec le correctif rouge, `verify` renverrait 2 pour toujours : un changement prouvé serait masqué, et `verify` divergerait de `compare`. Le test proposé par le rouge ne détecterait pas cette régression.
  - **Alternative précise, alignée sur `ForComparison` :**
    `var errors = expected.Errors.Count + actual.Errors.Count > 0;`
    `var code = errors ? Incomplete : verification.Differences.Count > 0 ? Differences : verification.Certain ? Clean : Incomplete;`
  - **Moteur : incomplet.**
    - Mémoriser les dossiers illisibles dans l'instantané : champ optionnel `UnreadableFolders`, que `Load` complète par `[]`, ce qui garde le schéma 1.
    - Dans `Compare` :
      - ne produire ni `Missing` ni `MissingFolder` pour les entrées attendues situées sous un dossier illisible **actuel** ;
      - ne produire ni `Added` ni `AddedFolder` pour les entrées actuelles situées sous un dossier illisible **de l'instantané**.
  - Afficher `actual.Errors` (et `expected.Errors`) dans `verify`. La correction moteur profite aussi à `SnapshotWindow.cs:101-114`.
  - **Tests :**
    - les deux sens (illisible avant et après), avec le code 2 et ni ABSENT ni AJOUTÉ ;
    - un fichier en ligne et un fichier modifié donnent toujours 1.

### AUT-03 — Le rapport `--json` s'écrit dans le dossier vérifié via une jonction ou `\\?\`
- **Verdict :** Partiellement réfuté : le problème est réel, mais sa gravité est surestimée.
- **Vérification de la preuve rouge :** rejouée (section 03).
  ```
  --json f1\direct.json : exit=3 ; sorties : OK — Les deux dossiers sont identiques. / Erreur : Enregistrez le rapport à l’extérieur …
  --json j\via-jonction.json : exit=0
  --json \\?\...\f1\via-prefixe.json : exit=1
  contenu de f1 : a.txt, via-jonction.json, via-prefixe.json
  ```
  `CheckerSanCli.cs:268-272` ne compare que du texte ; le refus n'arrive qu'après le scan (`:87`, `:148`, `:184`).
- **Arguments et preuve :**
  - **Rien n'est écrasé ni supprimé.** `FileMode.CreateNew` (`:277`) : relancer avec le même chemin donne `Erreur : The file '…\j\via-jonction.json' already exists.` et le code 3.
  - Le chemin est choisi par l'utilisateur. Il faut une erreur de configuration (jonction ou préfixe `\\?\` qui pointe dans l'arbre vérifié), et la conséquence est visible : au passage suivant, un fichier au nom du rapport apparaît comme écart.
  - La promesse « contrôle des alias » de PASSATION.md:203 vise les exports de l'app. Pour la ligne de commande, `CheckerSanCli.cs:264` annonce bien « outside the checked folders », et le contrôle reste incomplet : le problème est donc réel.
  - Le refus tardif est réel, mais le verdict a déjà été affiché à la console avant l'erreur (sortie ci-dessus).
- **Gravité proposée :** Moyenne.
- **Avis sur le correctif :** correct.
  - Plus simple : reprendre exactement le motif de `Snapshot.cs:184`. `WriteJson` exige de toute façon un dossier parent existant (`CreateNew`), donc `CanonicalDirectory(GetDirectoryName(path))` suffit et il n'est pas nécessaire de remonter les ancêtres.
  - Appliquer ce contrôle avant le scan, aux racines `GetFullPath` des arguments, puis à l'écriture, aux racines du résultat.
  - `CanonicalDirectory` retire `\\?\` (`FileSafety.cs:80-81`), ce qui couvre aussi une racine **donnée** avec `\\?\`.
  - Pour l'accès, un `InternalsVisibleTo("checkersan-cli")` évite d'élargir l'API publique de Core.

### AUT-04 — Ctrl+C pendant `snapshot` ou `verify` : exception non gérée, code 0xE0434352
- **Verdict :** Partiellement réfuté : le bug est réel, mais son impact est surestimé.
- **Vérification de la preuve rouge :**
  - `RunCoreAsync` n'attrape pas `OperationCanceledException` (`CheckerSanCli.cs:65`), et `SnapshotEngine` la lève (`Snapshot.cs:48`, `:66`, `:107`, `:144`).
  - Rejoué (section 04 et `bleucli cancel`) :
    ```
    snapshot annulé -> TaskCanceledException ; instantané écrit : False ; journal : False
    snapshot annulé après 60 ms -> exit code -532462766 (0xE0434352) ; instantané écrit : False ; stderr : Unhandled exception. System.Threading.Tasks.TaskCanceledException
    cmd : if errorlevel 1 FAUX
    ```
- **Arguments et preuve :**
  - L'arrêt est demandé par l'utilisateur lui-même, devant sa console : il sait que rien n'a été vérifié.
  - Rien n'est écrit : pas d'instantané partiel, car `Save` vient après la création (`CheckerSanCli.cs:125-126`). Aucun verdict n'est faussé.
  - Une tâche planifiée ne reçoit jamais de Ctrl+C.
  - La garantie de PASSATION.md:335 et le code 2 ne sont pas tenus pour 2 commandes sur 4, et le code négatif rend `if errorlevel 1` faux. Le défaut est réel, mais de faible portée.
- **Gravité proposée :** Basse.
- **Avis sur le correctif :**
  - **Correctif principal risqué.** Si `Create` renvoyait un instantané `Complete=false` au lieu de lever l'exception, `SnapshotWindow.cs:84-85` **enregistrerait** l'instantané partiel après « Arrêter ». Cela romprait le message garanti par `SnapshotWindow.cs:128` (« Arrêté — rien n'a été enregistré ni modifié »). Le refus de `Save` proposé en complément transformerait ce message en « Opération impossible ».
  - **Variante « à défaut » : à retenir.** `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)` dans `RunCoreAsync`, avec un message « INCOMPLET — arrêté », une ligne de journal et `return Incomplete`.
  - **En plus, filet général.** Un `catch (Exception)` final dans `RunAsync`, qui écrit l'erreur et renvoie `Failure`, garantit les codes 0 à 3 pour toute exception imprévue. Il couvre aussi AUT-05.

### AUT-05 — `--log ""` ou `--log C:\` fait planter la commande après la comparaison
- **Verdict :** Concédé, et plus grave que décrit.
- **Vérification de la preuve rouge :** rejouée (section 05).
  ```
  --log "" -> exit code -532462766 (0xE0434352) ; stderr : Erreur : The path is empty. (Parameter 'path')
  --log C:\ -> exit code -532462766 (0xE0434352) ; stderr : Erreur : Value cannot be null. (Parameter 'path')
  --log "<dossier existant>" -> exit code 1        (UnauthorizedAccessException bien absorbée)
  ```
  La trace est exacte :
  - `ActivityLog.cs:25` lève `ArgumentException` ou `ArgumentNullException`, que le filtre de `:29` n'attrape pas ;
  - le `catch` de `CheckerSanCli.cs:65` l'attrape ;
  - `:68` relance `ActivityLog.Write` avec le même chemin, et l'exception s'échappe.
- **Arguments et preuve :**
  - **Dans un script cmd, l'erreur passe sans bruit et inverse le verdict.** `BLEU\work\log-vide.cmd` (`set "LOG="` puis `--log "%LOG%"`) donne :
    ```
    errorlevel=-532462766
    if errorlevel 1 : FAUX alors que des ecarts sont prouves
    même comparaison avec un --log valide : errorlevel=1
    ```
    Un lot planifié qui teste `if errorlevel 1` conclut donc « rien à signaler » alors que des écarts sont prouvés.
  - **Seule atténuation : Windows PowerShell 5.1 ne transmet pas une chaîne vide.** `--log $vide` donne `exit=3 ; Missing value for --log.` avant tout scan. Ce n'est pas le cas de cmd, ni d'une action de tâche avec `--log ""`.
- **Gravité proposée :** Moyenne.
- **Avis sur le correctif :** correct et complet.
  - Le `try` doit englober `GetFullPath` et `GetDirectoryName` ; traiter le `null` au lieu de l'opérateur `!`.
  - Refuser un `--log` vide à l'analyse, avec le code 3.
  - Le filet général d'AUT-04 protège en plus contre toute autre exception de journal.

### AUT-06 — « Planifier » / « Ne plus planifier » efface le dernier résultat d'une vérification planifiée
- **Verdict :** Partiellement réfuté : le mécanisme est réel, mais sa portée est surestimée.
- **Vérification de la preuve rouge :** rejouée. `wpfpairs.exe` donne `Avant le clic : 23/10/2026 16:42 — 42 écart(s)` puis `Après le clic : Jamais vérifiée`, et `autcli stale` donne la même chose. `PairsWindow.cs:115-118` et `:126` enregistrent `Selected` en entier (`SavedPairs.cs:86`).
- **Arguments et preuve :**
  - **La fenêtre doit rester ouverte pendant la fin d'une vérification.** « Paires… » est modale (`MainWindow.xaml.cs:113-116`, `ShowDialog`) et relit la liste après chaque action (`Refresh`, `:91`, `:119`, `:127`) et à chaque ouverture (`:133`). Le cas « ou l'a été avant la nuit » du rouge ne pose donc aucun problème si elle a été fermée puis rouverte : seule une fenêtre restée ouverte pendant la fin d'une vérification est concernée.
  - **Seul `LastRun` est perdu, pas le résultat lui-même.**
    - Le rapport reste dans `Reports\<id>` (`Save` n'y touche pas).
    - La ligne d'activité reste écrite (`SavedPairs.cs:189`).
    - La fenêtre de résultat a été montrée par le processus planifié.
  - **`:90` (Enregistrer) ne travaille pas sur une copie périmée.** `existing` est relu au moment du clic (`:88`, `PairStore.Load`). Il reste au plus une fenêtre de course de quelques millisecondes.
- **Gravité proposée :** Basse.
- **Avis sur le correctif :** correct.
  - Plus générique : `PairStore.Modify(id, Func<SavedPair, SavedPair>)` sous le verrou, que `RecordRun`, la planification et la déplanification réutiliseraient.

### AUT-07 — Suppression depuis un état périmé : la tâche Windows reste orpheline
- **Verdict :** Partiellement réfuté.
- **Vérification de la preuve rouge :** (a) rejoué. `wpfpairs.exe` donne `Appels schtasks pendant la suppression : aucun … tâche Windows encore présente : True`. `PairsWindow.cs:104` ne retire la tâche que si la copie en mémoire est planifiée.
- **Arguments et preuve :**
  - **(a) exige deux instances de Checker San en même temps**, dont l'une garde la fenêtre modale « Paires » ouverte pendant que l'autre planifie la même paire.
  - **(b) se répare depuis l'app**, ce qui contredit « impossible à retirer depuis l'app ». Essai `bleupairs.exe`, avec la vraie `PairsWindow` et `pairs.json` verrouillé (`FileShare.None`) au premier clic :
    ```
    [1] Appels : /Create ; tâche présente : True ; paire planifiée : False
    [1] Statut : Opération impossible : The process cannot access the file '…\pairs.json' because it is being used by another process.
    [1] « Ne plus planifier » actif : False ; « Planifier » actif : True
    [2] Appels : /Create ; tâche présente : True ; paire planifiée : True ; « Ne plus planifier » actif : True
    [3] Appels : /Query,/Delete ; tâche présente : False ; paire planifiée : False
    ```
    L'échec est affiché. Un nouveau clic sur « Planifier » remplace la tâche (`/F`, `ScheduledTasks.cs:65`) et enregistre la paire, puis « Ne plus planifier » retire la tâche.
  - **Une orpheline ne passe pas inaperçue.** Elle affiche « la paire … n'existe plus. Supprimez sa tâche dans le Planificateur de tâches. » (`App.xaml.cs:44`), et ce cas est documenté (PASSATION.md:321). Ce n'est donc pas une exécution silencieuse et indéfinie.
- **Gravité proposée :** Basse.
- **Avis sur le correctif :**
  - (1) Toujours appeler `Unregister` à la suppression : correct et peu coûteux, puisqu'il ne lance qu'un `/Query`.
  - (2) Retour arrière par `Unregister` si `Save` échoue après `Register` : correct.
  - (3) Activer le bouton selon `ScheduledTasks.Exists` : **risqué**.
    - `ShowSelection` (`:149-158`) lancerait `schtasks.exe` sur le thread d'interface à chaque changement de sélection et à chaque `Refresh`.
    - Cela casserait les assertions du test existant : `PairsUiTests.cs:41` (`calls.Count == 0`) et `:46` (`"/Create"` exactement).
    - Alternative : laisser « Ne plus planifier » actif dès qu'une paire est sélectionnée. `Unregister` interroge déjà la tâche avant de la supprimer (`ScheduledTasks.cs:73-77`), et l'assertion `:36` serait à adapter.

### AUT-08 — Le contrôle « rapport hors des dossiers comparés » ne regarde que le dossier de la liste
- **Verdict :** Partiellement réfuté. L'écriture dans le dossier comparé est réelle ; la suppression de fichiers de l'utilisateur est fabriquée par la reproduction.
- **Vérification de la preuve rouge :** rejouée (`autcli reports-inside` : rapport écrit dans le dossier 1 : True ; 6 fichiers sur 25 supprimés). `SavedPairs.cs:199-201` ne teste que le parent de la liste.
- **Arguments et preuve :**
  - La rotation ne liste que `Reports\<id>`, sans récursion (`SavedPairs.cs:202`, `:219`). L'id est aléatoire, 8 chiffres hexadécimaux (`:55`).
  - Le rouge a dû **placer** ses 25 fichiers exactement dans `Reports\0a1b2c3d\`, avec le nom d'un rapport. Dans le cas réaliste (comparer le dossier `Reports` avec une sauvegarde), ce dossier ne contient que les rapports de Checker San pour cette paire.
  - Preuve `bleucli reports` : 51 fichiers de l'utilisateur dans le dossier 1 comparé `store\Reports`, dont 50 portent un nom de rapport (25 à la racine, 25 dans `Docs\`), puis 22 vérifications :
    ```
    Fichiers de l'utilisateur avant : 51 ; après 22 vérifications : 51 (supprimés : 0)
    Rapports de la paire dans Reports\b39f5894 : 20 (rotation à 20)
    Dernier code : 1, écarts : 73, rapport dans le dossier 1 : True
    Dossier 1 = dossier de la liste : code 1, rapport aucun, motif : rapport non enregistré : le dossier des rapports se trouve dans un dossier comparé.
    ```
  - Ce qui reste réel : la promesse de PASSATION.md:278 n'est pas tenue. Le dossier comparé reçoit des rapports et en perd par la rotation, d'où des écarts perpétuels, visibles. Cela n'arrive que si l'on compare `Reports` lui-même.
- **Gravité proposée :** Basse.
- **Avis sur le correctif :**
  - Contrôler la destination réelle `Reports\<id>` de façon canonique : correct, et cela suffit.
  - Le filtrage de la rotation par **contenu** (`"Application": "Checker San"` + `Pair`) est superflu une fois la destination garantie hors des racines ; il ajoute une lecture et du JSON à analyser par fichier. L'expression régulière stricte du nom reste un garde-fou peu coûteux.
  - Ne pas élargir le contrôle à « une racine dans le dossier de la liste », ce qui refuserait par exemple `D:\Data\Photos` pour `--pairs D:\Data\pairs.json`. Le rouge propose bien « racine dans la destination », qui est correct.

### AUT-09 — Le nom des rapports suit le calendrier de Windows ; la rotation supprime le rapport qu'elle vient d'écrire
- **Verdict :** Concédé.
- **Vérification de la preuve rouge :** rejouée (`autcli culture`) : `rapport : 2569-10-23 164143.json`, puis `ReportPath = 2026-10-23 164143.json, existe : False`. La ligne de commande ne règle jamais `CurrentCulture` (`L.SetLanguage`, `L.cs:14-18`), contrairement à l'app (`AppLanguage.cs:43-45`).
- **Arguments et preuve :**
  - Les conditions sont rares :
    - un format régional à calendrier non grégorien, par exemple th-TH ;
    - **et** la même paire exécutée à la fois par `checkersan-cli run` et par la tâche de l'app.
  - Le cas de l'horloge qui recule n'agit que si au moins 20 rapports ont un nom postérieur. Une correction NTP de quelques secondes ne suffit pas ; il faut un retour de plusieurs jours, par exemple une pile de carte mère.
  - La gravité Basse est donc juste.
- **Gravité proposée :** Basse.
- **Avis sur le correctif :** correct : `DateTime.Now.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture)`, et `path` exclu de la rotation. Le tri sur la date analysée est facultatif une fois les noms invariants.

### AUT-10 — Analyse des options : options inconnues ignorées, drapeau pris pour une valeur, `--help` → 0
- **Verdict :** Partiellement réfuté.
- **Vérification de la preuve rouge :** rejouée (section 10) : `--jsno : exit=1 ; rapport écrit : False`, `--json --no-moved : exit=1 ; fichier créé : --no-moved`, `compare … --help : exit=0`.
- **Arguments et preuve :**
  - **`--help` → 0 est voulu et conventionnel.** `CheckerSanCli.cs:40` renvoie explicitement `Clean` quand une commande accompagne l'aide, et 3 sans commande. Renvoyer 0 pour l'aide est la norme des outils en ligne de commande. Une tâche planifiée avec `--help` n'est pas un scénario crédible.
  - **La casse des drapeaux échoue toujours bruyamment.** Une variante en majuscules n'est pas reconnue comme drapeau : elle consomme l'argument suivant ou manque de valeur, et la commande se termine toujours par le code 3 :
    ```
    --No-Moved au milieu : exit=3        --NO-MOVED en dernier : exit=3
    --No-Moved --json r2.json : exit=3 ; r2.json : False        --Include-Hidden : exit=3
    ```
  - Ce qui reste réel : une option inconnue ou mal orthographiée est ignorée, et un drapeau peut devenir une valeur.
- **Gravité proposée :** Basse.
- **Avis sur le correctif :**
  - La liste blanche est correcte, mais il faut accepter partout les options communes (`--log`, `--lang`, `--readers`, `--pairs`), qui sont documentées comme « Options communes » (`CheckerSanCli.cs:302`). Refuser « une option propre à une autre commande » risquerait de casser des scripts sans bénéfice.
  - Refuser une valeur qui commence par `--` : correct.
  - Passer `--help` à 3 : à **rejeter**.

### Pistes non prouvées du rouge
1. **Chemins relatifs dans une paire : plausible, mais l'échec est bruyant.**
   - `bleucli relative` donne :
     ```
     Enregistrée telle quelle : Folder1='Photos'
     Dossier courant …\relative-164332203 -> code 0
     Dossier courant C:\WINDOWS\system32 -> code 3 ; Impossible d’ouvrir le dossier 1 : Could not find file 'C:\WINDOWS\system32\Photos'.
     ```
   - La tâche n'a pas de `WorkingDirectory` (inspection COM ci-dessus : `''`).
   - Le dossier de travail réel d'une tâche n'a pas été mesuré. Si c'est bien System32, on obtient une fenêtre « Vérification planifiée impossible », et non une comparaison silencieuse d'autres dossiers, sauf homonyme sous System32.
   - Il faut aussi que l'utilisateur ait tapé un chemin relatif à la main.
   - Gravité Basse. Correctif : `Path.GetFullPath` dans `PairStore.Save`, ou refus des chemins non absolus.
2. **Priorité de la tâche : établie pour le processeur, non prouvée pour les E/S.**
   - D'après la documentation TaskSettings.Priority, la priorité 7 correspond à `BELOW_NORMAL_PRIORITY_CLASS`. La fenêtre principale ouverte depuis le résultat est dans le même processus et tourne donc à cette priorité.
   - L'essai d'héritage (AUT-01) montre que tout enfant lancé par la tâche en hérite aussi.
   - Pour la priorité d'E/S, rien n'a été mesuré ni trouvé dans cette documentation.
   - Gravité Basse : c'est un effet de performance, et il est absorbé par le correctif d'AUT-01 si la priorité est rétablie.
3. **Verrou de `PairStore` lié à l'écriture du chemin : prouvé avec une jonction, mais négligeable en pratique.**
   - `bleucli lock` : la même liste est écrite par deux chemins, `real\pairs.json` et `alias\pairs.json` (jonction), soit 80 enregistrements en rafale. Deux essais :
     ```
     Paires attendues : 43 (échecs : 38) ; présentes : 41 ; perdues : 2
     Paires attendues : 45 (échecs : 36) ; présentes : 41 ; perdues : 4
     JSON lisible après coup : True
     ```
   - La plupart des collisions échouent avec une erreur, quelques mises à jour se perdent sans bruit, et le fichier reste valide.
   - Il faut qu'une **autre** écriture du chemin soit passée à `--pairs` (jonction, `subst`, `\\?\`, partage administratif) **et** que deux écritures tombent à la même milliseconde. L'app et la tâche utilisent toujours la même écriture (`ScheduledTasks.Arguments` : `GetFullPath` du chemin de l'app).
   - Gravité Basse. Correctif : nommer le mutex d'après `CanonicalDirectory(parent)` et le nom du fichier, quand le parent existe.


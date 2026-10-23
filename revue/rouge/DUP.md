# Équipe rouge — Doublons et retrait

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

## DUP-01 — Une quarantaine sous la racine analysée devient la « copie conservée » ; la règle par défaut retire l'exemplaire vivant

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

## DUP-02 — Mode Corbeille : le fichier vérifié reste renommable et le Shell agit sur le chemin

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

## DUP-03 — « Restaurer » suit les jonctions de la quarantaine (manifeste falsifié)

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

## DUP-04 — Journal écrit après l'action : une suppression définitive peut n'être ni journalisée ni rapportée

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

## DUP-05 — Cache de reprise : un faux groupe de doublons si les quatre dates sont restaurées

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

## DUP-06 — Quarantaine non restaurable si le dossier est rouvert sous un autre nom

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

## DUP-07 — Mise à l'écart impossible pour un nom d'un seul caractère

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

## DUP-08 — Un doublon en lecture seule bloque la suppression définitive et arrête tous les groupes suivants

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

## Pistes non prouvées

1. **`RecycleBinAvailable` se fonde sur la lettre du chemin textuel** (`DuplicateRemoval.cs:84-91`, `Path.GetPathRoot` puis `GetDriveTypeW`), pas sur le volume qui contient vraiment les fichiers. Trois cas peuvent rendre la Corbeille disponible alors que le volume physique n'en a pas :
   - une racine située sous un dossier de montage (clé USB montée dans `C:\mnt`) ;
   - un lecteur `subst` ;
   - une jonction ancêtre vers un volume amovible.

   Windows supprimerait alors définitivement, après sa propre alerte (`FOF_WANTNUKEWARNING`), et le journal dirait « trop volumineux pour la Corbeille » (222-227). Non testé : `subst`, les points de montage et l'exécution réelle du mode Corbeille sont interdits ici.
2. **Volumes FAT32/exFAT (cartes SD, clés USB).** Si `GetFileInformationByHandleEx(FileIdInfo)` n'y est pas pris en charge, `Identity` vaut null, `SameIdentity` vaut false (`DuplicateRemoval.cs:341,374-383`), et tout retrait est refusé sur ces supports. L'échec est sans risque, mais la fonction devient inutilisable. Non testé : aucun volume FAT n'était disponible.
3. **Racine atteinte par une jonction vers un autre volume fixe, en mode Corbeille.** Le Shell choisit peut-être la Corbeille d'après le chemin (`C:\$Recycle.Bin`) et procède par copie puis suppression. `RecycledOutcome` verrait alors le handle en *delete pending* et inscrirait « Supprimé définitivement (trop volumineux) » alors que le fichier est dans la Corbeille. Non testé, car le mode Corbeille réel est interdit.

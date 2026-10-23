# Équipe rouge — Copie et écritures

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

## COP-01 — La racine de destination n’est jamais revérifiée : une jonction posée à sa place fait écrire et renommer la copie vérifiée hors du dossier 2, et la passe finale modifie des dossiers étrangers
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

## COP-02 — Les flux de données alternatifs, dont la marque du Web (`Zone.Identifier`), sont perdus sans avertissement ; la comparaison suivante conclut « identiques »
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

## COP-03 — Après une copie 2 → 1, les champs Dossier 1 et Dossier 2 de la fenêtre principale sont inversés
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

## COP-04 — Contrôle d’espace calculé sur des clusters de 4 Kio, sans compter les dossiers : sur exFAT/FAT, il laisse démarrer une copie qui remplit le disque
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

## COP-05 — Un dossier parent de destination d’environ 207 à 257 caractères fait échouer chaque fichier : le nom temporaire de 52 caractères dépasse MAX_PATH dans un `CreateFileW` sans préfixe `\\?\`
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

## COP-06 — Le journal enregistre un nom NTFS non conforme à UTF-16 avec U+FFFD : la reprise de cet élément devient impossible
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

## COP-07 — Un `.checkersan-….partial` abandonné est recopié comme un fichier ordinaire par « Compléter les deux dossiers »
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

## COP-08 — Un dossier créé dans la destination après la comparaison reçoit les attributs (caché, lecture seule) et les dates du dossier source
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

## COP-09 — Si la copie a été renommée sans que l’attribut ait été appliqué (plantage, erreur, nouvelle tentative réseau), la reprise la déclare terminée sans attributs
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

## COP-10 — Si le compactage du journal et sa réouverture échouent tous les deux, `Journal.Record` lève une NullReferenceException : la copie s’arrête au lieu de reporter le compactage
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

## Pistes non prouvées
1. **Fichier devenu « en ligne uniquement » entre le plan et la copie.**
   - Constat : `Transfer()` ne consulte jamais `FileSafety.Access` sur la source ouverte (`CopyEngine.cs:342-343`) ; seule l’identité source (`ChangeTime`) protège. En simulation avec l’attribut Offline (scénario `online`), la copie est bloquée tant que `SourceIdentity` est présent (`bytes … = 0`, « La source a changé »). Sans identité, cas d’un volume ou d’un partage qui ne répond pas à `FileIdInfo`, le fichier est lu et copié (`bytes … = 2097152`, `Complete=True`).
   - Reste à vérifier : qu’un vrai déshydratage OneDrive, ou une hiérarchisation HSM sur un partage, modifie bien `ChangeTime`, et quels volumes renvoient une identité nulle.
2. **Reprise après échange de disque.**
   - Constat : `ValidateRoots` (`CopyEngine.cs:553-560`) ne compare que des chemins canoniques avec lettre de lecteur (`GetFinalPathNameByHandleW`, `FileSafety.cs:73-83`). Aucun numéro de série de volume ni identifiant de la racine n’est enregistré dans `CopyPlan`.
   - Hypothèse : en rotation de disques de sauvegarde (même lettre, même dossier), « Reprendre » écrirait dans un disque qui n’a jamais été comparé. Non reproduit faute de second volume.
3. **Périphérique qui refuse les E/S non mises en cache (erreur 87).**
   - Constat : le repli vers une lecture mise en cache n’existe que pour la relecture (`HashFromDisk`, 217-226). `OpenPartial` (197) et `RandomAccess.Write` (410) restent toujours en `FILE_FLAG_NO_BUFFERING`.
   - Hypothèse : sur un tel système de fichiers (pilotes tiers, WebDAV), chaque fichier échouerait au lieu de se replier. Non testé faute de périphérique de ce type.

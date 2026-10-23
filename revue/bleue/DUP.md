# Équipe bleue — Doublons et retrait

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

## DUP-01 — Une quarantaine sous la racine analysée devient la « copie conservée » ; la règle par défaut retire l'exemplaire vivant

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

## DUP-02 — Mode Corbeille : le fichier vérifié reste renommable et le Shell agit sur le chemin

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

## DUP-03 — « Restaurer » suit les jonctions de la quarantaine (manifeste falsifié)

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

## DUP-04 — Journal écrit après l'action : une suppression définitive peut n'être ni journalisée ni rapportée

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

## DUP-05 — Cache de reprise : un faux groupe de doublons si les quatre dates sont restaurées

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

## DUP-06 — Quarantaine non restaurable si le dossier est rouvert sous un autre nom

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

## DUP-07 — Mise à l'écart impossible pour un nom d'un seul caractère

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

## DUP-08 — Un doublon en lecture seule bloque la suppression définitive et arrête tous les groupes suivants

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

## Pistes non prouvées du rouge

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

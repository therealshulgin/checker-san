# Équipe rouge — Automatisation, ligne de commande et journaux

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

## AUT-01 — La fenêtre de résultat et « Ouvrir dans Checker San » vivent dans le processus de la tâche
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

## AUT-02 — `verify` annonce des fichiers « ABSENT » quand un sous-dossier est seulement illisible
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

## AUT-03 — Le rapport `--json` s'écrit dans le dossier vérifié via une jonction ou `\\?\`
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

## AUT-04 — Ctrl+C pendant `snapshot` ou `verify` : exception non gérée, code 0xE0434352
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

## AUT-05 — `--log ""` ou `--log C:\` fait planter la commande après la comparaison
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

## AUT-06 — « Planifier » / « Ne plus planifier » efface le dernier résultat d'une vérification planifiée
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

## AUT-07 — Suppression depuis un état périmé : la tâche Windows reste orpheline
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

## AUT-08 — Le contrôle « rapport hors des dossiers comparés » ne regarde que le dossier de la liste
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

## AUT-09 — Le nom des rapports suit le calendrier de Windows ; la rotation supprime le rapport qu'elle vient d'écrire
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

## AUT-10 — Analyse des options : options inconnues ignorées, drapeau pris pour une valeur, `--help` → 0
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

## Pistes non prouvées
1. **Chemins relatifs dans une paire.** `PairStore.Save` (`SavedPairs.cs:77`) et `PairsWindow` (`:90`) enregistrent le texte saisi tel quel. `NormalizeRoot` le résout ensuite avec le dossier courant du processus (`ComparisonEngine.cs:1094`). La tâche n'a pas de `WorkingDirectory` (`ScheduledTasks.cs:50`), et le Planificateur utilise alors son dossier par défaut (System32), si bien que la tâche comparerait d'autres dossiers que l'app ou le CLI. Non exécuté avec le vrai Planificateur.
2. **Priorité de la tâche.** `Priority 7` (`ScheduledTasks.cs:49`) abaisse la priorité processeur, et probablement d'E/S, de la tâche. La fenêtre principale ouverte depuis un résultat planifié (AUT-01) en hériterait pour ses copies. Non mesuré.
3. **Verrou de `PairStore` lié à l'écriture du chemin** (`SavedPairs.cs:140`). Les alias 8.3 sont normalisés par `GetFullPath`, et l'essai `autcli.exe alias` n'a perdu aucune mise à jour sur 81. En revanche, une liste désignée via une jonction ou un lecteur `subst` (`--pairs`) obtiendrait un autre mutex, donc des mises à jour perdues entre l'app et le CLI. Non testé, pour ne pas créer de lecteur `subst`.

# Équipe bleue — Automatisation, ligne de commande et journaux

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

## AUT-01 — La fenêtre de résultat et « Ouvrir dans Checker San » vivent dans le processus de la tâche
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

## AUT-02 — `verify` annonce des fichiers « ABSENT » quand un sous-dossier est seulement illisible
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

## AUT-03 — Le rapport `--json` s'écrit dans le dossier vérifié via une jonction ou `\\?\`
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

## AUT-04 — Ctrl+C pendant `snapshot` ou `verify` : exception non gérée, code 0xE0434352
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

## AUT-05 — `--log ""` ou `--log C:\` fait planter la commande après la comparaison
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

## AUT-06 — « Planifier » / « Ne plus planifier » efface le dernier résultat d'une vérification planifiée
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

## AUT-07 — Suppression depuis un état périmé : la tâche Windows reste orpheline
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

## AUT-08 — Le contrôle « rapport hors des dossiers comparés » ne regarde que le dossier de la liste
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

## AUT-09 — Le nom des rapports suit le calendrier de Windows ; la rotation supprime le rapport qu'elle vient d'écrire
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

## AUT-10 — Analyse des options : options inconnues ignorées, drapeau pris pour une valeur, `--help` → 0
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

## Pistes non prouvées du rouge
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

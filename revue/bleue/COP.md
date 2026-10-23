# Équipe bleue — Copie et écritures

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

## COP-01 — La racine de destination n'est jamais revérifiée : une jonction posée à sa place fait écrire et renommer la copie vérifiée hors du dossier 2, et la passe finale modifie des dossiers étrangers
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

## COP-02 — Les flux de données alternatifs, dont la marque du Web (`Zone.Identifier`), sont perdus sans avertissement ; la comparaison suivante conclut « identiques »
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

## COP-03 — Après une copie 2 → 1, les champs Dossier 1 et Dossier 2 de la fenêtre principale sont inversés
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

## COP-04 — Contrôle d'espace calculé sur des clusters de 4 Kio, sans compter les dossiers : sur exFAT/FAT, il laisse démarrer une copie qui remplit le disque
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

## COP-05 — Un dossier parent de destination d'environ 207 à 257 caractères fait échouer chaque fichier : le nom temporaire de 52 caractères dépasse MAX_PATH dans un `CreateFileW` sans préfixe `\\?\`
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

## COP-06 — Le journal enregistre un nom NTFS non conforme à UTF-16 avec U+FFFD : la reprise de cet élément devient impossible
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

## COP-07 — Un `.checkersan-….partial` abandonné est recopié comme un fichier ordinaire par « Compléter les deux dossiers »
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

## COP-08 — Un dossier créé dans la destination après la comparaison reçoit les attributs (caché, lecture seule) et les dates du dossier source
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

## COP-09 — Si la copie a été renommée sans que l'attribut ait été appliqué (plantage, erreur, nouvelle tentative réseau), la reprise la déclare terminée sans attributs
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

## COP-10 — Si le compactage du journal et sa réouverture échouent tous les deux, `Journal.Record` lève une NullReferenceException : la copie s'arrête au lieu de reporter le compactage
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

## Pistes non prouvées du rouge
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

## Point transversal DUP-07
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

# Revue adverse — équipe rouge — moteur de « Regrouper dans le dossier 1 ou 2 »

Cible : changement non commité au-dessus de `b5399b7` (`CopyEngine.CanCreatePlan` relâché, `BlockedPaths`/`IsBlocked`, `ComparisonEngine.FindTargetCopiesAsync`, `CopyEngine.CreateGatherPlan` / `GatherFileCount`, `FolderAlignment.Suggest`, `--simulate-copy into1|into2`).

Conventions : `<dépôt>` est la racine du dépôt, `<scratch>` le dossier de travail de cette revue, `<work>` le dossier de données d’un scénario (sous `<scratch>\work`). Toutes les sorties citées sont exactes, chemins absolus masqués.

## Banc de reproduction

- `<scratch>\bench\Program.cs` (`Bench.csproj`) référence la DLL compilée `<dépôt>\FolderChecker.Core\bin\Release\net10.0\FolderChecker.Core.dll` (28 octobre 22:37, plus récente que les sources modifiées). Un scénario par argument.
- Compilation, avec les variables du SDK local (`DOTNET_CLI_HOME`, `NUGET_PACKAGES`, `APPDATA`… comme dans `PASSATION.md`) :
  `<dépôt>\.tools\dotnet\dotnet.exe build <scratch>\bench\Bench.csproj -c Release --configfile <dépôt>\NuGet.Config`
- Lancement : `powershell -File <scratch>\run.ps1 -Scenario <nom>`. La sortie est copiée dans `<scratch>\out-<nom>.txt`.
- CLI de `dist` : `powershell -File <scratch>\cli.ps1 …`, qui passe toujours `--log <scratch>\activity.log`.
- Mesure de lectures : `<scratch>\perf\Program.cs`, compilé deux fois. Une fois contre la nouvelle DLL (`<scratch>\perf\out-new`), une fois contre une copie de l’ancien moteur tirée par `git archive b5399b7 FolderChecker.Core` et compilée dans `<scratch>\old` (`<scratch>\perfold\out`). Aucun projet du dépôt n’a été modifié ni recompilé.

## Synthèse

| Id | Titre | Gravité |
|---|---|---|
| REG-M01 | La Corbeille d’une racine de disque compte comme « exemplaire présent » ; elle est aussi regroupée comme du contenu | critique |
| REG-M02 | Un temporaire `.checkersan-….partial` d’une copie interrompue compte comme exemplaire : le fichier n’est plus jamais regroupé | haute |
| REG-M03 | Fichiers vides : tout fichier vide du 2 « contient » tous ceux du 1, dont les noms et les dossiers sont perdus | moyenne |
| REG-M04 | Exclusions muettes : le plan ne compte ni le contenu réel face à un lien ni les noms écartés ; le dialogue affirme « déjà un exemplaire de chaque fichier » ; `GatherFileCount` ≠ plan | moyenne |
| REG-M05 | Recherche inverse : lectures non bornées, ×6 mesuré (+500 %), au lieu des +29 % documentés | moyenne |
| REG-M06 | Les exemplaires « trouvés ailleurs » ne sont pas revérifiés quand le plan est préparé | basse |
| REG-M07 | Un fichier verrouillé que la comparaison n’a pas lu fait échouer tout le plan, malgré « les incidents ne bloquent plus » | basse |
| REG-M08 | Une copie du 1 modifiée rend « Non vérifié » le fichier du 2 qui la désignait : il sort aussi de « Compléter le dossier 1 » | basse |
| REG-M09 | CLI `into1/into2` : ni les éléments écartés ni l’absence de recherche des déplacés ne sont signalés | basse |

---

## REG-M01 — La Corbeille d’une racine de disque compte comme « exemplaire présent » ; elle est aussi regroupée comme du contenu

**Gravité : critique.** C’est la configuration réelle de l’utilisateur : un partage face à `G:\`, une racine de disque. Un fichier du 1 dont le seul exemplaire sur `G:` est dans la Corbeille (`$RECYCLE.BIN`) est déclaré présent et n’est pas copié. Le dialogue affirme alors « ce dossier a déjà un exemplaire de chaque fichier ». Si l’utilisateur vide la Corbeille, si Windows la purge (quota dépassé, Assistant stockage) ou s’il supprime ensuite le dossier 1, le fichier est perdu. `FolderAlignment` propose en plus de comparer avec un dossier **de la Corbeille**, « et les copies arriveront à côté d’eux ».

**Pourquoi c’est nouveau :**
- Avant ce changement, comparer une racine NTFS ne permettait aucune copie. `System Volume Information` est illisible, ce qui produit une erreur, et l’ancien `CanCreatePlan` exigeait `Errors.Count == 0` (voir le diff, et `PASSATION.md` : « Compléter… était grisé dès le premier incident »).
- Le regroupement est le premier mode qui **renonce** à copier sur la foi d’un exemplaire situé ailleurs.

**Fichier:ligne (preuve par lecture) :**
- `ComparisonEngine.cs:528-532` : l’inventaire inclut les éléments cachés et système (`AttributesToSkip = 0`). Aucune exclusion de `$RECYCLE.BIN`, `System Volume Information` ou `.CheckerSan-Quarantine` dans le moteur (recherche : aucune occurrence).
- `ComparisonEngine.cs:748-752` : les candidats de `FindMovedFilesAsync` sont tous les fichiers lisibles du 2, Corbeille comprise.
- `ComparisonEngine.cs:834-840` : de même pour `byHash` dans `FindTargetCopiesAsync`.
- `CopyEngine.cs:136-138` : vers le 2, seules les lignes `OnlySource` manquent ; une ligne `Moved` vers la Corbeille est donc écartée.
- `FolderAlignment.cs:14-36` : la suggestion est calculée sur les `TargetPath` des lignes `Moved`, sans filtre.
- `CopyDialog.cs:143-146` : texte affiché quand le plan est vide.

**Scénario :**
- Sur `G:\`, l’utilisateur a supprimé un ancien dossier `Travail` (il est dans `G:\$RECYCLE.BIN\S-…\$R…\`).
- Il compare son partage avec `G:\` et clique sur « Tout regrouper dans le 2 ».
- Les fichiers de `Travail` ne sont pas copiés.
- Dans l’autre sens, « Tout regrouper dans le 1 » copie dans le dossier 1 un `$RECYCLE.BIN\S-…\` caché et système, avec ses `$I…` et `desktop.ini`. Si le dossier 1 est lui-même une racine de disque, ces fichiers atterrissent dans sa Corbeille.

**Preuve :**
- Programme : `<scratch>\bench\Program.cs`, scénario `recycle`. La racine du disque 2 est simulée par un dossier qui contient `$RECYCLE.BIN` (caché, système), avec une SID fictive.
- Commande : `run.ps1 -Scenario recycle`, sortie dans `<scratch>\out-recycle.txt`, extrait :

```
comparaison : Finished=True erreurs=0 CanCreatePlan=True
  ligne Moved           File      Photos\IMG_0001.jpg  -> 2:<work>\racine-disque2\$RECYCLE.BIN\S-1-5-21-1111111111-2222222222-3333333333-1001\$RX7Q2LM\Photos\IMG_0001.jpg
  ligne Moved           File      Travail\devis.docx  -> 2:<work>\racine-disque2\$RECYCLE.BIN\S-1-5-21-1111111111-2222222222-3333333333-1001\$RX7Q2LM\Travail\devis.docx
  (… 5 lignes Moved au total, toutes vers la Corbeille)
GatherFileCount(into2)=0
  plan regrouper dans le 2 : 0 fichier(s), 0 dossier(s), UnsupportedNames=0, AbandonedTemporaries=0
FolderAlignment.Suggest : InFolder2=True Folder=<work>\racine-disque2\$RECYCLE.BIN\S-1-5-21-1111111111-2222222222-3333333333-1001\$RX7Q2LM Files=5
GatherFileCount(into1)=3
  plan regrouper dans le 1 : 3 fichier(s), 3 dossier(s), UnsupportedNames=0, AbandonedTemporaries=0
    COPIERAIT [Directory] $RECYCLE.BIN
    COPIERAIT [Directory] $RECYCLE.BIN\S-1-5-21-1111111111-2222222222-3333333333-1001
    COPIERAIT [File] $RECYCLE.BIN\S-1-5-21-1111111111-2222222222-3333333333-1001\$IX7Q2LM
    COPIERAIT [File] $RECYCLE.BIN\S-1-5-21-1111111111-2222222222-3333333333-1001\desktop.ini
```

- CLI de `dist` sur les mêmes données : `cli.ps1 -Work recycle-57e208a0 -One dossier1 -Two racine-disque2 -Mode into2`, sortie dans `<scratch>\out-cli-recycle-57e208a0-into2.txt` :

```
Identiques : 0 · absents du 2 : 3 · absents du 1 : 15 · différents : 0 · déplacés : 5 · non vérifiés : 0 · liens non suivis : 0
SIMULATION — rien n’est copié.
0 fichier(s) · 0 o · 0 dossier(s)
```

- Limite de la preuve : un vrai `G:\` n’a pas été touché. L’énumération d’un `$RECYCLE.BIN` réel par son propriétaire est le comportement Windows par défaut ; voir « Pistes non prouvées ».

**Correctif proposé :**
1. Ajouter `FileSafety.IsVolumeRoot(root)`, vrai quand `GetVolumePathNameW(root) == root + "\"` ; c’est le cas de `G:\`, d’un point de montage ou de la racine d’un partage.
   - Sur une telle racine, `ScanAsync` écarte au premier niveau `$RECYCLE.BIN`, `RECYCLER` et `System Volume Information` (casse ignorée) et ajoute un **avertissement** qui les nomme.
   - On peut aussi les garder comme lignes `Skipped` avec ce motif. Ils ne sont alors ni des candidats, ni des exemplaires, ni du contenu à regrouper, ce qui bloque aussi leurs descendants via `BlockedPaths`.
2. En plus, pour tout dossier :
   - aucun fichier sous `.CheckerSan-Quarantine` (`DuplicateRemoval.IsInQuarantine`) ne sert de candidat dans `FindMovedFilesAsync`, ni d’entrée de `byHash` dans `FindTargetCopiesAsync` ;
   - `FolderAlignment.Suggest` ignore les `TargetPath` et chemins relatifs situés sous ces dossiers, ainsi que sous un temporaire (voir REG-M02).
3. Hors de ces racines, un nom `$RECYCLE.BIN` reste un dossier ordinaire.

**Test proposé :** `GatherIgnoresRecycleBinAndQuarantine`.
- Le moteur reçoit une racine de volume simulée, par exemple via une méthode interne `ComparisonOptions.TreatAsVolumeRoot`, ou par réflexion sur l’indicateur de racine.
- On reprend la structure du scénario `recycle`, puis on vérifie :
  - aucune ligne `Moved` dont le `TargetPath` contient `\$RECYCLE.BIN\` ;
  - `CreateGatherPlan(result, true)` contient les 5 fichiers ;
  - `CreateGatherPlan(result, false)` ne contient rien sous `$RECYCLE.BIN` ;
  - `FolderAlignment.Suggest(result)` vaut `null`.
- Variante avec `.CheckerSan-Quarantine\<session>\…` : la quarantaine n’est jamais choisie comme exemplaire, ni suggérée.

---

## REG-M02 — Un temporaire `.checkersan-….partial` d’une copie interrompue compte comme exemplaire : le fichier n’est plus jamais regroupé

**Gravité : haute.** Le scénario est courant : l’utilisateur arrête la copie pendant la vérification SHA-256 d’un gros fichier, ou l’application s’arrête entre la vérification et le renommage. Le temporaire est alors complet, octet pour octet. Puis il relance une comparaison, comme le recommande la documentation, et clique sur « Tout regrouper » : le bouton ouvre le dialogue sur ce mode, pas sur la reprise. Le fichier source passe en `Moved` vers son propre temporaire et sort du plan. Le journal de reprise est remplacé par le nouveau regroupement, et le fichier n’arrive jamais sous son nom.

Le seul exemplaire dans la destination est donc un fichier que l’application elle-même :
- qualifie de « temporaire d’une copie interrompue… jamais recopié » ;
- présente dans l’aperçu comme un temporaire à écarter.

L’utilisateur le supprimera. Les comparaisons suivantes affichent « rien à regrouper ».

**Fichier:ligne :**
- `ComparisonEngine.cs:748-752` : tous les fichiers lisibles du 2 sont candidats, sans filtre `CopyEngine.IsCopyTemporary`.
- `ComparisonEngine.cs:834-840` : même chose pour `byHash`, en sens inverse.
- `ComparisonEngine.cs:662-664` : les temporaires ne reçoivent qu’une note dans le détail.
- `CopyEngine.cs:209` : ils sont refusés comme **sources**, mais acceptés comme **preuves de présence**, ce qui est incohérent.

**Preuve :**
- Programme : `<scratch>\bench\Program.cs`, scénario `partial-into2`. La copie est réellement arrêtée par un `IProgress` synchrone dès la phase « Vérification SHA-256 de la copie ».
- Commande : `run.ps1 -Scenario partial-into2`, sortie dans `<scratch>\out-partial-into2.txt` :

```
[2] copie arrêtée : Cancelled=True terminés=1/2
    dans le 2 : <work>\dossier2\Videos\.checkersan-e247a31a5c964ee8be2f9bddba97202e.partial  (6291579 o)
    temporaire identique à la source : True
[3] nouvelle comparaison : Finished=True erreurs=0
  ligne Moved           File      Videos\vacances.mp4  -> 2:<work>\dossier2\Videos\.checkersan-e247a31a5c964ee8be2f9bddba97202e.partial
    GatherFileCount(into2)=1
  plan regrouper dans le 2 : 1 fichier(s), 0 dossier(s), UnsupportedNames=0, AbandonedTemporaries=0
    COPIERAIT [File] Docs\nouveau.txt
    vacances.mp4 dans le plan de regroupement : False
    vacances.mp4 dans le plan « Compléter le 2 » : True
[4] regroupement lancé : Complete=True terminés=1/1
    le journal de reprise contient encore vacances.mp4 : False
[5] comparaison suivante : GatherFileCount(into2)=0 ; plan regrouper dans le 2 : 0 élément(s)
    Videos\vacances.mp4 existe dans le 2 : False
```

- Dans l’autre sens, scénario `partial-into1` (`<scratch>\out-partial-into1.txt`) :

```
  ligne OnlyTarget      File      Photos\scan.tif  SourceCopyPath=1:<work>\dossier1\Photos\.checkersan-5ff149bf120a467789976258fbc3df15.partial
GatherFileCount(into1)=0
  plan regrouper dans le 1 : 0 fichier(s), 0 dossier(s), UnsupportedNames=0, AbandonedTemporaries=0
Photos\scan.tif existe dans le 1 : False
```

**Correctif proposé :**
- Dans `FindMovedFilesAsync`, filtrer les candidats : `.Where(item => Readable(item) && !CopyEngine.IsCopyTemporary(Path.GetFileName(item.RelativePath)))`.
- Dans `FindTargetCopiesAsync`, appliquer le même filtre à la construction de `byHash`.
- Les temporaires restent des lignes `OnlyTarget`/`OnlySource` avec leur note, sans jamais servir de preuve.
- Défense en profondeur dans `GatherRows` : une ligne `Moved` dont le `TargetPath`, ou une ligne `OnlyTarget` dont le `SourceCopyPath`, désigne un temporaire est traitée comme manquante.

**Test proposé :** `GatherIgnoresInterruptedTemporaries`.
- Reprendre le scénario `partial-into2` : arrêt pendant la vérification, puis nouvelle comparaison.
- Attendus :
  - la ligne de `vacances.mp4` est `OnlySource` ;
  - aucun `TargetPath` ni `SourceCopyPath` ne finit par `.partial` ;
  - `CreateGatherPlan(result, true)` contient `Videos\vacances.mp4` ;
  - même vérification en sens inverse.

---

## REG-M03 — Fichiers vides : tout fichier vide du 2 « contient » tous ceux du 1, dont les noms et les dossiers sont perdus

**Gravité : moyenne.**
- Pour un fichier vide, « le même contenu existe ailleurs » ne prouve rien : son nom est sa seule information.
- Les fichiers vides sont pourtant des marqueurs fonctionnels : `__init__.py`, `.gitkeep`, `.nomedia`, fichiers témoins.
- Le regroupement ne crée pas non plus les dossiers manquants qui ne contiennent que ces fichiers (« dossier tenant seulement des fichiers déjà présents ailleurs »).
- Après « Tout regrouper dans le 2 » puis la suppression du 1, une arborescence de paquet Python ou de projet est cassée sans avertissement.
- La perte des noms des contenus dupliqués est un choix documenté. Pour un fichier vide, en revanche, la garantie « le contenu est conservé » est vide de sens.

**Fichier:ligne :**
- `ComparisonEngine.cs:1215-1216` : `Readable` accepte `Size == 0`.
- `ComparisonEngine.cs:748-752` et `834-840` : appariement par taille et SHA-256, y compris pour 0 octet.
- `CopyEngine.cs:139-154` : les dossiers qui ne contiennent que ces fichiers ne sont pas créés.

**Preuve :** `<scratch>\bench\Program.cs`, scénario `empty`, sortie dans `<scratch>\out-empty.txt` :

```
  ligne Moved           File      projet\logs\.gitkeep  -> 2:<work>\dossier2\divers\vide.txt
  ligne Moved           File      projet\paquet\sous\__init__.py  -> 2:<work>\dossier2\divers\vide.txt
  ligne Moved           File      projet\paquet\__init__.py  -> 2:<work>\dossier2\divers\vide.txt
  ligne Moved           File      projet\VERSION_VALIDEE  -> 2:<work>\dossier2\divers\vide.txt
GatherFileCount(into2)=0
  plan regrouper dans le 2 : 0 fichier(s), 0 dossier(s), UnsupportedNames=0, AbandonedTemporaries=0
  plan compléter le 2 (1 → 2) : 4 fichier(s), 3 dossier(s), UnsupportedNames=0, AbandonedTemporaries=0
```

Le dialogue afficherait alors « Rien à regrouper : ce dossier a déjà un exemplaire de chaque fichier » (`CopyDialog.cs:143-146`).

**Correctif proposé :** ne jamais apparier les fichiers vides par contenu :
- `FindMovedFilesAsync` saute `item.Size == 0`, dans les candidats comme dans les sources ;
- `FindTargetCopiesAsync` fait de même dans `bySize` et `byHash`.

Le regroupement copie alors chaque fichier vide à son chemin, pour un coût nul. On peut ajouter un seuil de quelques octets, mais 0 suffit à fermer le cas prouvé.

**Test proposé :** `GatherKeepsEmptyFiles`, sur les données du scénario `empty`. Le plan vers le 2 contient `projet\paquet`, `projet\paquet\sous`, `projet\logs` et les quatre fichiers vides. Aucune ligne `Moved` n’a `SourceSize == 0`.

---

## REG-M04 — Exclusions muettes : le plan ne compte ni le contenu réel face à un lien ni les noms écartés ; le dialogue affirme « déjà un exemplaire de chaque fichier » ; `GatherFileCount` ≠ plan

**Gravité : moyenne.** C’est une fausse sécurité par message.

`CopyPlan` n’expose que `AbandonedTemporaries` et `UnsupportedNames` (`CopyEngine.cs:30-33`). Plusieurs catégories de fichiers du dossier copié qui manquent dans la destination ne sont comptées nulle part :
- les fichiers réels face à une jonction de l’autre côté (lignes `Skipped`) ;
- les fichiers `OnlySource` écartés par un ancêtre en incident (`IsBlocked`, `CopyEngine.cs:207`).

Quand le plan est vide, le dialogue affirme « Rien à regrouper : ce dossier a déjà un exemplaire de chaque fichier » (`CopyDialog.cs:143-146`). Il n’a aucune note sur les liens, contrairement à la fenêtre principale. En parallèle, `GatherFileCount` (`CopyEngine.cs:108-114`) ne retire pas les noms non portables, alors que le plan les écarte (`CopyEngine.cs:212`). Le bouton annonce donc « 1 fichier(s) à copier » pour un plan vide.

**Preuve :**
- Programme : `<scratch>\bench\Program.cs`, scénario `silent`, sortie dans `<scratch>\out-silent.txt`.
- Données :
  - le dossier 1 contient un vrai dossier `Photos` ;
  - le dossier 2 contient une jonction `Photos` vers un dossier vide situé hors des deux racines ;
  - le dossier 1 contient aussi un nom à espace final, créé par `\\?\`.

```
comparaison : Finished=True erreurs=0 CanCreatePlan=True
  ligne OnlySource      File      Commun\note.txt 
  ligne Skipped         File      Photos\2024\a.jpg
  ligne Skipped         File      Photos\2024\b.jpg
GatherFileCount(into2)=1
  plan regrouper dans le 2 : 0 fichier(s), 0 dossier(s), UnsupportedNames=1, AbandonedTemporaries=0
Le plan dit combien de fichiers du 1 il laisse derrière des liens ? propriétés publiques : Schema, Bidirectional, SourceRoot, TargetRoot, SourceCanonical, TargetCanonical, CreatedAt, SourceRootId, TargetRootId, Items, AbandonedTemporaries, UnsupportedNames, FileCount, TotalBytes
fichiers du 1 sans exemplaire dans le 2 (lignes avec SourcePath et sans TargetPath) : 3
plan.Items.Count == 0 : True
```

Ce qu’on voit : un bouton « Tout regrouper dans le 2 · 1 fichier(s) à copier… », puis un dialogue qui dit « Rien à regrouper : ce dossier a déjà un exemplaire de chaque fichier », alors que trois fichiers du 1 n’ont aucun exemplaire dans le 2.

**Correctif proposé :**
1. Compter chaque fichier écarté et sa raison dans `CopyPlan`, en `[JsonIgnore]` comme les deux compteurs actuels :
   - `BlockedByIncident`, incrémenté à `CopyEngine.cs:207` ;
   - `BehindLinks` : lignes `Skipped` de type fichier présentes seulement du côté copié (`SourcePath != null && TargetPath == null` vers le 2, l’inverse vers le 1) ;
   - `OnlineOnly`.
2. Dans `GatherFileCount`, appliquer **exactement** les mêmes filtres que `AddItems`. `PortableName` ne lit pas le disque et peut y être appelé. Le mieux est d’extraire un prédicat commun `Copyable(row, sourceRoot)`.
3. Dans le dialogue, n’afficher « déjà un exemplaire de chaque fichier » que si tous ces compteurs sont nuls et qu’aucune ligne `Different` ou `Conflict` n’existe du côté copié. Sinon, afficher « Rien à copier automatiquement : N fichier(s) écarté(s) (liens, incidents, noms…) ».

**Test proposé :** `GatherCountsWhatItLeavesOut`, sur les données du scénario `silent` :
- `GatherFileCount(result, true) == plan.FileCount` ;
- `plan.BehindLinks == 2` ;
- `plan.UnsupportedNames == 1` ;
- côté interface, le résumé du dialogue ne contient pas « déjà un exemplaire ».

---

## REG-M05 — Recherche inverse : lectures non bornées, ×6 mesuré (+500 %), au lieu des +29 % documentés

**Gravité : moyenne** (performance ; aucun risque pour les données). Sur un partage ou un disque USB, où le scan réel de l’utilisateur a déjà duré 1 h 51, le temps peut être multiplié par un facteur non borné.

**Fichier:ligne :** `ComparisonEngine.cs:841-857`.
- Chaque ligne `OnlyTarget` de **même taille qu’une source déjà hachée** est lue en entier. Sous 4 Mio, il n’y a pas d’échantillon.
- Les sources hachées comprennent toutes les sources identiques au même chemin (`_coveredSourceFiles`).
- L’ancien moteur ne lisait pas ces cibles : `FindMovedFilesAsync` saute les sources couvertes (`ComparisonEngine.cs:764`).

**Scénario :** des fichiers de taille fixe. Exemples : blocs d’un outil de sauvegarde, journaux de capteurs, RAW non compressés au-delà de l’échantillon, ressources de jeux. Le dossier 1 en contient quelques-uns, identiques dans le 2, et le 2 en contient beaucoup d’autres. Le surcoût est proportionnel au nombre de fichiers du 2, pas à celui des fichiers comparés.

**Preuve :**
- Programmes `<scratch>\perf\out-new\Perf.dll` (nouvelle DLL) et `<scratch>\perfold\out\Perf.dll` (DLL de `b5399b7`), même code.
- Données (`Perf prepare`) : 200 fichiers de 10 000 octets identiques au même chemin, et 2 000 autres fichiers de 10 000 octets seulement dans le 2.
- Trois passages alternés, `ParallelReads = 1`, NVMe. Sortie dans `<scratch>\out-perf.txt` :

```
ancienne DLL (b5399b7) : fichiers lus en entier=400 octets lus=4,000,000 échantillons=0 durée=416 ms erreurs=0
nouvelle DLL : fichiers lus en entier=2400 octets lus=24,000,000 échantillons=0 durée=1096 ms erreurs=0
ancienne DLL (b5399b7) : fichiers lus en entier=400 octets lus=4,000,000 échantillons=0 durée=519 ms erreurs=0
nouvelle DLL : fichiers lus en entier=2400 octets lus=24,000,000 échantillons=0 durée=1696 ms erreurs=0
ancienne DLL (b5399b7) : fichiers lus en entier=400 octets lus=4,000,000 échantillons=0 durée=539 ms erreurs=0
nouvelle DLL : fichiers lus en entier=2400 octets lus=24,000,000 échantillons=0 durée=1171 ms erreurs=0
```

**Correctif proposé (au choix, cumulables) :**
1. Rendre la recherche inverse paresseuse. Elle ne sert qu’au regroupement vers le 1 et à l’affichage du `SourceCopyPath`. Elle s’exécute quand l’utilisateur choisit ce mode (avec sa progression), ou derrière une option. La comparaison ordinaire ne paie plus rien.
2. Sinon, présélectionner toutes les tailles, pas seulement au-delà de 4 Mio :
   - au-delà de 192 Kio, échantillons comme aujourd’hui ;
   - en dessous, une empreinte du premier et du dernier Kio, comparée à celle des sources de la même taille (relecture de 2 Kio par source).
3. Inclure ces lectures dans la mesure du surcoût de `PASSATION.md`. Le pire cas n’est pas « 40 tailles distinctes », c’est « peu de sources couvertes, beaucoup de cibles de même taille ».

Un faux négatif ne fait que recopier un fichier, sans risque pour les données. On peut donc aussi borner la recherche, par exemple ne pas lire plus de *k* cibles par source hachée et par taille.

**Test proposé :** un test de performance comme `GatherCountStaysFastOnLargeResults`, sur 20 sources couvertes et 2 000 cibles de même taille. Il vérifie `result.FilesVerified <= 40 + marge`, ou, avec la recherche paresseuse, que la comparaison seule ne lit que les paires.

---

## REG-M06 — Les exemplaires « trouvés ailleurs » ne sont pas revérifiés quand le plan est préparé

**Gravité : basse.** Le constat est vrai de toute comparaison : une ligne `Identical` n’est pas revérifiée non plus par « Compléter ». Mais le regroupement étend cette confiance à toutes les lignes `Moved` et `SourceCopyPath` : 40 973 dans le cas réel de l’utilisateur. Leur preuve repose sur **un autre fichier** que celui qu’on renonce à copier, souvent dans des dossiers mal rangés que l’utilisateur est justement en train de réorganiser. « Compléter » relit chaque source copiée (`AddItems`) ; le regroupement ne regarde ni le fichier écarté ni son exemplaire.

**Fichier:ligne :**
- `CopyEngine.cs:136-138` : la décision ne repose que sur le statut de la ligne.
- `CopyEngine.cs:217-225` : seuls les fichiers copiés sont ouverts.

**Preuve :** `<scratch>\bench\Program.cs`, scénario `stale`, sortie dans `<scratch>\out-stale.txt`. Après la comparaison, on supprime l’exemplaire dans le 2 et on modifie la source dans le 1 :

```
  ligne Moved           File      Docs\these.docx  -> 2:<work>\dossier2\Ancien\these-v1.docx
  ligne Moved           File      Photos\mariage.jpg  -> 2:<work>\dossier2\A trier\IMG_4411.jpg
  plan regrouper dans le 2 (préparé après ces changements) : 0 fichier(s), 0 dossier(s), UnsupportedNames=0, AbandonedTemporaries=0
copie : Complete=True erreurs=0
contenu de Photos\mariage.jpg présent quelque part dans le 2 : False
version actuelle de Docs\these.docx présente quelque part dans le 2 : False
  pour comparaison, plan « Compléter le 2 » sur la même comparaison : 2 fichier(s), 2 dossier(s), UnsupportedNames=0, AbandonedTemporaries=0
```

**Correctif proposé :**
- Conserver dans le résultat l’identité, avec la date de changement, de chaque exemplaire et de chaque fichier écarté. `_readIdentities` existe déjà : exposer par exemple `ComparisonRow.TargetIdentity` et `SourceCopyIdentity`.
- Dans `CreateGatherPlan`, réinterroger ces identités par handle (`FileSafety.CurrentIdentity`, environ 70 µs par fichier en NVMe, avec les lecteurs par disque).
- Si une identité a changé ou a disparu : copier le fichier (il est alors vraiment manquant) ou refuser le plan en demandant une nouvelle comparaison.

**Test proposé :** `GatherRechecksFoundCopiesAtPlanTime`, sur les données du scénario `stale` : après la suppression de l’exemplaire, `CreateGatherPlan(result, true)` contient `Photos\mariage.jpg`, ou lève une exception qui demande de comparer à nouveau.

---

## REG-M07 — Un fichier verrouillé que la comparaison n’a pas lu fait échouer tout le plan, malgré « les incidents ne bloquent plus »

**Gravité : basse.** Il n’y a aucun risque pour les données, mais la promesse de `PASSATION.md` est fausse dans un cas courant : un `.pst` d’Outlook ouvert, un disque de VM ou une base de données. Si aucun fichier de même taille n’existe de l’autre côté, la comparaison ne lit pas ce fichier : pas d’incident, ligne `OnlySource`. Préparer le plan lève alors une exception, et tout le regroupement est impossible. Le bouton, lui, annonce les fichiers à copier.

**Fichier:ligne :** `CopyEngine.cs:219` (`new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)` dans `AddItems`, sans capture).

**Preuve :** `<scratch>\bench\Program.cs`, scénario `locked` (fichier tenu en `FileShare.None` pendant tout le scénario), sortie dans `<scratch>\out-locked.txt` :

```
comparaison : Finished=True erreurs=0 CanCreatePlan=True
  ligne OnlySource      File      seulement\a.txt
  ligne OnlySource      File      seulement\base.pst
GatherFileCount(into2)=2
CreateGatherPlan lève IOException : The process cannot access the file '<work>\dossier1\seulement\base.pst' because it is being used by another process.
```

**Correctif proposé :**
- Dans `AddItems`, lire taille, dates, attributs et identité sur un handle `FILE_READ_ATTRIBUTES` seul (`0x80`, `FILE_FLAG_OPEN_REPARSE_POINT`). Les modes de partage d’un autre processus ne bloquent pas ce handle. La copie elle-même signale alors une erreur pour ce seul élément et continue avec les autres.
- À défaut, capturer `IOException` et `UnauthorizedAccessException` par élément et compter `plan.Unreadable`, comme `UnsupportedNames`.

**Test proposé :** `GatherSurvivesALockedUnreadFile`, sur les données du scénario `locked` :
- `CreateGatherPlan` réussit et contient `seulement\a.txt` ;
- `CopyAsync` copie `a.txt` et rend une seule erreur, nommant `base.pst`.

---

## REG-M08 — Une copie du 1 modifiée rend « Non vérifié » le fichier du 2 qui la désignait : il sort aussi de « Compléter le dossier 1 »

**Gravité : basse.** Rien n’est faussement déclaré présent, mais l’exclusion est trop large.

Quand l’exemplaire `S` du dossier 1 change pendant la comparaison, `MarkUnverified` passe en `Unverified` toute ligne dont le `SourceCopyPath` vaut `S`. Le fichier `T` du 2 est pourtant intact, et son absence du 1 à son chemin est certaine. Conséquences :
- il est exclu du regroupement, alors qu’il faudrait justement le copier puisque son exemplaire n’est plus prouvé ;
- il est exclu de « Compléter le dossier 1 », qui ne s’intéresse pas aux exemplaires ;
- il perd son statut « absent du 1 » dans les verdicts.

**Fichier:ligne :**
- `ComparisonEngine.cs:1166-1169` (`MarkUnverified`, troisième condition) ;
- `ComparisonEngine.cs:1083-1084`.

**Preuve :** `<scratch>\bench\Program.cs`, scénario `copychange` (même technique que `GatherRechecksCopiesFoundInFolderOne`), sortie dans `<scratch>\out-copychange.txt` :

```
comparaison : Finished=True erreurs=1 modifié=True
  ligne Unverified      File      a.txt
  ligne Unverified      File      b\y.txt  SourceCopyPath=1:<work>\dossier1\a.txt
  ligne OnlyTarget      File      b\z.txt
  plan compléter le 1 (2 → 1) : 1 fichier(s), 1 dossier(s), UnsupportedNames=0, AbandonedTemporaries=0
    COPIERAIT [Directory] b
    COPIERAIT [File] b\z.txt
  plan regrouper dans le 1 : 1 fichier(s), 1 dossier(s), UnsupportedNames=0, AbandonedTemporaries=0
b\y.txt existe au même chemin dans le 1 : False ; contenu de b\y.txt présent ailleurs dans le 1 : False
```

**Correctif proposé :**
- Dans `MarkUnverified`, quand la seule correspondance est `SourceCopyPath`, ne pas changer le statut. Retirer le `SourceCopyPath` (`row with { SourceCopyPath = null, Detail = row.Detail + " L’exemplaire trouvé dans le 1 a changé : il ne compte plus." }`).
- Garder `Unverified` quand `SourcePath` ou `TargetPath` correspond.
- Le fichier est alors regroupé dans le 1 et complété par « Compléter le 1 ».
- Adapter `GatherRechecksCopiesFoundInFolderOne` : `b\y.txt` doit être **dans** le plan, plus sûr que de l’écarter.

**Test proposé :** reprendre le scénario `copychange`, puis vérifier :
- `b\y.txt` est `OnlyTarget`, avec `SourceCopyPath == null` ;
- `CreatePlan(result, false)` et `CreateGatherPlan(result, false)` contiennent `b\y.txt` ;
- `a.txt` reste `Unverified`.

---

## REG-M09 — CLI `into1/into2` : ni les éléments écartés ni l’absence de recherche des déplacés ne sont signalés

**Gravité : basse.**

`SimulateCopy` (`CheckerSanCli.cs:137-169`) n’affiche que :
- les éléments du plan ;
- les conflits et les contenus différents (ligne 162-163).

Il manque :
- les éléments écartés sous un incident ou face à un lien ;
- les fichiers en ligne seulement ;
- `UnsupportedNames` et `AbandonedTemporaries` ;
- surtout, avec `--no-moved`, la simulation `into2` recopie tout chemin manquant, alors que l’aide promet « sans recopier les contenus qu’il a déjà ailleurs ». Le dialogue de l’application a cette note (`CopyDialog.cs:107`) ; le CLI ne l’a pas.

**Preuve :** `cli.ps1 -Work empty-71a32583 -One dossier1 -Two dossier2 -Mode into2 -NoMoved`, sortie dans `<scratch>\out-cli-empty-71a32583-into2-nomoved.txt`. Ce sont les mêmes données qu’en REG-M03 ; ici, elles sont toutes copiées sans un mot sur la recherche désactivée :

```
SIMULATION — rien n’est copié.
COPIERAIT 1 → 2  [dossier]  projet\logs
COPIERAIT 1 → 2  [dossier]  projet\paquet
COPIERAIT 1 → 2  [dossier]  projet\paquet\sous
COPIERAIT 1 → 2  0 o  projet\logs\.gitkeep
COPIERAIT 1 → 2  0 o  projet\VERSION_VALIDEE
COPIERAIT 1 → 2  0 o  projet\paquet\__init__.py
COPIERAIT 1 → 2  0 o  projet\paquet\sous\__init__.py
4 fichier(s) · 0 o · 3 dossier(s)
```

**Correctif proposé :**
- Pour `into1/into2` avec `!result.MovedFilesSearched`, afficher une ligne : « La recherche des fichiers déplacés est désactivée (--no-moved) : chaque chemin manquant serait copié, même présent ailleurs ».
- Afficher les compteurs de REG-M04 (incidents, liens, en ligne seulement, noms, temporaires) et les ajouter à l’objet `CopySimulation` du JSON.

**Test proposé :** dans `SnapshotAndCliTests`, avec `compare … --no-moved --simulate-copy into2`, la sortie contient « --no-moved ». Sur les données du scénario `silent`, elle contient le nombre de fichiers écartés derrière des liens.

---

## Contrôles menés sans trouvaille

Ces points ont été vérifiés et ne présentent pas de défaut. Ils sont utiles à l’arbitrage.

- **Équivalence de l’ancien test de préfixe et du nouveau test par ancêtres.**
  - Scénario `blocked-equiv` : `BlockedPaths` et `IsBlocked` appelés par réflexion, face à une réimplémentation de l’ancien `StartsWith(prefix + "\\")`.
  - 200 000 cas aléatoires : `/`, `\`, séparateurs doublés et finaux, casse, `.`, lettres à trois casses (ǅ). Sortie dans `<scratch>\out-blocked-equiv.txt` : `cas=200000 divergences=0`.
  - Seule différence théorique, sans effet : un chemin relatif commençant par `\`, ce que l’inventaire ne produit jamais.
- **Erreurs sans ligne.** Chaque `_errors.Add` du moteur produit une ligne bloquante :
  - racine : ligne `.` via `RootProblem`/`AddRootProblemRow` (`ComparisonEngine.cs:678-681` et `1110-1118`) ;
  - dossier illisible : `Problem`, puis `AncestorProblem` de l’autre côté (607-610) ;
  - collision de casse : `Problem` sur l’entrée gardée, et les descendants sont bloqués par ancêtre ;
  - échantillon ou empreinte : `MarkUnverified` ;
  - contrôle final : `VerifySnapshot` et `VerifyReadIdentitiesAsync`.
  
  La seule exception plausible est en « Pistes non prouvées ».
- **Coupure transitoire après l’inventaire.** `finished` n’est mis à vrai qu’à la ligne 171 ; le `catch` transitoire (185-194) le laisse faux, donc `CanCreatePlan` est faux. Une erreur non transitoire (disque USB arraché puis rebranché) produit des lignes `Unverified` ou une ligne `.`, sans `Finished` abusif.
- **Présélection de `FindTargetCopiesAsync`.** Un échantillon ne fait qu’écarter un candidat ; le SHA-256 complet reste exigé. Seuls des faux négatifs sont possibles, c’est-à-dire une copie en trop.
- **Reprise.** Une empreinte réutilisée exige la même identité, date de changement comprise (`ComparisonEngine.cs:1001-1002`). Une source réutilisée sans échantillon rend `Possible` vrai, ce qui est prudent.

## Pistes non prouvées

1. **`MarkDirectoryProblem` et les noms à point ou espace final.**
   - L’inventaire indexe les entrées par `FileSafety.RelativeTo`, une sous-chaîne brute. `MarkDirectoryProblem` (`ComparisonEngine.cs:588`) cherche l’entrée par `Path.GetRelativePath`, qui normalise et retire les points et espaces finaux.
   - Pour un dossier `x.` ou `x ` créé par un NAS, WSL ou macOS, l’échec de lecture pourrait n’être reporté sur aucune entrée, ou sur l’entrée `x`.
   - Il en resterait une erreur sans ligne. Depuis le relâchement de `CanCreatePlan`, une copie serait permise malgré elle. Les noms non portables sont toutefois écartés par `PortableName` dans le plan.
   - Non reproduit.
2. **Corbeille réelle.** Le scénario REG-M01 simule `$RECYCLE.BIN` dans un dossier. Il n’a pas été vérifié sur une vraie racine de volume (interdit ici) que le dossier `S-…` du propriétaire est énuméré sans erreur. C’est le comportement par défaut de Windows. Sur une racine FAT32 ou exFAT, `System Volume Information` est lisible : il serait regroupé comme du contenu.
3. **Quarantaine.** `.CheckerSan-Quarantine` commence par `.` et se trie avant presque tous les noms : c’est elle qui est choisie comme exemplaire (`Moved`, `SourceCopyPath`) et que `FolderAlignment` peut suggérer. Tant que l’exemplaire conservé par l’outil des doublons existe, rien n’est perdu. Ce n’est pas démontré au-delà.
4. **`MarkUnverified` est en O(lignes) par incident**, avec désormais trois comparaisons par ligne. Le relâchement rend exploitables des comparaisons à milliers d’incidents, dont le coût total devient quadratique. Le défaut existait avant ; il n’a pas été mesuré.
5. **Systèmes de fichiers sans `FileIdInfo`** (certains NAS). `_readIdentities` y reste vide, donc aucun contrôle final par handle du `SourceCopyPath`. C’est la même limite qu’existante pour `Identical` et `Moved`.

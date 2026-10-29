# Revue adverse « Regrouper dans le dossier 1 ou 2 » — équipe rouge, interface et parcours

Revue du changement non commité au-dessus de `b5399b7` (section « Regrouper dans le dossier 1 ou 2 — 28 octobre 2026 » de `PASSATION.md`), menée le 28 octobre 2026. Cible : l’interface WPF et le parcours de bout en bout (verdicts, boutons « Tout regrouper », dialogue de copie, suggestion de sous-dossier, aide intégrée). Le moteur n’est examiné que là où il décide de ce que l’utilisateur voit.

**Gravités** (même échelle que `revue/REVUE-ROUGE.md`) : Critique = perte, écrasement ou suppression de données, ou faux verdict « identique » ; Haute = écriture ou suppression hors de l’endroit prévu, verdict trompeur, journal qui empêche une reprise ou une restauration ; Moyenne = échec ou blocage sans perte, garantie non tenue dans un cas réaliste ; Basse = robustesse, message.

## Synthèse

| ID | Gravité | Titre | Preuve |
|---|---|---|---|
| REG-P01 | Haute | « Comparer avec ce dossier » retire de la comparaison les fichiers que l’utilisateur voulait regrouper ; la comparaison suivante affiche « Oui », voire « Les deux dossiers sont identiques » | Exécution `align`, `align1` (vrai `MainWindow.Transfer` et vrai clic dans le dialogue) |
| REG-P02 | Haute | « Rien à regrouper : ce dossier a déjà un exemplaire de chaque fichier » alors qu’un contenu n’y existe pas (version différente, incident, nom non portable, fichier en ligne) | Exécution `nothing`, `blocked`, `names` ; cas « en ligne » par lecture |
| REG-P03 | Moyenne | Le bouton « Tout regrouper » ouvre le dialogue sur le regroupement et non sur la reprise : journal de la copie inachevée remplacé, avancement perdu, temporaire laissé pour toujours dans la destination | Exécution `resume` |
| REG-P04 | Basse | Le nombre affiché sur le bouton compte des fichiers que le plan écarte ensuite (noms non portables) | Exécution `names` |
| REG-P05 | Moyenne | Libellé, infobulle, aide FR/EN et message de fin promettent plus que le regroupement ne fait | Exécution `nothing`, `nomoved`, `after`, `align` ; message de fin par lecture |
| REG-P06 | Basse | L’écran affiche « Non », le rapport CSV exporté de la même comparaison « Non vérifié » | Exécution `report` |
| REG-P07 | Basse | Ligne « Absent du dossier 1 » : le panneau de détail présente un autre fichier comme « 1 : … » et dit son SHA-256 « Non calculé » | Exécution `after` |

## Banc de reproduction

- Projet `<scratch>\harness\Harness.csproj` (`net10.0-windows`, `UseWPF`, `Exe`, `[STAThread]`) et `<scratch>\harness\Program.cs`. Il référence les DLL compilées du dépôt, sans les modifier :
  - `<dépôt>\FolderChecker.App\bin\Release\net10.0-windows\CheckerSan.dll` (28 octobre, 22:37) ;
  - `<dépôt>\FolderChecker.Core\bin\Release\net10.0\FolderChecker.Core.dll` (22:36–22:37), postérieures à la dernière modification des sources (`CopyDialog.cs`, 22:30).
- Initialisation comme `FolderChecker.App.Tests\Program.cs` : `new FolderChecker.App.App()`, `InitializeComponent()`, `AppLanguage.Apply("fr", persist: false)`.
- Sécurité :
  - dans chaque `MainWindow`, les champs `_checkpointPath` et `_copyCheckpointPath` sont redirigés par réflexion vers `<scratch>\data\<scénario>\journal\` avant tout `Show`, `RefreshCopy` ou `RefreshCheckpoint` ;
  - aucune copie n’est lancée par `MainWindow` (elle écrirait le journal d’activité réel). Les copies réelles passent par `CopyEngine.CopyAsync`, avec un journal dans `<scratch>` ;
  - la comparaison que l’utilisateur relancerait par « Comparer » est refaite par `ComparisonEngine.CompareAsync`, le même appel que `MainWindow.CompareAsync`, sans son journal d’activité ;
  - les fenêtres affichées (scénarios `align`, `align1`, `layout`) sont placées hors écran ;
  - toutes les données sont sous `<scratch>\data`.
- Commande : `powershell -NoProfile -File <scratch>\run.ps1 -Scenarios <nom>`.
  - Le script règle les variables du SDK local (`DOTNET_CLI_HOME`, `NUGET_PACKAGES`, `APPDATA` sous `<dépôt>\.tools`), compile le banc, puis lance `RougeParcours.dll <nom> <scratch>\data`.
  - La sortie est écrite dans `<scratch>\out-<nom>.txt`, et le banc y remplace lui-même le chemin du dossier de travail par `<scratch>`.
- Les extraits ci-dessous sont copiés tels quels de ces sorties. Une ligne `…` signale des lignes omises.

---

## REG-P01 — « Comparer avec ce dossier » retire de la comparaison les fichiers à regrouper, puis la comparaison suivante annonce « Oui » ou « identiques »

**Gravité : haute** (verdict trompeur obtenu en suivant la recommandation de l’application).
- La suggestion se déclenche sur une petite minorité de fichiers : 3 sur 23, alors que l’aide parle de « la plupart des fichiers d’un dossier ».
- Elle apparaît dans le dialogue même qu’ouvre « Tout regrouper », et restreint précisément le dossier dont ce mode copie les fichiers.
- La comparaison suivante affiche en vert « Oui », ou « Les deux dossiers sont identiques », alors que les fichiers à regrouper n’ont jamais été copiés. C’est le verdict sur lequel l’utilisateur s’appuie pour supprimer l’autre dossier.
- Seul le texte de la case rappelle que la comparaison ne porte plus que sur un sous-dossier.

**Fichiers :**
- `<dépôt>\FolderChecker.Core\FolderAlignment.cs:16` et `:32` : le seuil porte sur les seuls fichiers appariés (`moved + identical`), jamais sur le nombre de fichiers des dossiers ;
- `<dépôt>\FolderChecker.App\CopyDialog.cs:30-42` : bandeau et bouton proposés quel que soit le mode choisi ;
- `<dépôt>\FolderChecker.App\CopyDialog.cs:159-161` : le texte ne dit pas ce qui sort de la comparaison ;
- `<dépôt>\FolderChecker.App\MainWindow.Copy.cs:40-46` ;
- `<dépôt>\FolderChecker.App\MainWindow.xaml.cs:356` (détail du verdict), ainsi que l’aide FR `:468` et EN `:473`.

**Scénario (`align`) :**
- Dossier 1 : `a.txt`, `b.txt`, `c.txt` et 20 fichiers `projet-NN.txt`, soit 23 fichiers.
- Dossier 2 : `Sauvegarde\a.txt`, `Sauvegarde\b.txt`, `Sauvegarde\c.txt` (mêmes contenus) et 30 fichiers `photo-NN.jpg` à la racine.

Parcours de l’utilisateur :
1. Il clique sur « Comparer ». Il lit « Le dossier 1 contient tout le dossier 2 : Non », et à côté « Tout regrouper dans le 1 · 30 fichier(s) à copier… ».
2. Il clique sur ce bouton. Le dialogue s’ouvre sur ce mode, avec les 30 photos et un bandeau orange : « 3 fichier(s) du dossier 1 sont rangés à l’identique dans « …\Sauvegarde ». Comparez le dossier 1 avec ce sous-dossier… », suivi du bouton « Comparer avec ce dossier ».
3. Il clique sur « Comparer avec ce dossier ». La case 2 devient `…\dst\Sauvegarde`.
4. Il clique sur « Comparer ». L’application affiche « Le dossier 1 contient tout le dossier 2 : Oui » et le bouton « Regrouper dans le 1 » disparaît. Les 30 photos du dossier 2 ne sont plus ni comparées ni regroupées, et « Tout regrouper dans le 2 » déposerait les 20 projets dans `…\dst\Sauvegarde`.

**Scénario miroir (`align1`) :**
- Dossier 1 : `Ancien\a.txt`, `Ancien\b.txt`, `Ancien\c.txt` et 20 fichiers `projet-NN.txt`.
- Dossier 2 : `a.txt`, `b.txt`, `c.txt`.

L’utilisateur veut tout regrouper dans le 2, puis libérer le 1 :
1. Il clique sur « Tout regrouper dans le 2 · 20 fichier(s) à copier… ».
2. Il clique sur « Comparer avec ce dossier ». La case 1 devient `…\src\Ancien`.
3. Il clique sur « Comparer ». L’application affiche « Les deux dossiers sont identiques », avec « Oui » des deux côtés. Les 20 projets n’ont jamais été copiés : supprimer le dossier 1 d’origine les perdrait.

**Preuve :** `powershell -NoProfile -File <scratch>\run.ps1 -Scenarios align,align1`. `MainWindow.Transfer(IntoFolder1)` est appelée par réflexion avec un vrai `ShowDialog`. Un gestionnaire `Loaded` attend l’aperçu, puis déclenche le vrai `Click` de `CompareInsteadButton` ; rien d’autre n’est cliqué. Sortie exacte (extraits de `<scratch>\out-align.txt`) :

```
Fichiers : 23 dans le 1, 33 dans le 2 ; déplacés = 3, identiques = 0
Suggest : Suggestion { InFolder2 = True, Folder = <scratch>\data\align\dst\Sauvegarde, Files = 3 }
--- Fenêtre principale : première comparaison
…
  Verdict gauche    : Le dossier 1 contient tout le dossier 2 : Non
  GatherInto1Button : visible=True actif=True « Tout regrouper dans le 1 · 30 fichier(s) à copier… »
…
--- Dialogue : ouvert par « Tout regrouper dans le 1 »
…
  Mode sélectionné  : IntoFolder1 « Tout regrouper dans le dossier 1, sans recopier ce qu’il a déjà (2 → 1) »
  Bandeau aligné    : 3 fichier(s) du dossier 1 sont rangés à l’identique dans « <scratch>\data\align\dst\Sauvegarde ». Comparez le dossier 1 avec ce sous-dossier : ils y seront appariés chemin par chemin, et les copies arriveront à côté d’eux.
…
  Plan              : 30 élément(s), <scratch>\data\align\dst → <scratch>\data\align\src
…
  → clic sur « Comparer avec ce dossier »
--- Fenêtre principale : juste après « Comparer avec ce dossier »
  Case 1            : <scratch>\data\align\src
  Case 2            : <scratch>\data\align\dst\Sauvegarde
  _result           : null
…
--- Fenêtre principale : après la comparaison suggérée
…
  VerdictTitle      : Le dossier 1 contient tout le dossier 2
…
  Verdict gauche    : Le dossier 1 contient tout le dossier 2 : Oui
  GatherInto1Button : visible=False actif=True « Tout regrouper dans le 1 · 0 fichier(s) à copier… »
…
« Tout regrouper dans le 2 » copierait 20 fichier(s) vers <scratch>\data\align\dst\Sauvegarde, par ex. <scratch>\data\align\dst\Sauvegarde\projet-01.txt
Fichiers de l’ancien dossier 2 hors de la comparaison : 30 (photo-01.jpg … photo-30.jpg), jamais regroupés dans le 1 ; « Regrouper dans le 1 » en comptait 30 avant.
```

Extraits de `<scratch>\out-align1.txt` :

```
Suggest : Suggestion { InFolder2 = False, Folder = <scratch>\data\align1\src\Ancien, Files = 3 }
…
  GatherInto2Button : visible=True actif=True « Tout regrouper dans le 2 · 20 fichier(s) à copier… »
…
  Bandeau aligné    : 3 fichier(s) du dossier 2 sont rangés à l’identique dans « <scratch>\data\align1\src\Ancien ». Comparez ce sous-dossier avec le dossier 2 : ils y seront appariés chemin par chemin, et les copies arriveront à côté d’eux.
…
  → clic sur « Comparer avec ce dossier »
--- Fenêtre principale : juste après « Comparer avec ce dossier »
  Case 1            : <scratch>\data\align1\src\Ancien
…
--- Fenêtre principale : après la comparaison suggérée
…
  VerdictTitle      : Les deux dossiers sont identiques
  VerdictDetail     : Mêmes chemins, mêmes dossiers et mêmes contenus de fichiers, confirmés par SHA-256. Résultat valable pour l’état contrôlé à la fin du scan.
  Verdict gauche    : Le dossier 1 contient tout le dossier 2 : Oui
…
  Verdict droit     : Le dossier 2 contient tout le dossier 1 : Oui
```

Dans les deux sens, la case remplie est la bonne et le verdict précédent est bien effacé (voir « Points vérifiés »). Le défaut tient au périmètre et au message, non à la case.

**Correctif proposé :**
1. **Seuil.** `FolderAlignment.Suggest` doit rapporter `best.Value` au nombre de fichiers du dossier que la suggestion dit « rangé » ailleurs, et non aux seuls appariés : `best.Value * 2 > (inFolder2 ? result.SourceFileCount : result.TargetFileCount)`. Le texte de l’aide devient alors vrai.
2. **Dossier restreint.** Le dossier que la suggestion restreint doit être la destination du mode choisi :
   - `InFolder2` ne s’accorde qu’avec `IntoFolder2` ou `Forward` ;
   - `!InFolder2` ne s’accorde qu’avec `IntoFolder1` ou `Backward`.

   Dans `CopyDialog`, il faut donc afficher ou masquer `hint` dans `direction.SelectionChanged`, et ne jamais le proposer quand la restriction porte sur le dossier qui fournit les fichiers. C’est le sens même de la phrase « les copies arriveront à côté d’eux ».
3. **Fichiers exclus.** Compter et annoncer ce qui sort de la comparaison. Pour `InFolder2`, ce sont les lignes `Kind == File` dont `TargetPath` n’est pas sous `prefix` ; pour l’autre sens, les lignes symétriques. Le bandeau dit alors : « … ; 30 fichier(s) du dossier 2 situés hors de ce sous-dossier ne seront plus comparés ni regroupés. »
4. **Trace de la restriction.** Après `CompareInstead`, `MainWindow` mémorise la racine d’origine (`_narrowedFrom`), effacée par toute saisie manuelle dans les cases. `ShowResult` ajoute alors au détail : « Comparaison limitée à … , sous-dossier de … : N fichier(s) hors de ce sous-dossier ne sont pas couverts par ce verdict. »
5. **Aide.** Réécrire la phrase de l’aide FR et EN.

**Test proposé** (`FolderChecker.App.Tests`, `GatherSuggestionNeverDropsFilesToGather`) :
- avec la fixture `align`, `FolderAlignment.Suggest` rend `null` (3 fichiers sur 23) ; sans seuil, `CompareInsteadButton` est absent ou masqué en mode `IntoFolder1` ;
- avec la fixture `align1`, en mode `IntoFolder2`, aucun bouton « Comparer avec ce dossier » ; en mode `IntoFolder1`, le bandeau contient « 20 fichier(s) » ;
- dans une `MainWindow` : appeler `Transfer` avec un clic simulé sur le bouton, puis `ShowResult` du nouveau résultat. `VerdictDetail` doit mentionner le sous-dossier et le nombre de fichiers non couverts.

---

## REG-P02 — « Rien à regrouper : ce dossier a déjà un exemplaire de chaque fichier » alors qu’un contenu n’y existe pas

**Gravité : haute** (verdict trompeur). Cette phrase est exactement la confirmation qu’un utilisateur cherche avant de supprimer l’autre dossier. Elle est fausse dans au moins quatre cas réalistes. Les notes affichées plus haut dans le dialogue (« 1 contenu(s) différent(s)… exclus », « 2 élément(s) avec un incident… », « 1 chemin(s) au nom que Windows modifierait… ») ne la corrigent pas, elles la contredisent.

**Fichiers :**
- `<dépôt>\FolderChecker.App\CopyDialog.cs:143-146` : la phrase dépend du seul `plan.Items.Count == 0` ;
- `<dépôt>\FolderChecker.Core\CopyEngine.cs:133-152` (`GatherRows`) : seules les lignes `OnlySource`/`OnlyTarget` peuvent manquer, jamais `Different`/`Conflict` ;
- `CopyEngine.cs:203` et `:212` (`AddItems`) : écarte en plus les éléments en ligne, sous un incident ou au nom non portable.

**Scénarios et clics :**
- **Version différente (`nothing`).**
  - Données : `rapport.docx` contient « version 1 » dans le 1 et « version 2, corrigée et plus longue » dans le 2.
  - Les deux verdicts disent « Non » et aucun bouton « Tout regrouper » n’apparaît.
  - L’utilisateur ouvre « Compléter les dossiers… » et choisit « Tout regrouper dans le dossier 1 » (ou 2) : « Rien à regrouper : ce dossier a déjà un exemplaire de chaque fichier. »
  - Aucun des deux dossiers ne contient la version de l’autre.
- **Incident (`blocked`).**
  - Données : `Dossier\unique.txt` n’existe que dans le 1. Un autre programme écrit dans `dst\Dossier` pendant le contrôle final, et la ligne `Dossier` passe en `Unverified`.
  - Avec le comportement nouveau (copie malgré les incidents), `unique.txt` est écarté.
  - « Tout regrouper dans le dossier 2 » affiche « Rien à regrouper : ce dossier a déjà un exemplaire de chaque fichier. », alors que le dossier 2 n’a aucun exemplaire de `unique.txt`.
- **Nom non portable (`names`).** Le fichier `notes.` (point final) n’existe que dans le 1 ; même phrase.
- **Fichier en ligne (preuve par lecture).**
  - Une ligne `OnlySource` avec `OnlineOnly = true` est écartée par `GatherRows` (`:146`) et `AddItems` (`:203`). Le plan est vide, et la même phrase s’affiche à côté de la note « N fichier(s) disponible(s) uniquement en ligne ne seront pas copiés ».
  - Non exécuté : il faudrait un fournisseur de fichiers à la demande.

**Preuve :** `powershell -NoProfile -File <scratch>\run.ps1 -Scenarios nothing,blocked,names`. Sortie exacte (extraits) :

```
===== nothing =====
  Identical       File      commun.txt
  Different       File      rapport.docx
…
--- Dialogue : IntoFolder1
  Mode sélectionné  : IntoFolder1 « Tout regrouper dans le dossier 1, sans recopier ce qu’il a déjà (2 → 1) »
  Récapitulatif     : Source : <scratch>\data\nothing\dst ⏎ Destination : <scratch>\data\nothing\src ⏎ 0 fichier(s) · 0 o · 0 dossier(s) ⏎ Rien à regrouper : ce dossier a déjà un exemplaire de chaque fichier.
  Écarts exclus     : 1 contenu(s) différent(s) ou conflit(s) exclus : aucun remplacement automatique. Ces écarts resteront à résoudre après la copie.
…
--- Dialogue : IntoFolder2
…
  Récapitulatif     : Source : <scratch>\data\nothing\src ⏎ Destination : <scratch>\data\nothing\dst ⏎ 0 fichier(s) · 0 o · 0 dossier(s) ⏎ Rien à regrouper : ce dossier a déjà un exemplaire de chaque fichier.
…
SHA-256 de dst\rapport.docx présent dans src ? False
SHA-256 de src\rapport.docx présent dans dst ? False

===== blocked =====
--- Comparaison <scratch>\data\blocked\src ↔ <scratch>\data\blocked\dst : Finished=True Errors=2 SnapshotVerified=False
  Identical       File      commun.txt
  Unverified      Directory Dossier
  Unverified      File      Dossier\ajout.tmp
  OnlySource      File      Dossier\unique.txt
CanCreatePlan = True ; GatherFileCount(into2) = 0
…
  Verdict droit     : Le dossier 2 contient tout le dossier 1 : Non
--- Dialogue : « Tout regrouper dans le dossier 2 » choisi dans « Compléter les dossiers… »
…
  Récapitulatif     : Source : <scratch>\data\blocked\src ⏎ Destination : <scratch>\data\blocked\dst ⏎ 0 fichier(s) · 0 o · 0 dossier(s) ⏎ Rien à regrouper : ce dossier a déjà un exemplaire de chaque fichier.
…
  Incidents         : 2 élément(s) avec un incident (illisible, modifié pendant la comparaison…) écarté(s), avec ce qu’ils contiennent : ils ne seront pas copiés. Consultez le filtre « Non vérifiés, liens, erreurs ».
…
Un exemplaire de Dossier\unique.txt existe-t-il dans dst ? False

===== names =====
…
  Récapitulatif     : Source : <scratch>\data\names\src ⏎ Destination : <scratch>\data\names\dst ⏎ 0 fichier(s) · 0 o · 0 dossier(s) ⏎ Rien à regrouper : ce dossier a déjà un exemplaire de chaque fichier. ⏎ 1 chemin(s) au nom que Windows modifierait (point ou espace final, nom réservé ou mal formé) écarté(s) : à copier à la main.
…
dst contient-il notes. ? False
```

**Correctif proposé :**
1. Ajouter dans le moteur `CopyEngine.GatherShortfall(ComparisonResult, bool intoTarget)`, sans accès disque. Elle compte ce qui reste sans exemplaire dans la destination une fois le plan exécuté :
   - les lignes `Different` et `Conflict` (la version de l’autre côté) ;
   - les lignes « manquantes » de `GatherRows` qui sont sous un incident (`IsBlocked`), en ligne, ou au nom non portable (`PortableName`) ;
   - les temporaires restent exclus volontairement.
2. Dans `CopyDialog`, ne garder « ce dossier a déjà un exemplaire de chaque fichier » que si `plan.Items.Count == 0 && shortfall.Total == 0`.
3. Sinon, écrire : « Rien ne peut être copié automatiquement, mais N fichier(s) n’ont encore aucun exemplaire dans ce dossier : X version(s) différente(s) au même chemin, Y sous un incident, Z en ligne uniquement, W au nom non portable. Ne supprimez pas l’autre dossier avant de les avoir traités. » Ajouter la même phrase quand le plan n’est pas vide.

**Test proposé** (`GatherNeverClaimsCompletenessWhileContentsAreMissing`) :
- avec les fixtures `nothing` (deux modes), `blocked` (même astuce d’écriture pendant le contrôle final, par un `IProgress` synchrone) et `names`, `SummaryText` ne contient jamais « a déjà un exemplaire de chaque fichier » et contient le nombre de fichiers restants ;
- côté moteur, `GatherShortfall` vaut 1 dans chacun de ces cas.

---

## REG-P03 — « Tout regrouper » contourne la reprise d’une copie inachevée : journal remplacé, avancement perdu, temporaire orphelin

**Gravité : moyenne.** Aucun fichier de l’utilisateur n’est écrasé, et le dialogue affiche bien « Une copie incomplète est enregistrée. Reprenez-la pour conserver son avancement ; lancer une autre copie remplacera son journal. » (`CopyDialog.cs:79`). Mais :
- le nouveau bouton, appel à l’action principal du verdict, inverse le choix sûr d’avant : « Reprendre » était présélectionné ;
- l’avancement est perdu (0 octet repris) ;
- le temporaire `.checkersan-….partial` reste pour toujours dans la destination. Il peut peser jusqu’à la taille du fichier interrompu, des dizaines de Gio pour une vidéo ou une image disque ;
- il faut davantage d’espace (64,0 Mio nécessaires contre 63,0 Mio en reprise) ;
- le verdict « Le dossier 1 contient tout le dossier 2 : Non » reste figé, sans bouton pour y remédier.

**Fichiers :**
- `<dépôt>\FolderChecker.App\CopyDialog.cs:154-156` : `initial` l’emporte sur `canResume` ;
- `<dépôt>\FolderChecker.App\MainWindow.Copy.cs:32-38` ;
- `<dépôt>\FolderChecker.App\MainWindow.xaml.cs:311-319` : le libellé du bouton ignore `_savedCopy` ;
- `<dépôt>\FolderChecker.Core\CopyEngine.cs:426`, `:824` et `:883` : `Journal.Open` puis `Compact` remplacent le journal par `File.Move(temporary, _path, true)`.

**Scénario :**
1. L’utilisateur regroupe dans le 2 (64 Mio, `gros.bin` et `a-petit.txt`), puis clique sur « Arrêter et garder » pendant `gros.bin`.
2. Comme l’invite le verdict, il relance la comparaison. La fenêtre montre « Compléter / reprendre… » et « Tout regrouper dans le 2 · 2 fichier(s) à copier… ».
3. Il clique sur « Tout regrouper dans le 2 ». Le dialogue s’ouvre sur `IntoFolder2`, et non sur « Reprendre la copie enregistrée ».
4. Il clique sur « Regrouper dans le dossier 2 ».

**Preuve :** `powershell -NoProfile -File <scratch>\run.ps1 -Scenarios resume`.
- La première copie est arrêtée par un `IProgress` synchrone qui annule dès que des octets de `gros.bin` sont écrits.
- La seconde exécute `CopyEngine.CopyAsync` avec le plan du dialogue et le même chemin de journal, ce que fait `MainWindow.Transfer`.

Sortie exacte (extraits de `<scratch>\out-resume.txt`) :

```
Première copie : interrompue=True, 0/2 fichier(s), 1 048 576 octets écrits
Journal enregistré : 2 élément(s) inachevé(s) : gros.bin → .checkersan-ff270a1c49e441c691bbee66ffb20103.partial, a-petit.txt → .checkersan-9026323810da49b2852defde0061b4a6.partial
--- Fichiers de dst après l’arrêt
  .checkersan-ff270a1c49e441c691bbee66ffb20103.partial  (1 048 576 octets)
--- Fenêtre principale : après nouvelle comparaison, avec une copie inachevée enregistrée
…
  GatherInto2Button : visible=True actif=True « Tout regrouper dans le 2 · 2 fichier(s) à copier… »
…
  TransferButton    : visible=True actif=True « Compléter / reprendre… »
--- Dialogue : ouvert par « Compléter / reprendre… » (initial = null)
…
  Mode sélectionné  : Resume « Reprendre la copie enregistrée »
  Récapitulatif     : … nécessaire : 63,0 Mio
…
--- Dialogue : ouvert par « Tout regrouper dans le 2 » (initial = IntoFolder2)
…
  Mode sélectionné  : IntoFolder2 « Tout regrouper dans le dossier 2, sans recopier ce qu’il a déjà (1 → 2) »
  Récapitulatif     : … nécessaire : 64,0 Mio
…
  Bouton principal  : actif=True « Regrouper dans le dossier 2 »
…
Regroupement lancé depuis le bouton : complet=True, 2/2, 67 108 877 octets écrits, 0 repris
Journal après : 2 élément(s) ; référence encore .checkersan-ff270a1c49e441c691bbee66ffb20103.partial ? False
--- Fichiers de dst après le regroupement
  .checkersan-ff270a1c49e441c691bbee66ffb20103.partial  (1 048 576 octets)
  a-petit.txt  (13 octets)
  gros.bin  (67 108 864 octets)
--- Comparaison <scratch>\data\resume\src ↔ <scratch>\data\resume\dst : Finished=True Errors=0 SnapshotVerified=True
  OnlyTarget      File      .checkersan-ff270a1c49e441c691bbee66ffb20103.partial
…
  Verdict gauche    : Le dossier 1 contient tout le dossier 2 : Non
  GatherInto1Button : visible=False actif=True « Tout regrouper dans le 1 · 0 fichier(s) à copier… »
```

Dans les extraits, les récapitulatifs sont abrégés par `…` avant « nécessaire » ; la ligne complète se trouve dans le fichier de sortie.

**Correctif proposé :**
1. Dans `CopyDialog`, si `canResume` et que le plan enregistré porte sur les racines de `result` (`SourceCanonical` et `TargetCanonical`), sélectionner « Reprendre » même si `initial` est donné, et ajouter : « Reprenez d’abord la copie inachevée de ces dossiers. »
2. Pour un autre couple de dossiers, garder `initial`, mais confirmer au clic sur `start` quand `canResume && choice.Mode != Mode.Resume` : « Remplacer la copie inachevée ? Son ou ses N fichier(s) temporaire(s) (X Mio) resteront dans … ».
3. Quand `_savedCopy` a des éléments inachevés, les boutons disent « Reprendre / regrouper… ».
4. Côté moteur, quand un journal est remplacé, retirer par handle les temporaires de l’ancien plan dont `PartialIdentity` correspond encore, ou au moins les lister dans le résultat.

**Test proposé :**
- `GatherButtonKeepsTheSavedCopyFirst` (App.Tests) : un plan inachevé sur les mêmes racines et `new CopyDialog(result, saved, Mode.IntoFolder2)` donnent `SelectedMode == Mode.Resume`.
- Test moteur : après remplacement du journal, aucun `.checkersan-*.partial` de l’ancien plan ne reste dans la destination.

---

## REG-P04 — Le bouton compte des fichiers que le plan écarte ensuite

**Gravité : basse** (message). Le bouton annonce « 1 fichier(s) à copier ». Le dialogue qu’il ouvre ne copie rien et ajoute la phrase fausse de REG-P02.

**Fichier :** `<dépôt>\FolderChecker.Core\CopyEngine.cs:108-114` (`GatherFileCount`). Le comptage ne refait pas le contrôle `PortableName` de `AddItems` (`:212`). Ce contrôle est pourtant purement textuel (`:670-677`) et ne coûte aucun accès disque.

**Scénario :** `notes.` (nom à point final) n’existe que dans le 1. L’utilisateur clique sur « Comparer », puis sur « Tout regrouper dans le 2 · 1 fichier(s) à copier… ».

**Preuve :** `powershell -NoProfile -File <scratch>\run.ps1 -Scenarios names`. Sortie exacte (extraits) :

```
  OnlySource      File      notes.
…
  GatherInto2Button : visible=True actif=True « Tout regrouper dans le 2 · 1 fichier(s) à copier… »
…
  Plan              : 0 élément(s), <scratch>\data\names\src → <scratch>\data\names\dst
GatherFileCount(into2) = 1 ; CreateGatherPlan(into2).FileCount = 0
```

**Correctif proposé :** dans `GatherFileCount`, ajouter `&& PortableName(intoTarget ? comparison.SourceRoot : comparison.TargetRoot, row.RelativePath.Replace('/', '\\'))`. Le plus sûr serait un seul filtre partagé par `GatherFileCount` et `AddItems`.

**Test proposé :** pour la fixture `names`, `GatherFileCount(result, true) == CreateGatherPlan(result, true).FileCount`, et `GatherInto2Button` reste masqué (ou annonce des fichiers « à copier à la main »).

---

## REG-P05 — Libellé, infobulle, aide FR/EN et message de fin promettent plus que le regroupement ne fait

**Gravité : moyenne** (garantie non tenue dans un cas réaliste). L’infobulle et l’aide définissent ce que garantit « Tout regrouper ». Plusieurs de ces promesses sont démenties par l’exécution.

**Affirmations, emplacements et preuves :**
- **« Copie les fichiers dont ce dossier n’a aucun exemplaire, même ailleurs. »**
  - Emplacements : infobulle `Main_GatherTip` (`<dépôt>\FolderChecker.App\Strings.Main.cs:58`) ; aide FR (`MainWindow.xaml.cs:468`) et EN (`:473`).
  - Fausse dans deux cas :
    - une version différente au même chemin n’est jamais copiée (`nothing`, voir REG-P02) ;
    - quand la recherche des fichiers déplacés est désactivée, un fichier que le dossier a déjà ailleurs est recopié (`nomoved` : `Tri\photo.jpg` est recopié alors que le dossier 1 a `photo.jpg`).
- **« … sans recopier ce qu’il a déjà ».**
  - Emplacement : libellé des modes (`CopyDialog.cs:51-52`).
  - Il reste affiché quand `MovedFilesSearched == false` ; seule la note du mode corrige (`nomoved`).
- **« à côté de chaque verdict « Non » ».**
  - Emplacement : aide FR et EN.
  - « Non » apparaît sans bouton (`nothing`). Après un regroupement complet, le verdict reste « Non » sans bouton ni explication, car les copies sont à d’autres chemins (`after`) : l’utilisateur n’a aucune confirmation que le regroupement est complet.
- **« Si la plupart des fichiers d’un dossier se trouvent dans un sous-dossier de l’autre ».**
  - Emplacement : aide FR et EN.
  - La suggestion se déclenche pour 3 fichiers sur 23 (`align`, voir REG-P01).
- **« Les chemins manquants ont été copiés et vérifiés par SHA-256. »**
  - Emplacement : message de fin de copie (`MainWindow.Copy.cs:83`), commun à tous les modes.
  - Après un regroupement, ce n’est pas le cas : un chemin manquant dont le contenu existe ailleurs n’est pas copié, par construction. Preuve par lecture : `Transfer` calcule `gathering` (`:48`) mais ne s’en sert que pour le titre (`:60-61`).
- **« Seuls les chemins absents seront complétés. »**
  - Emplacement : `ExclusionsText`, statique (`CopyDialog.cs:61-65`).
  - Ce texte s’affiche aussi en mode regroupement (sorties `nothing`, `after`), comme le titre « Compléter les dossiers ».

**Preuve :** `powershell -NoProfile -File <scratch>\run.ps1 -Scenarios nomoved,after` (et `nothing`, `align` ci-dessus). Sortie exacte (extraits) :

```
===== nomoved =====
  OnlyTarget      File      chanson.mp3
  OnlySource      File      photo.jpg
  OnlyTarget      Directory Tri
  OnlyTarget      File      Tri\photo.jpg
…
  GatherInto1Button : visible=True actif=True « Tout regrouper dans le 1 · 2 fichier(s) à copier… »
                      infobulle « Copie les fichiers dont ce dossier n’a aucun exemplaire, même ailleurs. Rien n’est remplacé ni supprimé ; un récapitulatif s’affiche avant la copie. »
…
  Mode sélectionné  : IntoFolder1 « Tout regrouper dans le dossier 1, sans recopier ce qu’il a déjà (2 → 1) »
…
  Plan              : 3 élément(s), <scratch>\data\nomoved\dst → <scratch>\data\nomoved\src
      Directory Tri
      File chanson.mp3
      File Tri\photo.jpg

===== after =====
Regroupement dans le 1 : complet=True, 1/1
…
  Moved           File      photo.jpg
  OnlyTarget      Directory Tri
  OnlyTarget      File      Tri\photo.jpg  [copie dans 1 : photo.jpg]
…
  VerdictTitle      : Aucun dossier ne contient entièrement l’autre
  VerdictDetail     : Des éléments manquent au même chemin ou ont un contenu différent. Consultez « Tous les écarts » : les fichiers retrouvés ailleurs restent distincts des correspondances au même chemin.
  Verdict gauche    : Le dossier 1 contient tout le dossier 2 : Non
  GatherInto1Button : visible=False actif=True « Tout regrouper dans le 1 · 0 fichier(s) à copier… »
```

**Correctif proposé :**
1. **Infobulle et aide.** Écrire : « Copie les fichiers dont le contenu manque dans ce dossier. Si la recherche des fichiers déplacés était cochée, un contenu présent ailleurs (SHA-256) n’est pas recopié. Les versions différentes au même chemin, les éléments en incident, en ligne ou au nom non portable ne sont pas copiés. » Dans l’aide, remplacer « à côté de chaque verdict « Non » » par « à côté d’un verdict « Non », quand des fichiers peuvent être copiés ».
2. **Libellés des modes.** Retirer « sans recopier ce qu’il a déjà » quand `result.MovedFilesSearched` est faux. Reconstruire `ExclusionsText` et le titre selon le mode, dans `SelectionChanged`.
3. **Confirmation après regroupement.** Dans `ShowResult`, quand un verdict est « Non » mais que `GatherFileCount == 0` et que `GatherShortfall` (REG-P02) est nul, ajouter : « Le dossier 1 a un exemplaire de chaque fichier du dossier 2, parfois à un autre chemin. » Le sens 1 → 2 peut s’appuyer sur `ContentCovered`.
4. **Message de fin.** Pour un regroupement : « Les contenus absents ont été copiés et vérifiés par SHA-256 ; ceux déjà présents ailleurs n’ont pas été recopiés. »

**Test proposé :**
- Fixture `nomoved` : le libellé `IntoFolder1` ne contient pas « sans recopier ».
- Fixture `after` : `VerdictDetail` contient la confirmation.
- Un test de chaînes vérifie que l’aide FR/EN ne contient plus « à côté de chaque verdict » ni « la plupart des fichiers d’un dossier » sans seuil correspondant.

---

## REG-P06 — L’écran affiche « Non », le rapport exporté « Non vérifié »

**Gravité : basse** (message). Le détail du verdict invite à « consulter … le rapport avant de décider de supprimer une copie », et ce rapport contredit l’écran. L’écart est prudent (« Non vérifié » est plus faible), mais l’utilisateur reçoit deux réponses à la même question.

**Fichiers :**
- `<dépôt>\FolderChecker.App\MainWindow.xaml.cs:290-296` : nouvelle logique à trois états, côté écran seulement ;
- `<dépôt>\FolderChecker.App\ReportWriter.cs:39-41` : l’ancienne logique est conservée pour le CSV.

**Scénario :**
- Données : `gauche.txt` seulement dans le 1, `droite.txt` seulement dans le 2, `verrou.txt` verrouillé pendant la comparaison.
- L’utilisateur clique sur « Comparer » puis sur « Exporter… ».

**Preuve :** `powershell -NoProfile -File <scratch>\run.ps1 -Scenarios report`. Sortie exacte (extraits) :

```
  Verdict gauche    : Le dossier 1 contient tout le dossier 2 : Non
…
  Verdict droit     : Le dossier 2 contient tout le dossier 1 : Non
Rapport CSV exporté · Dossier 2 contient tout le dossier 1 : Non vérifié
Rapport CSV exporté · Dossier 1 contient tout le dossier 2 : Non vérifié
```

**Correctif proposé :**
- Placer la règle dans le moteur, par exemple `ComparisonVerdicts.Containment(ComparisonResult, bool folder2HoldsFolder1) : bool?`.
- L’utiliser dans `ShowContainmentVerdicts`, dans `ReportWriter` (CSV) et dans le CLI ; ajouter deux champs explicites au rapport JSON.

**Test proposé :** pour la fixture `report`, les deux colonnes de verdict du CSV sont égales au texte de l’écran (« Non »).

---

## REG-P07 — Ligne « Absent du dossier 1 » : un autre fichier présenté comme « 1 : … », SHA-256 « Non calculé »

**Gravité : basse** (message). Pour une ligne `OnlyTarget` qui a une copie ailleurs dans le 1, le panneau affiche « 1 : …\photo.jpg | 2 : …\Tri\photo.jpg », comme s’il s’agissait du même chemin. L’infobulle dit en outre « SHA-256 (1) : Non calculé », alors que la copie du 1 a justement été appariée par SHA-256 complet. Le statut de la ligne et sa phrase de détail rectifient en partie.

**Fichiers :** `<dépôt>\FolderChecker.App\MainWindow.xaml.cs:414` (`SelectedPaths`) et `:420` (`Details`).

**Scénario :** après un regroupement dans le 1, l’utilisateur relance la comparaison et sélectionne la ligne `Tri\photo.jpg`.

**Preuve :** `powershell -NoProfile -File <scratch>\run.ps1 -Scenarios after`. Sortie exacte (extrait) :

```
--- Ligne sélectionnée : Absent du dossier 1 · Tri\photo.jpg
  Chemins affichés  : 1 : <scratch>\data\after\src\photo.jpg     |     2 : <scratch>\data\after\dst\Tri\photo.jpg
  Infobulle         : Absent du dossier 1 — Fichier ⏎ Tri\photo.jpg ⏎ Ce chemin existe uniquement dans le dossier 2. Même contenu SHA-256 présent ailleurs dans le dossier 1 : photo.jpg ⏎ Dossier 1 : <scratch>\data\after\src\photo.jpg ⏎ Dossier 2 : <scratch>\data\after\dst\Tri\photo.jpg ⏎ SHA-256 (1) : Non calculé ⏎ SHA-256 (2) : 6105D6CC76AF400325E94D588CE511BE5BFDBB73B437DC51ECA43917D7A43E3D
```

**Correctif proposé :** quand `SourcePath` est nul et `SourceCopyPath` renseigné :
- afficher « 1 (même contenu, autre chemin) : … » ;
- dans `Details`, écrire « Copie dans le dossier 1 : … » et « SHA-256 (copie dans 1) : {TargetHash} ».

**Test proposé :** pour la fixture `after`, `SelectedPaths.Text` contient « autre chemin », et l’infobulle ne contient pas « SHA-256 (1) : Non calculé ».

---

## Points vérifiés sans défaut

Ces vérifications ont été exécutées ; elles serviront à l’équipe bleue.

- **« Comparer avec ce dossier ».**
  - La bonne case est remplie dans les deux sens : `InFolder2` donne la case 2 (`align`), l’autre sens la case 1 (`align1`).
  - Juste après, `_result` vaut `null`, les verdicts disent « Non vérifié », les deux boutons sont masqués et inactifs, et « Compléter les dossiers… » est inactif. Aucun verdict périmé ne reste affiché.
  - Le texte d’alignement n’inverse pas dossier 1 et dossier 2.
- **Correspondance des modes.** `IntoFolder1` copie bien du 2 vers le 1 (Source `dst` → Destination `src`) et `IntoFolder2` l’inverse (`nothing`).
- **Choix par défaut.** « Compléter / reprendre… » présélectionne toujours « Reprendre la copie enregistrée » (`resume`).
- **Changement de langue.** Avec un résultat affiché, les boutons, l’infobulle et les verdicts passent en anglais sans changer de visibilité (`lang`).
- **Opération en cours.** `SetBusy(true)` masque et désactive les deux boutons (`lang`, et le test existant).
- **Mise en page.** Les lignes de verdict ne sont pas tronquées à 920, 1 000 et 1 260 px, en FR et en EN (`layout`). Le retrait de `TextWrapping` sur ces lignes est donc sans effet visible à la largeur minimale.
- **Cohérence bouton-verdict.** Un bouton visible est toujours à côté d’un « Non », vérifié par lecture : `GatherFileCount > 0` exige `CanCreatePlan`, donc `Finished`, et une ligne `OnlySource` ou `OnlyTarget`, ce qui donne « Non » à la ligne `MainWindow.xaml.cs:293-296`.
- **« Non » malgré des incidents.** Aucun faux « Non » trouvé : sous un dossier illisible de l’autre côté, le moteur donne `Unverified` et non `OnlySource` (`ComparisonEngine.cs`, `AncestorProblem`). Un élément modifié pendant le contrôle final passe aussi en `Unverified` (`VerifySnapshot`, `MarkUnverified`, y compris par `SourceCopyPath`).

## Pistes non prouvées

1. **Incident côté destination et recopie.**
   - Si le dossier 1 contient un fichier illisible (verrouillé) dont le contenu est identique à un fichier présent seulement dans le dossier 2, ce dernier n’a pas de `SourceCopyPath`. `FindTargetCopiesAsync` n’indexe que les sources hachées.
   - « Tout regrouper dans le 1 » le recopierait, malgré « sans recopier ce qu’il a déjà ».
   - Ce scénario n’a pas été exécuté.
2. **Double clic sur « Tout regrouper ».**
   - Le dialogue modal, centré sur la fenêtre, peut apparaître sous le pointeur avant la fin du double clic. Le second clic tomberait alors sur la liste des modes ou sur « Comparer avec ce dossier », ce qui change une case.
   - Il ne peut pas lancer la copie, puisque le bouton principal est en bas à droite.
   - Non reproduit : il faudrait une vraie souris.
3. **Dossier vide sous un dossier manquant non créé.**
   - `GatherRows` (`CopyEngine.cs:133-152`) retient un sous-dossier vide `D\E` manquant, mais pas son parent `D`, qui ne contient que des fichiers retrouvés ailleurs.
   - `EnsureDirectory` crée alors `D` implicitement, sans ses dates ni ses attributs, contrairement au commentaire de `GatherRows`.
   - Non exécuté.
4. **Copie modifiée pendant le scan.**
   - `MarkUnverified` conserve `SourceCopyPath` sur une ligne devenue `Unverified`. « 1 : … » et « Ouvrir dans le 1 » désignent alors une copie qui a changé pendant le scan.
   - Constaté par lecture, non exécuté.
5. **Reprise d’un regroupement d’autres dossiers.**
   - Un journal de regroupement dans le 1 a `SourceRoot` = ancien dossier 2. Si on le reprend alors que d’autres dossiers sont affichés, `Transfer` remplit la case 1 avec l’ancien dossier 2 (`MainWindow.Copy.cs:56-57`), ce qui inverse les rôles des verdicts suivants.
   - Le comportement existait déjà pour « Compléter le dossier 1 » (2 → 1) ; il s’étend au regroupement.
   - Non exécuté.

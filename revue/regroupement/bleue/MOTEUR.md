# Revue adverse — équipe bleue — moteur de « Regrouper dans le dossier 1 ou 2 »

Objet : tenter de réfuter les trouvailles REG-M01 à REG-M09 de `revue\regroupement\rouge\MOTEUR.md`, examiner ses pistes non prouvées et juger les correctifs proposés.

Conventions : `<dépôt>` est la racine du dépôt, `<scratch>` le dossier de travail bleu, `<work>` le dossier de données d’un scénario (sous `<scratch>\work`), `<SID>` un SID d’utilisateur réel masqué. Les sorties citées sont exactes, à ce masquage près.

## Banc

- `<scratch>\bench` : le banc rouge copié tel quel (`Rouge.cs`), plus les scénarios bleus (`Bleue.cs`). Il référence la DLL compilée `<dépôt>\FolderChecker.Core\bin\Release\net10.0\FolderChecker.Core.dll`, dont le SHA-256 commence par `3CFBFC4E3C25`.
- `<scratch>\old` : `git archive b5399b7 FolderChecker.Core`, compilé dans `<scratch>\old\out`. Le SHA-256 de cette DLL commence par `C7ACE42812F2`.
- `<scratch>\benchold` : les scénarios rejoués avec le moteur de `b5399b7`.
- `<scratch>\perf` et `<scratch>\perfold` : le même programme de mesure, compilé une fois contre chaque DLL.
- Lancement : `powershell -File <scratch>\run.ps1 -Scenario <nom> [-Bench benchold]`. La sortie est copiée dans `<scratch>\out-<nom>.txt` ; les sorties du moteur ancien sont renommées `out-old-<nom>.txt`.
- CLI de `dist` : `<scratch>\cli.ps1`. Il passe toujours `--log <scratch>\activity.log`.
- Garde-fous respectés :
  - aucun projet du dépôt modifié ni recompilé ;
  - aucune commande git qui modifie l’état ;
  - rien d’écrit hors de `<scratch>`, à part ce rapport ;
  - `G:` jamais touché ;
  - sur `C:`, seulement des listes de métadonnées. Le contenu d’aucun fichier de l’utilisateur n’a été lu.

## Synthèse

| Id | Rouge | Verdict bleu | Gravité retenue | Nouveau par rapport à `b5399b7` ? |
|---|---|---|---|---|
| REG-M01 | critique | **confirmée** (réel sur une vraie racine NTFS) | **critique** pour « regrouper dans le 2 » ; basse pour la copie de la Corbeille vers le 1 | Oui : renoncer à copier est nouveau, et une racine NTFS n’était pas copiable avant le relâchement |
| REG-M02 | haute | **partiellement réfutée** | **moyenne** | Oui |
| REG-M03 | moyenne | **confirmée**, portée plus large que décrite | **moyenne** | Oui |
| REG-M04 | moyenne | **partiellement réfutée** | **basse** | En partie : le silence sur les liens existait déjà |
| REG-M05 | moyenne | **partiellement réfutée** | **basse** | Oui |
| REG-M06 | basse | **confirmée** | **basse** | Oui, comme extension d’une confiance existante |
| REG-M07 | basse | **partiellement réfutée** (pas nouvelle) | **basse** | **Non** : `b5399b7` lève la même exception |
| REG-M08 | basse | **confirmée** | **basse** | Oui |
| REG-M09 | basse | **partiellement réfutée** | **basse** | En partie |
| Piste 1 (point final) | non prouvée | **confirmée**, contredit un « contrôle sans trouvaille » rouge | basse | Exposition nouvelle (relâchement) |
| Piste 2 (vraie Corbeille) | non prouvée | **confirmée** (voir REG-M01) | — | — |
| Piste 3 (quarantaine) | non prouvée | **confirmée** | moyenne | Oui |
| Piste 4 (`MarkUnverified`) | non prouvée | **écartée** : défaut préexistant, indépendant du relâchement | — | Non |
| Piste 5 (sans `FileIdInfo`) | non prouvée | **écartée** : limite préexistante, héritée telle quelle | — | Non |

---

## REG-M01 — La Corbeille d’une racine de disque compte comme exemplaire, et est regroupée comme du contenu

**Verdict : confirmée. Gravité retenue : critique** pour le sens « regrouper dans le 2 ». La copie de la Corbeille *vers* le 1 ne perd rien, mais encombre : gravité basse pour ce volet.

### Réalité sur une vraie racine NTFS

On a vérifié en lecture seule, sur `C:`, la simulation rouge (un dossier ordinaire nommé `$RECYCLE.BIN`). La liste a été faite avec les options d’inventaire exactes du moteur (`AttributesToSkip = 0`, `IgnoreInaccessible = false`), sans lire aucun contenu.

Commande : `run.ps1 -Scenario realbin`, sortie dans `<scratch>\out-realbin.txt` :

```
Volume C: type=Fixed format=NTFS
GetVolumePathNameW(C:\) = C:\
Racine C:\ listée avec les options du moteur (AttributesToSkip=0) : 34 entrée(s), dont :
  « $Recycle.Bin » attributs=Hidden, System, Directory
  « System Volume Information » attributs=Hidden, System, Directory

Enfants de C:\$Recycle.Bin (mêmes options) :
  S-1-5-18                           attributs=Hidden, System, Directory → UnauthorizedAccessException (HResult 0x80070005)
  <SID de l’utilisateur courant>     attributs=Hidden, System, Directory → listé : 24 entrée(s) : $I fichiers=16, $R fichiers=5, $R dossiers=0, desktop.ini=1 (Hidden, System, Archive), attributs des $R : [Archive], entrées des 3 premiers dossiers $R : []
  <autre SID n°1>                    attributs=Hidden, System, Directory → UnauthorizedAccessException (HResult 0x80070005)
  <autre SID n°2>                    attributs=Hidden, System, Directory → UnauthorizedAccessException (HResult 0x80070005)

C:\System Volume Information → UnauthorizedAccessException (HResult 0x80070005)

ACL de C:\$Recycle.Bin (icacls, SID masqués) :
  C:\$Recycle.Bin BUILTIN\Administrators:(F)
                  BUILTIN\Administrators:(OI)(CI)(IO)(F)
                  NT AUTHORITY\SYSTEM:(F)
                  NT AUTHORITY\SYSTEM:(OI)(CI)(IO)(F)
                  BUILTIN\Users:(RX,AD,WA)
                  Mandatory Label\Low Mandatory Level:(OI)(CI)(IO)(NW)
ACL du dossier SID de l'utilisateur courant (icacls, SID masqués) :
  C:\$Recycle.Bin\<SID> BUILTIN\Administrators:(OI)(CI)(F)
                        NT AUTHORITY\SYSTEM:(OI)(CI)(F)
                        <machine>\<utilisateur>:(OI)(CI)(F)
```

Ce que Windows expose réellement :
- `$Recycle.Bin` est caché et système, et lisible par `Users` (RX).
- Le dossier `S-…` de l’utilisateur courant lui donne le contrôle total ; il est listé sans erreur, avec ses `$R…`, `$I…` et `desktop.ini` (caché et système).
- Les dossiers des autres SID, comme `System Volume Information`, sont refusés (0x80070005).

Le **vrai moteur** a ensuite été lancé sur la **vraie** Corbeille, face à un dossier vide, avec `FindMovedFiles = false`. Le dossier 2 étant vide, aucune paire ni aucun candidat n’existe, et aucun contenu n’est lu.

Commande : `run.ps1 -Scenario realbin-engine`, sortie dans `<scratch>\out-realbin-engine.txt` :

```
Finished=True Cancelled=False SnapshotVerified=False erreurs=3 CanCreatePlan=True
  autre (S-1-5-18)         Unverified   Directory : 1 ligne(s), en ligne seulement : 0
  autre SID utilisateur    Unverified   Directory : 2 ligne(s), en ligne seulement : 0
  SID courant              OnlySource   Directory : 1 ligne(s), en ligne seulement : 0
  SID courant              OnlySource   File      : 24 ligne(s), en ligne seulement : 0
  erreur : « C:\$Recycle.Bin\<SID> » : Dossier non entièrement lisible : Access to the path 'C:\$Recycle.Bin\<SID>' is denied.
  erreur : « C:\$Recycle.Bin\<SID> » : Dossier non entièrement lisible : Access to the path 'C:\$Recycle.Bin\<SID>' is denied.
  erreur : « C:\$Recycle.Bin\S-1-5-18 » : Dossier non entièrement lisible : Access to the path 'C:\$Recycle.Bin\S-1-5-18' is denied.
GatherFileCount(dossier 1 → dossier 2) = 24 fichier(s) de la Corbeille regroupables comme du contenu
```

Les 24 fichiers de la Corbeille de l’utilisateur sont des lignes `OnlySource` sans incident. Ils sont donc `Readable` (`ComparisonEngine.cs:1215-1216`), c’est-à-dire candidats de `FindMovedFilesAsync` et entrées de `byHash`. Les dossiers refusés produisent des erreurs, et le nouveau `CanCreatePlan` les accepte.

La simulation rouge reproduit donc fidèlement la structure réelle. Sur une racine NTFS, les dossiers des autres SID et `System Volume Information` ajoutent des lignes `Unverified` qui se bloquent elles-mêmes, sans rien changer au dossier de l’utilisateur. Le scénario rouge `recycle`, rejoué (`<scratch>\out-recycle.txt`), donne exactement la sortie rouge (5 lignes `Moved` vers `$RECYCLE.BIN\…`, `GatherFileCount(into2)=0`, suggestion `FolderAlignment` dans `$RX7Q2LM`).

Non vérifié ici, faute de volume FAT ou exFAT sans toucher `G:` : sur une telle racine, sans ACL, `System Volume Information` et tous les dossiers `S-…` seraient lisibles.

### Nouveauté

Même scan réel avec le moteur de `b5399b7` : `run.ps1 -Scenario realbin-engine -Bench benchold`, sortie dans `<scratch>\out-old-realbin-engine.txt` :

```
Cancelled=False SnapshotVerified=False erreurs=3 CanCreatePlan=False
CreatePlan refusé : Terminez une comparaison sans erreur avant de préparer une copie.
```

Sur une racine sans erreur, le cas d’une racine FAT ou exFAT, l’ancien moteur ne renonçait à aucune copie. Commande : `-Scenario recycle -Bench benchold`, sortie dans `<scratch>\out-old-recycle.txt` :

```
erreurs=0 CanCreatePlan=True déplacés=5 ContentCovered=True
  plan compléter le 2 (1 → 2) : 5 fichier(s), 3 dossier(s)
  plan compléter le 1 (2 → 1) : 7 fichier(s), 6 dossier(s)
    COPIERAIT [Directory] $RECYCLE.BIN
    …
```

Conclusions :
- La perte dans le sens « vers le 2 » est **entièrement nouvelle**. Elle vient du regroupement, qui renonce à copier, combiné au relâchement, qui rend une racine NTFS copiable.
- La copie de la Corbeille **vers le 1** existait déjà pour « Compléter le 1 » sur une racine FAT ou exFAT. Le relâchement l’étend aux racines NTFS.
- Le verdict `ContentCovered = True` qui s’appuie sur la Corbeille est un défaut d’affichage préexistant du même ordre.

### Gravité

« Critique » est justifié :
- c’est la configuration réelle de l’utilisateur (`G:\`, disque USB fixe, donc Corbeille active) ;
- l’échec est silencieux : la seule trace est une phrase de `Detail` parmi environ 41 000 lignes `Moved` ;
- la perte est différée et quasi certaine dès que l’utilisateur libère le dossier 1, car la Corbeille finit par être vidée (à la main, par le quota de taille du volume, ou par l’Assistant stockage selon sa configuration).

Aucune réfutation possible sur la réalité.

### Correctif

Le principe rouge est juste, mais il faut l’ajuster sur quatre points.
1. **Racine de volume.** La formule `GetVolumePathNameW(root) == root + "\"` est fausse pour une lettre de lecteur : `NormalizeRoot("G:\")` rend `G:\`, qui finit déjà par `\` (`ComparisonEngine.cs:1233`). Il faut comparer `Path.TrimEndingDirectorySeparator` des deux côtés, ou ajouter `\` seulement en son absence.
2. **Noms à écarter.** Ajouter `.Trashes` (macOS) et `.Trash-*` (Linux) aux noms écartés à la racine. Ce sont aussi des corbeilles, fréquentes sur les disques exFAT partagés.
3. **Écarter plutôt que `Skipped`.** Mieux vaut écarter ces dossiers de l’inventaire à la racine, avec un avertissement qui les nomme, que les marquer `Skipped` :
   - les lignes `Skipped` sont décrites comme « liens ou jonctions non suivis » (`MainWindow.xaml.cs:355` et la ligne « liens non suivis » du CLI), ce qui serait faux ;
   - les écarter supprime aussi les erreurs parasites des autres SID et de `System Volume Information`, qui comptent sans doute parmi les 12 incidents du scan réel.
4. **Un seul prédicat.** Défense en profondeur pour tout dossier, pas seulement les racines : un même prédicat « jamais une preuve de présence » pour `candidatesBySize` (`ComparisonEngine.cs:748-752`), `bySize`/`byHash` (834-840) et `FolderAlignment.Suggest`. Il couvre :
   - `CopyEngine.IsCopyTemporary(nom)` (REG-M02) ;
   - `DuplicateRemoval.IsInQuarantine(chemin relatif)` (piste 3) ;
   - les dossiers de corbeille ci-dessus.

   Ces fichiers restent des lignes visibles ; ils ne prouvent simplement plus rien.

Le test proposé (`GatherIgnoresRecycleBinAndQuarantine`) est adapté. Il faut y ajouter un cas `G:\`-like, où la racine finit déjà par `\`.

---

## REG-M02 — Un temporaire `.partial` complet compte comme exemplaire

**Verdict : partiellement réfutée. Gravité retenue : moyenne** (rouge : haute).

**Réalité : confirmée.** Le scénario rouge, rejoué, donne la même sortie (`<scratch>\out-partial-into2.txt`) :

```
  ligne Moved           File      Videos\vacances.mp4  -> 2:<work>\dossier2\Videos\.checkersan-0a17ca649ffe4eb780a203424ab847e6.partial
    vacances.mp4 dans le plan de regroupement : False
[4] regroupement lancé : Complete=True terminés=1/1
    le journal de reprise contient encore vacances.mp4 : False
[5] comparaison suivante : GatherFileCount(into2)=0 ; plan regrouper dans le 2 : 0 élément(s)
    Videos\vacances.mp4 existe dans le 2 : False
```

Le sens inverse se reproduit aussi (`<scratch>\out-partial-into1.txt`, `GatherFileCount(into1)=0`).

**Ce qui réduit la gravité :**
1. **La reprise règle le cas.** Scénario `partial-resume`, sortie dans `<scratch>\out-partial-resume.txt` :
   ```
   copie arrêtée : Cancelled=True ; phases vues : Élément terminé > Copie > Vérification SHA-256 de la copie
   journal : reprise possible (élément non terminé) = True
   reprise : Complete=True ; Videos\vacances.mp4 existe dans le 2 : True ; temporaires restants : 0
   ```
2. **Le dialogue avertit**, même ouvert sur le mode regroupement (preuve par lecture : `CopyDialog.cs:79`). Le texte : « Une copie incomplète est enregistrée. Reprenez-la pour conserver son avancement ; lancer une autre copie remplacera son journal. »
3. **La fenêtre est étroite.** Seul un temporaire **complet** est apparié, c’est-à-dire un arrêt pendant la relecture SHA-256 ou entre celle-ci et le renommage. Un arrêt pendant l’écriture laisse un temporaire plus court, que la taille écarte.
4. **La portée est d’un seul fichier par interruption**, car la copie est séquentielle.

**Ce qui maintient la trouvaille :**
- le chemin de perte reste réaliste : lancer un nouveau regroupement malgré l’avertissement, ou faire entre-temps une copie sur une autre paire, qui remplace l’unique journal (`PASSATION.md`, « une seule session de copie est proposée à la reprise ») ;
- l’application qualifie elle-même ce temporaire d’« interrompu… jamais recopié » (`ComparisonEngine.cs:662-664`), ce qui invite à le supprimer.

**Nouveauté : oui.** Dans `b5399b7`, « Compléter » copiait les lignes `Moved` (`MissingRows`), donc le fichier arrivait quand même.

**Correctif :** le correctif rouge est juste et complet. Il faut filtrer les temporaires à la fois :
- dans les **candidats** cibles de `FindMovedFilesAsync` ;
- dans `byHash` **côté source** de `FindTargetCopiesAsync`. C’est le cas `partial-into1` : le temporaire est dans le 1.

Il faut l’intégrer au prédicat unique de REG-M01. La défense en profondeur dans `GatherRows` est bienvenue.

---

## REG-M03 — Fichiers vides appariés par contenu

**Verdict : confirmée, portée plus large que décrite. Gravité retenue : moyenne.**

**Réalité :** le scénario rouge `empty`, rejoué, donne la même sortie (`<scratch>\out-empty.txt`) : quatre `Moved` vers `divers\vide.txt`, plan vide. Le défaut ne se limite pas à 0 octet.

Scénario `empty-tiny`, sortie dans `<scratch>\out-empty-tiny.txt`. Les fichiers font 1 octet (`"1"`) et 4 octets (`"8080"`), sans aucun lien entre eux :

```
  ligne Moved           File      config\actif.flag  -> 2:<work>\dossier2\divers\compteur.txt
  ligne Moved           File      config\port.txt  -> 2:<work>\dossier2\divers\autre.txt
  plan regrouper dans le 2 : 0 fichier(s), 0 dossier(s), UnsupportedNames=0, AbandonedTemporaries=0
```

Pour un très petit fichier, l’égalité de contenu est souvent une coïncidence (`1`, `true`, `{}`, un numéro de port), et c’est le nom qui porte l’information.

**Nouveauté : oui.** « Compléter » recopiait ces fichiers à leur chemin.

**Correctif :** le seuil `Size == 0` proposé par la rouge ferme le cas extrême, mais pas `empty-tiny`. Par ailleurs, le placer dans le moteur (`FindMovedFilesAsync`/`FindTargetCopiesAsync`) change les verdicts `Moved` et `ContentCovered` pour tous les modes.

Mieux vaut placer un seuil **dans `GatherRows` seulement** : un fichier sous *N* octets est « manquant » même si son contenu existe ailleurs.

```csharp
// CopyEngine.GatherRows : un très petit fichier ne vaut que par son nom ; le copier coûte au plus un cluster.
const long NameOnlyBelow = 4096;
bool Lacking(ComparisonRow row) => intoTarget
    ? row.Status == ComparisonStatus.OnlySource || row is { Status: ComparisonStatus.Moved, SourceSize: < NameOnlyBelow }
    : row is { Status: ComparisonStatus.OnlyTarget } && (row.SourceCopyPath is null || row.TargetSize < NameOnlyBelow);
```

Effets :
- `GatherFileCount` et la création des dossiers suivent ce seuil, puisqu’ils passent tous deux par `Lacking`. Les dossiers du scénario `empty` sont donc créés.
- La comparaison et « Compléter » restent inchangés.

Test : celui de la rouge (`GatherKeepsEmptyFiles`), plus le cas `empty-tiny`.

---

## REG-M04 — Exclusions muettes, message « déjà un exemplaire de chaque fichier », `GatherFileCount` ≠ plan

**Verdict : partiellement réfutée. Gravité retenue : basse** (rouge : moyenne).

**Ce qui est réfuté (preuve par lecture) :**
- **Les écartés sous un incident (`IsBlocked`) sont annoncés.** Le dialogue affiche « N élément(s) avec un incident (illisible, modifié pendant la comparaison…) écarté(s), avec ce qu’ils contiennent » dès qu’une ligne `Unverified` existe (`CopyDialog.cs:67-73`). L’ancêtre bloquant y est compté, avec la mention de son contenu.
- **Les noms non portables sont annoncés.** Le dialogue ajoute la ligne `UnsupportedNames` juste après le message de plan vide (`CopyDialog.cs:147-148`).

**Ce qui est confirmé :** scénario `silent-dialog`, qui recalcule les textes du dialogue avec ses propres règles. Sortie dans `<scratch>\out-silent-dialog.txt` :

```
bouton : « Tout regrouper dans le 2 · 1 fichier(s) à copier… »
note incidents du dialogue affichée : False (0) ; conflits : 0 ; lignes Skipped (aucune note dans le dialogue) : 4
fenêtre principale (MainWindow.xaml.cs:355) : « 4 élément(s) concerné(s) par des liens ou jonctions non suivis, exclus du verdict. »
résumé du dialogue (fin) :
  | Rien à regrouper : ce dossier a déjà un exemplaire de chaque fichier.
  | 1 chemin(s) au nom que Windows modifierait (point ou espace final, nom réservé ou mal formé) écarté(s) : à copier à la main.
```

- Le bouton annonce 1 fichier pour un plan vide : `GatherFileCount` n’applique pas `PortableName` (`CopyEngine.cs:108-114` contre 212). C’est cosmétique.
- Les fichiers réels face à une jonction ne sont mentionnés que dans la fenêtre principale. Ce silence du dialogue existait déjà pour « Compléter » dans `b5399b7`, où les lignes `Skipped` n’étaient ni copiées ni signalées.
- Le message affirmatif « déjà un exemplaire de chaque fichier » est nouveau. Il est contradictoire avec la ligne qui le suit.

**Gravité basse :**
- les jonctions face à de vrais dossiers sont rares sur un disque USB ;
- la fenêtre principale les signale ;
- le seul vrai trou silencieux est celui de la piste 1, plus bas.

**Correctif, plus léger que celui de la rouge :**
1. Un prédicat commun « copiable » pour `AddItems` et `GatherFileCount`, avec `PortableName`. La fonction est purement textuelle (`CopyEngine.cs:670-677`).
2. Dans le dialogue, compter les lignes `Skipped` du côté copié, comme `MainWindow.xaml.cs:355`.
3. N’afficher « déjà un exemplaire de chaque fichier » que si ni incident, ni `Skipped`, ni nom écarté ne concerne le côté copié. Sinon, afficher « Rien à copier automatiquement : N élément(s) écarté(s) ».

Les compteurs `BlockedByIncident`, `BehindLinks` et `OnlineOnly` dans `CopyPlan` sont acceptables, mais la note sur les incidents couvre déjà le premier, et la note sur les fichiers en ligne seulement couvre le troisième (`CopyDialog.cs:77-78`).

---

## REG-M05 — Recherche inverse : lectures supplémentaires

**Verdict : partiellement réfutée. Gravité retenue : basse** (rouge : moyenne). Défaut de performance seulement, sans risque pour les données.

**Réalité : la mesure rouge se reproduit.** Leurs données, nos deux compilations, `ParallelReads = 1`. Sortie dans `<scratch>\out-perf-rouge-donnees.txt` :

```
b5399b7 : fichiers lus=400 octets lus=4,000,000 durée=825 ms erreurs=0 déplacés=0 identiques=200
nouveau  : fichiers lus=2400 octets lus=24,000,000 durée=1303 ms erreurs=0 déplacés=0 identiques=200
```

Les durées sont bruitées par l’antivirus, dont le premier passage a duré 3,2 s et 11 s. Les lectures, elles, sont déterministes.

**Mais « non bornées » est inexact.**
- Chaque fichier est haché au plus une fois (cache `_hashes`, `ComparisonEngine.cs:847` et 980-981).
- Le surcoût est donc borné : au plus une lecture des fichiers `OnlyTarget` de moins de 4 Mio qui partagent une taille avec une source hachée, plus un échantillon pour les plus gros.
- Le rapport ancien/nouveau n’est sans limite que parce que le coût ancien peut être presque nul.

**Mesure sur des tailles réalistes.** Les tailles sont log-uniformes de 256 o à 64 Kio : 1 000 fichiers du 1 et 10 000 fichiers en plus dans le 2. Programme `<scratch>\perf\Program.cs`, sortie dans `<scratch>\out-perf-realiste.txt` (un passage représentatif par cas) :

```
== perf-aligne ==            (sauvegarde au même chemin + fichiers en plus)
b5399b7 : fichiers lus=2000 octets lus=22,852,172 …
nouveau  : fichiers lus=3036 octets lus=24,830,267 …
== perf-deplace ==           (forme du scan réel : 0 identique, tout déplacé ; les fichiers en plus se trient avant)
b5399b7 : fichiers lus=3036 octets lus=24,830,267 …
nouveau  : fichiers lus=3036 octets lus=24,830,267 …
== perf-deplace-avant ==     (même chose, les déplacés se trient avant)
b5399b7 : fichiers lus=2000 octets lus=22,852,172 durée=1305 ms …
nouveau  : fichiers lus=3036 octets lus=24,830,267 durée=1695 ms …
```

Sur des tailles réalistes, le surcoût va de **0** à **+52 % de fichiers lus, soit +8,7 % d’octets**. Il atteint ×6 seulement quand toutes les tailles sont identiques. Avec 0 identique, l’ancien parcours paresseux (`FindHashMatchAsync`) lisait déjà tous les candidats triés avant la correspondance.

En revanche, `PASSATION.md` a tort de présenter « +29 % » comme le « cas défavorable » : ce n’est pas une borne.

**Correctifs :**
1. *Recherche inverse à la demande*, quand l’utilisateur choisit « regrouper dans le 1 ». C’est la seule option qui rend la comparaison ordinaire gratuite. Il faut alors revérifier par handle les copies trouvées : aujourd’hui, `VerifyReadIdentitiesAsync` le fait pendant la comparaison.
2. *Empreinte du premier et du dernier Kio sous 192 Kio* : **peu utile**. Sur un disque dur ou un partage, le coût d’un petit fichier vient de l’ouverture et du premier accès, pas des octets. Lire 2 Kio ou 20 Kio coûte presque autant.
3. *Corriger la documentation* : le surcoût est au plus une lecture des fichiers en plus du 2 qui ont une taille commune, mesuré entre 0 et +52 % de fichiers.

---

## REG-M06 — Les exemplaires trouvés ailleurs ne sont pas revérifiés au moment du plan

**Verdict : confirmée. Gravité retenue : basse.**

Le scénario rouge `stale`, rejoué, donne la même sortie (`<scratch>\out-stale.txt`) : un plan de 0 fichier après la suppression de l’exemplaire, et un contenu absent du 2.

C’est la même confiance que pour les lignes `Identical` de « Compléter », qui ne sont pas revérifiées non plus. Le regroupement l’étend aux lignes `Moved` et `SourceCopyPath`. La documentation demande déjà de comparer à nouveau après tout changement.

**Correctif : juste**, avec une réserve de coût.
- Dans le sens « vers le 1 », les exemplaires sont sur le partage du dossier 1 : environ un aller-retour réseau par fichier, soit plusieurs minutes pour 41 000 copies.
- Il faut l’exécuter avec les lecteurs par disque (`DeviceReaders`) et une progression, dans la préparation asynchrone du dialogue.
- Variante moins chère et presque aussi utile : vérifier seulement l’existence, la taille et la date (`FileSafety.Stat`), puis l’identité par handle s’il y a un doute.

---

## REG-M07 — Un fichier verrouillé jamais lu fait échouer tout le plan

**Verdict : partiellement réfutée, car le défaut n’est pas nouveau. Gravité retenue : basse.**

**Réalité :** confirmée (`<scratch>\out-locked.txt`, la même `IOException`).

**Nouveauté : non.** Avec le moteur de `b5399b7` et les mêmes données : `-Scenario locked -Bench benchold`, sortie dans `<scratch>\out-old-locked.txt` :

```
erreurs=0 CanCreatePlan=True
CreatePlan (Compléter le 2) lève IOException : The process cannot access the file '<work>\dossier1\seulement\base.pst' because it is being used by another process.
```

Preuve par lecture : `git show b5399b7:FolderChecker.Core/CopyEngine.cs`, ligne 148, contient déjà le même `new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)`.

L’interface ne plante pas : le dialogue affiche le message de l’exception (`CopyDialog.cs:152`). La promesse « les incidents ne bloquent plus » concerne les incidents de la comparaison, et ce fichier n’en est pas un.

**Correctif : juste.**
- Un handle `FILE_READ_ATTRIBUTES` n’est pas soumis aux modes de partage.
- La copie continue élément par élément (`CopyEngine.cs:599-600`).
- Une violation de partage (32) n’est pas une erreur « transitoire » : elle n’arrête pas la boucle (`FileSystemRetryPolicy.cs:12`).

Ce correctif est indépendant du regroupement.

---

## REG-M08 — Une copie du 1 modifiée rend « Non vérifié » le fichier du 2 qui la désignait

**Verdict : confirmée. Gravité retenue : basse.**

Le scénario rouge `copychange`, rejoué, donne la même sortie (`<scratch>\out-copychange.txt`). `b\y.txt` est `Unverified` et absent des deux plans, alors que son propre contenu n’a pas changé.

**Nouveauté : oui.** La troisième condition de `MarkUnverified` (`ComparisonEngine.cs:1168`) est nouvelle. Dans `b5399b7`, l’erreur aurait de toute façon interdit tout plan. Avec le relâchement, l’exclusion devient trop large : on écarte un fichier sans incident.

**Correctif : juste, et indépendant de l’ordre d’exécution.**
- Retirer seulement `SourceCopyPath`, sans toucher au statut. Si `T` a lui-même changé, sa ligne passe de toute façon `Unverified` par `TargetPath`, puisque `T` est dans `proven`.
- Adapter `GatherRechecksCopiesFoundInFolderOne` (`GatherTests.cs:112-113`). Son message « never silently counted as present » reste vrai : le fichier sera **copié**.

---

## REG-M09 — CLI `into1/into2` : exclusions et `--no-moved` non signalés

**Verdict : partiellement réfutée. Gravité retenue : basse.**

**Réfuté :** les liens et les incidents **sont** signalés par la sortie de la comparaison, juste avant la simulation (`<scratch>\out-cli-silent-33a00bcf-into2.txt`) :

```
Identiques : 1 · absents du 2 : 1 · absents du 1 : 0 · différents : 0 · déplacés : 0 · non vérifiés : 0 · liens non suivis : 4
! Des éléments réels font face à des liens ou jonctions non suivis : leur contenu n’est pas comparé.
…
SIMULATION — rien n’est copié.
0 fichier(s) · 0 o · 0 dossier(s)
```

Les erreurs sont listées par des lignes `!` (`CheckerSanCli.cs:128`).

**Confirmé :**
- Ni `UnsupportedNames` ni `AbandonedTemporaries` ne sont affichés. Ce silence existait déjà pour `1to2`, `2to1` et `both` dans `b5399b7`.
- Aucune note n’accompagne `--no-moved` (`<scratch>\out-cli-empty-96bb320b-into2-nomoved.txt`, sortie identique à la rouge). Ce point est nouveau.

**Correctif :** plutôt que d’ajouter une note, **refuser** `--no-moved` avec `into1|into2` (code 3, « commande invalide »). Un regroupement sans recherche des déplacés n’est qu’un « Compléter » déguisé. Il faut aussi ajouter les deux compteurs du plan à la sortie et au JSON, pour tous les modes.

---

## Pistes non prouvées

### Piste 1 — `MarkDirectoryProblem` et les noms à point ou espace final : **confirmée** (gravité basse)

Ce résultat contredit le contrôle rouge « Erreurs sans ligne… chaque `_errors.Add` du moteur produit une ligne bloquante ».

Scénario `trailing-dot` : un dossier `Archives.` (créé par `\\?\`) existe des deux côtés, avec un contenu différent. Sortie dans `<scratch>\out-trailing-dot.txt` :

```
comparaison : Finished=True erreurs=2 CanCreatePlan=True SnapshotVerified=False
  erreur : « <work>\dossier1\Archives. » : Dossier non entièrement lisible : Could not find file '<work>\dossier1\Archives'.
  erreur : « <work>\dossier2\Archives. » : Dossier non entièrement lisible : Could not find file '<work>\dossier2\Archives'.
  ligne CommonDirectory Directory Archives.
  ligne OnlySource      Directory Docs
  ligne OnlySource      File      Docs\a.txt
    détail [Archives.] : Dossier présent au même chemin des deux côtés.
Path.GetRelativePath(racine, racine\Archives.) = « Archives »
GatherFileCount(into2)=1 GatherFileCount(into1)=0
```

Deux défauts s’enchaînent :
1. **Le dossier n’est pas listé.** `ReadDirectoryBatch` appelle `File.GetAttributes(directory)` sans préfixe `\\?\` (`ComparisonEngine.cs:525`). .NET retire le point final et cherche `Archives`.
2. **L’échec n’est rattaché à aucune ligne.** `MarkDirectoryProblem` cherche l’entrée par `Path.GetRelativePath` (`ComparisonEngine.cs:588`), qui renvoie `Archives` au lieu de `Archives.`.

La ligne `Archives.` reste `CommonDirectory`, sans incident. Conséquences :
- `CanCreatePlan` est vrai ;
- la note d’incidents du dialogue compte 0 ligne `Unverified` ;
- son contenu (`contrat.pdf` d’un côté, `autre.pdf` de l’autre) n’apparaît nulle part dans le dialogue ;
- si `Docs` n’existait pas, le dialogue afficherait « Rien à regrouper : ce dossier a déjà un exemplaire de chaque fichier ».

Seuls le bandeau « Vérification incomplète — 2 incident(s) » de la fenêtre principale et les lignes `!` du CLI (`<scratch>\out-cli-trailing-dot-18b404bc-into2.txt`) le signalent.

**Nouveauté :** le défaut existait (`b5399b7`, `ComparisonEngine.cs:581`), mais l’ancien `CanCreatePlan` le neutralisait. Sortie dans `<scratch>\out-old-trailing-dot.txt` :

```
erreurs=1 CanCreatePlan=False
```

C’est donc le relâchement qui le rend exploitable.

**Gravité basse :**
- ces noms sont rares (WSL, NAS, macOS) ;
- aucun fichier n’est faussement déclaré présent par contenu ;
- le contenu non listé est simplement ignoré.

**Correctif :**
1. `MarkDirectoryProblem` doit chercher l’entrée par `FileSafety.RelativeTo(snapshot.Root, fullPath)`, la même fonction que l’inventaire. La ligne passe alors en `Unverified`, et elle est bloquante et comptée.
2. Filet de sécurité : si aucune entrée ne correspond, rattacher le problème à la ligne `.`. `CanCreatePlan` refuse alors le plan, ce qui est prudent quand la portée de l’erreur est inconnue. Le relâchement repose sur « toute erreur a sa ligne bloquante » : cet invariant doit être garanti, pas supposé.
3. En option, lister ces dossiers par `FileSafety.Native(directory)` en recalculant les chemins relatifs sans le préfixe. Ils deviennent alors comparables.

Test : `GatherBlocksUnlistedFolders`, sur les données du scénario `trailing-dot`. Attendus : `Archives.` est `Unverified`, la note d’incidents compte au moins 1, et aucun message « déjà un exemplaire » n’apparaît.

### Piste 2 — Corbeille réelle : **confirmée**

Voir REG-M01 : liste réelle de `C:\$Recycle.Bin` et vrai moteur. Le dossier du propriétaire est listé sans erreur ; les autres SID et `System Volume Information` sont refusés, et ce refus ne bloque plus le plan. Le cas FAT/exFAT n’a pas été vérifié faute de volume disponible.

### Piste 3 — Quarantaine des doublons : **confirmée** (gravité moyenne, à corriger avec REG-M01)

Scénario `quarantine`, sortie dans `<scratch>\out-quarantine.txt` :

```
  ligne Moved           File      Photos\plage.jpg  -> 2:<work>\dossier2\.CheckerSan-Quarantine\2026-10-20 1200\Rangement\plage (2).jpg
  ligne OnlyTarget      File      Rangement\plage.jpg  SourceCopyPath=1:<work>\dossier1\Photos\plage.jpg
après suppression de l'exemplaire conservé, nouvelle comparaison :
  ligne Moved           File      Photos\plage.jpg  -> 2:<work>\dossier2\.CheckerSan-Quarantine\2026-10-20 1200\Rangement\plage (2).jpg
GatherFileCount(into2)=0
```

- Tant que l’exemplaire conservé existe, le choix de la quarantaine n’est qu’affichage.
- Dès qu’il disparaît, la quarantaine, faite pour être vidée, reste la seule preuve, et le fichier n’est plus regroupé.

L’application elle-même refuse de traiter une copie en quarantaine comme exemplaire vivant (`DuplicateSelection.cs:27`, `DuplicateRemoval.cs:117`). Le regroupement est incohérent avec cette règle. Le correctif est celui de REG-M01, point 4.

### Piste 4 — `MarkUnverified` en O(lignes) par incident : **écartée** comme défaut de ce changement

Le coût est réel mais préexistant. Le relâchement ne le rend pas « exploitable » : la comparaison appelle `MarkUnverified` qu’un plan soit permis ou non. Mesure faite sur 20 040 lignes, avec 2 000 fichiers modifiés pendant le contrôle final. Sortie dans `<scratch>\out-markunverified-cost.txt` :

```
passage 3 (sans modification) : … phase « Contrôle final »=3068 ms
passage 4 (2 000 fichiers modifiés pendant le contrôle final) : lignes=20040 erreurs=2000 Unverified=2000 phase « Contrôle final »=8179 ms
```

Environ 2 ms par incident à 20 000 lignes, y compris la modification des 2 000 dates. La troisième comparaison de chaînes ajoutée n’y change qu’un facteur constant. À traiter un jour, par un index chemin → clés de lignes, mais sans lien avec le regroupement.

### Piste 5 — Systèmes sans `FileIdInfo` : **écartée** comme défaut nouveau

Preuve par lecture : `_readIdentities` n’est rempli que si `closing.Identity` est non nul (`ComparisonEngine.cs:1040`), et `VerifyReadIdentitiesAsync` filtre sur cette table (1086). Les lignes `Identical` et `Moved` ont exactement la même limite. Le contrôle final par inventaire (taille et dates, `VerifySnapshot`) reste actif pour `SourceCopyPath`. C’est une limite connue, héritée sans aggravation.

---

## Ordre de traitement conseillé

1. **Prédicat « jamais une preuve de présence ».** Il couvre la Corbeille et les dossiers système à la racine du volume, la quarantaine et les temporaires `.partial`, avec l’exclusion de l’inventaire à la racine. Il corrige REG-M01, REG-M02 et la piste 3. À faire avant tout essai sur `G:\`.
2. **Invariant « toute erreur a sa ligne bloquante ».** Corriger `MarkDirectoryProblem` (piste 1) et ajouter un filet sur la ligne `.`.
3. **Seuil de taille dans `GatherRows`** (REG-M03).
4. **Lignes `SourceCopyPath` dans `MarkUnverified`** (REG-M08) et textes du dialogue (REG-M04).
5. Le reste : REG-M06, REG-M07 (préexistant), REG-M09, la documentation de REG-M05, puis la recherche inverse à la demande.

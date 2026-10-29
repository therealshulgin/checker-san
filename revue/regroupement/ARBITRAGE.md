# Arbitrage — revue adverse de « Regrouper dans le dossier 1 ou 2 »

Revue du changement non commité au-dessus de `b5399b7` (section « Regrouper dans le dossier 1 ou 2 — 28 octobre 2026 » de `PASSATION.md`).

Rapports :
- `rouge/MOTEUR.md` (REG-M01 à M09) et `rouge/PARCOURS.md` (REG-P01 à P07) ;
- `bleue/MOTEUR.md` et `bleue/PARCOURS.md`.

Les deux équipes ont rejoué les reproductions de l'autre : **aucune trouvaille n'est réfutée sur le fond**. Les désaccords portent sur la gravité, sur la nouveauté par rapport à `b5399b7` et sur plusieurs correctifs. L'équipe bleue a aussi confirmé par exécution deux pistes que l'équipe rouge n'avait pas prouvées : la piste 1 et la piste 3 du moteur.

**Ce qu'il faut retenir.** Le regroupement est le premier mode qui *renonce* à copier sur la foi d'un exemplaire situé ailleurs. Toute fausse preuve de présence devient donc une perte possible, dès que l'utilisateur libère l'autre dossier. La revue en trouve trois sources réelles :
- la Corbeille et les dossiers système d'une racine de disque, c'est-à-dire la configuration de l'utilisateur, `G:\` ;
- les temporaires `.partial` ;
- la quarantaine des doublons.

À cela s'ajoutent deux mécanismes qui rassurent à tort :
- la phrase « Rien à regrouper : ce dossier a déjà un exemplaire de chaque fichier » ;
- la suggestion « Comparer avec ce dossier », qui peut retirer de la comparaison les fichiers mêmes qu'on voulait regrouper.

**Ne pas utiliser le regroupement sur `G:\` avant le lot A.**

## Verdicts

Gravité arbitrée : critique = perte silencieuse probable ; haute = fausse présence ou recommandation trompeuse menant à une perte ; moyenne = garantie non tenue sans perte directe ; basse = robustesse, message, performance.

| Id | Rouge | Bleu | Arbitrage | Nouveau ? | Lot |
|---|---|---|---|---|---|
| REG-M01 Corbeille et dossiers système d'une racine | critique | critique (confirmée sur une vraie racine NTFS) | **critique** | oui | A |
| REG-M02 Temporaire `.partial` complet pris pour un exemplaire | haute | moyenne | **haute** | oui | A |
| Piste 3 du moteur : quarantaine prise pour un exemplaire | non prouvée | moyenne (confirmée) | **haute** (même mécanisme que M02) | oui | A |
| REG-M03 Fichiers vides ou minuscules appariés par contenu | moyenne | moyenne (portée plus large) | **moyenne** | oui | A |
| Piste 1 du moteur : erreur d'un dossier à point final sans ligne bloquante | non prouvée | basse (confirmée) | **moyenne** | exposée par le relâchement | A |
| REG-P01 « Comparer avec ce dossier » retire les fichiers à regrouper | haute | moyenne | **haute** | oui | A |
| REG-P02 et REG-M04 « Rien à regrouper… » faux ; exclusions muettes | haute / moyenne | moyenne / basse | **moyenne** | oui | A |
| REG-P04 Le nombre du bouton diffère du plan | basse | basse | **basse** | oui | A (même prédicat que P02) |
| REG-P03 Le bouton contourne la reprise d'une copie inachevée | moyenne | moyenne | **moyenne** | présélection seulement | A (point 1), B (le reste) |
| REG-M08 Copie du 1 modifiée : le fichier du 2 sort de tous les plans | basse | basse | **basse** | oui | B |
| REG-M07 Un fichier verrouillé jamais lu fait échouer tout le plan | basse | basse (existait déjà) | **basse** | non | B |
| REG-M09 CLI `into1/into2` : `--no-moved` et éléments écartés | basse | basse | **basse** | en partie | B |
| REG-P05 Libellés, infobulle, aide, message de fin | moyenne | basse | **basse** | oui | B |
| REG-P06 Écran « Non », CSV « Non vérifié » | basse | basse | **basse** | oui | B |
| REG-P07 Détail d'une ligne « Absent du dossier 1 » | basse | basse (portée) | **basse** | en partie | B |
| Piste 3 du parcours : parent d'un dossier vide créé sans ses dates | non prouvée | basse (confirmée) | **basse** | oui | B |
| REG-M05 Coût de la recherche inverse | moyenne | basse (bornée) | **basse** | oui | C (documentation tout de suite) |
| REG-M06 Exemplaires non revérifiés au moment du plan | basse | basse | **basse** | extension | C |
| Pistes écartées | — | — | Piste 4 du moteur (`MarkUnverified` en O(lignes) par incident : préexistant) ; pistes 2 (double clic, mesuré) et 5 (préexistant) du parcours ; piste 5 du moteur (préexistant) | — | — |

### Motifs des gravités contestées

- **REG-M02, haute et non moyenne.** L'équipe bleue a raison : la fenêtre de déclenchement est étroite, et la reprise règle le cas. Mais :
  - arrêter pendant la vérification SHA-256 d'une vidéo de plusieurs Gio est un geste ordinaire ;
  - le bouton « Tout regrouper » ouvre justement le dialogue sur un autre mode que la reprise (REG-P03) ;
  - l'application qualifie elle-même ce temporaire de fichier « jamais recopié », ce qui invite à le supprimer.

  C'est la même classe de défaut que REG-M01, une fausse preuve de présence, avec un déclencheur plus rare.
- **Piste 1 du moteur, moyenne et non basse.** Les noms à point final sont rares, mais le relâchement de `CanCreatePlan` repose entièrement sur un invariant : toute erreur a sa ligne bloquante. Cette piste le casse. Le contenu non listé disparaît alors du dialogue, sans note d'incident. Un invariant de sécurité se garantit ; il ne se suppose pas.
- **REG-P01, haute et non moyenne.** L'argument bleu est exact : le verdict « Oui » est vrai pour le chemin affiché dans la case. Mais :
  - c'est l'application qui recommande cette restriction, dans le dialogue même du regroupement vers le 1, où elle retire précisément les fichiers à copier ;
  - la configuration réelle de l'utilisateur déclenche ce cas : 55 512 fichiers sortiraient de la comparaison ;
  - rien ne rappelle ensuite la restriction.
- **REG-P02 et REG-M04, moyenne.** Une note contraire figure chaque fois sur le même écran, et le verdict principal reste « Non » ou « Non vérifié ». La phrase reste fausse au point de décision : correction en lot A, car elle ne coûte presque rien.
- **REG-M05, basse.** Chaque fichier n'est haché qu'une fois, donc le surcoût est borné. Le facteur 6 n'apparaît que lorsque toutes les tailles sont égales ; sur des tailles réalistes, l'équipe bleue mesure de 0 à +52 % de fichiers lus (+8,7 % d'octets). En revanche, `PASSATION.md` a tort de présenter +29 % comme le cas défavorable : à corriger.

## Correctifs retenus

### Lot A — aucune fausse présence (préalable à tout essai sur `G:\`) — **fait le 29 octobre 2026**

Bilan dans `PASSATION.md`, section « Lot A de la revue du regroupement ». Les reproductions rouges et bleues ont été rejouées sur le moteur corrigé ; seule REG-M08, du lot B, se comporte encore comme avant.

1. **Dossiers système d'une racine de volume (REG-M01).**
   - Quand la racine comparée est la racine d'un volume, l'inventaire écarte au premier niveau `$RECYCLE.BIN`, `RECYCLER`, `System Volume Information`, `.Trashes` et `.Trash-*`, sans tenir compte de la casse.
   - Un avertissement nomme les dossiers écartés.
   - Ils ne sont pas marqués `Skipped` : ce statut est affiché comme « lien non suivi ».
   - Le test de racine compare `GetVolumePathNameW` et la racine **après** `Path.TrimEndingDirectorySeparator` des deux côtés, car `G:\` finit déjà par `\` (correction bleue du correctif rouge).
   - Effet attendu : les erreurs parasites des autres SID et de `System Volume Information` disparaissent. Elles comptent probablement parmi les 12 incidents du scan réel.
2. **Jamais une preuve de présence (REG-M02 et piste 3 du moteur).**
   - Un seul prédicat, appliqué partout : aux candidats de `FindMovedFilesAsync`, à `bySize`/`byHash` de `FindTargetCopiesAsync` et aux votes de `FolderAlignment.Suggest`.
   - Il écarte :
     - les temporaires de copie (`CopyEngine.IsCopyTemporary`), des deux côtés ;
     - tout ce qui est sous `.CheckerSan-Quarantine`, à toute profondeur (`DuplicateRemoval.IsInQuarantine`).
   - Ces fichiers restent des lignes visibles ; ils ne prouvent simplement plus rien.
   - Défense en profondeur dans `GatherRows` : une ligne dont l'exemplaire (`TargetPath` d'une ligne `Moved`, ou `SourceCopyPath`) désigne un tel fichier compte comme manquante.
3. **Très petits fichiers (REG-M03).**
   - Sous 4 Kio, un fichier ne vaut que par son nom. `GatherRows` le traite comme manquant même si son contenu existe ailleurs ; le copier coûte au plus un cluster.
   - `FolderAlignment` ne compte pas leurs votes.
   - La comparaison et « Compléter » restent inchangés (correctif bleu, préféré au seuil `Size == 0` dans le moteur).
4. **Toute erreur a sa ligne bloquante (piste 1 du moteur).**
   - `MarkDirectoryProblem` cherche l'entrée par `FileSafety.RelativeTo`, la fonction de l'inventaire, et non par `Path.GetRelativePath`.
   - Filet de sécurité : une erreur qui ne trouve pas sa ligne marque la ligne `.`, et `CanCreatePlan` refuse alors le plan.
5. **Une complétude seulement si elle est prouvée (REG-P02, REG-M04, REG-P04).**
   - Un prédicat commun « copiable » pour `AddItems` et `GatherFileCount` : en ligne, sous un incident, temporaire, nom non portable. Le nombre du bouton est alors celui du plan.
   - « Ce dossier a déjà un exemplaire de chaque fichier » n'est affiché que si un indicateur prouvé le dit :
     - `ContentCovered` pour le dossier 2 ;
     - son symétrique, à ajouter dans le moteur, pour le dossier 1.

     Sinon, le dialogue dit « Rien à copier automatiquement » et indique combien d'éléments restent sans exemplaire prouvé.
   - Le dialogue compte les lignes `Skipped` du côté copié.
   - Le point 3 rouge de REG-P05 est **rejeté** : une confirmation fondée sur « plan vide et aucun manque compté » est fausse dans le cas `trou2` de l'équipe bleue.
6. **Suggestion de sous-dossier (REG-P01).**
   - Le bandeau n'est proposé que quand le dossier qu'il restreint est la **destination** du mode choisi :
     - `InFolder2` avec `IntoFolder2` ou « Compléter le dossier 2 » ;
     - l'inverse avec `IntoFolder1` ou « Compléter le dossier 1 » ;
     - jamais avec « les deux » ni avec la reprise.
   - Il annonce combien de fichiers sortiraient de la comparaison.
   - Seuil : plus de la moitié des fichiers du dossier rangé ailleurs, et non plus des seuls fichiers appariés. L'aide devient ainsi vraie.
   - Le détail du verdict de la fenêtre principale, qui ne connaît pas le mode, est reformulé selon la proposition bleue.
7. **La reprise d'abord (REG-P03, point 1).** Si une copie inachevée porte sur les mêmes dossiers, dans un sens ou dans l'autre (`ShowsFolders`), le dialogue présélectionne « Reprendre », même ouvert par « Tout regrouper », et dit pourquoi.

Tests : ceux proposés par les rapports, notamment :
- `GatherIgnoresRecycleBinAndQuarantine`, avec un cas où la racine finit par `\` ;
- `GatherIgnoresInterruptedTemporaries`, dans les deux sens ;
- `GatherKeepsEmptyFiles`, avec le cas de 1 et 4 octets ;
- `GatherBlocksUnlistedFolders` ;
- `GatherNeverClaimsCompletenessWhileContentsAreMissing`, sur les fixtures `nothing`, `blocked`, `names` et `trou` ;
- `GatherSuggestionNeverDropsFilesToGather` ;
- `GatherButtonKeepsTheSavedCopyFirst`.

### Lot B — robustesse et messages — **fait le 29 octobre 2026**

Bilan dans `PASSATION.md`, section « Lots B et C ».
- Le point 3 de REG-P03 (espace des temporaires) n'a pas d'objet : leur place est déjà retirée de l'espace libre mesuré.
- La confirmation après regroupement (REG-P05) repose sur `ContentCovered` et son symétrique, comme l'équipe bleue le proposait.

- **REG-M08.** Quand l'exemplaire du 1 change, retirer `SourceCopyPath` sans toucher au statut : le fichier du 2 redevient manquant et sera copié. Adapter `GatherRechecksCopiesFoundInFolderOne`.
- **REG-M07.** Lire taille, dates, attributs et identité par un handle `FILE_READ_ATTRIBUTES`, ou, à défaut, compter le fichier comme illisible au lieu de faire échouer tout le plan.
- **REG-M09.** Le CLI refuse `--no-moved` avec `into1|into2` (code 3). Il affiche `UnsupportedNames` et `AbandonedTemporaries` pour tous les modes, et les écrit dans le JSON.
- **REG-P03, points 2 et 3.** Avant de remplacer le journal d'une autre paire, demander confirmation en listant les temporaires orphelins et leur taille. Les compter dans l'espace nécessaire. Le bouton devient « Reprendre / regrouper… ». **Pas de suppression automatique** des temporaires (point 4 rouge rejeté).
- **REG-P05.**
  - Réécrire l'infobulle et l'aide FR/EN.
  - Retirer « sans recopier ce qu'il a déjà » quand la recherche des déplacés était désactivée.
  - Adapter au mode le titre, `ExclusionsText` et le message de fin.
- **REG-P06.** Une seule règle à trois états dans le moteur, utilisée par l'écran, le CSV et le CLI. Deux champs dans l'enveloppe du JSON, pas dans `ComparisonResult`.
- **REG-P07.** Le détail affiche « Copie dans le dossier 1 » et « SHA-256 (copie dans 1) ». Même libellé « autre chemin » pour le côté 2 des lignes « Retrouvé ailleurs ».
- **Piste 3 du parcours.** `GatherRows` retient aussi les dossiers manquants qui contiennent un sous-dossier vide retenu, afin de leur garder leurs dates et attributs.
- **REG-P01, point 4.** Après « Comparer avec ce dossier », le verdict suivant rappelle que la comparaison est limitée à un sous-dossier.

### Lot C — à mesurer, ou plus tard — **fait le 29 octobre 2026**, sauf la recherche inverse à la demande

Bilan dans `PASSATION.md`. La revérification se fait par handle, 8 requêtes à la fois. L'index de `MarkUnverified` ramène 2 000 incidents à un coût négligeable.

- **REG-M05.** Corriger `PASSATION.md` dès maintenant : +29 % n'est pas une borne. Le surcoût vaut au plus une lecture des fichiers en plus du 2 qui partagent une taille ; l'équipe bleue mesure de 0 à +52 % de fichiers lus sur des tailles réalistes. Une recherche inverse à la demande ne se justifiera que si une mesure sur le vrai disque le demande.
- **REG-M06.** Revérifier au moment du plan l'identité des exemplaires et des fichiers écartés. Il faut d'abord régler son coût sur un partage (lecteurs par disque, progression). En attendant, l'aide recommande de comparer à nouveau juste avant de regrouper.
- **Piste 4 du moteur**, préexistante : indexer `MarkUnverified` par chemin.

## Ce qui a été vérifié sans défaut

Points utiles pour la suite :
- Le nouveau test par ancêtres équivaut à l'ancien test par préfixe : 200 000 cas aléatoires, aucune divergence.
- Une coupure transitoire après l'inventaire laisse `Finished` faux.
- Les échantillons ne produisent que des faux négatifs, au pire une copie en trop.
- « Comparer avec ce dossier » remplit la bonne case dans les deux sens et efface le verdict précédent.
- « Compléter / reprendre… » présélectionne toujours la reprise.
- Le changement de langue et les opérations en cours masquent ou traduisent correctement les boutons.
- Les lignes de verdict ne sont pas tronquées aux largeurs essayées.
- Aucun faux « Non » n'a été trouvé.

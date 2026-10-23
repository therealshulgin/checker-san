# Arbitrage de la revue adverse — Checker San

23 octobre 2026. Code revu : lots 1 à 5, non commités.
Pièces :
- `REVUE-ROUGE.md` : 39 trouvailles, 4 agents ;
- `REVUE-BLEUE.md` : 39 réponses et 1 point transversal, 4 agents ;
- les programmes de reproduction et de contre-preuve dans le dossier temporaire de la session.

## Méthode

- **Sur pièces.** Je n'ai retenu un fait que s'il était prouvé par une exécution ou par une trace de code vérifiable.
- **Gravité selon deux critères :** l'impact sur les données ou sur le verdict, et le réalisme du déclencheur. Il faut un usage normal, une erreur plausible de l'utilisateur, un programme ordinaire concurrent, ou au contraire un processus hostile. Un faux « identique » obtenu sur un arbre statique ordinaire vaut plus qu'un faux « identique » qui exige une modification concurrente pendant le scan.
- **Un même défaut signalé deux fois n'est compté qu'une fois** : AUT-03 et CMP-04.
- **Vérification personnelle.** J'ai relancé moi-même le constat le plus lourd que l'équipe rouge n'avait pas vu, CMP-02 par lien physique (`cmp-bleu\out\bleu.exe hardlink`). Résultat : la fenêtre principale affiche « Les deux dossiers sont identiques » alors que les SHA-256 réels diffèrent (`826C82147C60DB70` contre `DAC7494485CF5BCE`). Le constat est confirmé.

## Bilan

- **Aucune trouvaille n'est réfutée sur le fond.** Les 39 se reproduisent. Elles correspondent à 38 problèmes distincts, auxquels s'ajoute le point transversal DUP-07 côté copie.
- **L'équipe rouge a surestimé la gravité dans 22 cas sur 39.** Il s'agit surtout d'attaques qui exigent un processus hostile, ou de limites déjà documentées.
- **L'équipe bleue a été honnête.** Elle a concédé 14 trouvailles, confirmé le point transversal, et en a aggravé 4 :
  - CMP-01 : cas inverse, celui laissé par robocopy ;
  - CMP-02 : liens physiques et instantanés ;
  - AUT-02 : cas symétrique « AJOUTÉ » ;
  - AUT-05 : `if errorlevel 1` faux.
- **L'équipe bleue a aussi corrigé 8 correctifs rouges faux ou risqués :**
  - AUT-02 : il masquerait des écarts prouvés ;
  - AUT-04 : il enregistrerait un instantané arrêté ;
  - AUT-07 : un bouton piloté par `schtasks` sur le thread d'interface casserait les tests ;
  - COP-10 : `FileShare.Delete` ne permet pas `File.Move` ;
  - DUP-01 : il bloquerait tout le plan ;
  - DUP-05 : il casserait la reprise réseau ;
  - CMP-02 : l'énumération est insuffisante ;
  - CMP-11 : régression sur les volumes insensibles à la casse.

| Gravité finale | Nombre | Trouvailles |
|---|---|---|
| Critique | 1 | CMP-01 |
| Haute | 2 | CMP-02, AUT-02 |
| Moyenne | 12 | DUP-01, DUP-02, DUP-03, DUP-04, DUP-07, AUT-01, AUT-03 = CMP-04, AUT-05, CMP-05, CMP-06, CMP-07, COP-05 |
| Basse | 22 | COP-01, COP-02, COP-03, COP-04, COP-06, COP-07, COP-08, COP-09, COP-10, DUP-05, DUP-06, DUP-08, AUT-04, AUT-06, AUT-07, AUT-08, AUT-09, AUT-10, CMP-03, CMP-08, CMP-09, CMP-11 |
| Info | 1 | CMP-10 |

## Décisions

« Correctif retenu » indique quelle version appliquer : R = rouge, B = alternative bleue. Le lot renvoie au plan de correction plus bas.

| ID | Rouge | Bleu | **Arbitrage** | Correctif retenu | Lot |
|---|---|---|---|---|---|
| CMP-01 | Critique | Critique | **Critique** | R (`Clean` seulement si `TreesIdentical`) + B (compter les liens dans `PairRun`, messages CLI, paires, fenêtre de résultat, aide) | A |
| CMP-02 | Critique | Haute | **Haute** | R sur le principe, avec la requête par handle de B (pas l'énumération), étendue aux instantanés | A |
| AUT-02 | Haute | Haute | **Haute** | B (code aligné sur `ForComparison`, dossiers illisibles mémorisés des deux côtés) | A |
| DUP-07 + copie | Moyenne | Basse / Moyenne | **Moyenne** | R (tampon d'au moins 24 octets) | A |
| CMP-05 | Haute | Moyenne | **Moyenne** | R (erreur en cas de collision, hachage de `info.FullName`) | A |
| AUT-03 = CMP-04 | Haute | Moyenne | **Moyenne** | R + B : une seule fonction canonique dans Core, validée avant le scan | B |
| DUP-01 | Haute | Moyenne | **Moyenne** | R points 1 et 3, B pour le point 2 (refus du seul groupe) + marquage dans l'aperçu | B |
| DUP-02 | Haute | Moyenne | **Moyenne** | R avec la précision de B : second handle élément par élément, ligne d'intention, bon classement | B |
| DUP-03 | Haute | Moyenne | **Moyenne** | B (`DirectoryLocks`, handle vérifié, `RenameByHandle`, refus des sessions et manifestes liens) | B |
| DUP-04 | Moyenne | Basse | **Moyenne** | B (sans changement de format) | C |
| AUT-01 | Haute | Moyenne | **Moyenne** | R + B (processus séparé à priorité normale, une fenêtre par paire, `--pair <id>`), à valider par un essai réel | C |
| AUT-05 | Moyenne | Moyenne | **Moyenne** | R + B (filet général dans le CLI) | C |
| CMP-06 / COP-05 | Moyenne | Moyenne | **Moyenne** | R (chemins `\\?\` partout, chemin relatif par découpe) + B (manifeste `longPathAware` du CLI) | C |
| CMP-07 | Moyenne | Moyenne | **Moyenne** | B (sauvegarde adaptative, limite proportionnelle, lecture hors du thread d'interface) | C |
| COP-01 | Haute | Basse | **Basse** | B (contrôle du chemin final sur le handle, passe finale par handle, identité des racines dans le plan) | B |
| COP-07 | Basse | Basse | **Basse** | R pour le plan, B pour la comparaison (libellé, pas de masquage) | B |
| COP-08 | Basse | Basse | **Basse** | R avec la précision de B (`CreatedByCopy` collant et nullable) | B |
| COP-09 | Basse | Basse | **Basse** | R avec la précision de B (handle `FILE_WRITE_ATTRIBUTES`, identité calculée après) | B |
| AUT-08 | Moyenne | Basse | **Basse** | R (destination réelle contrôlée de façon canonique) + nom strict ; pas de filtre par contenu | B |
| AUT-09 | Basse | Basse | **Basse** | R | B |
| CMP-08 | Moyenne | Basse | **Basse** | R + parent existant | B |
| CMP-09 | Basse | Basse | **Basse** | R, dans la fonction commune et `ValidateCheckpointPath` | B |
| COP-02 | Moyenne | Basse | **Basse** | B (aide et dialogue ; `Zone.Identifier` copié au mieux) | C |
| COP-03 | Moyenne | Basse | **Basse** | B (champs inchangés s'ils désignent déjà les deux racines) | C |
| COP-04 | Moyenne | Basse | **Basse** | R avec les réserves de B (cluster réel, dossiers non terminés, marge mesurée) | C |
| COP-06 | Moyenne | Basse | **Basse** | B (ligne écartée avec avertissement, pas de base64) | C |
| COP-10 | Basse | Basse | **Basse** | B (le correctif rouge est faux) | C |
| DUP-05 / CMP-03 | Moyenne / Haute | Basse | **Basse** | B (réutiliser seulement sur NTFS, ReFS et SMB ; limite écrite dans l'aide) ; USN plus tard, en défense supplémentaire | C |
| DUP-06 | Moyenne | Basse | **Basse** | R + B (racine canonique, même règle dans `QuarantineSessions`) | C |
| DUP-08 | Basse | Basse | **Basse** | R + B (`FileDispositionInfoEx`, lecture seule signalée dans l'aperçu, groupes suivants traités) | C |
| AUT-04 | Moyenne | Basse | **Basse** | B (variante « à défaut » + filet général) | C |
| AUT-06 | Moyenne | Basse | **Basse** | B (`PairStore.Modify` sous le verrou) | C |
| AUT-07 | Moyenne | Basse | **Basse** | R points 1 et 2 ; B pour le bouton (toujours actif) | C |
| AUT-10 | Basse | Basse | **Basse** | B (liste blanche avec les options communes ; `--help` garde le code 0) | C |
| CMP-11 | Basse | Basse | **Basse** | B (seulement dans les dossiers sensibles à la casse) | C |
| CMP-10 | Basse | Info | **Info** | R (contrôles de cohérence de `Load`, sans valeur de sécurité) | C |

## Motifs des points disputés

- **CMP-01, Critique.** Les deux équipes sont d'accord. L'arbre est statique et ordinaire : un dossier réel face à une jonction, par exemple celle que robocopy laisse en sauvegardant un profil. Il ne demande aucun droit et aucune modification pendant le scan. Pourtant, le canal fait pour alerter (tâche planifiée, CLI, liste des paires) reste muet et dit « identiques ». La fenêtre principale, elle, est juste : le défaut est dans `ExitCodes`, que j'ai écrit au lot 5.
- **CMP-02, Haute et non Critique.** Le faux « identiques » est réel, et il est plus large que ne le décrivait le rouge (lien physique, instantanés). Mais tout déclencheur exige une modification *pendant* le scan, ce que l'aide demande d'éviter. Il reste en tête du lot A, parce que le contrôle final est précisément la protection promise contre ce cas.
- **COP-01, Basse.** Les fenêtres exploitables durent de quelques microsecondes à quelques millisecondes. Pendant l'écriture, la fenêtre est fermée, et un échange entre deux éléments est détecté (preuves `staticswap`, `betweenitems` et `pinning`). Il faut donc un processus hostile avec les droits de l'utilisateur, qui peut de toute façon déjà tout faire. À l'inverse, **DUP-02 est Moyenne** : sa fenêtre dure des minutes, et un programme ordinaire suffit (enregistrement « sûr » d'un éditeur, client de synchronisation).
- **DUP-03, Moyenne.** Le seuil est franchi par un *autre compte* qui écrit dans un dossier partagé : la restauration de l'utilisateur déplace alors ses propres fichiers privés. C'est un vrai changement de frontière, contrairement à COP-01.
- **DUP-04, Moyenne et non Basse.** Le déclencheur (disque C: plein) est réaliste sur la machine de test. Le README promet « Every action is journaled ». En mode définitif, rien ne nomme ce qui a disparu.
- **DUP-07, Moyenne.** Un fichier nommé `a` n'est jamais copié, ni mis à l'écart, et chaque essai laisse un temporaire complet qui pollue les comparaisons suivantes (COP-07). Le correctif tient en une ligne.
- **AUT-01, Moyenne.** La suspension est bornée à 72 h, parce que le Planificateur envoie WM_CLOSE et que l'app quitte proprement. Le code 0 et la priorité basse héritée sont réels.
- **CMP-03 et DUP-05, Basse.** Aucun outil courant ne réécrit le `ChangeTime` propre d'un fichier. Un processus hostile obtiendrait le même résultat en réécrivant `resume.json`. La piste FAT/exFAT pourrait en faire un cas ordinaire sur clé USB. Le correctif bleu (pas de réutilisation hors NTFS, ReFS et SMB) la neutralise sans attendre de preuve.

## Pistes non prouvées à traiter

| Piste | Statut | Décision |
|---|---|---|
| Même dossier par deux noms réseau (`\\NAS` / `\\IP`, `\\localhost\…`) | Alias locaux réfutés ; alias réseau plausibles par le code | **Lot A** : comparer le numéro de volume et l'identifiant des deux racines et de leurs ancêtres. S'il se confirme, ce cas est Critique. |
| FAT/exFAT : `ChangeTime` figé | Non vérifiable ici | Neutralisée par le correctif de CMP-03 (lot C). À vérifier avec une clé exFAT si tu en as une. |
| Corbeille choisie d'après la lettre du chemin ; jonction vers un autre volume | Plausible, Basse | Lot B, avec DUP-02 : déterminer le volume réel par handle ; corriger le motif journalisé. |
| Fichier devenu « en ligne uniquement » après le plan | Plausible, Basse | Lot B : `FileSafety.Access` sur la source ouverte. |
| Reprise après échange de disque de même lettre | Plausible, Basse | Lot B : identité des racines dans `CopyPlan` (avec COP-01). |
| Erreur 87 en écriture non mise en cache | Plausible, non testable | Lot C : repli sans `NO_BUFFERING`, comme pour la relecture. |
| Chemins relatifs dans une paire | Échec bruyant, Basse | Lot C : `GetFullPath` à l'enregistrement. |
| Verrou de `PairStore` selon l'écriture du chemin | Prouvé, négligeable | Lot C : nom du mutex tiré du chemin canonique. |
| Caches de métadonnées SMB | Plausible, faible | Couvert en partie par CMP-02 (requête par handle). |

## Plan de correction proposé

Chaque correctif vient avec le test proposé par la revue, ajusté par l'équipe bleue, et la mesure des coûts avant activation, selon la règle du projet.

**Lot A — Verdicts justes (priorité 1) — fait le 23 octobre 2026** (détail et mesures dans `PASSATION.md`, tests dans `FolderChecker.Tests/VerdictFixTests.cs`)
1. CMP-01 :
   - `ExitCodes.ForComparison` rend `Clean` seulement si le résultat est certain et `TreesIdentical` ;
   - `PairRun` compte les liens non suivis ;
   - messages corrigés dans le CLI, la fenêtre de résultat, la liste des paires et l'aide.
2. CMP-02 :
   - au contrôle final, identité par handle (volume, identifiant, `ChangeTime`) de chaque fichier qui fonde une ligne Identique ou Déplacé, des deux côtés ;
   - même chose pour les instantanés ;
   - une identité devenue indisponible est une erreur ;
   - coût mesuré par l'équipe bleue : 51 à 55 µs par fichier sur NVMe ; à mesurer sur un partage.
3. AUT-02 : `verify` place les erreurs avant les écarts, l'instantané mémorise ses dossiers illisibles, et il n'y a plus d'ABSENT ni d'AJOUTÉ sous ces dossiers ; les erreurs sont affichées.
4. Identité des deux racines dans la comparaison (piste des alias réseau).
5. DUP-07 : tampon de `RenameByHandle`.
6. CMP-05 : collision de casse dans l'instantané.

**Lot B — Écritures et retraits au bon endroit — fait le 23 octobre 2026** (détail, mesures et seul écart voulu, la ligne d'intention de DUP-02, dans `PASSATION.md` ; tests dans `FolderChecker.Tests/WriteSafetyTests.cs`)
1. Une seule fonction `CreateOutside` dans Core : canonique, flux refusés, fichier existant refusé, parent exigé. Elle est appelée avant le scan et à l'écriture, par le CLI, l'app, les instantanés et les paires. Elle couvre AUT-03 = CMP-04, CMP-08 et CMP-09.
2. AUT-08 et AUT-09 : destination réelle des rapports contrôlée, nom invariant, rotation stricte qui épargne le nouveau rapport.
3. Doublons :
   - DUP-01 : quarantaine exclue à toute profondeur, jamais gardée ;
   - DUP-02 : second handle avant la Corbeille, ligne d'intention ;
   - DUP-03 : restauration par handle, sans suivre les liens.
4. Copie :
   - COP-01 : chemin final contrôlé sur le handle, passe finale par handle, identité des racines ;
   - COP-07, COP-08, COP-09 ;
   - pistes « en ligne » et « échange de disque ».

**Lot C — Robustesse, planification et ligne de commande — fait le 23 octobre 2026** (détail dans `PASSATION.md`, tests dans `FolderChecker.Tests/RobustnessTests.cs` ; essai réel avec le Planificateur fait : la fenêtre de résultat d'AUT-01 survit à la fin de la tâche)
1. AUT-01 : le processus planifié quitte toujours avec son code ; le résultat s'affiche dans un processus séparé, à priorité normale. Un essai réel avec le Planificateur, sur ton accord, vérifiera que ce processus survit à la fin de la tâche. Repli : correctif minimal du code de sortie.
2. CMP-06 / COP-05 (chemins `\\?\`), CMP-07 (coût de `resume.json`), AUT-04 et AUT-05 (filet du CLI, journal d'activité protégé).
3. DUP-04, DUP-06, DUP-08, COP-02, COP-03, COP-04, COP-06, COP-10, CMP-03/DUP-05, CMP-10, CMP-11, AUT-06, AUT-07, AUT-10 et les pistes du lot C.

**Documentation à corriger, quel que soit le lot :**
- Aide : « les liens n'empêchent pas le verdict », contredit par la règle du moteur.
- Aide : « la réutilisation revalide chaque fichier » ; il s'agit des métadonnées, pas d'une relecture.
- Aide : les flux alternatifs ne sont pas copiés.
- PASSATION : le manifeste n'active pas à lui seul les chemins longs.
- PASSATION : la quarantaine imbriquée.
- PASSATION : le modèle de confiance de `resume.json`.

## Hors de portée de cette revue

Partages réseau réels, volumes FAT/exFAT, vraie Corbeille, exécution en administrateur et interactions à la souris : tout cela était exclu par sécurité ou n'était pas disponible. Les pistes correspondantes figurent plus haut avec leur statut.

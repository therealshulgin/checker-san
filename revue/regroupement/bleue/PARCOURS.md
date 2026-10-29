# Revue adverse « Regrouper dans le dossier 1 ou 2 » — équipe bleue, interface et parcours

Réponse au rapport `<dépôt>\revue\regroupement\rouge\PARCOURS.md` (REG-P01 à REG-P07), menée le 28 octobre 2026 sur le changement non commité au-dessus de `b5399b7`.

Échelle de gravité : celle du rapport rouge.
- **Critique** : perte, écrasement ou suppression de données, ou faux verdict « identique ».
- **Haute** : écriture ou suppression hors de l’endroit prévu, verdict trompeur, journal qui empêche une reprise ou une restauration.
- **Moyenne** : échec ou blocage sans perte, garantie non tenue dans un cas réaliste.
- **Basse** : robustesse, message.

Les arbitrages précédents servent de repère : une sélection trompeuse sans perte immédiate, dont la perte exige ensuite une suppression manuelle, a été classée **moyenne** (DUP-01 dans `revue/bleue/DUP.md`).

## Banc et conditions d’essai

- **`<scratch>`** désigne le dossier de travail bleu de cette revue, `…\revue-regroupement\bleue\parcours`, dans le dossier temporaire de la session.
- **DLL examinées** :
  - `<dépôt>\FolderChecker.App\bin\Release\net10.0-windows\CheckerSan.dll` (28/10, 22:37:16) ;
  - `<dépôt>\FolderChecker.Core\bin\Release\net10.0\FolderChecker.Core.dll` (28/10, 22:37:31).

  Elles sont postérieures à la dernière source modifiée (`CopyDialog.cs`, 22:30). Aucun projet du dépôt n’a été modifié ni recompilé.
- **Banc rouge relancé sans modification.** `Program.cs` et `Harness.csproj` ont été copiés à l’identique (SHA-256 de `Program.cs` : `150C1E3B…602B2D`, le même des deux côtés) dans `<scratch>\rouge-copie\harness\`.
  - Script : `<scratch>\rouge-copie\run.ps1`. Il ajoute seulement un `dotnet restore --configfile <dépôt>\NuGet.Config` et écrit dans `<scratch>\rouge-copie\out-<nom>.txt`.
  - Commande : `powershell -NoProfile -File <scratch>\rouge-copie\run.ps1 -Scenarios <nom>`.
  - Dans ces sorties, `<scratch>` désigne `<scratch>\rouge-copie` : le masquage est celui du banc rouge.
  - Scénarios relancés : `align`, `align1`, `nothing`, `blocked`, `names`, `resume`, `nomoved`, `after`, `report`. **Les neuf reproduisent à l’identique les extraits du rapport rouge.** Seuls changent les GUID des temporaires et l’espace libre.
- **Contre-expériences bleues.**
  - Projet `<scratch>\harness\Bleu.csproj` (WPF, mêmes DLL), `<scratch>\harness\Program.cs`.
  - Commandes : `powershell -NoProfile -File <scratch>\run.ps1 -Scenario build`, puis `-Scenario <nom>`, avec `<nom>` parmi `seuils`, `reel`, `geometrie`, `trou`, `verrou1`, `videparent`, `moved` et `reprise`.
  - Sorties : `<scratch>\out-<nom>.txt`.
- **Moteur de `b5399b7`.**
  - `git archive b5399b7 FolderChecker.Core` a été extrait dans `<scratch>\old\`. C’est une commande en lecture seule.
  - Il est compilé par `<scratch>\oldengine\OldEngine.csproj`, puis lancé par `dotnet <scratch>\oldengine\bin\Release\net10.0\BleuAncienMoteur.dll <scratch>\data`.
  - Sortie : `<scratch>\out-ancien-reprise.txt`.
- **Sécurité.**
  - Toutes les données sont sous `<scratch>\data`.
  - Dans chaque `MainWindow`, `_checkpointPath` et `_copyCheckpointPath` sont redirigés par réflexion avant tout usage.
  - Aucune copie n’est lancée par `MainWindow`. Les copies passent par `CopyEngine.CopyAsync`, avec un journal sous `<scratch>`.
  - Les fenêtres affichées sont placées hors écran. Aucune tâche planifiée n’a été créée, et `%LOCALAPPDATA%\CheckerSan` n’a pas été touché.

## Synthèse

| ID | Gravité rouge | Verdict bleu | Gravité retenue | Correctif rouge : avis |
|---|---|---|---|---|
| REG-P01 | Haute | Partiellement réfutée | Moyenne | Point 1 insuffisant : il laisse la suggestion dans le cas réel de l’utilisateur. Le point 2 est le correctif essentiel. Les points 3 à 5 sont utiles. Le détail du verdict, sans mode, est aussi à reformuler. |
| REG-P02 | Haute | Partiellement réfutée | Moyenne | `GatherShortfall` est incomplet : un fichier en incident du côté copié lui échappe (prouvé). Il faut conditionner la phrase à un indicateur prouvé du moteur (`ContentCovered`). |
| REG-P03 | Moyenne | Confirmée (nouveauté limitée au choix par défaut) | Moyenne | Points 1 à 3 corrects. Point 4 (supprimer des temporaires) à remplacer par une liste dans la confirmation. |
| REG-P04 | Basse | Confirmée | Basse | Correct. Prédicat commun avec `AddItems`. |
| REG-P05 | Moyenne | Partiellement réfutée | Basse | Points 1, 2 et 4 corrects. Point 3 dangereux tel que formulé : il ajouterait une confirmation fausse (prouvé). |
| REG-P06 | Basse | Confirmée | Basse | Correct, sans effet sur les tests CSV existants (vérifié par lecture). |
| REG-P07 | Basse | Partiellement réfutée (portée) | Basse | Le chemin « 1 : … » suit la convention existante des lignes « Retrouvé ailleurs ». Seul « SHA-256 (1) : Non calculé » est nouveau et inexact. |

---

## REG-P01 — « Comparer avec ce dossier » retire de la comparaison les fichiers à regrouper

- **Verdict :** partiellement réfutée. Les faits sont confirmés. La gravité est réduite, parce que le verdict final est exact pour les chemins affichés.
- **Gravité retenue :** moyenne. C’est la plus urgente du lot, car elle touche la configuration réelle de l’utilisateur.

### 1. Réalité : confirmée, y compris dans la forme du cas réel

**Seuils exacts de `FolderAlignment.Suggest`** (preuve par lecture, `<dépôt>\FolderChecker.Core\FolderAlignment.cs`) :
- `:14-16` : il faut au moins 3 lignes `Moved` (fichiers, `TargetPath` renseigné) et strictement plus de `Moved` que de lignes `Identical` de type fichier ;
- `:20-28` : un fichier vote pour un sous-dossier quand l’un des deux chemins relatifs est l’autre précédé de ce sous-dossier ;
- `:32` : le sous-dossier retenu doit recueillir plus de la moitié de `Moved + Identical`. Le nombre de fichiers des dossiers n’intervient jamais ;
- `:35` : le préfixe doit être non vide, sans composant `""`, `.` ni `..`.

`PASSATION.md` (ligne 291) annonce exactement cette règle : « plus de la moitié des fichiers appariés ». Le code est donc conforme à l’intention. Ce sont l’aide FR et EN (`MainWindow.xaml.cs:468` et `:473`, « la plupart des fichiers d’un dossier ») qui promettent davantage.

**Relance rouge** (`<scratch>\rouge-copie\out-align.txt`) : identique au rapport. On y lit `Suggest : Suggestion { InFolder2 = True, … Files = 3 }` pour 23 fichiers dans le 1, puis `Verdict gauche : Le dossier 1 contient tout le dossier 2 : Oui` après la restriction. `out-align1.txt` se termine par `VerdictTitle : Les deux dossiers sont identiques`.

**Cas réel de l’utilisateur.**
- Le scénario `align` (3 fichiers sur 23) est artificiel. Le défaut se produit pourtant aussi dans la configuration décrite par `PASSATION.md` : un dossier de travail face à un disque dont un sous-dossier est une ancienne sauvegarde, et qui contient bien d’autres fichiers (55 512 « absents du 1 » dans le scan réel).
- Programme `reel` (`<scratch>\out-reel.txt`). Données :
  - dossier 1 : 40 fichiers, `Projets\doc-01…38` et deux nouveaux ;
  - dossier 2 : `Sauvegarde\Projets\doc-01…38` et 60 photos hors de `Sauvegarde`.

```
Suggest : Suggestion { InFolder2 = True, Folder = <scratch>\data\reel\dst\Sauvegarde, Files = 38 }
Seuil rouge : garde la suggestion (38 × 2 > 40)
Avant restriction · regrouper dans le 1 : 60 fichier(s), 1 dossier(s), <scratch>\data\reel\dst → <scratch>\data\reel\src
Avant restriction · regrouper dans le 2 : 2 fichier(s), 1 dossier(s), <scratch>\data\reel\src → <scratch>\data\reel\dst
      Directory Projets  → <scratch>\data\reel\dst\Projets
      File Projets\nouveau-1.txt  → <scratch>\data\reel\dst\Projets\nouveau-1.txt
Dialogue IntoFolder1 · bandeau : 38 fichier(s) du dossier 1 sont rangés à l’identique dans « <scratch>\data\reel\dst\Sauvegarde ». Comparez le dossier 1 avec ce sous-dossier : ils y seront appariés chemin par chemin, et les copies arriveront à côté d’eux. · bouton « Comparer avec ce dossier » : présent
Dialogue IntoFolder2 · bandeau : 38 fichier(s) du dossier 1 sont rangés à l’identique dans « … ». … · bouton « Comparer avec ce dossier » : présent
Après « Comparer avec ce dossier » (<scratch>\data\reel\src ↔ <scratch>\data\reel\dst\Sauvegarde) : Moved=0 Identical=38 OnlySource=2 OnlyTarget=0 TargetFullyContained=True
Après restriction · regrouper dans le 1 : 0 fichier(s), 0 dossier(s), <scratch>\data\reel\dst\Sauvegarde → <scratch>\data\reel\src
Après restriction · regrouper dans le 2 : 2 fichier(s), 0 dossier(s), <scratch>\data\reel\src → <scratch>\data\reel\dst\Sauvegarde
      File Projets\nouveau-1.txt  → <scratch>\data\reel\dst\Sauvegarde\Projets\nouveau-1.txt
Fichiers du dossier 2 d’origine hors de la comparaison restreinte : 60
…
Fenêtre, comparaison restreinte · titre : Le dossier 1 contient tout le dossier 2
  gauche : Le dossier 1 contient tout le dossier 2 : Oui · droite : Le dossier 2 contient tout le dossier 1 : Non
  case 2 : <scratch>\data\reel\dst\Sauvegarde
  pied : 41 / 41 éléments · 40 fichiers dans le 1, 38 dans le 2 · …
```

Cette sortie montre que la suggestion a deux effets opposés selon le sens du regroupement :
- **Vers le 2 (le dossier qui contient le sous-dossier), elle est utile.** Sans restriction, les deux nouveaux fichiers arriveraient dans `dst\Projets`, une arborescence parallèle à la racine du disque. Après restriction, ils arrivent dans `dst\Sauvegarde\Projets`, à côté des autres. C’est bien le sens de « les copies arriveront à côté d’eux ».
- **Vers le 1, elle est inutile puis nuisible.** Sans restriction, le regroupement dans le 1 copie déjà les 60 photos et saute les 38 documents présents ailleurs : l’alignement n’apporte rien. Après restriction, il ne copie plus rien, et les 60 photos sortent de la comparaison.

Le bandeau est proposé dans les deux modes, avec le même texte.

**Ce que l’utilisateur voit.**
- La case 2 affiche le sous-dossier.
- Le pied passe de « 40 fichiers dans le 1, 98 dans le 2 » à « 38 dans le 2 ».
- Le bandeau donne le nombre de fichiers alignés (3 dans `align`, 38 ici), mais jamais le nombre de fichiers qui sortiront de la comparaison.
- Après la nouvelle comparaison, rien ne rappelle la restriction : `Suggest` du nouveau résultat vaut `null`.

### 2. Nouveauté

Elle est entière : `FolderAlignment.cs`, le bandeau (`CopyDialog.cs:30-42`) et le remplissage de la case (`MainWindow.Copy.cs:40-46`) sont nouveaux. Avant, il fallait taper soi-même un sous-dossier pour obtenir une comparaison restreinte.

### 3. Gravité

Trois éléments séparent le message trompeur d’une perte réelle :
- **Le verdict est exact pour les chemins affichés.** « Le dossier 1 contient tout le dossier 2 : Oui » porte sur `dst\Sauvegarde`, qui est dans la case 2.
- **L’application n’écrit rien hors de l’endroit prévu.** Après restriction, « Tout regrouper dans le 2 » dépose les fichiers dans le sous-dossier annoncé par le bandeau.
- **La perte exige une suppression manuelle hors de l’application** (formatage du disque, suppression du dossier d’origine), visant un dossier qui n’est pas celui affiché.

C’est le profil de DUP-01 : une sélection trompeuse, issue d’une recommandation de l’application, sans perte immédiate. D’où la gravité moyenne.

Le défaut reste sérieux. La recommandation vient de l’application elle-même, s’affiche dans le dialogue ouvert par « Tout regrouper », et vise précisément le dossier dont ce mode copie les fichiers. Dans le cas réel, ce sont 55 512 fichiers qui sortiraient de la comparaison.

### 4. Correctif

- **Point 1 (seuil rapporté au nombre de fichiers) : juste mais insuffisant.**
  - `seuils` (`<scratch>\out-seuils.txt`) :

    ```
    == align (rouge : 3 sur 23)            … seuil rouge (fix 1)  : null (3 × 2 ≤ 23)
    == align1 (rouge : le 2 entier sous 1\Ancien) … seuil rouge (fix 1)  : garde la suggestion (3 × 2 > 3)
    == reel (forme du cas réel …)          … seuil rouge (fix 1)  : garde la suggestion (38 × 2 > 40)
    == reel1 (miroir …)                    … seuil rouge (fix 1)  : garde la suggestion (38 × 2 > 40)
    ```

  - Le point 1 ne corrige que `align`. Dans `align1` et dans la forme du cas réel, la suggestion reste, à juste titre puisqu’elle est vraie.
  - Le test existant `GatherSuggestsTheSubfolder` (3 fichiers alignés sur 4) passe toujours : 6 > 4.
- **Point 2 (proposer seulement quand le dossier restreint est la destination) : correct, et c’est le correctif essentiel.**
  - `reel` montre qu’il garde le seul cas utile et supprime le cas nuisible.
  - Règle : `InFolder2` avec `IntoFolder2` ou `Forward`, `!InFolder2` avec `IntoFolder1` ou `Backward`. Masquer avec `Both` et `Resume`.
  - Créer le bandeau une fois, et basculer sa `Visibility` dans `direction.SelectionChanged`. `GatherSuggestsTheSubfolder` (mode `IntoFolder2`, suggestion `InFolder2`) reste vrai.
- **Point 3 (annoncer les fichiers exclus) : correct.** Il est utile même dans le bon sens, puisque les fichiers du 2 hors du sous-dossier ne sont plus couverts par les verdicts.
- **Point 4 (trace `_narrowedFrom`) : utile en défense en profondeur**, secondaire une fois le point 2 appliqué.
  - Affecter `_narrowedFrom` **après** `TextBox.Text = …` : `TextChanged` se déclenche pendant l’affectation et l’effacerait.
  - Garder le nombre de fichiers du premier résultat, pour pouvoir l’afficher ensuite.
- **Point 5 (aide) : correct.**
- **Manque : le détail du verdict de la fenêtre principale** (`MainWindow.xaml.cs:356`). Il ajoute `AlignmentText` sans connaître le mode et promet « les copies arriveront à côté d’eux ». Proposition :
  > « 38 fichier(s) du dossier 1 sont rangés à l’identique dans « … ». Pour regrouper dans le dossier 2 à côté d’eux, comparez le dossier 1 avec ce sous-dossier. Pour regrouper dans le dossier 1, gardez cette comparaison : les 60 fichier(s) du dossier 2 hors de ce sous-dossier en sortiraient. »

  `GatherSuggestsTheSubfolder` vérifie seulement que le détail nomme le sous-dossier : il reste vrai.
- **Doublon avec MOTEUR.md.** `Suggest` peut aussi désigner un dossier de la Corbeille (REG-M01) ou un temporaire (REG-M02) ; ces filtres sont du ressort du moteur.

---

## REG-P02 — « Rien à regrouper : ce dossier a déjà un exemplaire de chaque fichier » alors qu’un contenu n’y existe pas

- **Verdict :** partiellement réfutée (gravité).
- **Gravité retenue :** moyenne.

### 1. Réalité : confirmée, et un cas de plus

**Relance rouge.** `out-nothing.txt`, `out-blocked.txt` et `out-names.txt` sont identiques au rapport : la phrase s’affiche avec un plan vide, alors que `SHA-256 … présent … ? False` et `Un exemplaire … dans dst ? False`.

**Cas supplémentaire (`trou`, `<scratch>\out-trou.txt`).**
- Un fichier du 1, illisible pendant la comparaison et sans exemplaire dans le 2, déclenche aussi la phrase.
- Ce cas échappe au correctif rouge :

```
  OnlyTarget      File      autre.bin
  Identical       File      commun.txt
  Unverified      File      compta.bin
CanCreatePlan=True GatherFileCount(into2)=0
Plan regrouper dans le 2 : 0 élément(s)
GatherShortfall tel que proposé par le rouge (into2) : 0
Lignes du côté copié en Unverified (SourcePath renseigné) : 1
Récapitulatif : … ⏎ 0 fichier(s) · 0 o · 0 dossier(s) ⏎ Rien à regrouper : ce dossier a déjà un exemplaire de chaque fichier.
Incidents     : 1 élément(s) avec un incident (illisible, modifié pendant la comparaison…) écarté(s), …
Un exemplaire de compta.bin existe-t-il dans dst ? False
```

### 2. Nouveauté

La phrase est nouvelle (`CopyDialog.cs:143-146`). L’ancienne, « Aucun chemin manquant à copier dans ce sens. », était vraie dans tous ces cas.

### 3. Gravité : haute → moyenne

- **Chaque cas affiche, sur le même écran, une note précise qui contredit la phrase :**
  - `nothing` : « 1 contenu(s) différent(s) ou conflit(s) exclus … Ces écarts resteront à résoudre après la copie » ;
  - `blocked` et `trou` : la note orange des incidents ;
  - `names` : la ligne suivante du même récapitulatif, « … écarté(s) : à copier à la main ».
- **Le verdict de la fenêtre principale reste « Non »** ou « Non vérifié » dans tous les cas (sorties rouges et `trou`). L’application ne donne jamais de « Oui ».
- **La phrase n’apparaît qu’avec un plan vide.** Dans `nothing`, `blocked` et `trou`, `GatherFileCount` vaut 0 : aucun bouton « Tout regrouper » n’y mène. Il faut ouvrir « Compléter les dossiers… », qui présélectionne « Compléter les deux dossiers », puis choisir un regroupement à la main. Seuls les noms non portables (REG-P04) et les liens (REG-M04) y mènent par le bouton.
- **Rien n’est écrit.**

Le message est faux, au point de décision, dans des cas réalistes : c’est une garantie non tenue (moyenne), pas un faux verdict. Les cas les plus graves de cette même phrase n’ont aucune note voisine : Corbeille (REG-M01), fichiers vides (REG-M03). Ils relèvent du moteur et de l’autre équipe bleue.

### 4. Correctif rouge : incomplet. Correctif bleu proposé

- `GatherShortfall`, tel que proposé, ne compte que les lignes de `GatherRows`, c’est-à-dire les lignes `OnlySource` ou `OnlyTarget`.
  - Un fichier du côté copié devenu `Unverified` n’y figure pas : `trou` donne 0.
  - Les fichiers réels face à un lien (`Skipped`, REG-M04) n’y figurent pas non plus.
- **Correctif bleu : ne jamais déduire la complétude de ce qui reste dans le plan, mais d’un indicateur prouvé.**
  1. **Vers le 2.** `ComparisonResult.ContentCovered` existe depuis `b5399b7` (`ComparisonEngine.cs:220` ; `<scratch>\old\FolderChecker.Core\ComparisonEngine.cs:213`). Il exige `certain` (ni arrêt, ni erreur, contrôle final réussi) et un exemplaire de chaque fichier du 1. Près de vingt vérifications des tests moteur le couvrent déjà (`FolderChecker.Tests`). Il n’est affiché nulle part dans l’application (aucune occurrence dans `FolderChecker.App`). Mesuré dans `trou` :

     ```
       trou (incident côté 1)          : False
       nothing (version différente)    : False
       names (nom non portable)        : False
       fait (après un regroupement complet dans le 2, copie complete=True) : ContentCovered=True, plan into2=0 élément(s), …
     ```

  2. **Vers le 1.** Ajouter l’indicateur symétrique dans le moteur, par exemple `TargetContentCovered`. Il vaut `certain && MovedFilesSearched`, sans ligne `Different`, `Conflict`, `Unverified` ou `Skipped`, et chaque ligne `OnlyTarget` de type fichier y porte un `SourceCopyPath`.
  3. **Dans le dialogue.**
     - Si l’indicateur du mode est vrai, garder « Rien à regrouper : ce dossier a déjà un exemplaire de chaque fichier. »
     - Sinon : « Rien à regrouper automatiquement. Des fichiers restent sans exemplaire prouvé dans ce dossier (voir les notes ci-dessus) : ne supprimez pas l’autre dossier avant de les avoir traités. »
  4. **Dépendance.** Ces indicateurs reposent sur l’appariement par contenu. Les correctifs moteur REG-M01, REG-M02 et REG-M03 (Corbeille, temporaires et fichiers vides exclus des exemplaires) sont donc un préalable.
- **Tests.**
  - `GatherDialogSkipsWhatIsElsewhere` (`GatherUiTests.cs:106-113`) : `ContentCovered` y est vrai, puisque la photo est retrouvée ailleurs et la comparaison certaine. La phrase positive reste. Mieux vaut resserrer le test sur « a déjà un exemplaire de chaque fichier ».
  - Ajouter les fixtures `nothing`, `blocked`, `names` et `trou` : la phrase positive n’y apparaît jamais.

---

## REG-P03 — « Tout regrouper » contourne la reprise d’une copie inachevée

- **Verdict :** confirmée. La nouveauté se limite au choix par défaut ; les conséquences existaient déjà.
- **Gravité retenue :** moyenne.

### 1. Réalité

**Relance rouge** (`<scratch>\rouge-copie\out-resume.txt`) : identique au rapport.
- Le mode présélectionné est `IntoFolder2`, avec « nécessaire : 64,0 Mio » contre 63,0 Mio en reprise.
- Après la copie : `0 repris`, `référence encore … .partial ? False`, et le temporaire de 1 048 576 octets reste dans `dst`.

**Ce que le dialogue affiche** (`reprise`, `<scratch>\out-reprise.txt`). Tous les textes sont relevés. L’avertissement est bien présent, mais le bouton principal est « Regrouper dans le dossier 2 » :

```
Dialogue ouvert par « Tout regrouper dans le 2 » · mode : IntoFolder2
  …
  texte : Une copie incomplète est enregistrée. Reprenez-la pour conserver son avancement ; lancer une autre copie remplacera son journal.
Comparaison suivante · OnlyTarget .checkersan-e03292bd4e6345269af0a6fd09697967.partial · détail : Ce chemin existe uniquement dans le dossier 2. Fichier temporaire d’une copie Checker San interrompue : il n’est jamais recopié.
```

**Correction sur le scénario.** « Comme l’invite le verdict, il relance la comparaison » est inexact. Après un arrêt, le verdict dit « Utilisez « Compléter / reprendre… » pour poursuivre. » (`MainWindow.Copy.cs:83`). Tant qu’aucune comparaison n’est relancée, `_result` vaut `null` (`:51`) et aucun bouton « Tout regrouper » n’est visible. Le parcours reste réaliste après une relance de l’application, ou pour faire le point le lendemain, mais il passe par une comparaison que l’application ne demande pas.

### 2. Nouveauté : seulement la présélection

Le moteur de `b5399b7` produit exactement les mêmes conséquences quand l’utilisateur choisit à la main un autre mode que « Reprendre » (`<scratch>\out-ancien-reprise.txt`) :

```
b5399b7 · première copie (Compléter le dossier 2) : interrompue=True, temporaire .checkersan-11320073ce99466bb672b3dc77ee13ec.partial
b5399b7 · CanCreatePlan après l’arrêt = True
b5399b7 · nouvelle copie choisie à la main au lieu de « Reprendre » : complet=True, 16,777,229 octets écrits, 0 repris
b5399b7 · le journal référence encore .checkersan-11320073ce99466bb672b3dc77ee13ec.partial ? False
  dst : .checkersan-11320073ce99466bb672b3dc77ee13ec.partial (1,048,576 octets)
```

Ce qui est nouveau, c’est le choix par défaut, et ce changement est bien réel :
- dans `b5399b7`, `CopyDialog.cs:107` présélectionnait « Reprendre » dès qu’une copie inachevée existait ;
- aujourd’hui, `CopyDialog.cs:154-156` fait passer `initial` avant `canResume`.

### 3. Gravité : moyenne, maintenue

Aucun fichier de l’utilisateur n’est touché, et l’avertissement orange est affiché. Mais :
- l’avancement est perdu ;
- le temporaire orphelin peut atteindre la taille du fichier interrompu ;
- `SpaceNeeds` ne compte pas cet orphelin, qui peut à lui seul faire échouer le nouveau regroupement faute de place.

C’est un blocage sans perte. La gravité monte à haute en combinaison avec REG-M02 : quand le temporaire est complet, il compte comme exemplaire et le fichier n’est jamais regroupé.

### 4. Correctif

- **Points 1 à 3 : corrects.**
  - Pour le point 1, comparer les racines **dans les deux sens**. Un journal de regroupement dans le 1 a `SourceRoot` = dossier 2 (`OneWayPlan`, `CopyEngine.cs:116-124`). Réutiliser `ShowsFolders`, comme `MainWindow.Copy.cs:56`.
  - Aucun test existant n’est touché : tous construisent `CopyDialog(…, saved: null, …)`.
- **Point 4 : à remplacer.** Supprimer des fichiers dans la destination de l’utilisateur au remplacement d’un journal ajoute une suppression automatique dans un moteur qui n’en fait aucune. Suffisent :
  - la liste des temporaires de l’ancien plan, avec leur taille totale, dans la confirmation du point 2 ;
  - leur ajout au besoin d’espace (`SpaceNeeds`) ;
  - la note existante de la comparaison (COP-07), complétée par « il peut être supprimé ».

---

## REG-P04 — Le bouton compte des fichiers que le plan écarte ensuite

- **Verdict :** confirmée.
- **Gravité retenue :** basse.

**Preuve.** La relance rouge (`out-names.txt`) donne `GatherFileCount(into2) = 1 ; CreateGatherPlan(into2).FileCount = 0`, et le bouton affiche « Tout regrouper dans le 2 · 1 fichier(s) à copier… ». La lecture le confirme :
- `CopyEngine.cs:108-114` ne refait pas le contrôle `PortableName` de `:212` ;
- ce contrôle est purement textuel (`:670-677` : `StrictUtf8.GetByteCount`, `Path.GetFullPath` sans accès disque).

**Nouveauté.** Totale : `GatherFileCount` est nouveau.

**Correctif.** Correct. Extraire un prédicat commun, `Copyable(row, sourceRoot)` : en ligne seulement, sous un incident, temporaire, nom non portable. `GatherFileCount` et `AddItems` l’appellent tous deux. Les contrôles disque restent propres au plan (REG-M07). `GatherButtonsFollowTheVerdicts` n’utilise que des noms portables : il n’est pas touché.

---

## REG-P05 — Libellé, infobulle, aide et message de fin promettent plus que le regroupement ne fait

- **Verdict :** partiellement réfutée (gravité).
- **Gravité retenue :** basse.

### Réalité

Chaque affirmation a été vérifiée, par la relance (`out-nomoved.txt`, `out-after.txt`, `out-nothing.txt`) ou par lecture :
- **infobulle** (`Strings.Main.cs:58`) et **aide** (`:468`, `:473`) : vrai qu’une version différente n’est jamais copiée. Vrai aussi que, sans recherche des déplacés, `Tri\photo.jpg` est recopié ;
- **« sans recopier ce qu’il a déjà »** (`CopyDialog.cs:51-52`) : reste affiché quand `MovedFilesSearched` est faux ;
- **« à côté de chaque verdict « Non » »** : faux dans `nothing` et dans `after`, où un « Non » n’a pas de bouton ;
- **« Les chemins manquants ont été copiés… »** (`MainWindow.Copy.cs:83`) : `gathering` (`:48`) ne sert qu’au titre (`:60-61`) ;
- **« Seuls les chemins absents seront complétés. »** (`CopyDialog.cs:61-63`) : statique, affiché aussi en regroupement.

Un cas de plus confirme que « sans recopier ce qu’il a déjà » n’est pas absolu, même avec la recherche active : voir la piste 1 (`verrou1`).

### Nouveauté

Tous ces textes sont nouveaux. Le message de fin existait (`b5399b7`, `MainWindow.Copy.cs:69`) ; c’est son emploi après un regroupement qui est nouveau.

### Gravité : moyenne → basse

- **Chaque promesse excessive est corrigée sur l’écran où la copie se décide :**
  - par la note du mode (« La recherche des fichiers déplacés était désactivée : chaque fichier absent au même chemin sera copié… », relance `nomoved`) ;
  - par la note des contenus différents ;
  - par la note des incidents ;
  - par la ligne des noms.
- **Les effets sont sans perte :**
  - des copies en trop (`nomoved`, `verrou1`) ;
  - ou l’absence de confirmation après un regroupement complet (`after`). Le verdict « Non » y reste prudent.
- **La fausse complétude** (« Rien à regrouper… ») est traitée en REG-P02.

### Correctif

- **Points 1, 2 et 4 : corrects.**
  - Garder l’infobulle courte, par exemple : « Copie dans ce dossier les fichiers dont le contenu y manque, sans recopier ceux qu’il a déjà ailleurs (SHA-256). Les écarts non copiés sont listés avant la copie. »
  - Les libellés des modes 3 et 4 peuvent changer sans toucher aux index 0 à 2 dont dépendent les tests. Aucun test ne vérifie ces libellés, l’infobulle, l’aide ni le message de fin : recherche faite dans les projets de tests, seule « Rien à regrouper » est vérifiée.
- **Point 3 : dangereux tel que formulé.**
  - La condition proposée (« Non » + `GatherFileCount == 0` + `GatherShortfall == 0`) est remplie alors qu’un fichier du 1 n’a aucun exemplaire dans le 2.
  - `trou`, variante `trou2` (`<scratch>\out-trou.txt`) :

    ```
      OnlyTarget      File      autre.bin
      Unverified      File      compta.bin
      Moved           File      photo.jpg  [copie dans 2 : Tri\photo.jpg]
    …
    Verdict droit : Le dossier 2 contient tout le dossier 1 : Non · bouton « Tout regrouper dans le 2 » visible=False
    GatherFileCount(into2)=0 ; GatherShortfall rouge (into2)=0 ; ContentCovered=False
    => condition du correctif rouge 3 de REG-P05 (verdict « Non », GatherFileCount 0, shortfall 0) remplie : True ; un exemplaire de compta.bin dans dst : False
    ```

  - Le correctif rouge ajouterait donc « Le dossier 2 a un exemplaire de chaque fichier du dossier 1 » à côté d’un fichier qui n’en a aucun. Ce serait un faux verdict positif, de gravité haute.
  - La confirmation ne doit s’appuyer que sur `ContentCovered` (vers le 2) et sur son symétrique (vers le 1), qui exigent une comparaison certaine. Voir REG-P02.

---

## REG-P06 — L’écran affiche « Non », le rapport exporté « Non vérifié »

- **Verdict :** confirmée.
- **Gravité retenue :** basse.

**Preuve.** La relance rouge (`out-report.txt`) est identique : à l’écran, `Verdict gauche : … : Non` et `Verdict droit : … : Non` ; dans le CSV, deux fois `Non vérifié`.

**Nouveauté.** Elle vient de ce changement :
- avant, l’écran sortait aussi en « Non vérifié » dès la première ligne `Unverified` (diff de `MainWindow.xaml.cs`, ancien `ShowContainmentVerdicts`) ;
- `ReportWriter.cs:39-41` n’a pas changé.

L’écart est prudent : le rapport est plus faible que l’écran.

**Correctif.** Correct. Effet sur les tests vérifié par lecture :
- `CsvCompleteness` (`FolderChecker.App.Tests\Program.cs:121-123`) utilise `Sample`, sans `Finished` (`:554-561`). La règle à trois états y donne `null`, donc « Non vérifié » : le test reste vrai ;
- `UnverifiedReport` (`:233-245`) n’a qu’une ligne `Unverified`, sans ligne `OnlySource`, `Moved`, `Different` ou `Conflict`. Il reste « Non vérifié » ;
- `DirectionalReports` et `EmptyReport` sont certains : inchangés.

Dans le JSON, `SourceFullyContained` et `TargetFullyContained` valent `false` quand la comparaison est incertaine (`ComparisonEngine.cs:201-203`). Ajouter deux champs à trois états à l’objet d’enveloppe, et non à `ComparisonResult`, pour ne pas toucher l’aller-retour testé par `JsonCompleteness`.

---

## REG-P07 — Ligne « Absent du dossier 1 » : chemin « 1 : … » d’un autre fichier, SHA-256 « Non calculé »

- **Verdict :** partiellement réfutée (portée).
- **Gravité retenue :** basse.

**Ce qui n’est pas nouveau.** Une ligne « Retrouvé ailleurs » affiche déjà, depuis `b5399b7`, le chemin d’un autre fichier sous « 2 : » (`b5399b7`, `MainWindow.xaml.cs:389`). La nouvelle ligne suit la même convention, en miroir. Preuve, `moved` (`<scratch>\out-moved.txt`), sur les mêmes données que `after` :

```
--- Retrouvé ailleurs · photo.jpg
  Chemins affichés : 1 : <scratch>\data\moved\src\photo.jpg     |     2 : <scratch>\data\moved\dst\Tri\photo.jpg
  Infobulle        : … ⏎ SHA-256 (1) : 6105D6CC…3E3D ⏎ SHA-256 (2) : 6105D6CC…3E3D
--- Absent du dossier 1 · Tri\photo.jpg
  Chemins affichés : 1 : <scratch>\data\moved\src\photo.jpg     |     2 : <scratch>\data\moved\dst\Tri\photo.jpg
  Infobulle        : … ⏎ SHA-256 (1) : Non calculé ⏎ SHA-256 (2) : 6105D6CC…3E3D
  Hashes de la ligne : SourceHash=null TargetHash=renseigné
```

Les deux lignes affichent la même chose, et le statut ainsi que le détail (« Même contenu SHA-256 présent ailleurs dans le dossier 1 : photo.jpg ») disent qu’il s’agit d’un autre chemin.

**Ce qui est nouveau et inexact.** « SHA-256 (1) : Non calculé » : le SHA-256 de la copie a été calculé, et il est égal à `TargetHash`.

**Correctif.** Correct pour l’infobulle : « SHA-256 (copie dans 1) : {TargetHash} ». Ne pas remplir `SourceHash` dans le moteur : le CSV l’écrit dans la colonne « SHA-256 dossier 1 », et `CsvCompleteness` le compare.

Pour le libellé « 1 (même contenu, autre chemin) : … », l’appliquer aussi au côté « 2 : » des lignes « Retrouvé ailleurs », sinon les deux conventions divergent.

---

## Pistes non prouvées du rapport rouge

1. **Incident côté destination et recopie : confirmée par exécution, mais ce n’est pas un défaut.** `verrou1` (`<scratch>\out-verrou1.txt`) :

   ```
     OnlyTarget      File      Archives\facture-2024.pdf
     Unverified      File      facture.pdf
   GatherFileCount(into1)=1
   Plan regrouper dans le 1 : 1 fichier(s), 1 dossier(s), …
         File Archives\facture-2024.pdf  → <scratch>\data\verrou1\src\Archives\facture-2024.pdf
   Note incidents du dialogue : 1 élément(s) avec un incident … écarté(s) …
   ```

   - Le fichier est recopié parce que son jumeau du 1 était illisible. C’est le choix prudent : l’application ne peut pas prouver la présence, et ne coûte qu’une copie en trop.
   - La seule inexactitude est le libellé « sans recopier ce qu’il a déjà » (REG-P05).
2. **Double clic sur « Tout regrouper » : écartée, par mesure de géométrie et de délai, mais sans vraie souris.** `geometrie` (`<scratch>\out-geometrie.txt`) :
   - Méthode : fenêtre principale et dialogue placés selon `CenterOwner` ; recherche (`InputHitTest`) de l’élément du dialogue situé sous le centre, la gauche et la droite de chaque bouton.

     ```
     Délai de double-clic du système : 500 ms ; …
     1260×940 · GatherInto1Button … : centre → fond du dialogue ; gauche → hors du dialogue ; droite → texte « Seuls les chemins absents seront complétés. … »
     1260×940 · GatherInto2Button … : centre → fond du dialogue ; gauche → fond du dialogue ; droite → hors du dialogue
     920×640  · GatherInto1Button … : centre → liste de l’aperçu ; …
     1366×728 · GatherInto2Button … : centre → fond du dialogue ; gauche → liste de l’aperçu ; droite → hors du dialogue
     1920×1040 · GatherInto1Button … : centre → texte « Source : <scratch>\data\geometrie\dst ⏎ … » ; …
     Ouverture n°1 par Transfer(IntoFolder1) : Loaded à 159 ms, premier rendu à 252 ms
     Ouverture n°2 par Transfer(IntoFolder1) : Loaded à 82 ms, premier rendu à 160 ms
     ```

   - À ces quatre tailles, ni « Comparer avec ce dossier » ni la liste des modes ne se trouvent sous le bouton. On y trouve du texte, le fond du dialogue ou la liste de l’aperçu, tous sans effet.
   - Le dialogue n’est disposé qu’après 78 à 159 ms et rendu qu’après 145 à 252 ms. Pendant ce temps, `ShowDialog` a déjà désactivé la fenêtre principale, où tomberait le second clic.
3. **Dossier vide sous un dossier manquant non créé : confirmée par exécution.** Gravité basse (métadonnées).
   - Preuve : `videparent` (`<scratch>\out-videparent.txt`) :

     ```
       OnlySource      Directory D
       OnlySource      Directory D\E
       Moved           File      D\f.txt  [copie dans 2 : ailleurs\f-copie.txt]
     Plan regrouper dans le 2 : 1 fichier(s), 2 dossier(s), …
           Directory G  → …   Directory D\E  → …   File G\seul.txt  → …
     Copie : complete=True erreurs=0
       dst\D : existe · date 2026-10-28 (source 2001-01-01) · caché=False (source True) · dans le plan=False
       dst\D\E : existe · date 2001-01-01 (source 2001-01-01) · caché=True (source True) · dans le plan=True
     ```

   - Cause : `CopyEngine.cs:153-154` retient `D\E` mais pas `D`. `EnsureDirectory` (`:758-769`) crée `D` implicitement, sans ses dates ni ses attributs.
   - Nouveauté : propre au regroupement. `MissingRows` retient toujours les parents.
   - Correctif : dans `GatherRows`, ajouter à `holdingLacking` les ancêtres de chaque dossier manquant retenu parce qu’il ne contient aucun fichier.
4. **Copie modifiée pendant le scan : confirmée par lecture, doublon de REG-M08.**
   - `MarkUnverified` (`ComparisonEngine.cs:1164-1169`) passe la ligne en `Unverified` et garde `SourceCopyPath`. « 1 : … » et « Ouvrir dans le 1 » désignent alors la copie modifiée.
   - Le correctif de REG-M08 la règle aussi : retirer `SourceCopyPath` sans changer le statut.
5. **Reprise d’un regroupement d’autres dossiers : confirmée par lecture, antérieure au changement.**
   - `MainWindow.Copy.cs:56-57` est identique à `b5399b7` (`MainWindow.Copy.cs:43-44`). Le même renversement existait pour « Compléter le dossier 1 ».
   - Gravité basse : les verdicts suivants portent sur les cases affichées.

## Doublons avec MOTEUR.md

| PARCOURS | MOTEUR | Recouvrement | Conséquence |
|---|---|---|---|
| REG-P02 | REG-M04 (et la phrase citée par REG-M01, REG-M03) | Même phrase fausse de `CopyDialog.cs:143-146`. REG-M04 ajoute les liens (`Skipped`) ; REG-P02 ajoute les versions différentes et les incidents ; la présente revue ajoute un fichier en incident du côté copié (`trou`). | Un seul correctif : la phrase positive conditionnée à `ContentCovered` et à son symétrique, après les correctifs moteur M01 à M03. |
| REG-P04 | REG-M04 | Même écart `GatherFileCount` / plan sur `PortableName`. | Un seul prédicat commun. |
| REG-P03 | REG-M02 | REG-M02 entre par le parcours de REG-P03 : le bouton ouvre le regroupement au lieu de la reprise. Les causes sont distinctes : choix par défaut d’un côté, temporaire complet compté comme exemplaire de l’autre. | Le point 1 de REG-P03 ferme l’entrée par le bouton, mais pas le choix manuel : le correctif moteur de REG-M02 reste nécessaire. |
| REG-P01 | REG-M01, REG-M02 | `FolderAlignment.Suggest` peut proposer un dossier de la Corbeille ou situé sous un temporaire. | Les filtres d’entrée vont dans le moteur. La règle mode/destination (REG-P01, point 2) s’ajoute. |
| REG-P05 (recherche des déplacés désactivée) | REG-M09 | Même cause, `MovedFilesSearched` faux, sur deux surfaces : dialogue et CLI. | Même texte de note dans les deux. |
| Piste 4 | REG-M08 | Même ligne de `MarkUnverified`. | Correctif de REG-M08. |

namespace FolderChecker.App;

public static class DuplicateStrings
{
    public static IReadOnlyDictionary<string, (string Fr, string En)> Values { get; } =
        new Dictionary<string, (string Fr, string En)>
        {
            ["Dup_Title"] = ("Doublons dans un dossier", "Duplicates in one folder"),
            ["Dup_Subtitle"] = ("Tous les fichiers et sous-dossiers · contenu identique confirmé par SHA-256", "All files and subfolders · identical contents confirmed by SHA-256"),
            ["Dup_FolderLabel"] = ("DOSSIER À ANALYSER", "FOLDER TO SCAN"),
            ["Dup_FolderName"] = ("Dossier à analyser", "Folder to scan"),
            ["Dup_FolderHint"] = ("Chemin local, lecteur réseau ou \\\\PC\\Partage\\Dossier", "Local path, network drive or \\\\PC\\Share\\Folder"),
            ["Dup_Browse"] = ("Parcourir…", "Browse…"),
            ["Dup_ReuseHint"] = ("Reprend aussi les vérifications terminées avant une interruption, après contrôle de chaque fichier.", "Also resumes checks completed before an interruption, after validating each file."),
            ["Dup_Reuse"] = ("Réutiliser les vérifications précédentes · reprise après interruption", "Reuse previous checks · resume after interruption"),
            ["Dup_RetryHint"] = ("3 nouvelles tentatives en cas de coupure réseau.", "3 retries after a network interruption."),
            ["Dup_Stop"] = ("Arrêter et garder", "Stop and save"),
            ["Dup_Scan"] = ("Analyser / reprendre", "Scan / resume"),
            ["Dup_InitialStatus"] = ("Rechercher des copies identiques", "Find identical copies"),
            ["Dup_InitialStats"] = ("Les tailles et quelques blocs écartent les fichiers différents avant la lecture complète.", "File sizes and sample blocks rule out different files before reading the full contents."),
            ["Dup_Issues"] = ("Détails et incidents", "Details and issues"),
            ["Dup_RemoveColumn"] = ("RETIRER", "REMOVE"),
            ["Dup_GroupColumn"] = ("GROUPE", "GROUP"),
            ["Dup_PathColumn"] = ("CHEMIN RELATIF", "RELATIVE PATH"),
            ["Dup_SizeColumn"] = ("TAILLE", "SIZE"),
            ["Dup_ChoiceColumn"] = ("CHOIX", "CHOICE"),
            ["Dup_InitialEmpty"] = ("Les groupes de fichiers identiques apparaîtront ici.", "Groups of identical files will appear here."),
            ["Dup_SelectExtras"] = ("Sélectionner les copies supplémentaires", "Select extra copies"),
            ["Dup_SelectExtrasHint"] = ("Garde le premier chemin par ordre alphabétique dans chaque groupe. Vérifiez ce choix avant de continuer.", "Keeps the first path alphabetically in each group. Review this choice before continuing."),
            ["Dup_ClearSelection"] = ("Tout désélectionner", "Deselect all"),
            ["Dup_Reveal"] = ("Voir le fichier choisi", "Show selected file"),
            ["Dup_NoSelection"] = ("Rien n’est sélectionné. Gardez au moins un exemplaire par groupe.", "Nothing selected. Keep at least one copy in each group."),
            ["Dup_RemovalModeName"] = ("Mode de retrait", "Removal mode"),
            ["Dup_QuarantineChoice"] = ("Mettre à l’écart · réversible, sans libérer d’espace", "Move aside · reversible, does not free space"),
            ["Dup_PermanentChoice"] = ("Supprimer définitivement · irréversible", "Delete permanently · irreversible"),
            ["Dup_ReviewSelection"] = ("Vérifier la sélection…", "Review selection…"),
            ["Dup_QuarantineHint"] = ("La mise à l’écart déplace les copies dans .CheckerSan-Quarantine, sous ce dossier. Leur arborescence est conservée pour une restauration manuelle.", "Moving aside places copies in .CheckerSan-Quarantine inside this folder. Their folder structure is kept for manual restoration."),
            ["Dup_OpenQuarantine"] = ("Ouvrir la mise à l’écart", "Open files moved aside"),
            ["Dup_Close"] = ("Fermer", "Close"),
        };
}

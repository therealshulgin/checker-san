using System.ComponentModel;
using FolderChecker.Core;

namespace FolderChecker.App;

public sealed class DuplicateRowView(int groupNumber, DuplicateFile file) : INotifyPropertyChanged
{
    private bool _isSelected;
    public int GroupNumber { get; } = groupNumber;
    public DuplicateFile File { get; } = file;
    public string RelativePath => File.RelativePath;
    public string SizeLabel => RowView.FormatSize(File.Size);
    public string SelectionLabel => "Retirer " + File.RelativePath;
    public string ActionLabel => IsSelected ? "À retirer" : "Conservé";
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActionLabel)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

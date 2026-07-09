using CommunityToolkit.Mvvm.ComponentModel;

namespace GlassTodo.ViewModels;

/// <summary>Anything that can receive a due date / reminder from the shared due picker.</summary>
public interface IDueTarget
{
    DateTime? DueAt { get; set; }
    DateTime? RemindAt { get; set; }
}

public enum ChipKind
{
    Today,
    All,
    List,
    Add,
}

public partial class ChipViewModel : ObservableObject
{
    public ChipKind Kind { get; }
    public Guid ListId { get; }
    public bool IsDefaultList { get; set; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _colorHex = "#5B9DFF";
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editText = "";

    public bool IsUserList => Kind == ChipKind.List;
    public bool IsAddChip => Kind == ChipKind.Add;

    public ChipViewModel(ChipKind kind, Guid listId = default)
    {
        Kind = kind;
        ListId = listId;
    }
}

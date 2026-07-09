using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GlassTodo.Models;

namespace GlassTodo.ViewModels;

public partial class TaskItemViewModel : ObservableObject, IDueTarget
{
    private readonly MainViewModel _owner;
    private bool _syncing;

    public TodoTask Model { get; }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private bool _isDone;
    [ObservableProperty] private Priority _priority;
    [ObservableProperty] private DateTime? _dueAt;
    [ObservableProperty] private DateTime? _remindAt;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editText = "";
    [ObservableProperty] private bool _isLeaving;
    [ObservableProperty] private bool _showListDot;
    [ObservableProperty] private string _listColorHex = "#5B9DFF";

    public TaskItemViewModel(MainViewModel owner, TodoTask model)
    {
        _owner = owner;
        Model = model;
        _syncing = true;
        Title = model.Title;
        IsDone = model.IsDone;
        Priority = model.Priority;
        DueAt = model.DueAt;
        RemindAt = model.RemindAt;
        _syncing = false;
    }

    // ----- computed display -----

    public string DueText
    {
        get
        {
            if (DueAt is not { } d) return "";
            int days = (d.Date - DateTime.Today).Days;
            if (days < 0) return $"已过期 · {d:M月d日}";
            return days switch
            {
                0 => "今天",
                1 => "明天",
                2 => "后天",
                <= 6 => "周" + "日一二三四五六"[(int)d.DayOfWeek],
                _ => d.ToString("M月d日"),
            };
        }
    }

    public bool IsOverdue => !IsDone && DueAt is { } d && d.Date < DateTime.Today;
    public bool HasDue => DueAt != null;
    public bool HasReminder => RemindAt != null;
    public string RemindTimeText => RemindAt is { } r ? r.ToString("HH:mm") : "";
    public bool HasMeta => HasDue || HasReminder || ShowListDot;

    // ----- change propagation to the model -----

    partial void OnTitleChanged(string value)
    {
        if (_syncing) return;
        Model.Title = value;
        _owner.RequestSave();
    }

    partial void OnPriorityChanged(Priority value)
    {
        if (_syncing) return;
        Model.Priority = value;
        _owner.RequestSave();
        _owner.OnTaskPriorityChanged(this); // 今天视图按优先级参与排序
    }

    partial void OnIsDoneChanged(bool value)
    {
        OnPropertyChanged(nameof(IsOverdue));
        if (_syncing) return;
        _owner.OnTaskDoneChanged(this);
    }

    partial void OnDueAtChanged(DateTime? value)
    {
        if (!_syncing) Model.DueAt = value;
        RaiseMetaChanged();
        if (!_syncing) _owner.OnTaskScheduleChanged(this);
    }

    partial void OnRemindAtChanged(DateTime? value)
    {
        if (!_syncing)
        {
            Model.RemindAt = value;
            Model.ReminderFiredAt = null; // a changed reminder may fire again
        }
        RaiseMetaChanged();
        if (!_syncing) _owner.OnTaskScheduleChanged(this);
    }

    partial void OnShowListDotChanged(bool value) => OnPropertyChanged(nameof(HasMeta));

    /// <summary>跨天后刷新依赖“今天”的展示（到期文案、过期红标）。</summary>
    public void RefreshDayDependent() => RaiseMetaChanged();

    private void RaiseMetaChanged()
    {
        OnPropertyChanged(nameof(DueText));
        OnPropertyChanged(nameof(IsOverdue));
        OnPropertyChanged(nameof(HasDue));
        OnPropertyChanged(nameof(HasReminder));
        OnPropertyChanged(nameof(RemindTimeText));
        OnPropertyChanged(nameof(HasMeta));
    }

    // ----- commands -----

    [RelayCommand]
    private void Delete() => _owner.DeleteTask(this);

    [RelayCommand]
    private void CyclePriority() => Priority = Priority switch
    {
        Priority.None => Priority.Low,
        Priority.Low => Priority.Medium,
        Priority.Medium => Priority.High,
        _ => Priority.None,
    };

    [RelayCommand]
    private void BeginEdit()
    {
        EditText = Title;
        IsEditing = true;
    }

    [RelayCommand]
    private void CommitEdit()
    {
        if (!IsEditing) return;
        string t = EditText.Trim();
        if (t.Length > 0) Title = t;
        IsEditing = false;
    }

    [RelayCommand]
    private void CancelEdit() => IsEditing = false;
}

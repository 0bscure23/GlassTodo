using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GlassTodo.ViewModels;

public sealed class CalendarDayViewModel
{
    public DateTime Date { get; init; }
    public string Label => Date.Day.ToString();
    public bool InMonth { get; init; }
    public bool IsToday { get; init; }
    public bool IsSelected { get; init; }
}

/// <summary>
/// Backs the shared due-date / reminder popup: a mini month calendar, quick chips,
/// and a freely editable reminder time. Writes through to whichever
/// <see cref="IDueTarget"/> it was opened for (a task row or the quick-add compose slot).
/// </summary>
public partial class DuePickerViewModel : ObservableObject
{
    private readonly Action _save;
    private IDueTarget? _target;
    private bool _syncing;

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private bool _reminderOn;
    /// <summary>Canonical reminder time "HH:mm".</summary>
    [ObservableProperty] private string _selectedTime = "09:00";
    /// <summary>Free-typed textbox buffer, committed on Enter / focus loss.</summary>
    [ObservableProperty] private string _editTimeText = "09:00";
    [ObservableProperty] private DateTime _displayMonth = FirstOfMonth(DateTime.Today);
    [ObservableProperty] private List<CalendarDayViewModel> _days = new();

    public string MonthTitle => DisplayMonth.ToString("yyyy年M月");

    public string CurrentText =>
        _target?.DueAt is { } d
            ? $"{d:M月d日}" + (_target?.RemindAt is { } r ? $" · {r:HH:mm} 提醒" : "")
            : "未设置日期";

    public bool HasDue => _target?.DueAt != null;
    public bool IsTodayQuick => _target?.DueAt?.Date == DateTime.Today;
    public bool IsTomorrowQuick => _target?.DueAt?.Date == DateTime.Today.AddDays(1);
    public bool IsMondayQuick => _target?.DueAt?.Date == NextMonday();

    public DuePickerViewModel(Action save)
    {
        _save = save;
    }

    public void Open(IDueTarget target)
    {
        _target = target;
        _syncing = true;
        SelectedTime = target.RemindAt is { } r ? r.ToString("HH:mm") : "09:00";
        EditTimeText = SelectedTime;
        ReminderOn = target.RemindAt != null;
        _syncing = false;

        var month = FirstOfMonth(target.DueAt ?? DateTime.Today);
        if (DisplayMonth != month) DisplayMonth = month; // change callback rebuilds
        else RebuildDays();
        RaiseAll();
        IsOpen = true;
    }

    public void Close() => IsOpen = false;

    // ------------------------------------------------------------------
    //  calendar
    // ------------------------------------------------------------------

    partial void OnDisplayMonthChanged(DateTime value)
    {
        OnPropertyChanged(nameof(MonthTitle));
        RebuildDays();
    }

    private void RebuildDays()
    {
        var today = DateTime.Today;
        var selected = _target?.DueAt?.Date;
        int offset = ((int)DisplayMonth.DayOfWeek + 6) % 7; // Monday-first grid
        var start = DisplayMonth.AddDays(-offset);

        var list = new List<CalendarDayViewModel>(42);
        for (int i = 0; i < 42; i++)
        {
            var d = start.AddDays(i);
            list.Add(new CalendarDayViewModel
            {
                Date = d,
                InMonth = d.Month == DisplayMonth.Month,
                IsToday = d == today,
                IsSelected = selected == d,
            });
        }
        Days = list;
    }

    [RelayCommand]
    private void PrevMonth() => DisplayMonth = DisplayMonth.AddMonths(-1);

    [RelayCommand]
    private void NextMonth() => DisplayMonth = DisplayMonth.AddMonths(1);

    [RelayCommand]
    private void SelectDay(CalendarDayViewModel? day)
    {
        if (day != null) SetDue(day.Date);
    }

    [RelayCommand]
    private void SetQuick(string param)
    {
        DateTime due = param == "monday" ? NextMonday() : DateTime.Today.AddDays(int.Parse(param));
        SetDue(due);
    }

    [RelayCommand]
    private void ClearDue()
    {
        if (_target == null) return;
        _syncing = true;
        ReminderOn = false;
        _syncing = false;
        _target.RemindAt = null;
        _target.DueAt = null;
        _save();
        RebuildDays();
        RaiseAll();
    }

    // ------------------------------------------------------------------
    //  reminder time
    // ------------------------------------------------------------------

    [RelayCommand]
    private void SetTime(string time)
    {
        SelectedTime = time;
        EditTimeText = time;
        if (!ReminderOn) ReminderOn = true; // change callback applies
        else ApplyReminder();
    }

    /// <summary>Commits the free-typed time. Accepts "8:30" / "08:30" / "830" / "2145".</summary>
    public void CommitTimeText()
    {
        string s = EditTimeText.Trim();
        if (s.Length is 3 or 4 && s.All(char.IsDigit))
            s = s[..^2] + ":" + s[^2..];
        if (s.Contains(':') && TimeSpan.TryParse(s, out var t)
            && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))
        {
            SelectedTime = t.ToString(@"hh\:mm");
            if (!ReminderOn) ReminderOn = true; // typing a time implies wanting the reminder
            else ApplyReminder();
        }
        EditTimeText = SelectedTime; // always snap back to the canonical value
    }

    partial void OnReminderOnChanged(bool value)
    {
        if (_syncing) return;
        ApplyReminder();
    }

    // ------------------------------------------------------------------
    //  write-through
    // ------------------------------------------------------------------

    private void SetDue(DateTime date)
    {
        if (_target == null) return;
        _target.DueAt = date.Date;
        var month = FirstOfMonth(date);
        if (DisplayMonth != month) DisplayMonth = month;
        else RebuildDays();
        if (ReminderOn) ApplyReminder();
        else _save();
        RaiseAll();
    }

    private void ApplyReminder()
    {
        if (_target == null) return;
        if (ReminderOn)
        {
            if (_target.DueAt == null)
            {
                _target.DueAt = DateTime.Today; // enabling a reminder implies a date
                RebuildDays();
            }
            if (TimeSpan.TryParse(SelectedTime, out var t))
                _target.RemindAt = _target.DueAt.Value.Date + t;
        }
        else
        {
            _target.RemindAt = null;
        }
        _save();
        RaiseAll();
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(CurrentText));
        OnPropertyChanged(nameof(HasDue));
        OnPropertyChanged(nameof(IsTodayQuick));
        OnPropertyChanged(nameof(IsTomorrowQuick));
        OnPropertyChanged(nameof(IsMondayQuick));
    }

    private static DateTime FirstOfMonth(DateTime d) => new(d.Year, d.Month, 1);

    private static DateTime NextMonday()
    {
        var today = DateTime.Today;
        int add = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        if (add == 0) add = 7;
        return today.AddDays(add);
    }
}

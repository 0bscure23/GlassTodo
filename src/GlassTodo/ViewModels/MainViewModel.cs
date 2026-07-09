using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GlassTodo.Models;
using GlassTodo.Services;

namespace GlassTodo.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private static readonly string[] ListColorCycle =
    {
        "#5B9DFF", "#5BB98B", "#FFC53D", "#FF6B6B", "#B98CFF", "#56C2E6", "#FF9F5B", "#F272B6",
    };

    private readonly JsonStore<AppData> _data;
    private readonly JsonStore<AppSettings> _settingsStore;
    private readonly Dictionary<Guid, TaskItemViewModel> _vmCache = new();
    private readonly System.Windows.Threading.DispatcherTimer _midnightTimer = new();
    private DateTime _today = DateTime.Today;

    private string _currentView = "today"; // "today" | "all" | list GUID string
    private Guid _defaultListId;

    public AppSettings Settings => _settingsStore.Data;

    public ObservableCollection<ChipViewModel> Chips { get; } = new();
    public ObservableCollection<TaskItemViewModel> Pending { get; } = new();
    public ObservableCollection<TaskItemViewModel> Completed { get; } = new();
    public DuePickerViewModel DuePicker { get; }
    public ComposeDueProxy ComposeTarget { get; }

    [ObservableProperty] private string _quickAddText = "";
    [ObservableProperty] private Priority _composePriority = Priority.None;
    [ObservableProperty] private DateTime? _composeDueAt;
    [ObservableProperty] private DateTime? _composeRemindAt;
    [ObservableProperty] private bool _isPinned;
    [ObservableProperty] private bool _canReorder;
    [ObservableProperty] private bool _isSettingsOpen;
    /// <summary>液态玻璃外观（光斑跟随 + 弹性动画）开关，由设置驱动。</summary>
    [ObservableProperty] private bool _isLiquidStyle = true;
    [ObservableProperty] private SettingsViewModel? _settingsVM;

    /// <summary>Raised whenever a due date / reminder changed so the reminder scheduler can rescan.</summary>
    public event Action? ScheduleChanged;

    public string TodayText
    {
        get
        {
            var t = DateTime.Today;
            return $"{t:M月d日} 周{"日一二三四五六"[(int)t.DayOfWeek]}";
        }
    }

    public bool ComposeHasSchedule => ComposeDueAt != null;

    public MainViewModel(JsonStore<AppData> data, JsonStore<AppSettings> settingsStore, bool firstRun)
    {
        _data = data;
        _settingsStore = settingsStore;
        DuePicker = new DuePickerViewModel(RequestSave);
        ComposeTarget = new ComposeDueProxy(this);

        EnsureDefaultList();
        if (firstRun) SeedWelcome();

        _isPinned = Settings.Pinned;
        _currentView = NormalizeView(Settings.LastSelectedView);
        BuildChips();
        RebuildView();

        _midnightTimer.Tick += (_, _) => CheckDayRollover();
        ScheduleMidnightTimer();
    }

    private void ScheduleMidnightTimer()
    {
        _midnightTimer.Interval = DateTime.Today.AddDays(1).AddSeconds(5) - DateTime.Now;
        _midnightTimer.Start();
    }

    /// <summary>
    /// 跨天刷新：头部日期、每行到期文案/过期红标、今天视图的成员与排序。
    /// 由午夜定时器触发；睡眠唤醒后由 App 再调一次以防定时器漂移。
    /// </summary>
    public void CheckDayRollover()
    {
        _midnightTimer.Stop();
        if (_today != DateTime.Today)
        {
            _today = DateTime.Today;
            OnPropertyChanged(nameof(TodayText));
            foreach (var vm in _vmCache.Values) vm.RefreshDayDependent();
            RebuildView();
        }
        ScheduleMidnightTimer();
    }

    public void RequestSave() => _data.RequestSave();

    // ------------------------------------------------------------------
    //  bootstrapping
    // ------------------------------------------------------------------

    private void EnsureDefaultList()
    {
        var lists = _data.Data.Lists;
        if (lists.Count == 0)
        {
            lists.Add(new TodoList { Name = "默认清单", IsDefault = true, SortOrder = 0 });
            _data.RequestSave();
        }
        var def = lists.FirstOrDefault(l => l.IsDefault) ?? lists.OrderBy(l => l.SortOrder).First();
        def.IsDefault = true;
        _defaultListId = def.Id;
    }

    private void SeedWelcome()
    {
        string[] titles =
        {
            "把鼠标移到屏幕右边缘，即可呼出琉璃清单",
            "按 Alt+Q 随时呼出并直接输入新任务",
            "双击任务可编辑，悬停可设优先级、日期或删除",
        };
        long order = 0;
        foreach (string t in titles)
        {
            _data.Data.Tasks.Add(new TodoTask
            {
                ListId = _defaultListId,
                Title = t,
                DueAt = DateTime.Today,
                SortOrder = order += 1000,
            });
        }
        _data.RequestSave();
    }

    private string NormalizeView(string view)
    {
        if (view is "today" or "all") return view;
        return Guid.TryParse(view, out var id) && _data.Data.Lists.Any(l => l.Id == id) ? view : "today";
    }

    // ------------------------------------------------------------------
    //  chips / views
    // ------------------------------------------------------------------

    public void BuildChips()
    {
        Chips.Clear();
        Chips.Add(new ChipViewModel(ChipKind.Today) { Name = "今天" });
        Chips.Add(new ChipViewModel(ChipKind.All) { Name = "全部" });
        foreach (var list in _data.Data.Lists.OrderBy(l => l.SortOrder))
        {
            Chips.Add(new ChipViewModel(ChipKind.List, list.Id)
            {
                Name = list.Name,
                ColorHex = list.ColorAccent,
                IsDefaultList = list.IsDefault,
            });
        }
        Chips.Add(new ChipViewModel(ChipKind.Add) { Name = "" });
        SyncChipSelection();
    }

    private void SyncChipSelection()
    {
        foreach (var c in Chips)
        {
            c.IsSelected = c.Kind switch
            {
                ChipKind.Today => _currentView == "today",
                ChipKind.All => _currentView == "all",
                ChipKind.List => _currentView == c.ListId.ToString(),
                _ => false,
            };
        }
    }

    [RelayCommand]
    private void SelectChip(ChipViewModel chip)
    {
        if (chip.Kind == ChipKind.Add)
        {
            chip.EditText = "";
            chip.IsEditing = true;
            return;
        }
        _currentView = chip.Kind switch
        {
            ChipKind.Today => "today",
            ChipKind.All => "all",
            _ => chip.ListId.ToString(),
        };
        Settings.LastSelectedView = _currentView;
        _settingsStore.RequestSave();
        SyncChipSelection();
        RebuildView();
    }

    private Guid CurrentListIdForAdd =>
        Guid.TryParse(_currentView, out var id) ? id : _defaultListId;

    private bool IsSmartView => _currentView is "today" or "all";

    // ------------------------------------------------------------------
    //  view building
    // ------------------------------------------------------------------

    public void RebuildView()
    {
        CanReorder = !IsSmartView;
        var tasks = _data.Data.Tasks;
        var listOrder = _data.Data.Lists.OrderBy(l => l.SortOrder).Select((l, i) => (l.Id, i))
            .ToDictionary(x => x.Id, x => x.i);

        IEnumerable<TodoTask> pending;
        IEnumerable<TodoTask> completed;

        if (_currentView == "today")
        {
            var today = DateTime.Today;
            pending = tasks.Where(t => !t.IsDone && t.DueAt != null && t.DueAt.Value.Date <= today)
                .OrderBy(t => t.DueAt!.Value.Date)
                .ThenByDescending(t => (int)t.Priority)
                .ThenBy(t => t.SortOrder);
            completed = tasks.Where(t => t.IsDone && t.CompletedAt?.Date == today);
        }
        else if (_currentView == "all")
        {
            pending = tasks.Where(t => !t.IsDone)
                .OrderBy(t => listOrder.GetValueOrDefault(t.ListId, int.MaxValue))
                .ThenBy(t => t.SortOrder);
            completed = tasks.Where(t => t.IsDone);
        }
        else
        {
            var id = CurrentListIdForAdd;
            pending = tasks.Where(t => !t.IsDone && t.ListId == id).OrderBy(t => t.SortOrder);
            completed = tasks.Where(t => t.IsDone && t.ListId == id);
        }

        Pending.Clear();
        foreach (var t in pending) Pending.Add(GetVm(t));
        Completed.Clear();
        foreach (var t in completed.OrderByDescending(t => t.CompletedAt)) Completed.Add(GetVm(t));
    }

    private TaskItemViewModel GetVm(TodoTask task)
    {
        if (!_vmCache.TryGetValue(task.Id, out var vm))
        {
            vm = new TaskItemViewModel(this, task);
            _vmCache[task.Id] = vm;
        }
        vm.IsLeaving = false;
        vm.ShowListDot = IsSmartView;
        vm.ListColorHex = _data.Data.Lists.FirstOrDefault(l => l.Id == task.ListId)?.ColorAccent ?? "#5B9DFF";
        return vm;
    }

    private bool MatchesPendingFilter(TodoTask t)
    {
        if (t.IsDone) return false;
        return _currentView switch
        {
            "today" => t.DueAt != null && t.DueAt.Value.Date <= DateTime.Today,
            "all" => true,
            _ => t.ListId == CurrentListIdForAdd,
        };
    }

    // ------------------------------------------------------------------
    //  task operations
    // ------------------------------------------------------------------

    [RelayCommand]
    private void AddTask()
    {
        string title = QuickAddText.Trim();
        if (title.Length == 0) return;

        var task = new TodoTask
        {
            ListId = CurrentListIdForAdd,
            Title = title,
            Priority = ComposePriority,
            DueAt = ComposeDueAt ?? (_currentView == "today" ? DateTime.Today : null),
            RemindAt = ComposeRemindAt,
            SortOrder = NextTopSortOrder(CurrentListIdForAdd),
        };
        _data.Data.Tasks.Add(task);

        QuickAddText = "";
        ComposePriority = Priority.None;
        ComposeDueAt = null;
        ComposeRemindAt = null;
        RequestSave();
        ScheduleChanged?.Invoke();

        if (!MatchesPendingFilter(task)) return;
        int index = FindPendingInsertIndex(task);
        Pending.Insert(index, GetVm(task));
    }

    private long NextTopSortOrder(Guid listId)
    {
        var orders = _data.Data.Tasks.Where(t => !t.IsDone && t.ListId == listId).Select(t => t.SortOrder).ToList();
        return orders.Count == 0 ? 0 : orders.Min() - 1000;
    }

    private int FindPendingInsertIndex(TodoTask task)
    {
        if (_currentView == "today")
        {
            // ordered by due date — a freshly added "today" task goes after existing overdue/today items
            for (int i = 0; i < Pending.Count; i++)
                if (Pending[i].Model.DueAt?.Date > task.DueAt?.Date) return i;
            return Pending.Count;
        }
        if (_currentView == "all")
        {
            for (int i = 0; i < Pending.Count; i++)
                if (Pending[i].Model.ListId == task.ListId) return i;
            return 0;
        }
        return 0;
    }

    public async void OnTaskDoneChanged(TaskItemViewModel item)
    {
        item.Model.IsDone = item.IsDone;
        item.Model.CompletedAt = item.IsDone ? DateTime.Now : null;
        RequestSave();

        if (item.IsDone && Pending.Contains(item))
        {
            await Task.Delay(500);
            if (!item.IsDone) return;
            item.IsLeaving = true;
            await Task.Delay(280);
            item.IsLeaving = false;
            if (!item.IsDone) return;
            Pending.Remove(item);
            if (!Completed.Contains(item)) Completed.Insert(0, item);
        }
        else if (!item.IsDone && Completed.Contains(item))
        {
            Completed.Remove(item);
            if (MatchesPendingFilter(item.Model) && !Pending.Contains(item))
                Pending.Insert(FindPendingInsertIndex(item.Model), item);
        }
    }

    public async void DeleteTask(TaskItemViewModel item)
    {
        item.IsLeaving = true;
        await Task.Delay(260);
        Pending.Remove(item);
        Completed.Remove(item);
        _vmCache.Remove(item.Model.Id);
        _data.Data.Tasks.Remove(item.Model);
        RequestSave();
    }

    [RelayCommand]
    private void ClearCompleted()
    {
        foreach (var vm in Completed.ToList())
        {
            _data.Data.Tasks.Remove(vm.Model);
            _vmCache.Remove(vm.Model.Id);
        }
        Completed.Clear();
        RequestSave();
    }

    public void OnTaskScheduleChanged(TaskItemViewModel item)
    {
        RequestSave();
        ScheduleChanged?.Invoke();
        if (_currentView == "today")
        {
            bool shown = Pending.Contains(item);
            bool shouldShow = MatchesPendingFilter(item.Model);
            if (shown != shouldShow) RebuildView();
            else if (shown) ResortPendingItem(item); // 日期变了但仍在视图内 → 就地重排
        }
    }

    public void OnTaskPriorityChanged(TaskItemViewModel item)
    {
        if (_currentView == "today" && Pending.Contains(item)) ResortPendingItem(item);
    }

    /// <summary>今天视图内按 (到期日, 优先级降序, 手动序) 将单个条目移动到正确位置。</summary>
    private void ResortPendingItem(TaskItemViewModel item)
    {
        int cur = Pending.IndexOf(item);
        if (cur < 0) return;
        static (DateTime, int, long) Key(Models.TodoTask t) =>
            (t.DueAt?.Date ?? DateTime.MaxValue, -(int)t.Priority, t.SortOrder);
        var key = Key(item.Model);
        int target = 0;
        for (int i = 0; i < Pending.Count; i++)
        {
            if (i == cur) continue;
            if (Key(Pending[i].Model).CompareTo(key) < 0) target++;
        }
        if (target != cur) Pending.Move(cur, target);
    }

    /// <summary>Persist a manual drag reorder of the pending section (user-list views only).</summary>
    [RelayCommand]
    private void CommitReorder()
    {
        long order = 0;
        foreach (var vm in Pending)
        {
            vm.Model.SortOrder = order += 1000;
        }
        RequestSave();
    }

    [RelayCommand]
    private void CycleComposePriority()
    {
        ComposePriority = ComposePriority switch
        {
            Priority.None => Priority.Low,
            Priority.Low => Priority.Medium,
            Priority.Medium => Priority.High,
            _ => Priority.None,
        };
    }

    partial void OnComposeDueAtChanged(DateTime? value) => OnPropertyChanged(nameof(ComposeHasSchedule));

    partial void OnIsPinnedChanged(bool value)
    {
        Settings.Pinned = value;
        _settingsStore.RequestSave();
    }

    // ------------------------------------------------------------------
    //  list management
    // ------------------------------------------------------------------

    [RelayCommand]
    private void CommitAddList(ChipViewModel chip)
    {
        chip.IsEditing = false;
        string name = chip.EditText.Trim();
        if (name.Length == 0) return;

        var list = new TodoList
        {
            Name = name,
            SortOrder = _data.Data.Lists.Count == 0 ? 0 : _data.Data.Lists.Max(l => l.SortOrder) + 1,
            ColorAccent = ListColorCycle[_data.Data.Lists.Count % ListColorCycle.Length],
        };
        _data.Data.Lists.Add(list);
        RequestSave();

        _currentView = list.Id.ToString();
        Settings.LastSelectedView = _currentView;
        _settingsStore.RequestSave();
        BuildChips();
        RebuildView();
    }

    [RelayCommand]
    private void CancelChipEdit(ChipViewModel chip) => chip.IsEditing = false;

    [RelayCommand]
    private void BeginRenameList(ChipViewModel chip)
    {
        if (!chip.IsUserList) return;
        chip.EditText = chip.Name;
        chip.IsEditing = true;
    }

    [RelayCommand]
    private void CommitRenameList(ChipViewModel chip)
    {
        chip.IsEditing = false;
        string name = chip.EditText.Trim();
        if (name.Length == 0 || name == chip.Name) return;
        var list = _data.Data.Lists.FirstOrDefault(l => l.Id == chip.ListId);
        if (list == null) return;
        list.Name = name;
        chip.Name = name;
        RequestSave();
    }

    [RelayCommand]
    private void DeleteList(ChipViewModel chip)
    {
        var list = _data.Data.Lists.FirstOrDefault(l => l.Id == chip.ListId);
        if (list == null || list.IsDefault) return;

        foreach (var t in _data.Data.Tasks.Where(t => t.ListId == list.Id))
            t.ListId = _defaultListId;
        _data.Data.Lists.Remove(list);
        RequestSave();

        if (_currentView == chip.ListId.ToString())
        {
            _currentView = "today";
            Settings.LastSelectedView = _currentView;
            _settingsStore.RequestSave();
        }
        BuildChips();
        RebuildView();
    }

    public void SetListColor(ChipViewModel chip, string hex)
    {
        var list = _data.Data.Lists.FirstOrDefault(l => l.Id == chip.ListId);
        if (list == null) return;
        list.ColorAccent = hex;
        chip.ColorHex = hex;
        RequestSave();
        RebuildView(); // refresh list dots
    }

    // ------------------------------------------------------------------
    //  reminder-service entry points
    // ------------------------------------------------------------------

    public string GetListName(Guid listId) =>
        _data.Data.Lists.FirstOrDefault(l => l.Id == listId)?.Name ?? "默认清单";

    /// <summary>Mark a task done from outside the panel (reminder toast).</summary>
    public void CompleteFromExternal(TodoTask task)
    {
        if (_vmCache.TryGetValue(task.Id, out var vm) && (Pending.Contains(vm) || Completed.Contains(vm)))
        {
            vm.IsDone = true; // full animation + move pipeline
        }
        else
        {
            task.IsDone = true;
            task.CompletedAt = DateTime.Now;
            RequestSave();
            RebuildView();
        }
    }

    /// <summary>Switch the panel to the list containing the given task.</summary>
    public void ShowTaskList(TodoTask task)
    {
        _currentView = _data.Data.Lists.Any(l => l.Id == task.ListId) ? task.ListId.ToString() : "all";
        Settings.LastSelectedView = _currentView;
        _settingsStore.RequestSave();
        IsSettingsOpen = false;
        SyncChipSelection();
        RebuildView();
    }

    public void ShowTodayView()
    {
        _currentView = "today";
        Settings.LastSelectedView = _currentView;
        _settingsStore.RequestSave();
        IsSettingsOpen = false;
        SyncChipSelection();
        RebuildView();
    }
}

/// <summary>Adapts the quick-add compose fields to the shared due picker.</summary>
public sealed class ComposeDueProxy : IDueTarget
{
    private readonly MainViewModel _vm;

    public ComposeDueProxy(MainViewModel vm) => _vm = vm;

    public DateTime? DueAt
    {
        get => _vm.ComposeDueAt;
        set => _vm.ComposeDueAt = value;
    }

    public DateTime? RemindAt
    {
        get => _vm.ComposeRemindAt;
        set => _vm.ComposeRemindAt = value;
    }
}

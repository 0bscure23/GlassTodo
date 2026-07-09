using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GlassTodo.Models;
using GlassTodo.Services;

namespace GlassTodo.ViewModels;

/// <summary>Inline settings page. Every change applies live and persists immediately.</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly JsonStore<AppSettings> _store;
    private readonly Action _applyPanelLayout;
    private readonly Func<string, bool> _applyHotkey;
    private readonly Action _applyTheme;
    private readonly Action _applyStyle;
    private bool _syncing;

    [ObservableProperty] private bool _autoStart;
    [ObservableProperty] private int _triggerDwellMs;
    [ObservableProperty] private int _hideGraceMs;
    [ObservableProperty] private double _panelHeightPercent;
    [ObservableProperty] private int _themeIndex;
    [ObservableProperty] private double _glassDensityPercent;
    [ObservableProperty] private double _cardDensityPercent;
    [ObservableProperty] private int _styleIndex;
    [ObservableProperty] private string _hotkeyText = "";
    [ObservableProperty] private string _hotkeyStatus = "";
    [ObservableProperty] private bool _isRecordingHotkey;

    public string DataFolder => App.DataDir;
    public string VersionText => "琉璃清单 v1.0.0";

    private AppSettings S => _store.Data;

    public SettingsViewModel(JsonStore<AppSettings> store, Action applyPanelLayout,
        Func<string, bool> applyHotkey, Action applyTheme, Action applyStyle)
    {
        _store = store;
        _applyPanelLayout = applyPanelLayout;
        _applyHotkey = applyHotkey;
        _applyTheme = applyTheme;
        _applyStyle = applyStyle;

        _syncing = true;
        AutoStart = AutoStartService.IsEnabled();
        TriggerDwellMs = S.TriggerDwellMs;
        HideGraceMs = S.HideGraceMs;
        PanelHeightPercent = Math.Round(S.PanelHeightRatio * 100);
        ThemeIndex = (int)S.Theme;
        GlassDensityPercent = S.GlassDensity;
        CardDensityPercent = S.CardDensity;
        StyleIndex = S.VisualStyle;
        HotkeyText = S.HotKey;
        _syncing = false;
    }

    partial void OnAutoStartChanged(bool value)
    {
        if (_syncing) return;
        AutoStartService.SetEnabled(value);
        S.AutoStart = value;
        _store.RequestSave();
    }

    partial void OnTriggerDwellMsChanged(int value)
    {
        if (_syncing) return;
        S.TriggerDwellMs = Math.Clamp(value, 100, 800);
        _store.RequestSave();
    }

    partial void OnHideGraceMsChanged(int value)
    {
        if (_syncing) return;
        S.HideGraceMs = Math.Clamp(value, 300, 2000);
        _store.RequestSave();
    }

    partial void OnPanelHeightPercentChanged(double value)
    {
        if (_syncing) return;
        S.PanelHeightRatio = Math.Clamp(value, 50, 90) / 100.0;
        _store.RequestSave();
        _applyPanelLayout();
    }

    partial void OnThemeIndexChanged(int value)
    {
        if (_syncing) return;
        S.Theme = (ThemeMode)Math.Clamp(value, 0, 2);
        _store.RequestSave();
        _applyTheme();
    }

    partial void OnGlassDensityPercentChanged(double value)
    {
        if (_syncing) return;
        S.GlassDensity = (int)Math.Clamp(value, 5, 85);
        _store.RequestSave();
        _applyStyle();
    }

    partial void OnCardDensityPercentChanged(double value)
    {
        if (_syncing) return;
        S.CardDensity = (int)Math.Clamp(value, 50, 300);
        _store.RequestSave();
        _applyStyle();
    }

    /// <summary>选择新的数据文件夹：迁移文件并自动重启。</summary>
    [RelayCommand]
    private void ChangeDataFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择琉璃清单数据文件夹",
            InitialDirectory = App.DataDir,
        };
        if (dlg.ShowDialog() != true) return;
        ((App)System.Windows.Application.Current).ChangeDataDirAndRestart(dlg.FolderName);
    }

    [RelayCommand]
    private void SetTheme(string index) => ThemeIndex = int.Parse(index);

    partial void OnStyleIndexChanged(int value)
    {
        if (_syncing) return;
        S.VisualStyle = Math.Clamp(value, 0, 1);
        _store.RequestSave();
        _applyStyle();
    }

    [RelayCommand]
    private void SetStyle(string index) => StyleIndex = int.Parse(index);

    /// <summary>Called by the recorder when a full chord was captured. Reverts on conflict.</summary>
    public void TrySetHotkey(string chord)
    {
        IsRecordingHotkey = false;
        if (chord == S.HotKey)
        {
            HotkeyStatus = "";
            return;
        }
        if (_applyHotkey(chord))
        {
            S.HotKey = chord;
            _store.RequestSave();
            HotkeyText = chord;
            HotkeyStatus = "已生效";
        }
        else
        {
            _applyHotkey(S.HotKey); // restore the previous working chord
            HotkeyText = S.HotKey;
            HotkeyStatus = "注册失败（可能被占用），已还原";
        }
    }

    public void CancelHotkeyRecording()
    {
        IsRecordingHotkey = false;
        HotkeyText = S.HotKey;
        HotkeyStatus = "";
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = App.DataDir,
                UseShellExecute = true,
            });
        }
        catch
        {
            // explorer refusing to open is not fatal
        }
    }
}

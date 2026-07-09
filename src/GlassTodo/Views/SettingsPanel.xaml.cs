using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GlassTodo.Services;
using GlassTodo.ViewModels;

namespace GlassTodo.Views;

public partial class SettingsPanel : UserControl
{
    public SettingsPanel()
    {
        InitializeComponent();
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null) Vm.IsSettingsOpen = false;
    }

    // ----- hotkey recorder -----

    private void HotkeyBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var svm = Vm?.SettingsVM;
        if (svm == null) return;
        svm.IsRecordingHotkey = true;
        svm.HotkeyStatus = "按下新的组合键（Esc 取消）…";
        HotkeyBox.Text = "…";
    }

    private void HotkeyBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var svm = Vm?.SettingsVM;
        if (svm == null) return;
        if (svm.IsRecordingHotkey) svm.CancelHotkeyRecording();
        HotkeyBox.Text = svm.HotkeyText;
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var svm = Vm?.SettingsVM;
        if (svm == null || !svm.IsRecordingHotkey) return;
        e.Handled = true;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            svm.CancelHotkeyRecording();
            HotkeyBox.Text = svm.HotkeyText;
            Keyboard.ClearFocus();
            return;
        }

        string? chord = HotkeyService.ChordFromKeyEvent(e);
        if (chord == null)
        {
            HotkeyBox.Text = "…";
            return;
        }

        svm.TrySetHotkey(chord);
        HotkeyBox.Text = svm.HotkeyText;
        Keyboard.ClearFocus();
    }
}

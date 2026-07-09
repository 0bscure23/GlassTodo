using System.Windows.Threading;
using GlassTodo.Interop;
using GlassTodo.Models;

namespace GlassTodo.Services;

/// <summary>
/// Low-cost cursor polling (10 Hz). While hidden it watches the right screen edge with a
/// dwell requirement; while shown it watches for the cursor leaving the panel.
/// </summary>
public sealed class EdgeTriggerService
{
    private const int IntervalMs = 100;

    private readonly PanelController _panel;
    private readonly Func<AppSettings> _settings;
    private readonly DispatcherTimer _timer;

    private int _dwellMs;
    private int _outsideMs;
    private bool _armed = true;

    public EdgeTriggerService(PanelController panel, Func<AppSettings> settings)
    {
        _panel = panel;
        _settings = settings;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(IntervalMs),
        };
        _timer.Tick += OnTick;
        // After a hide the trigger stays disarmed until the cursor has left the edge zone once,
        // otherwise the panel would bounce right back.
        _panel.HiddenCompleted += () => _armed = false;
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    private void OnTick(object? sender, EventArgs e)
    {
        if (!NativeMethods.GetCursorPos(out var pt)) return;
        var s = _settings();

        switch (_panel.State)
        {
            case PanelState.Hidden:
                bool inZone = _panel.IsCursorInTriggerZone(pt.X, pt.Y);
                if (!_armed)
                {
                    if (!inZone) _armed = true;
                    _dwellMs = 0;
                    return;
                }
                if (inZone && !ScreenInterop.IsAnyMouseButtonDown() && !ScreenInterop.IsFullscreenAppActive())
                {
                    _dwellMs += IntervalMs;
                    if (_dwellMs >= s.TriggerDwellMs)
                    {
                        _dwellMs = 0;
                        _panel.ShowPanel(activate: false);
                    }
                }
                else
                {
                    _dwellMs = 0;
                }
                break;

            case PanelState.Shown:
                if (_panel.Pinned || _panel.InteractionLocked)
                {
                    _outsideMs = 0;
                    return;
                }
                if (_panel.IsCursorInsidePanel(pt.X, pt.Y))
                {
                    _outsideMs = 0;
                }
                else
                {
                    _outsideMs += IntervalMs;
                    if (_outsideMs >= s.HideGraceMs)
                    {
                        _outsideMs = 0;
                        _panel.HidePanel();
                    }
                }
                break;

            default:
                _dwellMs = 0;
                _outsideMs = 0;
                break;
        }
    }
}

using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using GlassTodo.Interop;
using GlassTodo.Models;

namespace GlassTodo.Services;

/// <summary>
/// Resolves the effective theme (settings override or OS), swaps the palette resource
/// dictionary, and applies DWM dark-mode/backdrop to every registered window.
/// Falls back to a near-solid tint when transparency effects are unavailable.
/// </summary>
public sealed class ThemeService
{
    private const string PersonalizeKey =
        @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly Func<AppSettings> _settings;
    private readonly List<Window> _windows = new();

    public bool IsDark { get; private set; } = true;
    public bool TransparencyEnabled { get; private set; } = true;

    public event Action? ThemeChanged;

    public ThemeService(Func<AppSettings> settings)
    {
        _settings = settings;
    }

    public void Register(Window window)
    {
        if (!_windows.Contains(window)) _windows.Add(window);
        ApplyToWindow(window);
    }

    public void Unregister(Window window) => _windows.Remove(window);

    /// <summary>磨砂是否应生效（液态风格 + 系统透明效果开启）；小磨砂窗由 PanelController 管理。</summary>
    public bool FrostActive { get; private set; }

    /// <summary>当前磨砂着色（AABBGGRR），供 PanelController 的磨砂窗池取用。</summary>
    public uint CurrentFrostTint => FrostTint();

    public void Refresh()
    {
        var s = _settings();
        bool systemLight = ReadDword("AppsUseLightTheme", 1) != 0;
        TransparencyEnabled = ReadDword("EnableTransparency", 1) != 0;
        IsDark = s.Theme switch
        {
            ThemeMode.Light => false,
            ThemeMode.Dark => true,
            _ => !systemLight,
        };

        SwapPalette(IsDark);
        bool anyGlass = false;
        foreach (var w in _windows)
            anyGlass |= ApplyToWindow(w);

        // 主面板为逐像素透明的清玻璃：液态 → 按用户浓度着色（可透视），经典 → 近实色（重可读性）
        var app = Application.Current;
        if (_settings().VisualStyle == 1)
        {
            var palette = PaletteDict();

            // 磨砂（iOS 质感）：真实模糊由逐卡磨砂窗承担，雾感浓淡 = 滑杆控制其着色
            FrostActive = TransparencyEnabled;

            // 玻璃底：磨砂生效时本体只留极薄的膜（透光交给模糊层）；
            // 磨砂不可用时退回滑杆着色的清玻璃
            var baseTint = (palette?["GlassTintLiquidBrush"] as System.Windows.Media.SolidColorBrush)?.Color
                           ?? System.Windows.Media.Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF);
            byte alpha = FrostActive
                ? (byte)0x12
                : (byte)(Math.Clamp(_settings().GlassDensity, 5, 85) * 255 / 100);
            app.Resources["GlassTintBrush"] =
                Frozen(System.Windows.Media.Color.FromArgb(alpha, baseTint.R, baseTint.G, baseTint.B));

            // 任务卡等表面：按「任务卡浓度」倍率缩放主题默认透明度
            double factor = Math.Clamp(_settings().CardDensity, 50, 300) / 100.0;
            OverrideSurface(app, palette, "CardFillBrush", factor);
            OverrideSurface(app, palette, "CardFillHoverBrush", factor);
            OverrideSurface(app, palette, "InputFillBrush", factor);
        }
        else
        {
            FrostActive = false;
            app.Resources["GlassTintBrush"] = app.TryFindResource("GlassFallbackSolidBrush");
            app.Resources.Remove("CardFillBrush");
            app.Resources.Remove("CardFillHoverBrush");
            app.Resources.Remove("InputFillBrush");
        }

        ThemeChanged?.Invoke();
    }

    private static int ReadDword(string name, int fallback)
    {
        try
        {
            return Registry.GetValue(PersonalizeKey, name, fallback) is int v ? v : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>磨砂背景板着色（AABBGGRR）：中性底色，透明度来自「玻璃雾感」滑杆（映射调淡）。</summary>
    private uint FrostTint()
    {
        // 雾感只作用在任务卡小面积上，映射整体调淡：5–85% 滑杆 → 约 3–50% 实际着色
        byte a = (byte)Math.Max(0x08, Math.Clamp(_settings().GlassDensity, 5, 85) * 150 / 100);
        byte r, g, b;
        if (IsDark) { r = 0x16; g = 0x18; b = 0x1E; }
        else { r = 0xFA; g = 0xFA; b = 0xFC; }
        return (uint)((a << 24) | (b << 16) | (g << 8) | r);
    }

    /// <summary>定位当前生效的调色板字典（读取原始值，避免被应用级覆盖影响）。</summary>
    private static ResourceDictionary? PaletteDict() =>
        Application.Current.Resources.MergedDictionaries
            .FirstOrDefault(d => (d.Source?.OriginalString ?? "").Contains("Palette."));

    private static void OverrideSurface(Application app, ResourceDictionary? palette, string key, double factor)
    {
        if (Math.Abs(factor - 1.0) < 0.01)
        {
            app.Resources.Remove(key); // 回到调色板默认
            return;
        }
        if (palette?[key] is not System.Windows.Media.SolidColorBrush brush) return;
        var c = brush.Color;
        byte a = (byte)Math.Clamp(c.A * factor, 0, 240);
        app.Resources[key] = Frozen(System.Windows.Media.Color.FromArgb(a, c.R, c.G, c.B));
    }

    private static System.Windows.Media.SolidColorBrush Frozen(System.Windows.Media.Color color)
    {
        var b = new System.Windows.Media.SolidColorBrush(color);
        b.Freeze();
        return b;
    }

    private static void SwapPalette(bool dark)
    {
        var dicts = Application.Current.Resources.MergedDictionaries;
        string want = dark ? "Palette.Dark" : "Palette.Light";
        var uri = new Uri($"Themes/{want}.xaml", UriKind.Relative);
        for (int i = 0; i < dicts.Count; i++)
        {
            string src = dicts[i].Source?.OriginalString ?? "";
            if (!src.Contains("Palette.")) continue;
            if (!src.Contains(want))
                dicts[i] = new ResourceDictionary { Source = uri };
            return;
        }
        dicts.Insert(0, new ResourceDictionary { Source = uri });
    }

    private bool ApplyToWindow(Window w)
    {
        // 分层窗口（主面板）自带逐像素透明，玻璃由 XAML 图层呈现，不走 DWM 材质
        if (w.AllowsTransparency) return true;

        var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return false;
        return WindowEffects.ApplyGlass(hwnd, IsDark, CurrentBackdrop);
    }

    /// <summary>Backdrop for non-layered windows (toast): system acrylic unless transparency is off.</summary>
    private GlassBackdrop CurrentBackdrop =>
        !TransparencyEnabled ? GlassBackdrop.None : GlassBackdrop.SystemAcrylic;
}

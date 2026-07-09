namespace GlassTodo.Models;

public enum ThemeMode
{
    Auto = 0,
    Light = 1,
    Dark = 2,
}

public class AppSettings
{
    public bool AutoStart { get; set; }
    /// <summary>Global hotkey chord, e.g. "Alt+Q".</summary>
    public string HotKey { get; set; } = "Alt+Q";
    /// <summary>How long the cursor must dwell at the screen edge before the panel slides in.</summary>
    public int TriggerDwellMs { get; set; } = 300;
    /// <summary>How long the cursor must stay outside the panel before it slides away.</summary>
    public int HideGraceMs { get; set; } = 500;
    public bool Pinned { get; set; }
    public ThemeMode Theme { get; set; } = ThemeMode.Auto;
    /// <summary>0 = 经典玻璃, 1 = 液态玻璃（光斑跟随/弹性动画/棱边）.</summary>
    public int VisualStyle { get; set; } = 1;
    /// <summary>玻璃底色浓度（不透明度 %，5–85，仅液态风格生效）。</summary>
    public int GlassDensity { get; set; } = 30;
    /// <summary>任务卡浓度倍率（%，100 = 主题默认，50–300，仅液态风格生效）。</summary>
    public int CardDensity { get; set; } = 100;
    public double PanelHeightRatio { get; set; } = 0.70;
    public int EdgeZonePx { get; set; } = 2;
    /// <summary>"today" | "all" | list GUID string.</summary>
    public string LastSelectedView { get; set; } = "today";
}

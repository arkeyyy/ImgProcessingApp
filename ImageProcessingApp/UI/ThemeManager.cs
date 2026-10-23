using System.Text.Json;

namespace ImageProcessingApp;

internal sealed record ThemePalette(Color Background, Color Surface, Color Canvas, Color Text,
    Color Muted, Color Border, Color Accent, Color OnAccent, Color AccentSurface,
    Color Success, Color OnSuccess, Color ControlBorder)
{
    private static Color Hex(string value) => ColorTranslator.FromHtml(value);
    public static ThemePalette Light { get; } = new(Hex("#F5F7FF"), Hex("#FFFFFF"), Hex("#E9EDF2"),
        Hex("#172033"), Hex("#475569"), Hex("#CBD5E1"), Hex("#4F46E5"), Color.White,
        Hex("#EEF2FF"), Hex("#0F766E"), Color.White, Hex("#7C8AA5"));
    public static ThemePalette Dark { get; } = new(Hex("#101827"), Hex("#1E293B"), Hex("#151E2C"),
        Hex("#F1F5F9"), Hex("#CBD5E1"), Hex("#475569"), Hex("#A5B4FC"), Hex("#172033"),
        Hex("#283454"), Hex("#5EEAD4"), Hex("#172033"), Hex("#8290A8"));
    public static ThemePalette HighContrast => new(SystemColors.Control, SystemColors.Window,
        SystemColors.Window, SystemColors.WindowText, SystemColors.WindowText, SystemColors.WindowText,
        SystemColors.Highlight, SystemColors.HighlightText, SystemColors.Window,
        SystemColors.Highlight, SystemColors.HighlightText, SystemColors.WindowText);
}

internal interface IThemeAware
{
    void ApplyTheme(ThemePalette palette);
}

internal static class ThemeManager
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImageProcessingApp", "settings.json");
    public static bool IsDark { get; private set; }
    public static ThemePalette Current => SystemInformation.HighContrast ? ThemePalette.HighContrast
        : IsDark ? ThemePalette.Dark : ThemePalette.Light;
    public static event EventHandler? ThemeChanged;

    public static void Initialize()
    {
        try
        {
            if (File.Exists(SettingsPath))
                IsDark = JsonSerializer.Deserialize<ThemeSettings>(File.ReadAllText(SettingsPath))?.DarkMode ?? false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            IsDark = false;
        }
    }

    // A failed preference write must not prevent changing the current theme.
    public static bool SetDark(bool dark, bool persist = true)
    {
        IsDark = dark;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
        if (!persist) return true;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new ThemeSettings(dark)));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void RefreshSystemColors() => ThemeChanged?.Invoke(null, EventArgs.Empty);

    public static void Apply(Control root)
    {
        var p = Current;
        ApplyControl(root, p);
        root.Invalidate(true);
    }

    private static void ApplyControl(Control control, ThemePalette p)
    {
        string role = control.Tag as string ?? "";
        control.ForeColor = role == "muted" ? p.Muted : p.Text;
        control.BackColor = role == "surface" ? p.Surface : control.Parent?.BackColor ?? p.Background;
        switch (control)
        {
            // These controls own their colors and interaction states through IThemeAware.
            case RoundedButton:
            case RoundedRadioButton:
                break;
            case TabPage page:
                page.UseVisualStyleBackColor = false;
                page.BackColor = p.Background;
                break;
            case Button button:
                bool primary = role == "primary";
                button.BackColor = primary ? p.Accent : p.Surface;
                button.ForeColor = primary ? p.OnAccent : p.Text;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = primary ? p.Accent : p.Muted;
                button.FlatAppearance.MouseOverBackColor = Blend(button.BackColor, p.Accent, .15f);
                button.FlatAppearance.MouseDownBackColor = Blend(button.BackColor, p.Accent, .25f);
                button.UseVisualStyleBackColor = false;
                break;
            case RadioButton radio:
                radio.UseVisualStyleBackColor = false;
                if (radio.Appearance == Appearance.Button)
                {
                    radio.BackColor = radio.Checked ? p.Accent : p.Surface;
                    radio.ForeColor = radio.Checked ? p.OnAccent : p.Text;
                    radio.FlatAppearance.BorderColor = p.Muted;
                    radio.FlatAppearance.CheckedBackColor = p.Accent;
                    radio.FlatAppearance.MouseOverBackColor = radio.Checked ? p.Accent : Blend(p.Surface, p.Accent, .12f);
                    radio.FlatAppearance.MouseDownBackColor = radio.Checked ? p.Accent : Blend(p.Surface, p.Accent, .2f);
                }
                break;
            case ComboBox combo:
                combo.BackColor = p.Surface;
                combo.FlatStyle = FlatStyle.Flat;
                break;
            case TextBoxBase textBox:
                textBox.BackColor = p.Surface;
                break;
            case DataGridView grid:
                grid.BackgroundColor = p.Surface;
                grid.GridColor = p.Border;
                grid.EnableHeadersVisualStyles = false;
                grid.DefaultCellStyle.BackColor = p.Surface;
                grid.DefaultCellStyle.ForeColor = p.Text;
                grid.DefaultCellStyle.SelectionBackColor = p.Accent;
                grid.DefaultCellStyle.SelectionForeColor = p.OnAccent;
                grid.ColumnHeadersDefaultCellStyle.BackColor = p.Background;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = p.Text;
                grid.RowHeadersDefaultCellStyle.BackColor = p.Background;
                grid.RowHeadersDefaultCellStyle.ForeColor = p.Text;
                break;
            case ToolStrip strip:
                strip.Renderer = new ToolStripProfessionalRenderer(new PaletteColorTable(p));
                foreach (ToolStripItem item in strip.Items) item.ForeColor = p.Text;
                break;
        }
        foreach (Control child in control.Controls) ApplyControl(child, p);
        if (control is IThemeAware themed) themed.ApplyTheme(p);
    }

    internal static Color Blend(Color from, Color to, float amount) => Color.FromArgb(
        (int)(from.R + (to.R - from.R) * amount),
        (int)(from.G + (to.G - from.G) * amount),
        (int)(from.B + (to.B - from.B) * amount));

    private sealed record ThemeSettings(bool DarkMode);
    private sealed class PaletteColorTable(ThemePalette p) : ProfessionalColorTable
    {
        public override Color StatusStripGradientBegin => p.Surface;
        public override Color StatusStripGradientEnd => p.Surface;
        public override Color ToolStripBorder => p.Border;
        public override Color ToolStripGradientBegin => p.Surface;
        public override Color ToolStripGradientMiddle => p.Surface;
        public override Color ToolStripGradientEnd => p.Surface;
    }
}

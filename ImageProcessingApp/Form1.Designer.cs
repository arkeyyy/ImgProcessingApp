namespace ImageProcessingApp;

partial class Form1
{
    private System.ComponentModel.IContainer components = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ThemeManager.ThemeChanged -= OnThemeChanged;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        SuspendLayout();
        // 
        // Form1
        // 
        AutoScaleDimensions = new SizeF(120F, 120F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1500, 950);
        Margin = new Padding(4, 4, 4, 4);
        MinimumSize = new Size(796, 588);
        Name = "Form1";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Image Processing App";
        Load += Form1_Load;
        ResumeLayout(false);
    }
}

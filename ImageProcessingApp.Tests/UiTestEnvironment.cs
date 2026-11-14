using System.Drawing;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ImageProcessingApp.Tests;

[TestClass]
public sealed class UiTestEnvironment
{
    [AssemblyInitialize]
    public static void Initialize(TestContext context)
    {
        // Establish the application's process-wide UI configuration before any
        // test creates an HWND. Test execution order must not decide the DPI.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetDefaultFont(new Font("Segoe UI", 10));
    }
}

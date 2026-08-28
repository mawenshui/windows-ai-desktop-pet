using System;
using System.Windows;

namespace AiPet.App;

/// <summary>
/// Custom entry point. We avoid the WPF-generated Main so we can apply
/// [STAThread] explicitly and set up unhandled exception handlers before
/// the WPF runtime starts.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Console.Error.WriteLine($"[AiPet] fatal: {e.ExceptionObject?.GetType().Name ?? "unknown"}");
        };
        try
        {
            var app = new App();
            return app.Run();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AiPet] startup failed: {ex.GetType().Name}");
            System.Windows.MessageBox.Show("应用启动失败，请重新启动或检查安装完整性。", "Windows AI Desktop Pet",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return 1;
        }
    }
}

using System.Windows;
using ImageGuard.Services;

namespace ImageGuard;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var images = new ImageService();
        var watermarks = new WatermarkService();
        var crypto = new CryptoService();
        var protection = new ProtectionService(images, watermarks, crypto);

        var window = new MainWindow(images, watermarks, crypto, protection);
        MainWindow = window;
        window.Show();
    }
}

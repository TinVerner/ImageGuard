using System.Windows;
using ImageGuard.Services;
using ImageGuard.ViewModels;

namespace ImageGuard;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var cryptoService = new CryptoService();
        var keyService = new KeyService(cryptoService);
        var documentService = new SignatureDocumentService();
        var imageFileService = new ImageFileService();
        var colorSpaceService = new ColorSpaceService();
        var watermarkCodec = new WatermarkCodec();
        var watermarkService = new WatermarkService(
            new DctService(),
            watermarkCodec,
            colorSpaceService);
        var metricsService = new ImageQualityMetricsService();
        var protectionService = new ProtectionService(
            imageFileService,
            watermarkService,
            metricsService,
            cryptoService,
            keyService,
            documentService);
        var verificationService = new VerificationService(
            imageFileService,
            watermarkService,
            cryptoService,
            keyService,
            documentService);
        var dialogs = new FileDialogService();

        var mainViewModel = new MainViewModel(
            new ProtectViewModel(dialogs, imageFileService, watermarkService, protectionService),
            new VerifyViewModel(dialogs, verificationService),
            new KeysViewModel(dialogs, keyService));

        var window = new MainWindow { DataContext = mainViewModel };
        MainWindow = window;
        window.Show();
    }
}

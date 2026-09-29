using System.Windows;
using ImageGuard.Services;
using ImageGuard.Services.Cryptography;
using ImageGuard.Services.Experiments;
using ImageGuard.Services.Imaging;
using ImageGuard.Services.Transformations;
using ImageGuard.Services.Verification;
using ImageGuard.Services.Watermarking;
using ImageGuard.ViewModels;

namespace ImageGuard;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var hashService = new HashService();
        var keyService = new KeyService(hashService);
        var signatureService = new DigitalSignatureService();
        var documentService = new SignatureDocumentService();
        var imageFileService = new ImageFileService();
        var colorSpaceService = new ColorSpaceService();
        var textCodec = new WatermarkTextCodec();
        var watermarkService = new WatermarkService(
            new DctService(),
            textCodec,
            colorSpaceService,
            new SequentialBlockSelector());
        var metricsService = new ImageQualityMetricsService();
        var protectionService = new ProtectionService(
            imageFileService,
            watermarkService,
            metricsService,
            hashService,
            keyService,
            signatureService,
            documentService);
        var verificationService = new VerificationService(
            imageFileService,
            watermarkService,
            hashService,
            keyService,
            signatureService,
            documentService);
        IImageTransformation[] transformations =
        [
            new JpegCompressionTransformation(),
            new BrightnessTransformation(),
            new ContrastTransformation(),
            new GaussianNoiseTransformation(),
            new ResizeTransformation(),
            new CropTransformation(),
            new RegionModificationTransformation()
        ];
        var experimentService = new ExperimentService(
            imageFileService,
            metricsService,
            verificationService,
            textCodec,
            watermarkService,
            documentService,
            transformations);
        var dialogs = new FileDialogService();

        var mainViewModel = new MainViewModel(
            new ProtectViewModel(dialogs, imageFileService, watermarkService, protectionService),
            new VerifyViewModel(dialogs, verificationService),
            new KeysViewModel(dialogs, keyService),
            new ResearchViewModel(dialogs, experimentService));

        var window = new MainWindow { DataContext = mainViewModel };
        MainWindow = window;
        window.Show();
    }
}

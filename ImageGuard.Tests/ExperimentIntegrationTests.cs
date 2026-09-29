using ImageGuard.Enums;
using ImageGuard.Models;

namespace ImageGuard.Tests;

public sealed class ExperimentIntegrationTests
{
    [Fact]
    public async Task BrightnessExperiment_VerifiesActualOutputAndExportsCsv()
    {
        var services = new TestServices();
        var directory = Path.Combine(Path.GetTempPath(), $"ImageGuardExperiment-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var inputPath = Path.Combine(directory, "input.png");
            var protectedPath = Path.Combine(directory, "protected.png");
            var signaturePath = Path.Combine(directory, "protected.igsig");
            var outputDirectory = Path.Combine(directory, "results");
            var csvPath = Path.Combine(directory, "results.csv");
            var settings = TestServices.ReliableSettings();
            services.Images.Save(
                TestServices.CreateTexturedImage(),
                inputPath,
                ImageOutputFormat.Png);
            var keys = services.Keys.GenerateKeyPair(directory, "experiment", "password");
            var protection = await services.Protection.ProtectAsync(new(
                inputPath,
                protectedPath,
                "EXPERIMENT-001",
                settings,
                keys.PrivateKeyPath,
                "password",
                signaturePath,
                ImageOutputFormat.Png));
            Assert.True(protection.Success, protection.ErrorMessage);

            var results = await services.Experiments.RunAsync(new(
                protectedPath,
                signaturePath,
                keys.PublicKeyPath,
                outputDirectory,
                "EXPERIMENT-001",
                settings,
                [
                    new(
                        ImageTransformationType.Brightness,
                        new ImageTransformationParameters { Value = 5 },
                        "Brightness +5"),
                    new(
                        ImageTransformationType.JpegCompression,
                        new ImageTransformationParameters { Value = 90 },
                        "JPEG 90")
                ]));

            Assert.Equal(2, results.Count);
            var result = results[0];
            Assert.True(File.Exists(result.OutputFilePath));
            Assert.Equal(SignatureStatus.Invalid, result.SignatureStatus);
            Assert.False(result.SignatureValid);
            Assert.NotNull(result.Mse);
            Assert.NotNull(result.Psnr);

            var jpeg = results[1];
            Assert.EndsWith(".jpg", jpeg.OutputFilePath, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(jpeg.OutputFilePath));
            Assert.Equal(SignatureStatus.Invalid, jpeg.SignatureStatus);
            Assert.NotNull(jpeg.Mse);
            Assert.NotNull(jpeg.Psnr);

            services.Experiments.ExportCsv(results, csvPath);
            var csv = File.ReadAllText(csvPath);
            Assert.Contains("Brightness +5", csv);
            Assert.Contains("Invalid", csv);
            Assert.Contains(",BER,MSE,PSNR,", csv);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

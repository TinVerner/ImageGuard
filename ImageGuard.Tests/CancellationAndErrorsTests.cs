using ImageGuard.Enums;
using ImageGuard.Models;

namespace ImageGuard.Tests;

public sealed class CancellationAndErrorsTests
{
    [Fact]
    public async Task Services_PreCancelledToken_PropagatesCancellation()
    {
        var services = new TestServices();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            services.Protection.ProtectAsync(
                new("", "", "", new(), "", null, "", ImageOutputFormat.Png),
                cancellationToken: cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            services.Verification.VerifyAsync(
                new("", "", "", new()),
                cancellationToken: cancellation.Token));
    }

    [Fact]
    public void WatermarkEmbed_CancellationInsideBlockLoop_IsObserved()
    {
        var services = new TestServices();
        using var cancellation = new CancellationTokenSource();
        var progress = new CancelOnFirstProgress(cancellation);

        Assert.Throws<OperationCanceledException>(() =>
            services.Watermarks.Embed(
                TestServices.CreateTexturedImage(512, 512),
                new string('A', 100),
                TestServices.ReliableSettings(),
                progress,
                cancellation.Token));
    }

    [Fact]
    public async Task Protect_WrongPrivateKeyPassword_ReturnsFriendlyFailure()
    {
        var services = new TestServices();
        var directory = CreateDirectory();
        try
        {
            var inputPath = Path.Combine(directory, "input.png");
            var outputPath = Path.Combine(directory, "protected.png");
            var signaturePath = Path.Combine(directory, "protected.igsig");
            services.Images.Save(
                TestServices.CreateTexturedImage(),
                inputPath,
                ImageOutputFormat.Png);
            var keys = services.Keys.GenerateKeyPair(directory, "password", "correct-password");

            var result = await services.Protection.ProtectAsync(new(
                inputPath,
                outputPath,
                "PASSWORD-TEST",
                TestServices.ReliableSettings(),
                keys.PrivateKeyPath,
                "wrong-password",
                signaturePath,
                ImageOutputFormat.Png));

            Assert.False(result.Success);
            Assert.Contains("ключ", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("пароль", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(signaturePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Protect_ReportsProgressFromZeroToCompletion()
    {
        var services = new TestServices();
        var directory = CreateDirectory();
        try
        {
            var inputPath = Path.Combine(directory, "input.png");
            var outputPath = Path.Combine(directory, "protected.png");
            var signaturePath = Path.Combine(directory, "protected.igsig");
            services.Images.Save(
                TestServices.CreateTexturedImage(),
                inputPath,
                ImageOutputFormat.Png);
            var keys = services.Keys.GenerateKeyPair(directory, "progress", "password");
            var progress = new CollectingProgress();

            var result = await services.Protection.ProtectAsync(
                new(
                    inputPath,
                    outputPath,
                    "PROGRESS",
                    TestServices.ReliableSettings(),
                    keys.PrivateKeyPath,
                    "password",
                    signaturePath,
                    ImageOutputFormat.Png),
                progress: progress);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Contains(0, progress.Values);
            Assert.Contains(1, progress.Values);
            Assert.All(progress.Values, value => Assert.InRange(value, 0, 1));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ImageGuardCancellation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class CancelOnFirstProgress(CancellationTokenSource cancellation)
        : IProgress<double>
    {
        public void Report(double value) => cancellation.Cancel();
    }

    private sealed class CollectingProgress : IProgress<double>
    {
        public List<double> Values { get; } = [];

        public void Report(double value) => Values.Add(value);
    }
}

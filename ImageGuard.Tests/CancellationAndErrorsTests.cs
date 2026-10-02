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
                new("", "", "", new(), "", ""),
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
                inputPath);
            var keys = services.Crypto.GenerateKeyPair(directory, "progress");
            var progress = new CollectingProgress();

            var result = await services.Protection.ProtectAsync(
                new(
                    inputPath,
                    outputPath,
                    "PROGRESS",
                    TestServices.ReliableSettings(),
                    keys.PrivateKeyPath,
                    signaturePath),
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

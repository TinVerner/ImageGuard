using System.Buffers.Binary;
using ImageGuard.Enums;
using ImageGuard.Models;

namespace ImageGuard.Services;

public interface IWatermarkService
{
    WatermarkCapacity CalculateCapacity(ImagePixelData image, string text);

    WatermarkEmbedResult Embed(
        ImagePixelData image,
        string text,
        WatermarkSettings settings,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    WatermarkExtractionResult Extract(
        ImagePixelData image,
        WatermarkSettings settings,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class WatermarkService(
    DctService dctService,
    WatermarkCodec codec,
    ColorSpaceService colorSpaceService) : IWatermarkService
{
    private const int LengthPrefixBits = WatermarkCodec.LengthPrefixSize * 8;
    // The IDCT -> YCbCr/RGB -> YCbCr/DCT round-trip can reduce the coefficient
    // difference. This fixed margin compensates rounding; it is not adaptive,
    // and extraction still uses the user-selected Delta as its threshold.
    private const double NumericalMargin = 25.0;

    public WatermarkCapacity CalculateCapacity(ImagePixelData image, string text)
    {
        ArgumentNullException.ThrowIfNull(image);
        var data = codec.Encode(text);
        var capacityBits = GetCapacityBits(image);
        return new(
            capacityBits,
            data.Length * 8,
            Math.Max(0, (capacityBits - LengthPrefixBits) / 8),
            data.Length * 8 <= capacityBits);
    }

    public WatermarkEmbedResult Embed(
        ImagePixelData image,
        string text,
        WatermarkSettings settings,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        var bits = codec.EncodeBits(text);
        var capacity = CalculateCapacity(image, text);
        if (!capacity.Fits)
        {
            throw new InvalidOperationException(
                "Изображение недостаточно велико для выбранного водяного знака. " +
                $"Доступно {capacity.CapacityBits} бит, требуется {capacity.RequiredBits}.");
        }

        // Convert RGB image to YCbCr. We use only Y for watermark embedding.
        var planes = colorSpaceService.ToYCbCr(image);
        var blocksX = image.Width / WatermarkSettings.BlockSize;
        var blocksY = image.Height / WatermarkSettings.BlockSize;
        using var positions = EnumerateBlocks(blocksX, blocksY).GetEnumerator();
        var modifiedBlocks = new HashSet<BlockPosition>();

        // One watermark bit is embedded into one complete 8x8 luminance block.
        for (var bitIndex = 0; bitIndex < bits.Length; bitIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!positions.MoveNext())
            {
                throw new InvalidOperationException("Недостаточно полных блоков 8x8.");
            }

            var position = positions.Current;
            modifiedBlocks.Add(position);
            var block = ReadBlock(planes.Y, position);
            var coefficients = dctService.Forward(block);
            EmbedBit(coefficients, bits[bitIndex], settings.Delta);
            WriteBlock(planes.Y, position, dctService.Inverse(coefficients));

            if (bitIndex % 16 == 0 || bitIndex == bits.Length - 1)
            {
                progress?.Report((bitIndex + 1.0) / bits.Length);
            }
        }

        var result = colorSpaceService.FromYCbCr(planes, image.DpiX, image.DpiY);
        RestoreUntouchedPixels(image, result, modifiedBlocks);
        return new(result, capacity);
    }

    public WatermarkExtractionResult Extract(
        ImagePixelData image,
        WatermarkSettings settings,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        var capacityBits = GetCapacityBits(image);
        if (capacityBits < LengthPrefixBits)
        {
            return new(
                WatermarkStatus.NotFound,
                null,
                "Изображение слишком мало: отсутствуют 32 бита длины.");
        }

        var planes = colorSpaceService.ToYCbCr(image);
        var blocksX = image.Width / WatermarkSettings.BlockSize;
        var blocksY = image.Height / WatermarkSettings.BlockSize;
        using var positions = EnumerateBlocks(blocksX, blocksY).GetEnumerator();

        if (!TryReadBits(
                planes.Y,
                positions,
                LengthPrefixBits,
                settings.Delta,
                cancellationToken,
                out var lengthBits,
                out var ambiguousBlock,
                out var ambiguousDifference))
        {
            return LowConfidence(ambiguousBlock, ambiguousDifference);
        }

        var lengthBytes = WatermarkCodec.ToBytesMsbFirst(lengthBits);
        var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(lengthBytes);
        var maximumPayloadBytes = (capacityBits - LengthPrefixBits) / 8;
        if (payloadLength == 0 || payloadLength > maximumPayloadBytes)
        {
            return new(
                WatermarkStatus.NotFound,
                null,
                "Корректный префикс длины водяного знака не найден.");
        }

        var payloadBitsCount = checked((int)payloadLength * 8);
        var totalBits = LengthPrefixBits + payloadBitsCount;
        progress?.Report((double)LengthPrefixBits / totalBits);

        if (!TryReadBits(
                planes.Y,
                positions,
                payloadBitsCount,
                settings.Delta,
                cancellationToken,
                out var payloadBits,
                out ambiguousBlock,
                out ambiguousDifference,
                progress,
                LengthPrefixBits,
                totalBits))
        {
            return LowConfidence(ambiguousBlock, ambiguousDifference);
        }

        var allBits = new bool[totalBits];
        Array.Copy(lengthBits, allBits, lengthBits.Length);
        Array.Copy(payloadBits, 0, allBits, lengthBits.Length, payloadBits.Length);
        var decoded = codec.DecodeBits(allBits);
        progress?.Report(1);

        return decoded.IsValid
            ? new(WatermarkStatus.Valid, decoded.Text, null)
            : new(WatermarkStatus.Error, null, decoded.ErrorMessage);
    }

    private static void EmbedBit(double[,] coefficients, bool bit, double delta)
    {
        var first = coefficients[
            WatermarkSettings.Coefficient1Row,
            WatermarkSettings.Coefficient1Column];
        var second = coefficients[
            WatermarkSettings.Coefficient2Row,
            WatermarkSettings.Coefficient2Column];
        var difference = first - second;
        var targetDifference = delta + NumericalMargin;

        // C(2,3) and C(3,2) encode one bit.
        // Bit 1: C1-C2 >= Delta. Bit 0: C1-C2 <= -Delta.
        if ((bit && difference >= targetDifference) ||
            (!bit && difference <= -targetDifference))
        {
            return;
        }

        var mean = (first + second) / 2;
        var halfDifference = targetDifference / 2;
        coefficients[
            WatermarkSettings.Coefficient1Row,
            WatermarkSettings.Coefficient1Column] =
            bit ? mean + halfDifference : mean - halfDifference;
        coefficients[
            WatermarkSettings.Coefficient2Row,
            WatermarkSettings.Coefficient2Column] =
            bit ? mean - halfDifference : mean + halfDifference;
    }

    private bool TryReadBits(
        double[,] yPlane,
        IEnumerator<BlockPosition> positions,
        int count,
        double delta,
        CancellationToken cancellationToken,
        out bool[] bits,
        out int ambiguousBlock,
        out double ambiguousDifference,
        IProgress<double>? progress = null,
        int completedBits = 0,
        int totalBits = 0)
    {
        bits = new bool[count];
        ambiguousBlock = -1;
        ambiguousDifference = double.NaN;
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!positions.MoveNext())
            {
                throw new InvalidOperationException("Недостаточно полных блоков 8x8.");
            }

            var coefficients = dctService.Forward(
                ReadBlock(yPlane, positions.Current));
            var difference =
                coefficients[
                    WatermarkSettings.Coefficient1Row,
                    WatermarkSettings.Coefficient1Column] -
                coefficients[
                    WatermarkSettings.Coefficient2Row,
                    WatermarkSettings.Coefficient2Column];

            if (!TryExtractBit(difference, delta, out bits[index]))
            {
                ambiguousBlock = completedBits + index;
                ambiguousDifference = difference;
                return false;
            }

            if (progress is not null &&
                totalBits > 0 &&
                (index % 16 == 0 || index == count - 1))
            {
                progress.Report((double)(completedBits + index + 1) / totalBits);
            }
        }

        return true;
    }

    private static bool TryExtractBit(double difference, double delta, out bool bit)
    {
        if (difference >= delta)
        {
            bit = true;
            return true;
        }

        if (difference <= -delta)
        {
            bit = false;
            return true;
        }

        bit = false;
        return false;
    }

    private static WatermarkExtractionResult LowConfidence(
        int blockIndex,
        double difference) =>
        new(
            WatermarkStatus.Error,
            null,
            $"Низкая уверенность извлечения в блоке {blockIndex + 1}: " +
            $"разность коэффициентов {difference:F2} находится между -Delta и +Delta.");

    private static int GetCapacityBits(ImagePixelData image) =>
        image.Width / WatermarkSettings.BlockSize *
        (image.Height / WatermarkSettings.BlockSize);

    private static IEnumerable<BlockPosition> EnumerateBlocks(int blocksX, int blocksY)
    {
        for (var blockRow = 0; blockRow < blocksY; blockRow++)
        {
            for (var blockColumn = 0; blockColumn < blocksX; blockColumn++)
            {
                yield return new(blockRow, blockColumn);
            }
        }
    }

    private static double[,] ReadBlock(double[,] yPlane, BlockPosition position)
    {
        var block = new double[WatermarkSettings.BlockSize, WatermarkSettings.BlockSize];
        var startRow = position.Row * WatermarkSettings.BlockSize;
        var startColumn = position.Column * WatermarkSettings.BlockSize;
        for (var row = 0; row < WatermarkSettings.BlockSize; row++)
        {
            for (var column = 0; column < WatermarkSettings.BlockSize; column++)
            {
                block[row, column] = yPlane[startRow + row, startColumn + column];
            }
        }

        return block;
    }

    private static void WriteBlock(
        double[,] yPlane,
        BlockPosition position,
        double[,] block)
    {
        var startRow = position.Row * WatermarkSettings.BlockSize;
        var startColumn = position.Column * WatermarkSettings.BlockSize;
        for (var row = 0; row < WatermarkSettings.BlockSize; row++)
        {
            for (var column = 0; column < WatermarkSettings.BlockSize; column++)
            {
                yPlane[startRow + row, startColumn + column] =
                    Math.Clamp(block[row, column], 0, 255);
            }
        }
    }

    private static void RestoreUntouchedPixels(
        ImagePixelData original,
        ImagePixelData result,
        IReadOnlySet<BlockPosition> modifiedBlocks)
    {
        for (var y = 0; y < original.Height; y++)
        {
            for (var x = 0; x < original.Width; x++)
            {
                var block = new BlockPosition(
                    y / WatermarkSettings.BlockSize,
                    x / WatermarkSettings.BlockSize);
                if (modifiedBlocks.Contains(block))
                {
                    continue;
                }

                var pixelIndex = (y * original.Width + x) * 4;
                Buffer.BlockCopy(
                    original.Pixels,
                    pixelIndex,
                    result.Pixels,
                    pixelIndex,
                    4);
            }
        }
    }

    private readonly record struct BlockPosition(int Row, int Column);
}

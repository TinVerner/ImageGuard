using System.Buffers.Binary;
using System.Text;
using ImageGuard.Models;

namespace ImageGuard.Services;

public sealed class WatermarkService
{
    public const int LengthPrefixSize = 4;
    public const int Size = 8;
    private const int LengthPrefixBits = LengthPrefixSize * 8;
    // The IDCT -> YCbCr/RGB -> YCbCr/DCT round-trip can reduce the coefficient
    // difference. This fixed margin compensates rounding; it is not adaptive,
    // and extraction still uses the user-selected Delta as its threshold.
    private const double NumericalMargin = 25.0;

    private static readonly double[,] CosTable = BuildCosTable();
    private static readonly double[] Scale =
        [1.0 / Math.Sqrt(2), 1, 1, 1, 1, 1, 1, 1];

    public WatermarkCapacity CalculateCapacity(ImagePixelData image, string text)
    {
        ArgumentNullException.ThrowIfNull(image);
        var data = Encode(text);
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

        var bits = EncodeBits(text);
        var capacity = CalculateCapacity(image, text);
        if (!capacity.Fits)
        {
            throw new InvalidOperationException(
                "Изображение недостаточно велико для выбранного водяного знака. " +
                $"Доступно {capacity.CapacityBits} бит, требуется {capacity.RequiredBits}.");
        }

        // Convert RGB image to YCbCr. We use only Y for watermark embedding.
        var planes = ToYCbCr(image);
        var blocksX = image.Width / WatermarkSettings.BlockSize;
        var blocksY = image.Height / WatermarkSettings.BlockSize;
        using var positions = EnumerateBlocks(blocksX, blocksY).GetEnumerator();
        var modifiedBlocks = new HashSet<BlockPosition>();

        // One watermark bit is embedded into one complete 8x8 luminance block.
        for (var bitIndex = 0; bitIndex < bits.Length; bitIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!positions.MoveNext())
                throw new InvalidOperationException("Недостаточно полных блоков 8x8.");

            var position = positions.Current;
            modifiedBlocks.Add(position);
            var block = ReadBlock(planes.Y, position);
            var coefficients = Forward(block);
            EmbedBit(coefficients, bits[bitIndex], settings.Delta);
            WriteBlock(planes.Y, position, Inverse(coefficients));

            if (bitIndex % 16 == 0 || bitIndex == bits.Length - 1)
            {
                progress?.Report((bitIndex + 1.0) / bits.Length);
            }
        }

        var result = FromYCbCr(planes, image.DpiX, image.DpiY);
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

        var planes = ToYCbCr(image);
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

        var lengthBytes = ToBytesMsbFirst(lengthBits);
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
            return LowConfidence(ambiguousBlock, ambiguousDifference);

        var allBits = new bool[totalBits];
        Array.Copy(lengthBits, allBits, lengthBits.Length);
        Array.Copy(payloadBits, 0, allBits, lengthBits.Length, payloadBits.Length);
        var decoded = DecodeBits(allBits);
        progress?.Report(1);

        return decoded.IsValid
            ? new(WatermarkStatus.Valid, decoded.Text, null)
            : new(WatermarkStatus.Error, null, decoded.ErrorMessage);
    }

    public byte[] Encode(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException(
                "Текст цифрового водяного знака не может быть пустым.",
                nameof(text));
        }

        var payload = Encoding.UTF8.GetBytes(text);
        var data = new byte[LengthPrefixSize + payload.Length];

        // Первые 32 бита содержат длину UTF-8 текста в байтах (Big Endian).
        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(0, LengthPrefixSize),
            checked((uint)payload.Length));
        payload.CopyTo(data, LengthPrefixSize);
        return data;
    }

    private bool[] EncodeBits(string text) => ToBitsMsbFirst(Encode(text));

    public WatermarkTextDecodeResult Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < LengthPrefixSize)
            return new(false, null, "Недостаточно данных для чтения длины водяного знака.");

        var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(
            data[..LengthPrefixSize]);
        if (payloadLength == 0 ||
            payloadLength > int.MaxValue ||
            payloadLength > data.Length - LengthPrefixSize)
            return new(false, null, "Длина водяного знака некорректна.");

        try
        {
            var utf8 = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true);
            var text = utf8.GetString(
                data.Slice(LengthPrefixSize, checked((int)payloadLength)));
            return new(true, text, null);
        }
        catch (DecoderFallbackException)
        {
            return new(false, null, "Водяной знак содержит некорректный UTF-8.");
        }
    }

    private WatermarkTextDecodeResult DecodeBits(IReadOnlyList<bool> bits) =>
        Decode(ToBytesMsbFirst(bits));

    private static bool[] ToBitsMsbFirst(ReadOnlySpan<byte> bytes)
    {
        var bits = new bool[bytes.Length * 8];
        for (var byteIndex = 0; byteIndex < bytes.Length; byteIndex++)
        {
            for (var bitIndex = 0; bitIndex < 8; bitIndex++)
            {
                bits[byteIndex * 8 + bitIndex] =
                    (bytes[byteIndex] & (1 << (7 - bitIndex))) != 0;
            }
        }

        return bits;
    }

    private static byte[] ToBytesMsbFirst(IReadOnlyList<bool> bits)
    {
        ArgumentNullException.ThrowIfNull(bits);
        if (bits.Count % 8 != 0)
        {
            throw new ArgumentException(
                "Количество битов должно быть кратно восьми.",
                nameof(bits));
        }

        var bytes = new byte[bits.Count / 8];
        for (var bitIndex = 0; bitIndex < bits.Count; bitIndex++)
        {
            if (bits[bitIndex])
                bytes[bitIndex / 8] |= (byte)(1 << (7 - bitIndex % 8));
        }

        return bytes;
    }

    private YCbCrPlanes ToYCbCr(ImagePixelData image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var yPlane = new double[image.Height, image.Width];
        var cbPlane = new double[image.Height, image.Width];
        var crPlane = new double[image.Height, image.Width];
        var alpha = new byte[image.Width * image.Height];

        for (var row = 0; row < image.Height; row++)
        {
            for (var column = 0; column < image.Width; column++)
            {
                var pixelIndex = row * image.Stride + column * 4;
                var planeIndex = row * image.Width + column;
                var b = image.Pixels[pixelIndex];
                var g = image.Pixels[pixelIndex + 1];
                var r = image.Pixels[pixelIndex + 2];

                yPlane[row, column] = 0.299 * r + 0.587 * g + 0.114 * b;
                cbPlane[row, column] =
                    -0.168736 * r - 0.331264 * g + 0.5 * b + 128;
                crPlane[row, column] =
                    0.5 * r - 0.418688 * g - 0.081312 * b + 128;
                alpha[planeIndex] = image.Pixels[pixelIndex + 3];
            }
        }

        return new(yPlane, cbPlane, crPlane, alpha, image.Width, image.Height);
    }

    private ImagePixelData FromYCbCr(YCbCrPlanes planes, double dpiX, double dpiY)
    {
        ArgumentNullException.ThrowIfNull(planes);
        var pixels = new byte[planes.Width * planes.Height * 4];

        for (var row = 0; row < planes.Height; row++)
        {
            for (var column = 0; column < planes.Width; column++)
            {
                var pixelIndex = (row * planes.Width + column) * 4;
                var y = planes.Y[row, column];
                var cbOffset = planes.Cb[row, column] - 128;
                var crOffset = planes.Cr[row, column] - 128;

                var r = y + 1.402 * crOffset;
                var g = y - 0.34414 * cbOffset - 0.71414 * crOffset;
                var b = y + 1.772 * cbOffset;

                pixels[pixelIndex] = ClampToByte(b);
                pixels[pixelIndex + 1] = ClampToByte(g);
                pixels[pixelIndex + 2] = ClampToByte(r);
                pixels[pixelIndex + 3] = planes.Alpha[row * planes.Width + column];
            }
        }

        return new(planes.Width, planes.Height, dpiX, dpiY, pixels);
    }

    public double[,] Forward(double[,] block)
    {
        ValidateSize(block, nameof(block));
        var coefficients = new double[Size, Size];

        // DCT-II для блока 8x8. Level shift (-128) центрирует яркость около нуля
        // и отделяет постоянную составляющую (DC) от частотных коэффициентов.
        for (var u = 0; u < Size; u++)
        {
            for (var v = 0; v < Size; v++)
            {
                var sum = 0.0;
                for (var x = 0; x < Size; x++)
                {
                    for (var y = 0; y < Size; y++)
                        sum += (block[x, y] - 128.0) * CosTable[x, u] * CosTable[y, v];
                }

                coefficients[u, v] = 0.25 * Scale[u] * Scale[v] * sum;
            }
        }

        return coefficients;
    }

    public double[,] Inverse(double[,] coefficients)
    {
        ValidateSize(coefficients, nameof(coefficients));
        var block = new double[Size, Size];

        for (var x = 0; x < Size; x++)
        {
            for (var y = 0; y < Size; y++)
            {
                var sum = 0.0;
                for (var u = 0; u < Size; u++)
                {
                    for (var v = 0; v < Size; v++)
                    {
                        sum += Scale[u] * Scale[v] * coefficients[u, v] *
                               CosTable[x, u] * CosTable[y, v];
                    }
                }

                block[x, y] = 0.25 * sum + 128.0;
            }
        }

        return block;
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
                throw new InvalidOperationException("Недостаточно полных блоков 8x8.");

            var coefficients = Forward(ReadBlock(yPlane, positions.Current));
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
                yield return new(blockRow, blockColumn);
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
                block[row, column] = yPlane[startRow + row, startColumn + column];
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

    private static double[,] BuildCosTable()
    {
        var table = new double[Size, Size];
        for (var x = 0; x < Size; x++)
        {
            for (var u = 0; u < Size; u++)
            {
                table[x, u] = Math.Cos((2 * x + 1) * u * Math.PI / 16.0);
            }
        }

        return table;
    }

    private static void ValidateSize(double[,] values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.GetLength(0) != Size || values.GetLength(1) != Size)
        {
            throw new ArgumentException("DCT работает только с блоками 8x8.", parameterName);
        }
    }

    private static byte ClampToByte(double value) =>
        (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    private readonly record struct BlockPosition(int Row, int Column);
}

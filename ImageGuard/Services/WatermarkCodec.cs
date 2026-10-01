using System.Buffers.Binary;
using System.Text;
using ImageGuard.Models;

namespace ImageGuard.Services;

public sealed class WatermarkCodec
{
    public const int LengthPrefixSize = 4;

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

    public bool[] EncodeBits(string text) => ToBitsMsbFirst(Encode(text));

    public WatermarkTextDecodeResult Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < LengthPrefixSize)
        {
            return new(false, null, "Недостаточно данных для чтения длины водяного знака.");
        }

        var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(
            data[..LengthPrefixSize]);
        if (payloadLength == 0 ||
            payloadLength > int.MaxValue ||
            payloadLength > data.Length - LengthPrefixSize)
        {
            return new(false, null, "Длина водяного знака некорректна.");
        }

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

    public WatermarkTextDecodeResult DecodeBits(IReadOnlyList<bool> bits) =>
        Decode(ToBytesMsbFirst(bits));

    public static bool[] ToBitsMsbFirst(ReadOnlySpan<byte> bytes)
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

    public static byte[] ToBytesMsbFirst(IReadOnlyList<bool> bits)
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
            {
                bytes[bitIndex / 8] |= (byte)(1 << (7 - bitIndex % 8));
            }
        }

        return bytes;
    }
}

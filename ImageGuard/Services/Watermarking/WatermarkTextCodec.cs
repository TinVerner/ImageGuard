using System.Buffers.Binary;
using System.Text;
using ImageGuard.Models;

namespace ImageGuard.Services.Watermarking;

public interface IWatermarkTextCodec
{
    byte[] Encode(string text);
    WatermarkTextDecodeResult Decode(ReadOnlySpan<byte> data);
}

public sealed class WatermarkTextCodec : IWatermarkTextCodec
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
}

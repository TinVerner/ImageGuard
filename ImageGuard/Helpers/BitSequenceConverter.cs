namespace ImageGuard.Helpers;

public static class BitSequenceConverter
{
    public static bool[] ToBitsMsbFirst(ReadOnlySpan<byte> bytes)
    {
        var bits = new bool[bytes.Length * 8];
        for (var byteIndex = 0; byteIndex < bytes.Length; byteIndex++)
        {
            for (var bitIndex = 0; bitIndex < 8; bitIndex++)
            {
                bits[byteIndex * 8 + bitIndex] = (bytes[byteIndex] & (1 << (7 - bitIndex))) != 0;
            }
        }

        return bits;
    }

    public static byte[] ToBytesMsbFirst(IReadOnlyList<bool> bits)
    {
        ArgumentNullException.ThrowIfNull(bits);
        if (bits.Count % 8 != 0)
        {
            throw new ArgumentException("Количество битов должно быть кратно восьми.", nameof(bits));
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

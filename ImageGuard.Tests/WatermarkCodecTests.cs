using System.Buffers.Binary;
using System.Text;

namespace ImageGuard.Tests;

public sealed class WatermarkCodecTests
{
    [Fact]
    public void Encode_UsesOnlyBigEndianLengthAndUtf8Payload()
    {
        var codec = new TestServices().Watermarks;
        const string text = "Привет";
        var utf8 = Encoding.UTF8.GetBytes(text);

        var data = codec.Encode(text);

        Assert.Equal(4 + utf8.Length, data.Length);
        Assert.Equal((uint)utf8.Length, BinaryPrimitives.ReadUInt32BigEndian(data));
        Assert.Equal(utf8, data[4..]);
    }

    [Fact]
    public void Decode_RestoresUtf8Text()
    {
        var codec = new TestServices().Watermarks;

        var result = codec.Decode(codec.Encode("Привет мир"));

        Assert.True(result.IsValid);
        Assert.Equal("Привет мир", result.Text);
    }

    [Fact]
    public void Decode_RejectsInvalidLengthAndUtf8()
    {
        var codec = new TestServices().Watermarks;
        var invalidLength = new byte[] { 0, 0, 0, 2, (byte)'A' };
        var invalidUtf8 = new byte[] { 0, 0, 0, 1, 0xFF };

        Assert.False(codec.Decode(invalidLength).IsValid);
        Assert.False(codec.Decode(invalidUtf8).IsValid);
    }
}

using ImageGuard.Services;

namespace ImageGuard.Tests;

public sealed class DctServiceTests
{
    [Fact]
    public void ForwardThenInverse_RestoresBlock()
    {
        var service = new DctService();
        var original = new double[8, 8];
        for (var row = 0; row < 8; row++)
        {
            for (var column = 0; column < 8; column++)
            {
                original[row, column] = 20 + row * 12 + column * 3.5;
            }
        }

        var restored = service.Inverse(service.Forward(original));

        for (var row = 0; row < 8; row++)
        {
            for (var column = 0; column < 8; column++)
            {
                Assert.Equal(original[row, column], restored[row, column], precision: 8);
            }
        }
    }

    [Fact]
    public void Forward_Constant129Block_HasOnlyExpectedDcCoefficient()
    {
        var service = new DctService();
        var block = new double[8, 8];
        for (var row = 0; row < 8; row++)
        {
            for (var column = 0; column < 8; column++)
            {
                block[row, column] = 129;
            }
        }

        var coefficients = service.Forward(block);

        Assert.Equal(8, coefficients[0, 0], precision: 10);
        for (var row = 0; row < 8; row++)
        {
            for (var column = 0; column < 8; column++)
            {
                if (row != 0 || column != 0)
                {
                    Assert.Equal(0, coefficients[row, column], precision: 10);
                }
            }
        }
    }
}

namespace ImageGuard.Services.Watermarking;

public interface IDctService
{
    double[,] Forward(double[,] block);
    double[,] Inverse(double[,] coefficients);
}

public sealed class DctService : IDctService
{
    public const int Size = 8;
    private static readonly double[,] CosTable = BuildCosTable();
    private static readonly double[] Scale =
        [1.0 / Math.Sqrt(2), 1, 1, 1, 1, 1, 1, 1];

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
                    {
                        sum += (block[x, y] - 128.0) * CosTable[x, u] * CosTable[y, v];
                    }
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
}

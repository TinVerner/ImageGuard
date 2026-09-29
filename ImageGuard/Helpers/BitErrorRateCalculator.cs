namespace ImageGuard.Helpers;

public static class BitErrorRateCalculator
{
    public static double? Calculate(bool[]? expectedBits, bool[]? actualBits)
    {
        if (expectedBits is null ||
            actualBits is null ||
            expectedBits.Length == 0 ||
            expectedBits.Length != actualBits.Length)
        {
            return null;
        }

        var incorrectBits = 0;
        for (var index = 0; index < expectedBits.Length; index++)
        {
            if (expectedBits[index] != actualBits[index])
            {
                incorrectBits++;
            }
        }

        // BER определяется по непосредственным решениям извлечения DCT-битов,
        // без повторного кодирования уже декодированного текста.
        return (double)incorrectBits / expectedBits.Length;
    }
}

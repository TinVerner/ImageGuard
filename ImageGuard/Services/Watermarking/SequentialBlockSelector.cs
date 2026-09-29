namespace ImageGuard.Services.Watermarking;

public readonly record struct BlockPosition(int Row, int Column);

public interface IBlockSelector
{
    IEnumerable<BlockPosition> Select(int blocksX, int blocksY);
}

public sealed class SequentialBlockSelector : IBlockSelector
{
    public IEnumerable<BlockPosition> Select(int blocksX, int blocksY)
    {
        for (var blockRow = 0; blockRow < blocksY; blockRow++)
        {
            for (var blockColumn = 0; blockColumn < blocksX; blockColumn++)
            {
                yield return new BlockPosition(blockRow, blockColumn);
            }
        }
    }
}

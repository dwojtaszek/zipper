namespace Zipper;

internal static class ZipSizeVerifier
{
    public static (bool IsWithinTolerance, double Deviation) Verify(long targetSize, long actualSize)
    {
        if (targetSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetSize), "targetSize must be positive.");
        }
        double deviation = Math.Abs(actualSize - targetSize) / (double)targetSize;
        bool isWithinTolerance = deviation <= 0.10;
        return (isWithinTolerance, deviation);
    }
}

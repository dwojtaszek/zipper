namespace Zipper
{
    internal static class ZipSizeVerifier
    {
        public static (bool IsWithinTolerance, double Deviation) Verify(long targetSize, long actualSize)
        {
            double deviation = targetSize > 0 ? Math.Abs(actualSize - targetSize) / (double)targetSize : 0;
            bool isWithinTolerance = deviation <= 0.10;
            return (isWithinTolerance, deviation);
        }
    }
}

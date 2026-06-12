namespace Zipper
{
    internal static class ZipSizeVerifier
    {
        public static (bool IsWithinTolerance, double Deviation) Verify(long targetSize, long actualSize)
        {
            if (targetSize <= 0)
            {
                bool isZeroMatch = targetSize == 0 && actualSize == 0;
                return (isZeroMatch, isZeroMatch ? 0.0 : double.PositiveInfinity);
            }
            double deviation = Math.Abs(actualSize - targetSize) / (double)targetSize;
            bool isWithinTolerance = deviation <= 0.10;
            return (isWithinTolerance, deviation);
        }
    }
}

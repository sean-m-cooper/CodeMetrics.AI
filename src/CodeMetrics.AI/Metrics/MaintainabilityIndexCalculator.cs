namespace CodeMetrics.AI.Metrics;

public static class MaintainabilityIndexCalculator
{
    public static int Calculate(int cc, int sourceLines, double halsteadVolume)
    {
        if (sourceLines == 0) return 100;

        double hv = Math.Max(halsteadVolume, 1.0);
        double loc = Math.Max(sourceLines, 1.0);

        double raw = 171.0
            - 5.2 * Math.Log(hv)
            - 0.23 * cc
            - 16.2 * Math.Log(loc);

        double normalized = Math.Max(0, raw) * 100.0 / 171.0;

        return (int)Math.Clamp(Math.Round(normalized), 0, 100);
    }
}

namespace ACOdysseyUMM;

internal sealed class ScaledProgress(IProgress<double>? outer, double start, double end) : IProgress<double>
{
    public void Report(double value)
    {
        if (outer is null)
            return;
        var normalized = Math.Clamp(value, 0d, 1d);
        outer.Report(start + ((end - start) * normalized));
    }

    public static IProgress<double>? Slice(IProgress<double>? outer, double start, double end) =>
        outer is null ? null : new ScaledProgress(outer, start, end);
}

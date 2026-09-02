namespace ACOdysseyUMM;

internal enum MercenaryLevelMode
{
    Off,
    Light,
    Linear
}

internal enum HunterPressureMode
{
    X3,
    X6
}

internal sealed record MercenaryInstallSelection(
    MercenaryLevelMode LevelMode,
    HunterPressureMode PressureMode,
    bool ImmediateRecycle);

internal static class MercenaryPatchSelectionResolver
{
    public const string LevelLinearPatchId = "mercenary.level.linear";
    public const string LevelLightPatchId = "mercenary.level.light";
    public const string PressureX3PatchId = "bounty.behavior.pressure-x3";
    public const string PressureX6PatchId = "bounty.behavior.pressure-x6";
    public const string Selector255PatchId = "mercenary.selector255";
    public const string FastTravelRedispatchPatchId = "mercenary.dispatch.fasttravel-1000ms";
    public const string ImmediateRecyclePatchId = "mercenary.lifecycle.immediate-recycle";

    public static IReadOnlyList<string> Resolve(MercenaryInstallSelection selection)
    {
        var result = new List<string>(5)
        {
            FastTravelRedispatchPatchId
        };

        switch (selection.LevelMode)
        {
            case MercenaryLevelMode.Off:
                break;
            case MercenaryLevelMode.Light:
                result.Add(LevelLightPatchId);
                result.Add(Selector255PatchId);
                break;
            case MercenaryLevelMode.Linear:
                result.Add(LevelLinearPatchId);
                result.Add(Selector255PatchId);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(selection), "Unknown mercenary level mode.");
        }

        result.Add(selection.PressureMode switch
        {
            HunterPressureMode.X3 => PressureX3PatchId,
            HunterPressureMode.X6 => PressureX6PatchId,
            _ => throw new ArgumentOutOfRangeException(nameof(selection), "Unknown hunter pressure mode.")
        });

        if (selection.ImmediateRecycle)
            result.Add(ImmediateRecyclePatchId);

        return result;
    }

    public static string Describe(MercenaryInstallSelection selection)
    {
        var level = selection.LevelMode switch
        {
            MercenaryLevelMode.Off => "Mercenary Level Unlock: Off",
            MercenaryLevelMode.Light => "Mercenary Level Unlock: Light",
            MercenaryLevelMode.Linear => "Mercenary Level Unlock: Linear",
            _ => throw new ArgumentOutOfRangeException(nameof(selection), "Unknown mercenary level mode.")
        };

        var pressure = selection.PressureMode switch
        {
            HunterPressureMode.X3 => "Hunter Pressure: x3 (max 15)",
            HunterPressureMode.X6 => "Hunter Pressure: x6 (max 30)",
            _ => throw new ArgumentOutOfRangeException(nameof(selection), "Unknown hunter pressure mode.")
        };

        var recycle = selection.ImmediateRecycle ? ", Immediate Mercenary Recycle" : string.Empty;
        return $"{level}, {pressure}, Dead Hunter Release, Immediate Refill, Fast-Travel Redispatch: 1s{recycle}";
    }
}
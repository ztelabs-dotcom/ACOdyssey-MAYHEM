namespace ACOdysseyUMM;

internal enum MilitaryNpcRank
{
    Soldier = 0,
    Captain = 1,
    Polemarch = 2,
}

internal static class NightmareScalingPolicy
{
    internal const int NightmareDifficultyProfileId = 3;
    internal const int MinimumMercenaryHierarchyPosition = 1;
    internal const int MaximumMercenaryHierarchyPosition = 39;

    internal static bool IsEnabled(int effectiveDifficultyProfileId)
        => effectiveDifficultyProfileId == NightmareDifficultyProfileId;

    internal static bool TryGetMilitaryLevel(
        int vanillaSubregionBase,
        MilitaryNpcRank rank,
        out int level)
    {
        level = default;

        if (vanillaSubregionBase is < 1 or > 99)
        {
            return false;
        }

        int offset = rank switch
        {
            MilitaryNpcRank.Soldier => 94,
            MilitaryNpcRank.Captain => 97,
            MilitaryNpcRank.Polemarch => 99,
            _ => int.MinValue,
        };

        if (offset == int.MinValue)
        {
            return false;
        }

        level = checked(vanillaSubregionBase + offset);
        return true;
    }

    internal static bool TryGetMercenaryLevel(int hierarchyPosition, out int level)
    {
        level = default;

        if (hierarchyPosition is < MinimumMercenaryHierarchyPosition or > MaximumMercenaryHierarchyPosition)
        {
            return false;
        }

        level = 99 + (((MaximumMercenaryHierarchyPosition - hierarchyPosition) * 156 + 19) / 38);
        return true;
    }
}

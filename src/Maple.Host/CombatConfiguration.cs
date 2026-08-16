using Maple.Contracts;
using Maple.Input;

namespace Maple.Host;

public enum CombatAttackMode { Single, Auto, Group }

public sealed record CombatConfiguration
{
    public int SchemaVersion { get; init; } = ContractConstants.SchemaVersion;
    public CombatAttackMode AttackMode { get; init; } = CombatAttackMode.Single;
    public ResourceMode HpThresholdMode { get; init; } = ResourceMode.Percent;
    public double HpThreshold { get; init; } = 50;
    public ResourceMode MpThresholdMode { get; init; } = ResourceMode.Percent;
    public double MpThreshold { get; init; } = 30;
    public string SingleAttackKey { get; init; } = "Ctrl";
    public string AreaAttackKey { get; init; } = "Ctrl";
    public string HpPotionKey { get; init; } = "Delete";
    public string MpPotionKey { get; init; } = "End";
    public string JumpKey { get; init; } = "Alt";
    public bool PickupEnabled { get; init; } = true;
    public string PickupKey { get; init; } = "Z";
    public int PreferredDistancePx { get; init; } = 70;
    public int AreaTargetCount { get; init; } = 3;
    public int SwitchCooldownMs { get; init; } = 1200;

    public static CombatConfiguration Default { get; } = new();
}

public static class CombatConfigurationValidator
{
    public static CombatConfiguration ValidateAndNormalize(CombatConfiguration value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.SchemaVersion != ContractConstants.SchemaVersion) throw new ArgumentException("CONFIG_SCHEMA_INVALID", nameof(value));
        ValidateThreshold(value.HpThresholdMode, value.HpThreshold, "HP_THRESHOLD_INVALID");
        ValidateThreshold(value.MpThresholdMode, value.MpThreshold, "MP_THRESHOLD_INVALID");
        if (value.PreferredDistancePx is < 20 or > 500) throw new ArgumentException("PREFERRED_DISTANCE_INVALID", nameof(value));
        if (value.AreaTargetCount is < 2 or > 20) throw new ArgumentException("AREA_TARGET_COUNT_INVALID", nameof(value));
        if (value.SwitchCooldownMs is < 100 or > 10000) throw new ArgumentException("SWITCH_COOLDOWN_INVALID", nameof(value));

        CombatConfiguration normalized = value with
        {
            SingleAttackKey = BrokerKeyProfile.NormalizeLogicalKey(value.SingleAttackKey),
            AreaAttackKey = BrokerKeyProfile.NormalizeLogicalKey(value.AreaAttackKey),
            HpPotionKey = BrokerKeyProfile.NormalizeLogicalKey(value.HpPotionKey),
            MpPotionKey = BrokerKeyProfile.NormalizeLogicalKey(value.MpPotionKey),
            JumpKey = BrokerKeyProfile.NormalizeLogicalKey(value.JumpKey),
            PickupKey = BrokerKeyProfile.NormalizeLogicalKey(value.PickupKey),
        };
        string[] exclusiveKeys = [normalized.HpPotionKey, normalized.MpPotionKey, normalized.JumpKey, normalized.PickupKey];
        bool exclusiveConflict = exclusiveKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != exclusiveKeys.Length;
        bool attackConflict = exclusiveKeys.Contains(normalized.SingleAttackKey, StringComparer.OrdinalIgnoreCase)
            || exclusiveKeys.Contains(normalized.AreaAttackKey, StringComparer.OrdinalIgnoreCase);
        if (exclusiveConflict || attackConflict)
            throw new ArgumentException("ACTION_KEY_CONFLICT", nameof(value));
        return normalized;
    }

    private static void ValidateThreshold(ResourceMode mode, double value, string code)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || (mode == ResourceMode.Percent && value > 100))
            throw new ArgumentException(code);
    }
}

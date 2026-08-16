using Xunit;

namespace Maple.Host.Tests;

public sealed class CombatConfigurationStoreTests
{
    [Fact]
    public void DefaultsMatchTheConfirmedClientBindings()
    {
        CombatConfiguration configuration = CombatConfiguration.Default;

        Assert.Equal(CombatAttackMode.Single, configuration.AttackMode);
        Assert.Equal("Ctrl", configuration.SingleAttackKey);
        Assert.Equal("Ctrl", configuration.AreaAttackKey);
        Assert.Equal("Delete", configuration.HpPotionKey);
        Assert.Equal("End", configuration.MpPotionKey);
        Assert.Equal("Alt", configuration.JumpKey);
        Assert.Equal("Z", configuration.PickupKey);
        Assert.Equal(configuration, CombatConfigurationValidator.ValidateAndNormalize(configuration));
    }

    [Fact]
    public async Task SaveAndLoadPreservesValidatedCombatConfiguration()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"maple-combat-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "combat-v2.json");
        try
        {
            var store = new CombatConfigurationStore(path);
            CombatConfiguration config = CombatConfiguration.Default with
            {
                AttackMode = CombatAttackMode.Single,
                SingleAttackKey = "Ctrl",
                PickupEnabled = false,
                HpThreshold = 42,
            };

            await store.SaveAsync(config, CancellationToken.None);
            var reloaded = new CombatConfigurationStore(path);

            Assert.Equal(config, await reloaded.LoadAsync(CancellationToken.None));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CorruptFileFallsBackToDefaultsAndConflictingKeysAreRejected()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"maple-combat-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "combat-v2.json");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(path, "{broken", CancellationToken.None);
            var store = new CombatConfigurationStore(path);

            Assert.Equal(CombatConfiguration.Default, await store.LoadAsync(CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(
                CombatConfiguration.Default with { JumpKey = "Z" }, CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(
                CombatConfiguration.Default with { HpThreshold = 101 }, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

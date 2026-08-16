using System.Text.Json;
using System.Text.Json.Serialization;

namespace Maple.Host;

public interface ICombatConfigurationStore
{
    CombatConfiguration Current { get; }
    Task<CombatConfiguration> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(CombatConfiguration configuration, CancellationToken cancellationToken);
}

public sealed class CombatConfigurationStore : ICombatConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string path;

    public CombatConfigurationStore(string? path = null)
    {
        this.path = string.IsNullOrWhiteSpace(path)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Maple", "config", "combat-v2.json")
            : Path.GetFullPath(path);
    }

    public CombatConfiguration Current { get; private set; } = CombatConfiguration.Default;

    public async Task<CombatConfiguration> LoadAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path)) return Current = CombatConfiguration.Default;
            try
            {
                string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                CombatConfiguration? loaded = JsonSerializer.Deserialize<CombatConfiguration>(json, JsonOptions);
                return Current = CombatConfigurationValidator.ValidateAndNormalize(loaded ?? throw new JsonException("empty configuration"));
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException or IOException)
            {
                return Current = CombatConfiguration.Default;
            }
        }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(CombatConfiguration configuration, CancellationToken cancellationToken)
    {
        CombatConfiguration normalized = CombatConfigurationValidator.ValidateAndNormalize(configuration);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string temporaryPath = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string json = JsonSerializer.Serialize(normalized, JsonOptions);
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
            if (File.Exists(path)) File.Replace(temporaryPath, path, null, ignoreMetadataErrors: true);
            else File.Move(temporaryPath, path);
            Current = normalized;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            gate.Release();
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

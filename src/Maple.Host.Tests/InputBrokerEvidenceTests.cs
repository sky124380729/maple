using System.Text.Json;
using Xunit;

namespace Maple.Host.Tests;

public sealed class InputBrokerEvidenceTests
{
    [Fact]
    public void WriterAppendsOneCamelCaseJsonObjectPerLine()
    {
        string root = Path.Combine(Path.GetTempPath(), "maple-evidence-" + Guid.NewGuid().ToString("N"));
        try
        {
            var writer = new InputBrokerEvidenceWriter(root);
            writer.Append(new InputBrokerEvidenceRecord(
                "move-left", DateTimeOffset.UtcNow, 123, 45, true, 8192, 12288, 12288,
                0x25, 0x4B, 1, 3,
                "move-left-before.png", "move-left-after.png",
                "CLIENT_EFFECT_CONFIRMED", true));

            string[] lines = File.ReadAllLines(writer.JsonlPath);
            Assert.Single(lines);
            using JsonDocument json = JsonDocument.Parse(lines[0]);
            Assert.Equal("move-left", json.RootElement.GetProperty("actionId").GetString());
            Assert.Equal(0x4B, json.RootElement.GetProperty("scanCode").GetInt32());
            Assert.True(json.RootElement.GetProperty("allKeysReleased").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void WriterRejectsEvidencePathsOutsideItsSession()
    {
        string root = Path.Combine(Path.GetTempPath(), "maple-evidence-" + Guid.NewGuid().ToString("N"));
        try
        {
            var writer = new InputBrokerEvidenceWriter(root);
            var record = new InputBrokerEvidenceRecord(
                "jump", DateTimeOffset.UtcNow, 123, 45, true, 8192, 12288, 12288,
                0x12, 0x38, 0, 2,
                "..\\before.png", "jump-after.png",
                "CLIENT_EFFECT_CONFIRMED", true);

            Assert.Throws<InvalidDataException>(() => writer.Append(record));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void CurrentWindowsProcessIntegrityCanBeRead()
    {
        if (!OperatingSystem.IsWindows()) return;

        Assert.True(WindowsProcessIntegrity.ReadRid(Environment.ProcessId) >= 4096);
    }

    [Fact]
    public void WriterRefusesToAppendToAnExistingSession()
    {
        string root = Path.Combine(Path.GetTempPath(), "maple-evidence-" + Guid.NewGuid().ToString("N"));
        try
        {
            var writer = new InputBrokerEvidenceWriter(root);
            File.WriteAllText(writer.JsonlPath, "existing");

            Assert.Throws<InvalidDataException>(() => new InputBrokerEvidenceWriter(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}

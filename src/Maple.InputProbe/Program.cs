using System;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace Maple.InputProbe;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (HasArgument(args, "--self-test"))
        {
            return RunSelfTest(GetArgumentValue(args, "--output"));
        }

        bool authorizedRun = HasArgument(args, "--authorized-run-once");
        if (authorizedRun && !string.Equals(
                GetArgumentValue(args, "--ack"),
                "authorized-foreground-test",
                StringComparison.Ordinal))
        {
            return 2;
        }

        ApplicationConfiguration.Initialize();
        var options = new ProbeRunOptions
        {
            OutputRoot = string.IsNullOrWhiteSpace(GetArgumentValue(args, "--output"))
                ? new ProbeRunOptions().OutputRoot
                : Path.GetFullPath(GetArgumentValue(args, "--output"))
        };
        Application.Run(new ProbeForm(
            new ProbeRunner(new TargetWindowInspector(), new WindowsKeybdEventSender()),
            options,
            authorizedRun));
        return 0;
    }

    private static int RunSelfTest(string outputValue)
    {
        string output = string.IsNullOrWhiteSpace(outputValue)
            ? Path.Combine(AppContext.BaseDirectory, "self-test", "probe-evidence.jsonl")
            : Path.GetFullPath(outputValue);
        string path = string.Equals(Path.GetExtension(output), ".jsonl", StringComparison.OrdinalIgnoreCase)
            ? output
            : Path.Combine(output, "probe-evidence.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var evidence = new ProbeEvidence
        {
            SessionId = "self-test",
            ActionId = "self-test",
            Classification = "SELF_TEST_NO_INPUT",
            Reason = "Diagnostic-only self-test sends no input",
            InputAttempted = false,
            AllKeysReleased = true
        };
        string json = JsonSerializer.Serialize(evidence, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        File.WriteAllText(path, json + Environment.NewLine);
        return 0;
    }

    private static bool HasArgument(string[] args, string name)
    {
        foreach (string argument in args)
        {
            if (string.Equals(argument, name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string GetArgumentValue(string[] args, string name)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
        }
        return null;
    }
}

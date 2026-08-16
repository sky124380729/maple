using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Maple.Input;

namespace Maple.InputBroker;

internal static class Program
{
    private const int HeartbeatTimeoutMs = 1_500;
    private static readonly Regex PipeNamePattern = new(
        "^[A-Za-z0-9.-]{16,128}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        if (!TryParse(args, out BrokerOptions options))
        {
            WriteStartupDiagnostic("arguments-rejected", null);
            return 2;
        }
        WriteStartupDiagnostic("starting", null);
        using var cancellation = new CancellationTokenSource();
        try
        {
            using Process parent = Process.GetProcessById(options.ParentPid);
            parent.EnableRaisingEvents = true;
            parent.Exited += (_, _) => cancellation.Cancel();
            var clock = new SystemBrokerClock();
            await using var session = new BrokerInputSession(
                new WindowsKeybdEventSender(),
                new BrokerSafetyGate(clock),
                clock,
                HeartbeatTimeoutMs);
            var server = new BrokerServer(
                options.PipeName,
                options.ParentPid,
                session,
                new BrokerMessageCodec(),
                new BrokerRequestValidator());
            WriteStartupDiagnostic("waiting-for-client", null);
            await server.RunAsync(cancellation.Token);
            WriteStartupDiagnostic("completed", null);
            return 0;
        }
        catch (OperationCanceledException)
        {
            WriteStartupDiagnostic("cancelled", null);
            return 0;
        }
        catch (Exception exception)
        {
            WriteStartupDiagnostic("faulted", exception);
            return 1;
        }
    }

    private static void WriteStartupDiagnostic(string stage, Exception exception)
    {
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Maple",
                "InputBroker");
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "last-run.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    stage,
                    processId = Environment.ProcessId,
                    isElevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator),
                    exceptionType = exception?.GetType().Name,
                    exceptionMessage = exception?.Message,
                    writtenAtUtc = DateTimeOffset.UtcNow,
                }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private static bool TryParse(string[] args, out BrokerOptions options)
    {
        options = null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
                return false;
            if (!values.TryAdd(args[index], args[index + 1])) return false;
        }

        if (!values.TryGetValue("--pipe", out string pipeName) || !PipeNamePattern.IsMatch(pipeName))
            return false;
        if (!values.TryGetValue("--parent-pid", out string parentText) ||
            !int.TryParse(parentText, out int parentPid) || parentPid <= 0)
            return false;
        if (!values.TryGetValue("--protocol-version", out string versionText) ||
            !int.TryParse(versionText, out int version) || version != BrokerProtocol.Version)
            return false;
        if (values.Count != 3) return false;

        options = new BrokerOptions(pipeName, parentPid);
        return true;
    }

    private sealed record BrokerOptions(string PipeName, int ParentPid);
}

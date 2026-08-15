using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using Maple.Input;

namespace Maple.Host;

public sealed class BrokerProcessLauncher : IDisposable
{
    private readonly object sync = new();
    private Process? ownedProcess;

    public int? OwnedProcessId
    {
        get
        {
            lock (sync)
            {
                try { return ownedProcess is { HasExited: false } ? ownedProcess.Id : null; }
                catch (InvalidOperationException) { return null; }
            }
        }
    }

    public ProcessStartInfo CreateStartInfo(
        string brokerExecutable,
        string pipeName,
        int parentPid)
    {
        if (string.IsNullOrWhiteSpace(brokerExecutable))
            throw new ArgumentException("BROKER_EXECUTABLE_REQUIRED", nameof(brokerExecutable));
        if (string.IsNullOrWhiteSpace(pipeName) || pipeName.Contains(' ') || pipeName.Contains('"'))
            throw new ArgumentException("BROKER_PIPE_NAME_INVALID", nameof(pipeName));
        if (parentPid <= 0) throw new ArgumentOutOfRangeException(nameof(parentPid));

        return new ProcessStartInfo
        {
            FileName = brokerExecutable,
            Arguments = $"--pipe {pipeName} --parent-pid {parentPid} --protocol-version {BrokerProtocol.Version}",
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = System.IO.Path.GetDirectoryName(brokerExecutable) ?? string.Empty,
            WindowStyle = ProcessWindowStyle.Hidden
        };
    }

    public Process Launch(string brokerExecutable, string pipeName, int parentPid)
    {
        try
        {
            Process process = Process.Start(CreateStartInfo(brokerExecutable, pipeName, parentPid))
                ?? throw new InputUnavailableException("INPUT_BROKER_START_FAILED");
            lock (sync)
            {
                if (ownedProcess is { HasExited: false })
                {
                    process.Kill(entireProcessTree: true);
                    process.Dispose();
                    throw new InputUnavailableException("INPUT_BROKER_ALREADY_RUNNING");
                }
                ownedProcess?.Dispose();
                ownedProcess = process;
            }
            return process;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new InputUnavailableException("INPUT_BROKER_ELEVATION_CANCELLED", exception);
        }
    }

    public static string CreatePipeName() =>
        "maple." + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public void Dispose()
    {
        Process? process;
        lock (sync)
        {
            process = ownedProcess;
            ownedProcess = null;
        }
        if (process == null) return;
        try
        {
            if (!process.HasExited && !process.WaitForExit(2_000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2_000);
            }
        }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
        finally { process.Dispose(); }
    }
}

public sealed class InputUnavailableException : Exception
{
    public InputUnavailableException(string code, Exception? inner = null)
        : base(code, inner) => Code = code;

    public string Code { get; }
}

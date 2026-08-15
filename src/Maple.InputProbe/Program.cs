using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Maple.Contracts;
using Maple.Input;

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

        ApplicationConfiguration.Initialize();
        Application.Run(new ProbeForm(new ProbeRunner(new TargetWindowInspector(), new WindowsKeybdEventSender())));
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

        var recordingSender = new ProbeKeyboardEventRecorder(new NoOpKeyboardEventSender());
        var adapter = new KeybdEventInputAdapter(
            recordingSender,
            new SelfTestSafetyGate(),
            KeybdEventMode.ExtendedScanCode);
        var lines = new List<string>();
        (string Key, ActionType ActionType)[] cases =
        {
            ("Left", ActionType.MoveLeft),
            ("Up", ActionType.ClimbUp),
            ("Right", ActionType.MoveRight),
            ("Down", ActionType.ClimbDown)
        };

        foreach ((string key, ActionType actionType) in cases)
        {
            string actionId = "self-test-" + key.ToLowerInvariant();
            var action = new AbstractAction
            {
                ActionId = actionId,
                Type = actionType,
                HoldMs = 1,
                MaxDurationMs = 100
            };
            int marker = recordingSender.Mark();
            InputResult down = adapter.KeyDown(action, key, 10);
            InputResult up = adapter.KeyUp(action, key, 11);
            InputResult release = adapter.ReleaseAll(12);
            ProbeActionInputEvidence input = ProbeActionInputEvidence.FromEmittedEvents(
                KeybdEventMode.ExtendedScanCode,
                recordingSender.GetEventsSince(marker));

            lines.Add(ProbeEvidenceJson.Serialize(new ProbeEvidence
            {
                SessionId = "self-test",
                ActionId = actionId,
                InputMode = input.InputMode,
                Vk = input.VirtualKey,
                ScanCode = input.ScanCode,
                FlagsDown = input.FlagsDown,
                FlagsUp = input.FlagsUp,
                Classification = "SELF_TEST_NO_INPUT",
                Reason = $"Diagnostic-only self-test sends no input;{down.Message};{up.Message}",
                InputAttempted = false,
                AllKeysReleased = release.Status == InputStatus.Completed &&
                    adapter.GetStatus().ActiveKeys.Count == 0
            }));
        }

        File.WriteAllLines(path, lines);
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

    private sealed class SelfTestSafetyGate : IInputSafetyGate
    {
        public bool CanSend(string reason) => true;
    }

    private sealed class NoOpKeyboardEventSender : IKeyboardEventSender
    {
        public void Send(ushort virtualKey, uint scanCode, uint flags)
        {
        }
    }
}

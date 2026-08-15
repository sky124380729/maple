using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Maple.Contracts;
using Maple.Input;

namespace Maple.InputProbe;

internal sealed class ProbeRunner
{
    private readonly TargetWindowInspector inspector;
    private readonly IKeyboardEventSender sender;
    private readonly object sync = new();
    private KeybdEventInputAdapter activeAdapter;

    public ProbeRunner(TargetWindowInspector inspector, IKeyboardEventSender sender)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        this.sender = sender ?? throw new ArgumentNullException(nameof(sender));
    }

    public async Task<ProbeRunResult> RunAsync(
        ProbeRunOptions options,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TargetWindowInfo> targets = inspector.FindTargets();
        if (targets.Count == 0) throw new InvalidOperationException("TARGET_NOT_FOUND");
        if (targets.Count > 1) throw new InvalidOperationException("MULTIPLE_TARGETS_FOUND");

        TargetWindowInfo target = targets[0];
        ValidateInitialTarget(target);
        string sessionId = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff");
        var logger = new ProbeLogger(options.OutputRoot, sessionId);
        var gate = new ProbeSafetyGate(inspector, target.Hwnd);
        var adapter = new KeybdEventInputAdapter(sender, gate);
        lock (sync) activeAdapter = adapter;

        bool released = false;
        try
        {
            progress.Report($"已绑定 {target.Title} / PID {target.ProcessId} / HWND 0x{target.Hwnd.ToInt64():X}");
            for (int seconds = options.CountdownSeconds; seconds > 0; seconds--)
            {
                progress.Report($"{seconds} 秒后切换到游戏并测试左右键");
                await Task.Delay(1000, cancellationToken);
            }

            if (!inspector.TryBringToForeground(target.Hwnd))
            {
                for (int attempt = 0; attempt < 10 && !inspector.IsForeground(target.Hwnd); attempt++)
                {
                    await Task.Delay(100, cancellationToken);
                }
            }

            if (!inspector.IsForeground(target.Hwnd))
            {
                throw new InvalidOperationException("FOREGROUND_CONFIRMATION_FAILED");
            }

            progress.Report("目标窗口已前台确认，执行左键单次测试");
            await ExecuteActionAsync(
                adapter,
                gate,
                target.Hwnd,
                logger,
                "probe-left",
                "Left",
                ActionType.MoveLeft,
                options.HoldMs,
                cancellationToken);

            await Task.Delay(options.BetweenActionsMs, cancellationToken);
            if (!inspector.IsForeground(target.Hwnd))
            {
                throw new InvalidOperationException("TARGET_LOST_FOREGROUND_BETWEEN_ACTIONS");
            }

            progress.Report("执行右键单次测试");
            await ExecuteActionAsync(
                adapter,
                gate,
                target.Hwnd,
                logger,
                "probe-right",
                "Right",
                ActionType.MoveRight,
                options.HoldMs,
                cancellationToken);

            InputResult releaseResult = adapter.ReleaseAll(Environment.TickCount64);
            released = releaseResult.Status == InputStatus.Completed && adapter.GetStatus().ActiveKeys.Count == 0;
            progress.Report(released ? "测试完成，全部按键已释放" : "测试结束，但释放状态异常");
            return new ProbeRunResult
            {
                SessionDirectory = logger.SessionDirectory,
                EvidencePath = logger.JsonlPath,
                AllKeysReleased = released
            };
        }
        finally
        {
            InputResult finalRelease = adapter.ReleaseAll(Environment.TickCount64);
            released = finalRelease.Status == InputStatus.Completed && adapter.GetStatus().ActiveKeys.Count == 0;
            lock (sync) activeAdapter = null;
            progress.Report(released ? "安全清理完成：全部按键已释放" : "安全清理报告释放失败");
        }
    }

    public bool StopAndRelease()
    {
        KeybdEventInputAdapter adapter;
        lock (sync) adapter = activeAdapter;
        if (adapter == null) return true;

        InputResult result = adapter.ReleaseAll(Environment.TickCount64);
        return result.Status == InputStatus.Completed && adapter.GetStatus().ActiveKeys.Count == 0;
    }

    private async Task ExecuteActionAsync(
        KeybdEventInputAdapter adapter,
        ProbeSafetyGate gate,
        IntPtr hwnd,
        ProbeLogger logger,
        string actionId,
        string key,
        ActionType actionType,
        int holdMs,
        CancellationToken cancellationToken)
    {
        TargetWindowInfo before = inspector.Inspect(hwnd);
        string beforePath = WindowScreenshot.Capture(before, logger.SessionDirectory, actionId + "-before.png");
        var action = new AbstractAction
        {
            ActionId = actionId,
            Type = actionType,
            IssuedAtMonoMs = Environment.TickCount64,
            HoldMs = holdMs,
            MaxDurationMs = Math.Max(holdMs + 500, 1000)
        };

        InputResult down = adapter.KeyDown(action, key, Environment.TickCount64);
        bool attempted = down.Status == InputStatus.Accepted;
        string reason = down.Message;
        try
        {
            if (attempted)
            {
                int elapsed = 0;
                while (elapsed < holdMs)
                {
                    int slice = Math.Min(50, holdMs - elapsed);
                    await Task.Delay(slice, cancellationToken);
                    elapsed += slice;
                    if (!inspector.IsForeground(hwnd))
                    {
                        reason = "TARGET_LOST_FOREGROUND_DURING_HOLD";
                        break;
                    }
                }
            }
        }
        finally
        {
            InputResult up = adapter.KeyUp(action, key, Environment.TickCount64);
            if (up.Status != InputStatus.Completed)
            {
                reason += ";" + up.Message;
            }
        }

        await Task.Delay(600, cancellationToken);
        TargetWindowInfo after = inspector.Inspect(hwnd);
        string afterPath = WindowScreenshot.Capture(after, logger.SessionDirectory, actionId + "-after.png");
        bool foregroundConfirmed = before.ForegroundHwnd == hwnd && after.ForegroundHwnd == hwnd;
        string classification = foregroundConfirmed && attempted
            ? "UNKNOWN_MANUAL_VISUAL_REVIEW"
            : "UNKNOWN_SAFETY_GATE";

        InputResult release = adapter.ReleaseAll(Environment.TickCount64);
        bool allReleased = release.Status == InputStatus.Completed && adapter.GetStatus().ActiveKeys.Count == 0;
        logger.Append(new ProbeEvidence
        {
            SessionId = System.IO.Path.GetFileName(logger.SessionDirectory),
            ActionId = actionId,
            TargetHwnd = hwnd.ToInt64(),
            TargetPid = before.ProcessId,
            TargetClass = before.ClassName,
            TargetTitle = before.Title,
            ClientWidth = before.ClientWidth,
            ClientHeight = before.ClientHeight,
            Dpi = before.Dpi,
            TargetIntegrity = before.TargetIntegrity,
            ProbeIntegrity = before.ProbeIntegrity,
            ForegroundBefore = before.ForegroundHwnd.ToInt64(),
            ForegroundAfter = after.ForegroundHwnd.ToInt64(),
            ForegroundConfirmed = foregroundConfirmed,
            IsMinimized = before.IsMinimized || after.IsMinimized,
            HoldMs = holdMs,
            Vk = key.Equals("Left", StringComparison.OrdinalIgnoreCase) ? VirtualKeyMap.Left : VirtualKeyMap.Right,
            ScanCode = 0,
            FlagsDown = 0,
            FlagsUp = KeybdEventInputAdapter.KeyEventFKeyUp,
            InputAttempted = attempted,
            ScreenshotBefore = beforePath,
            ScreenshotAfter = afterPath,
            Classification = classification,
            Reason = reason + ";GATE=" + gate.LastReason,
            AllKeysReleased = allReleased
        });

        if (!foregroundConfirmed)
        {
            throw new InvalidOperationException("TARGET_LOST_FOREGROUND_AFTER_ACTION");
        }
    }

    private static void ValidateInitialTarget(TargetWindowInfo target)
    {
        if (!target.IsVisible) throw new InvalidOperationException("TARGET_NOT_VISIBLE");
        if (target.IsMinimized) throw new InvalidOperationException("TARGET_MINIMIZED");
        if (target.ClientWidth <= 0 || target.ClientHeight <= 0) throw new InvalidOperationException("TARGET_CLIENT_EMPTY");
        if (target.TargetIntegrity < 0 || target.ProbeIntegrity < 0) throw new InvalidOperationException("INTEGRITY_UNKNOWN");
        if (target.TargetIntegrity > target.ProbeIntegrity) throw new InvalidOperationException("INTEGRITY_MISMATCH");
    }
}

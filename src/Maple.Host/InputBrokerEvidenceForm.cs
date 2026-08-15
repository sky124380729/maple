using System.Diagnostics;
using System.Globalization;
using Maple.Capture;
using Maple.Contracts;
using Maple.Input;

namespace Maple.Host;

public sealed class InputBrokerEvidenceForm : Form
{
    private static readonly Color BackColorValue = Color.FromArgb(17, 24, 30);
    private static readonly Color PanelColor = Color.FromArgb(24, 34, 42);
    private static readonly Color AccentColor = Color.FromArgb(48, 184, 132);
    private static readonly Color MutedColor = Color.FromArgb(139, 158, 171);
    private readonly EvidenceActionSpec[] actions =
    [
        new("move-left", "向左移动", ActionType.MoveLeft, null, null, 500),
        new("move-right", "向右移动", ActionType.MoveRight, null, null, 500),
        new("jump", "跳跃", ActionType.Jump, null, "Alt", 140),
        new("climb-up", "向上攀爬", ActionType.ClimbUp, null, null, 500),
        new("climb-down", "向下攀爬", ActionType.ClimbDown, null, null, 500),
        new("single-attack", "单体攻击", ActionType.Attack, ActionProfileId.SingleAttack, "J", 140),
        new("pickup", "拾取", ActionType.Pickup, null, "Z", 140),
        new("release-all", "全键释放", null, null, null, 140),
    ];
    private readonly InputBrokerEvidenceWriter writer;
    private readonly WindowsTargetWindowLocator locator = new(new Win32WindowSystem());
    private readonly WindowsForegroundWindowController foreground = new();
    private readonly WindowsBitBltFrameSource capture = new();
    private readonly WindowsPngMapFrameEncoder pngEncoder = new();
    private readonly BrokerProcessLauncher launcher = new();
    private readonly BrokerInputAdapter adapter;
    private readonly Label statusLabel = new();
    private readonly Label pathLabel = new();
    private readonly ProgressBar progress = new();
    private readonly List<Button> actionButtons = [];
    private int currentIndex;
    private long frameId;
    private bool busy;
    private bool halted;
    private bool disposed;
    private CancellationTokenSource? actionCancellation;

    public InputBrokerEvidenceForm(string evidenceRoot, string applicationDirectory)
    {
        writer = new InputBrokerEvidenceWriter(evidenceRoot);
        adapter = new BrokerInputAdapter(new LaunchingBrokerClientFactory(
            launcher,
            Path.Combine(applicationDirectory, "Maple.InputBroker.exe")));
        Text = "Maple · 生产输入验收";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(880, 650);
        MinimumSize = new Size(760, 580);
        BackColor = BackColorValue;
        ForeColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 10F);
        BuildUi();
        UpdateActionAvailability();
        FormClosing += OnFormClosing;
    }

    public int ExitCode { get; private set; } = 2;

    public static string CreateDefaultSessionRoot()
    {
        string basePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Maple",
            "input-broker-evidence");
        return Path.Combine(basePath, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture));
    }

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 118, Padding = new Padding(28, 22, 28, 12), BackColor = BackColorValue };
        var title = new Label { Text = "生产输入验收", AutoSize = true, Font = new Font(Font.FontFamily, 18F, FontStyle.Bold), ForeColor = Color.White, Location = new Point(28, 20) };
        var subtitle = new Label { Text = "每次只发送一个抽象动作，自动切回游戏、截图并释放全部按键。", AutoSize = true, ForeColor = MutedColor, Location = new Point(30, 60) };
        pathLabel.Text = writer.Root;
        pathLabel.AutoEllipsis = true;
        pathLabel.ForeColor = Color.FromArgb(96, 119, 132);
        pathLabel.Location = new Point(30, 86);
        pathLabel.Size = new Size(810, 22);
        header.Controls.AddRange([title, subtitle, pathLabel]);

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 8, 28, 18), BackColor = BackColorValue };
        var instruction = new Label
        {
            Text = "测试前请把角色放在适合当前动作的位置。点击后会出现 UAC（首次），随后倒计时 3 秒；不要触碰键盘，出现审阅窗后再判断结果。",
            Dock = DockStyle.Top,
            Height = 56,
            ForeColor = Color.FromArgb(204, 216, 224),
            BackColor = PanelColor,
            Padding = new Padding(15, 10, 15, 8)
        };
        content.Controls.Add(instruction);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 340,
            Padding = new Padding(0, 18, 0, 0),
            ColumnCount = 2,
            RowCount = 4,
            BackColor = BackColorValue
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int row = 0; row < 4; row++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        for (int index = 0; index < actions.Length; index++)
        {
            EvidenceActionSpec spec = actions[index];
            var button = new Button
            {
                Text = $"{index + 1}. {spec.Label}",
                Dock = DockStyle.Fill,
                Margin = new Padding(index % 2 == 0 ? 0 : 8, 0, index % 2 == 0 ? 8 : 0, 10),
                FlatStyle = FlatStyle.Flat,
                BackColor = PanelColor,
                ForeColor = Color.White,
                FlatAppearance = { BorderColor = Color.FromArgb(47, 64, 74), BorderSize = 1 },
                Tag = index,
                Cursor = Cursors.Hand
            };
            button.Click += OnActionClicked;
            actionButtons.Add(button);
            grid.Controls.Add(button, index % 2, index / 2);
        }
        content.Controls.Add(grid);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 105, Padding = new Padding(0, 14, 0, 0), BackColor = BackColorValue };
        statusLabel.Text = "等待测试：向左移动";
        statusLabel.Dock = DockStyle.Top;
        statusLabel.Height = 30;
        statusLabel.ForeColor = Color.FromArgb(215, 226, 233);
        progress.Dock = DockStyle.Top;
        progress.Height = 8;
        progress.Maximum = actions.Length;
        progress.Style = ProgressBarStyle.Continuous;
        var close = new Button { Text = "关闭", Dock = DockStyle.Right, Width = 110, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(31, 43, 51), ForeColor = Color.White };
        close.FlatAppearance.BorderColor = Color.FromArgb(57, 73, 83);
        close.Click += (_, _) => Close();
        footer.Controls.Add(close);
        footer.Controls.Add(progress);
        footer.Controls.Add(statusLabel);
        content.Controls.Add(footer);

        Controls.Add(content);
        Controls.Add(header);
    }

    private async void OnActionClicked(object? sender, EventArgs eventArgs)
    {
        if (busy || sender is not Button { Tag: int index } || index != currentIndex) return;
        busy = true;
        actionCancellation = new CancellationTokenSource();
        UpdateActionAvailability();
        try
        {
            await RunActionAsync(actions[index], actionCancellation.Token);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            SafeReleaseAll();
            statusLabel.Text = "测试中止：" + exception.Message;
            statusLabel.ForeColor = Color.FromArgb(255, 144, 132);
            MessageBox.Show(this, exception.Message, "输入验收已安全停止", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            actionCancellation.Dispose();
            actionCancellation = null;
            busy = false;
            UpdateActionAvailability();
        }
    }

    private async Task RunActionAsync(EvidenceActionSpec spec, CancellationToken cancellationToken)
    {
        await adapter.EnsureStartedAsync(cancellationToken);
        for (int remaining = 3; remaining >= 1; remaining--)
        {
            statusLabel.Text = $"{remaining} 秒后切回游戏并测试：{spec.Label}";
            await Task.Delay(1000, cancellationToken);
        }

        WindowIdentity target = RequireTarget();
        nint hwnd = ParseHwnd(target.Hwnd);
        if (!foreground.TryActivate(hwnd)) throw new InvalidOperationException("TARGET_ACTIVATION_FAILED");
        for (int attempt = 0; attempt < 20 && foreground.GetForegroundWindow() != hwnd; attempt++)
            await Task.Delay(50, cancellationToken);
        if (foreground.GetForegroundWindow() != hwnd) throw new InvalidOperationException("TARGET_FOREGROUND_TIMEOUT");

        target = RequireTarget();
        if (!target.IsForeground || target.IsMinimized) throw new InvalidOperationException("TARGET_NOT_READY");
        adapter.ArmTarget(new ArmTargetPayload(
            hwnd.ToInt64(),
            target.Pid,
            target.ProcessStartedAtUtc.UtcDateTime.Ticks,
            target.ProcessPath));

        string beforeName = spec.Id + "-before.png";
        string afterName = spec.Id + "-after.png";
        await CaptureAsync(target, beforeName, cancellationToken);

        BrokerKeyEncoding? encoding = null;
        InputResult releaseResult;
        try
        {
            if (spec.Type.HasValue)
            {
                AbstractAction action = CreateAction(spec);
                BrokerActionKind brokerAction = BrokerActionMapping.ToBrokerAction(action);
                encoding = BrokerKeyProfile.For(brokerAction, spec.LogicalKey);
                InputResult result = adapter.Press(action, spec.LogicalKey!, Environment.TickCount64);
                if (result.Status != InputStatus.Completed) throw new InvalidOperationException(result.Message ?? "BROKER_ACTION_FAILED");
            }
            else
            {
                var primer = new EvidenceActionSpec("release-primer", "释放前短按", ActionType.MoveLeft, null, null, 140);
                AbstractAction action = CreateAction(primer);
                InputResult result = adapter.KeyDown(action, null!, Environment.TickCount64);
                if (result.Status != InputStatus.Accepted) throw new InvalidOperationException(result.Message ?? "BROKER_KEY_DOWN_FAILED");
                await Task.Delay(primer.HoldMs, cancellationToken);
            }
        }
        finally
        {
            releaseResult = adapter.ReleaseAll(Environment.TickCount64);
        }

        await Task.Delay(350, cancellationToken);
        WindowIdentity afterTarget = RequireTarget();
        if (!afterTarget.IsForeground || afterTarget.Pid != target.Pid || !string.Equals(afterTarget.Hwnd, target.Hwnd, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("TARGET_CHANGED_AFTER_ACTION");
        await CaptureAsync(afterTarget, afterName, cancellationToken);

        bool allKeysReleased = releaseResult.Status == InputStatus.Completed && adapter.GetStatus().ActiveKeys.Count == 0;
        if (!allKeysReleased) throw new InvalidOperationException("BROKER_RELEASE_ALL_FAILED");
        int brokerPid = launcher.OwnedProcessId ?? -1;
        int hostIntegrity = WindowsProcessIntegrity.ReadRid(Environment.ProcessId);
        int brokerIntegrity = WindowsProcessIntegrity.ReadRid(brokerPid);
        int targetIntegrity = WindowsProcessIntegrity.ReadRid(target.Pid);

        RestoreEvidenceWindow();
        using var review = new InputBrokerEvidenceReviewDialog(
            spec.Label,
            Path.Combine(writer.Root, beforeName),
            Path.Combine(writer.Root, afterName),
            spec.Type.HasValue ? "确认角色/技能对该动作产生了预期响应。" : "确认释放后没有继续移动或卡键。" );
        bool confirmed = review.ShowDialog(this) == DialogResult.Yes;
        string classification = confirmed ? "CLIENT_EFFECT_CONFIRMED" : "CLIENT_EFFECT_REJECTED";
        int flagsDown = encoding?.Extended == true ? (int)BrokerKeyFlags.ExtendedKey : 0;
        int flagsUp = encoding is not null ? flagsDown | (int)BrokerKeyFlags.KeyUp : 0;
        writer.Append(new InputBrokerEvidenceRecord(
            spec.Id,
            DateTimeOffset.UtcNow,
            hwnd.ToInt64(),
            target.Pid,
            true,
            hostIntegrity,
            brokerIntegrity,
            targetIntegrity,
            encoding?.VirtualKey ?? 0,
            checked((int)(encoding?.ScanCode ?? 0)),
            flagsDown,
            flagsUp,
            beforeName,
            afterName,
            classification,
            allKeysReleased));

        if (!confirmed)
        {
            statusLabel.Text = $"已拒绝：{spec.Label}。剩余动作已锁定。";
            statusLabel.ForeColor = Color.FromArgb(255, 144, 132);
            halted = true;
            return;
        }

        currentIndex++;
        progress.Value = currentIndex;
        if (currentIndex == actions.Length)
        {
            ExitCode = 0;
            statusLabel.Text = "8 项动作已确认，证据可执行严格验收。";
            statusLabel.ForeColor = AccentColor;
        }
        else
        {
            statusLabel.Text = $"已确认 {spec.Label}。下一项：{actions[currentIndex].Label}";
            statusLabel.ForeColor = Color.FromArgb(215, 226, 233);
        }
    }

    private async Task CaptureAsync(WindowIdentity target, string fileName, CancellationToken cancellationToken)
    {
        var captureTarget = new CaptureTarget
        {
            Hwnd = target.Hwnd,
            Pid = target.Pid,
            ClientLeft = target.ClientLeft,
            ClientTop = target.ClientTop,
            ClientWidth = target.ClientWidth,
            ClientHeight = target.ClientHeight,
            Dpi = target.Dpi,
            IsForeground = target.IsForeground,
            IsMinimized = target.IsMinimized
        };
        using CapturedFrame? frame = await capture.TryCaptureAsync(
            captureTarget,
            Interlocked.Increment(ref frameId),
            Environment.TickCount64,
            cancellationToken);
        if (frame is null) throw new InvalidOperationException("CLIENT_CAPTURE_FAILED");
        byte[] png = pngEncoder.EncodePng(frame);
        File.WriteAllBytes(Path.Combine(writer.Root, fileName), png);
    }

    private WindowIdentity RequireTarget()
    {
        TargetWindowDiscoveryResult discovery = locator.Locate();
        return discovery.Target ?? throw new InvalidOperationException(discovery.DiagnosticCode);
    }

    private static AbstractAction CreateAction(EvidenceActionSpec spec)
    {
        long now = Environment.TickCount64;
        return new AbstractAction
        {
            ActionId = spec.Id + "-" + Guid.NewGuid().ToString("N"),
            Type = spec.Type ?? ActionType.MoveLeft,
            ProfileId = spec.Profile,
            IssuedAtMonoMs = now,
            HoldMs = spec.HoldMs,
            MaxDurationMs = 1500
        };
    }

    private static nint ParseHwnd(string value)
    {
        string digits = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        if (!long.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long parsed) || parsed == 0)
            throw new InvalidOperationException("TARGET_HWND_INVALID");
        return (nint)parsed;
    }

    private void RestoreEvidenceWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        TopMost = true;
        Activate();
        BringToFront();
        TopMost = false;
    }

    private void SafeReleaseAll()
    {
        try { _ = adapter.ReleaseAll(Environment.TickCount64); }
        catch { }
    }

    private void UpdateActionAvailability()
    {
        for (int index = 0; index < actionButtons.Count; index++)
        {
            Button button = actionButtons[index];
            bool completed = index < currentIndex;
            button.Enabled = !busy && !halted && index == currentIndex && currentIndex < actions.Length;
            button.BackColor = completed ? Color.FromArgb(25, 75, 59) : PanelColor;
            if (completed && !button.Text.EndsWith("  已确认", StringComparison.Ordinal)) button.Text += "  已确认";
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (busy)
        {
            eventArgs.Cancel = true;
            actionCancellation?.Cancel();
            SafeReleaseAll();
            statusLabel.Text = "正在取消当前动作并释放按键，请稍后再次关闭。";
            return;
        }
        if (disposed) return;
        disposed = true;
        SafeReleaseAll();
        adapter.Dispose();
    }

    private sealed record EvidenceActionSpec(
        string Id,
        string Label,
        ActionType? Type,
        ActionProfileId? Profile,
        string? LogicalKey,
        int HoldMs);
}

internal sealed class InputBrokerEvidenceReviewDialog : Form
{
    public InputBrokerEvidenceReviewDialog(string actionLabel, string beforePath, string afterPath, string prompt)
    {
        Text = "审阅动作：" + actionLabel;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1180, 720);
        MinimumSize = new Size(900, 620);
        BackColor = Color.FromArgb(17, 24, 30);
        ForeColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 10F);

        var title = new Label { Text = prompt, Dock = DockStyle.Top, Height = 54, Padding = new Padding(20, 17, 20, 8), ForeColor = Color.FromArgb(220, 230, 236) };
        var images = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(16, 0, 16, 10) };
        images.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        images.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        images.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        images.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        images.Controls.Add(new Label { Text = "动作前", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(139, 158, 171) }, 0, 0);
        images.Controls.Add(new Label { Text = "动作后", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(139, 158, 171) }, 1, 0);
        images.Controls.Add(CreatePicture(beforePath), 0, 1);
        images.Controls.Add(CreatePicture(afterPath), 1, 1);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 70, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16, 13, 16, 10) };
        var confirm = new Button { Text = "确认客户端有效", DialogResult = DialogResult.Yes, Width = 170, Height = 40, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(42, 145, 105), ForeColor = Color.White };
        var reject = new Button { Text = "异常，停止测试", DialogResult = DialogResult.No, Width = 150, Height = 40, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(61, 42, 43), ForeColor = Color.FromArgb(255, 166, 154) };
        confirm.FlatAppearance.BorderColor = Color.FromArgb(54, 188, 136);
        reject.FlatAppearance.BorderColor = Color.FromArgb(111, 58, 57);
        footer.Controls.Add(confirm);
        footer.Controls.Add(reject);
        CancelButton = reject;
        Controls.Add(images);
        Controls.Add(footer);
        Controls.Add(title);
    }

    private static PictureBox CreatePicture(string path)
    {
        using Image source = Image.FromFile(path);
        var picture = new PictureBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 10, 0),
            BackColor = Color.Black,
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = new Bitmap(source)
        };
        picture.Disposed += (_, _) => picture.Image?.Dispose();
        return picture;
    }
}

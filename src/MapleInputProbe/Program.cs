using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint type;
        public INPUTUNION U;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    internal struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    internal delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr extra);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindowAsync(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    internal static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr extra);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateFile(
        string name,
        uint access,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr handle);

    internal const uint INPUT_KEYBOARD = 1;
    internal const uint KEYEVENTF_KEYUP = 2;
    internal const uint KEYEVENTF_EXTENDEDKEY = 1;
    internal const uint KEYEVENTF_SCANCODE = 8;
    internal const int SW_RESTORE = 9;
    internal const uint GENERIC_READ = 0x80000000;
    internal const uint GENERIC_WRITE = 0x40000000;
    internal const uint FILE_SHARE_READ = 1;
    internal const uint FILE_SHARE_WRITE = 2;
    internal const uint OPEN_EXISTING = 3;
    internal static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

    internal static string Title(IntPtr hwnd)
    {
        var buffer = new StringBuilder(512);
        GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    internal static bool TryOpenVirtualHid(out int error)
    {
        // The future signed VHF driver will expose this private device path.
        // No third-party driver is opened or reused by this probe.
        var handle = CreateFile(
            @"\\.\MapleVhfKeyboard",
            GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            0,
            IntPtr.Zero);
        if (handle == IntPtr.Zero || handle == INVALID_HANDLE_VALUE)
        {
            error = Marshal.GetLastWin32Error();
            return false;
        }

        CloseHandle(handle);
        error = 0;
        return true;
    }
}

internal sealed class WindowChoice
{
    internal IntPtr Handle;
    internal string Title;
}

internal enum SendMode
{
    ScanCode,
    VirtualKey
}

internal sealed class ProbeForm : Form
{
    private const string TargetTitle = "\u5192\u9669\u5c9b\u6000\u65e7\u670d";
    private const string LogFileName = "input-test.log";

    private readonly CheckBox authorization = new CheckBox();
    private readonly CheckBox refocus = new CheckBox();
    private readonly NumericUpDown hold = new NumericUpDown();
    private readonly RadioButton scanMode = new RadioButton();
    private readonly RadioButton virtualKeyMode = new RadioButton();
    private readonly Label targetLabel = new Label();
    private readonly Label hidLabel = new Label();
    private readonly Label status = new Label();
    private readonly TextBox logBox = new TextBox();
    private readonly HashSet<ushort> activeScans = new HashSet<ushort>();
    private WindowChoice target;

    internal ProbeForm()
    {
        Text = "\u67ab\u53f6\u89c6\u89c9\u52a9\u624b - \u6700\u5c0f\u8f93\u5165\u9a8c\u8bc1";
        ClientSize = new Size(650, 510);
        MinimumSize = new Size(650, 510);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(24, 35, 33);
        ForeColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9F);
        FormClosing += delegate { ReleaseAllInjectedKeys(); };

        AddLabel("\u76ee\u6807\u6e38\u620f\u7a97\u53e3", 20, 16, 220, 24);
        targetLabel.SetBounds(20, 43, 600, 28);
        targetLabel.ForeColor = Color.FromArgb(150, 220, 200);
        Controls.Add(targetLabel);

        authorization.Text = "\u6211\u786e\u8ba4\u8fd9\u662f\u6388\u6743\u6d4b\u8bd5\uff0c\u4ec5\u5141\u8bb8\u5355\u6b21\u6309\u952e";
        authorization.SetBounds(20, 80, 440, 25);
        authorization.AutoSize = true;
        Controls.Add(authorization);

        refocus.Text = "\u53d1\u9001\u524d\u81ea\u52a8\u5207\u56de\u6e38\u620f\uff08\u4ecd\u4ec5\u524d\u53f0\u8f93\u5165\uff09";
        refocus.SetBounds(20, 108, 500, 25);
        refocus.AutoSize = true;
        refocus.Checked = true;
        Controls.Add(refocus);

        AddLabel("\u6309\u4e0b\u6301\u7eed\uff08\u6beb\u79d2\uff09", 20, 143, 150, 24);
        hold.SetBounds(165, 140, 75, 25);
        hold.Minimum = 20;
        hold.Maximum = 500;
        hold.Value = 250;
        Controls.Add(hold);

        AddLabel("SendInput \u65b9\u5f0f", 280, 143, 130, 24);
        scanMode.Text = "\u626b\u63cf\u7801";
        scanMode.SetBounds(390, 140, 90, 25);
        scanMode.AutoSize = true;
        scanMode.Checked = true;
        Controls.Add(scanMode);
        virtualKeyMode.Text = "\u865a\u62df\u952e\u7801";
        virtualKeyMode.SetBounds(490, 140, 110, 25);
        virtualKeyMode.AutoSize = true;
        Controls.Add(virtualKeyMode);

        hidLabel.SetBounds(20, 178, 600, 25);
        Controls.Add(hidLabel);

        status.Text = "\u5c1a\u672a\u53d1\u9001\u3002\u672c\u7a0b\u5e8f\u4e0d\u8bfb\u53d6\u6216\u4fee\u6539\u6e38\u620f\u5185\u5b58\uff0c\u4e0d\u6ce8\u518c\u5168\u5c40\u70ed\u952e\u3002";
        status.SetBounds(20, 208, 600, 40);
        status.ForeColor = Color.FromArgb(150, 220, 200);
        Controls.Add(status);

        var left = NewButton("\u53d1\u9001\u5de6\u952e", 20, 265, 180, 42);
        left.Click += delegate { SendOne(Keys.Left); };
        Controls.Add(left);
        var right = NewButton("\u53d1\u9001\u53f3\u952e", 215, 265, 180, 42);
        right.Click += delegate { SendOne(Keys.Right); };
        Controls.Add(right);
        var roundTrip = NewButton("\u5de6\u53f3\u5404\u4e00\u6b21", 410, 265, 210, 42);
        roundTrip.Click += delegate { SendRoundTrip(); };
        Controls.Add(roundTrip);

        var selfTest = NewButton("\u81ea\u68c0\uff08\u4e0d\u53d1\u9001\u8f93\u5165\uff09", 20, 320, 290, 36);
        selfTest.Click += delegate { ShowSelfTest(); };
        Controls.Add(selfTest);
        var release = NewButton("\u505c\u6b62\u5e76\u91ca\u653e\u5168\u90e8\u6309\u952e", 330, 320, 290, 36);
        release.BackColor = Color.FromArgb(130, 57, 45);
        release.Click += delegate { ReleaseAllInjectedKeys(); SetStatus("\u5df2\u505c\u6b62\uff0c\u5df2\u53d1\u9001\u6240\u6709\u5df2\u77e5\u6ce8\u5165\u6309\u952e\u7684\u91ca\u653e\u3002", Color.FromArgb(255, 195, 150)); };
        Controls.Add(release);

        logBox.Multiline = true;
        logBox.ReadOnly = true;
        logBox.ScrollBars = ScrollBars.Vertical;
        logBox.BackColor = Color.FromArgb(14, 24, 22);
        logBox.ForeColor = Color.FromArgb(205, 230, 220);
        logBox.SetBounds(20, 370, 600, 115);
        Controls.Add(logBox);

        RefreshStatus();
    }

    private string LogPath
    {
        get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LogFileName); }
    }

    private void AddLabel(string text, int x, int y, int w, int h)
    {
        var label = new Label();
        label.Text = text;
        label.AutoSize = false;
        label.SetBounds(x, y, w, h);
        Controls.Add(label);
    }

    private Button NewButton(string text, int x, int y, int w, int h)
    {
        var button = new Button();
        button.Text = text;
        button.SetBounds(x, y, w, h);
        button.BackColor = Color.FromArgb(31, 94, 79);
        button.ForeColor = Color.White;
        button.FlatStyle = FlatStyle.Flat;
        return button;
    }

    private WindowChoice FindTargetWindow()
    {
        WindowChoice found = null;
        NativeMethods.EnumWindows(delegate(IntPtr hwnd, IntPtr extra)
        {
            if (NativeMethods.IsWindowVisible(hwnd))
            {
                var title = NativeMethods.Title(hwnd);
                if (title.IndexOf(TargetTitle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found = new WindowChoice { Handle = hwnd, Title = title };
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private void RefreshStatus()
    {
        target = FindTargetWindow();
        if (target == null)
        {
            targetLabel.Text = "\u672a\u627e\u5230\uff1a" + TargetTitle;
            targetLabel.ForeColor = Color.FromArgb(240, 170, 110);
        }
        else
        {
            targetLabel.Text = "\u5df2\u81ea\u52a8\u6355\u83b7\uff1a" + target.Title;
            targetLabel.ForeColor = Color.FromArgb(150, 220, 200);
        }

        int error;
        if (NativeMethods.TryOpenVirtualHid(out error))
        {
            hidLabel.Text = "\u865a\u62df HID \u72b6\u6001\uff1a\u5df2\u8fde\u63a5\uff08MapleVhfKeyboard\uff09";
            hidLabel.ForeColor = Color.FromArgb(150, 220, 200);
        }
        else
        {
            hidLabel.Text = "\u865a\u62df HID \u72b6\u6001\uff1a\u672a\u5b89\u88c5\uff08\u672c\u7248\u4ec5\u63a2\u6d4b\uff0c\u4e0d\u52a0\u8f7d\u9a71\u52a8\uff09\uff0c\u9519\u8bef\u7801 " + error;
            hidLabel.ForeColor = Color.FromArgb(240, 170, 110);
        }
    }

    private bool EnsureAuthorizedTarget()
    {
        if (!authorization.Checked)
        {
            SetStatus("\u5df2\u62d2\u7edd\uff1a\u8bf7\u5148\u52fe\u9009\u6388\u6743\u6d4b\u8bd5\u3002", Color.FromArgb(255, 170, 130));
            return false;
        }

        target = FindTargetWindow();
        if (target == null)
        {
            SetStatus("\u5df2\u62d2\u7edd\uff1a\u672a\u627e\u5230\u5192\u9669\u5c9b\u7a97\u53e3\u3002", Color.FromArgb(255, 170, 130));
            RefreshStatus();
            return false;
        }

        IntPtr foregroundBefore = NativeMethods.GetForegroundWindow();
        bool minimizedBefore = NativeMethods.IsIconic(target.Handle);
        bool restoreResult = false;
        if (minimizedBefore)
        {
            restoreResult = NativeMethods.ShowWindowAsync(target.Handle, NativeMethods.SW_RESTORE);
            for (int i = 0; i < 20 && NativeMethods.IsIconic(target.Handle); i++)
            {
                System.Threading.Thread.Sleep(50);
            }
        }

        bool activateResult = true;
        if (refocus.Checked && NativeMethods.GetForegroundWindow() != target.Handle)
        {
            activateResult = NativeMethods.SetForegroundWindow(target.Handle);
        }

        bool foregroundConfirmed = false;
        for (int i = 0; i < 20; i++)
        {
            if (NativeMethods.GetForegroundWindow() == target.Handle)
            {
                foregroundConfirmed = true;
                break;
            }
            System.Threading.Thread.Sleep(50);
        }

        IntPtr foregroundAfter = NativeMethods.GetForegroundWindow();
        WriteLog(DateTime.Now.ToString("O") + " action=activate-target activateResult=" + activateResult + " targetHandle=" + target.Handle + " minimizedBefore=" + minimizedBefore + " restoreResult=" + restoreResult + " minimizedAfter=" + NativeMethods.IsIconic(target.Handle) + " foregroundBefore=" + foregroundBefore + " foregroundBeforeTitle=" + NativeMethods.Title(foregroundBefore) + " foregroundAfter=" + foregroundAfter + " foregroundAfterTitle=" + NativeMethods.Title(foregroundAfter) + " foregroundConfirmed=" + foregroundConfirmed);

        if (!foregroundConfirmed)
        {
            SetStatus("\u672a\u53d1\u9001\uff1a\u6e38\u620f\u4e0d\u662f\u524d\u53f0\u7a97\u53e3\u3002", Color.FromArgb(255, 170, 130));
            return false;
        }
        return true;
    }

    private void SendRoundTrip()
    {
        if (!EnsureAuthorizedTarget()) return;
        SendOneCore(Keys.Left);
        System.Threading.Thread.Sleep(1000);
        if (NativeMethods.GetForegroundWindow() == target.Handle) SendOneCore(Keys.Right);
        else SetStatus("\u4e2d\u6b62\uff1a\u6e38\u620f\u5931\u53bb\u7126\u70b9\uff0c\u5df2\u91ca\u653e\u6309\u952e\u3002", Color.FromArgb(255, 195, 150));
    }

    private void SendOne(Keys key)
    {
        if (!EnsureAuthorizedTarget()) return;
        SendOneCore(key);
    }

    private void SendOneCore(Keys key)
    {
        ushort vk = (ushort)key;
        ushort scan = (ushort)NativeMethods.MapVirtualKey(vk, 0);
        bool useScan = scanMode.Checked;
        uint flags = useScan ? NativeMethods.KEYEVENTF_SCANCODE : 0;
        if (useScan && (key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down)) flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;

        var down = new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD };
        down.U.ki.wVk = useScan ? (ushort)0 : vk;
        down.U.ki.wScan = useScan ? scan : (ushort)0;
        down.U.ki.dwFlags = flags;
        var up = down;
        up.U.ki.dwFlags = flags | NativeMethods.KEYEVENTF_KEYUP;
        int size = Marshal.SizeOf(typeof(NativeMethods.INPUT));

        uint sentDown = NativeMethods.SendInput(1, new[] { down }, size);
        int downError = sentDown == 1 ? 0 : Marshal.GetLastWin32Error();
        if (sentDown == 1) activeScans.Add(scan);

        System.Threading.Thread.Sleep((int)hold.Value);

        uint sentUp = NativeMethods.SendInput(1, new[] { up }, size);
        int upError = sentUp == 1 ? 0 : Marshal.GetLastWin32Error();
        if (sentUp == 1) activeScans.Remove(scan);

        string mode = useScan ? "scancode" : "virtual-key";
        string line = DateTime.Now.ToString("O") + " mode=" + mode + " key=" + key + " scan=" + scan + " foreground=" + NativeMethods.Title(NativeMethods.GetForegroundWindow()) + " down=" + sentDown + " downError=" + downError + " up=" + sentUp + " upError=" + upError;
        WriteLog(line);
        SetStatus("\u5df2\u53d1\u9001 " + key + "\uff1a\u6309\u4e0b=" + sentDown + "\uff0c\u91ca\u653e=" + sentUp + "\uff08\u6e38\u620f\u9700\u5904\u4e8e\u524d\u53f0\uff09", sentDown == 1 && sentUp == 1 ? Color.FromArgb(150, 220, 200) : Color.FromArgb(255, 170, 130));
    }

    private void ReleaseAllInjectedKeys()
    {
        var keys = new List<ushort>(activeScans);
        foreach (ushort scan in keys)
        {
            var up = new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD };
            up.U.ki.wScan = scan;
            up.U.ki.dwFlags = NativeMethods.KEYEVENTF_SCANCODE | NativeMethods.KEYEVENTF_KEYUP;
            uint sent = NativeMethods.SendInput(1, new[] { up }, Marshal.SizeOf(typeof(NativeMethods.INPUT)));
            if (sent == 1) activeScans.Remove(scan);
            else WriteLog(DateTime.Now.ToString("O") + " action=release-retry-needed scan=" + scan + " sent=" + sent + " error=" + Marshal.GetLastWin32Error());
        }
        WriteLog(DateTime.Now.ToString("O") + " action=release-all count=" + keys.Count);
    }

    private void ShowSelfTest()
    {
        var lines = BuildSelfTestLines();
        foreach (string line in lines) WriteLog(line);
        SetStatus("\u81ea\u68c0\u5b8c\u6210\uff1a\u672c\u6b21\u672a\u53d1\u9001\u4efb\u4f55\u8f93\u5165\u3002", Color.FromArgb(150, 220, 200));
    }

    internal void RunAuthorizedAutoTest()
    {
        authorization.Checked = true;
        refocus.Checked = true;
        scanMode.Checked = true;
        hold.Value = 250;
        // Establish a foreground relationship before requesting the game window.
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
        for (int i = 0; i < 15 && NativeMethods.GetForegroundWindow() != Handle; i++)
        {
            System.Threading.Thread.Sleep(100);
            Activate();
        }
        WriteLog(DateTime.Now.ToString("O") + " action=probe-foreground-handshake confirmed=" + (NativeMethods.GetForegroundWindow() == Handle));
        if (!EnsureAuthorizedTarget())
        {
            WriteLog(DateTime.Now.ToString("O") + " action=authorized-auto-test blocked-before-input");
            Close();
            return;
        }
        CaptureTarget("auto-before");
        SendOneCore(Keys.Left);
        System.Threading.Thread.Sleep(1000);
        CaptureTarget("auto-after-left");
        if (NativeMethods.GetForegroundWindow() == target.Handle)
        {
            SendOneCore(Keys.Right);
            System.Threading.Thread.Sleep(1000);
            CaptureTarget("auto-after-right");
        }
        else
        {
            SetStatus("\u4e2d\u6b62\uff1a\u6e38\u620f\u5931\u53bb\u7126\u70b9\u3002", Color.FromArgb(255, 195, 150));
        }
        CaptureTarget("auto-after");
        WriteLog(DateTime.Now.ToString("O") + " action=authorized-auto-test complete");
        Close();
    }

    private void CaptureTarget(string suffix)
    {
        if (target == null || !NativeMethods.IsWindow(target.Handle)) return;
        NativeMethods.RECT rect;
        if (!NativeMethods.GetWindowRect(target.Handle, out rect)) return;
        int width = Math.Max(1, rect.Right - rect.Left);
        int height = Math.Max(1, rect.Bottom - rect.Top);
        using (var bitmap = new Bitmap(width, height))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height));
            bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, suffix + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        }
    }

    internal static List<string> BuildSelfTestLines()
    {
        var lines = new List<string>();
        WindowChoice found = FindTargetWindowStatic();
        lines.Add("TARGET=" + (found == null ? "NOT_FOUND" : "FOUND") + " title=" + (found == null ? "" : found.Title));
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        lines.Add("FOREGROUND=" + (found != null && foreground == found.Handle ? "TARGET" : "OTHER") + " title=" + NativeMethods.Title(foreground));
        lines.Add("SENDINPUT_STRUCT_SIZE=" + Marshal.SizeOf(typeof(NativeMethods.INPUT)));
        int error;
        lines.Add("VIRTUAL_HID=" + (NativeMethods.TryOpenVirtualHid(out error) ? "READY" : "NOT_INSTALLED") + " probeError=" + error);
        lines.Add("GLOBAL_HOTKEYS=DISABLED");
        lines.Add("INPUT_POLICY=FOREGROUND_ONLY");
        return lines;
    }

    private static WindowChoice FindTargetWindowStatic()
    {
        WindowChoice found = null;
        NativeMethods.EnumWindows(delegate(IntPtr hwnd, IntPtr extra)
        {
            if (NativeMethods.IsWindowVisible(hwnd))
            {
                var title = NativeMethods.Title(hwnd);
                if (title.IndexOf("\u5192\u9669\u5c9b\u6000\u65e7\u670d", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found = new WindowChoice { Handle = hwnd, Title = title };
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private void SetStatus(string text, Color color)
    {
        status.Text = text;
        status.ForeColor = color;
    }

    private void WriteLog(string line)
    {
        try { File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8); } catch { }
        logBox.AppendText(line + Environment.NewLine);
    }

    private void DismissKnownOverlay()
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (NativeMethods.Title(foreground) != "\u5feb\u901f\u8bbe\u7f6e") return;

        var down = new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD };
        down.U.ki.wVk = (ushort)Keys.Escape;
        var up = down;
        up.U.ki.dwFlags = NativeMethods.KEYEVENTF_KEYUP;
        int size = Marshal.SizeOf(typeof(NativeMethods.INPUT));
        uint sentDown = NativeMethods.SendInput(1, new[] { down }, size);
        uint sentUp = NativeMethods.SendInput(1, new[] { up }, size);
        WriteLog(DateTime.Now.ToString("O") + " action=dismiss-known-overlay title=\u5feb\u901f\u8bbe\u7f6e down=" + sentDown + " up=" + sentUp);
        System.Threading.Thread.Sleep(300);
    }

    [STAThread]
    internal static void Main(string[] args)
    {
        if (args.Length > 0 && (args[0] == "--self-test" || args[0] == "--probe-virtual-hid"))
        {
            foreach (string line in BuildSelfTestLines()) Console.WriteLine(line);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length > 0 && args[0] == "--authorized-auto-test")
        {
            var form = new ProbeForm();
            form.Shown += delegate { form.DismissKnownOverlay(); form.RunAuthorizedAutoTest(); };
            Application.Run(form);
            return;
        }
        Application.Run(new ProbeForm());
    }
}

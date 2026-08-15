using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Maple.InputProbe;

internal sealed class ProbeForm : Form
{
    private readonly ProbeRunner runner;
    private readonly CheckBox authorization;
    private readonly Button startButton;
    private readonly Button stopButton;
    private readonly Label status;
    private readonly TextBox log;
    private CancellationTokenSource cancellation;

    public ProbeForm(ProbeRunner runner)
    {
        this.runner = runner;
        Text = "枫叶输入诊断 · 扩展扫描码";
        ClientSize = new Size(720, 500);
        MinimumSize = new Size(680, 460);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(17, 24, 28);
        ForeColor = Color.FromArgb(230, 237, 240);
        Font = new Font("Microsoft YaHei UI", 10F);

        var title = new Label
        {
            AutoSize = true,
            Text = "前台输入最小验证",
            Font = new Font(Font.FontFamily, 20F, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(28, 24)
        };
        var description = new Label
        {
            AutoSize = false,
            Text = "扩展扫描码模式：只测试一次左键和一次右键。不会修改内存，不会后台运行，不会自动重复。",
            ForeColor = Color.FromArgb(161, 177, 184),
            Location = new Point(31, 70),
            Size = new Size(640, 46)
        };

        authorization = new CheckBox
        {
            Text = "我确认这是授权测试客户端，并允许程序短暂切换到游戏前台",
            AutoSize = true,
            Location = new Point(32, 130),
            ForeColor = Color.FromArgb(215, 224, 228)
        };

        startButton = new Button
        {
            Text = "开始测试",
            Location = new Point(32, 174),
            Size = new Size(210, 46),
            BackColor = Color.FromArgb(20, 135, 112),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        startButton.FlatAppearance.BorderSize = 0;
        startButton.Click += async (_, _) => await StartProbeAsync();

        stopButton = new Button
        {
            Text = "停止并释放按键",
            Location = new Point(254, 174),
            Size = new Size(210, 46),
            BackColor = Color.FromArgb(79, 91, 98),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Enabled = false
        };
        stopButton.FlatAppearance.BorderSize = 0;
        stopButton.Click += (_, _) => StopProbe();

        status = new Label
        {
            AutoSize = false,
            Text = "状态：尚未开始 · 未发送任何输入",
            Location = new Point(32, 238),
            Size = new Size(640, 32),
            ForeColor = Color.FromArgb(81, 206, 173)
        };

        log = new TextBox
        {
            Location = new Point(32, 278),
            Size = new Size(640, 172),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(11, 17, 20),
            ForeColor = Color.FromArgb(190, 205, 211),
            BorderStyle = BorderStyle.FixedSingle
        };

        Controls.AddRange(new Control[] { title, description, authorization, startButton, stopButton, status, log });
        FormClosing += (_, _) =>
        {
            cancellation?.Cancel();
            runner.StopAndRelease();
        };
    }

    private async Task StartProbeAsync()
    {
        if (!authorization.Checked)
        {
            MessageBox.Show(this, "请先勾选授权确认。", "尚未授权", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        startButton.Enabled = false;
        stopButton.Enabled = true;
        cancellation = new CancellationTokenSource();
        var progress = new Progress<string>(AppendLog);

        try
        {
            WindowState = FormWindowState.Minimized;
            ProbeRunResult result = await runner.RunAsync(new ProbeRunOptions(), progress, cancellation.Token);
            WindowState = FormWindowState.Normal;
            status.Text = result.AllKeysReleased
                ? "状态：测试结束 · 全部按键已释放"
                : "状态：测试结束 · 释放状态异常";
            AppendLog("证据目录：" + result.SessionDirectory);
        }
        catch (OperationCanceledException)
        {
            WindowState = FormWindowState.Normal;
            status.Text = "状态：已取消 · 已请求释放全部按键";
            AppendLog("测试已由用户取消。");
        }
        catch (Exception exception)
        {
            WindowState = FormWindowState.Normal;
            status.Text = "状态：已停止 · 未满足输入安全门";
            AppendLog("停止原因：" + exception.Message);
        }
        finally
        {
            bool released = runner.StopAndRelease();
            AppendLog(released ? "安全状态：全部按键已释放" : "安全状态：释放失败，请检查日志");
            stopButton.Enabled = false;
            startButton.Enabled = true;
            cancellation.Dispose();
            cancellation = null;
        }
    }

    private void StopProbe()
    {
        cancellation?.Cancel();
        bool released = runner.StopAndRelease();
        status.Text = released ? "状态：已停止 · 全部按键已释放" : "状态：停止时释放失败";
    }

    private void AppendLog(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(AppendLog), message);
            return;
        }

        log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }
}

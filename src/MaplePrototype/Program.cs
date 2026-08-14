using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace MapleVisualPrototype
{
    internal sealed class PrototypeForm : Form
    {
        private readonly Panel leftPanel = new Panel();
        private readonly Panel mainPanel = new Panel();
        private readonly Panel bottomBar = new Panel();
        private readonly Panel contentPanel = new Panel();
        private readonly Panel rightPanel = new Panel();
        private readonly PreviewCanvas preview = new PreviewCanvas();
        private readonly Label stateBadge = new Label();
        private readonly Label targetLabel = new Label();
        private readonly Label hidLabel = new Label();
        private readonly Label pauseLabel = new Label();
        private readonly Label hpValue = new Label();
        private readonly Label mpValue = new Label();
        private readonly Label perfLabel = new Label();
        private readonly Label mapLabel = new Label();
        private readonly Label statusLabel = new Label();
        private readonly TextBox logBox = new TextBox();
        private readonly TextBox rightLogBox = new TextBox();
        private readonly Label rightSafetyValue = new Label();
        private Label rightFocusValue = new Label();
        private readonly Label rightConfidenceValue = new Label();
        private readonly Label rightMapValue = new Label();
        private Label rightModelValue = new Label();
        private readonly ComboBox modeBox = new ComboBox();
        private readonly NumericUpDown hpThreshold = new NumericUpDown();
        private readonly NumericUpDown mpThreshold = new NumericUpDown();
        private readonly TextBox jumpKey = new TextBox();
        private readonly TextBox pickupKey = new TextBox();
        private readonly CheckBox pickupEnabled = new CheckBox();
        private readonly Timer captureTimer = new Timer();
        private readonly Timer telemetryTimer = new Timer();
        private TargetWindowInfo target;
        private SessionState sessionState = SessionState.Stopped;
        private readonly PrototypeTelemetry telemetry = new PrototypeTelemetry();
        private DateTime lastCapture = DateTime.MinValue;
        private int captureCount;
        private int recognitionCount;
        private int lastWidth;
        private int lastHeight;
        private string currentTab = "实时画面";

        internal PrototypeForm()
        {
            Text = "枫叶视觉助手 · 安全原型";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1080, 680);
            ClientSize = new Size(1440, 900);
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Font = new Font("Microsoft YaHei UI", 9F);
            FormClosing += OnClosing;
            BuildShell();
            SetState(SessionState.Stopped, "尚未启动观察");
            captureTimer.Interval = 300;
            captureTimer.Tick += CaptureTimerTick;
            telemetryTimer.Interval = 1000;
            telemetryTimer.Tick += TelemetryTimerTick;
            Shown += delegate { RefreshTargetAndFrame(); telemetryTimer.Start(); captureTimer.Start(); };
        }

        private void BuildShell()
        {
            var top = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = Theme.Surface };
            top.Padding = new Padding(18, 9, 18, 9);
            top.Controls.Add(BuildTabs());
            top.Controls.Add(BuildBrand());
            Controls.Add(top);

            bottomBar.Dock = DockStyle.Bottom;
            bottomBar.Height = 54;
            bottomBar.BackColor = Theme.Surface;
            bottomBar.Padding = new Padding(16, 8, 16, 8);
            bottomBar.Controls.Add(perfLabel);
            perfLabel.Dock = DockStyle.Fill;
            Theme.StyleLabel(perfLabel, true);
            Controls.Add(bottomBar);

            mainPanel.Dock = DockStyle.Fill;
            mainPanel.Padding = new Padding(10, 10, 10, 10);
            mainPanel.BackColor = Theme.Window;
            Controls.Add(mainPanel);

            leftPanel.BackColor = Theme.Surface;
            leftPanel.Padding = new Padding(12);
            leftPanel.Dock = DockStyle.Fill;

            contentPanel.Padding = new Padding(10, 0, 0, 0);
            contentPanel.BackColor = Theme.Window;
            contentPanel.Dock = DockStyle.Fill;
            rightPanel.BackColor = Theme.Surface;
            rightPanel.Padding = new Padding(12);
            rightPanel.Dock = DockStyle.Fill;

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Theme.Window };
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 286F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300F));
            grid.Controls.Add(leftPanel, 0, 0);
            grid.Controls.Add(contentPanel, 1, 0);
            grid.Controls.Add(rightPanel, 2, 0);
            mainPanel.Controls.Add(grid);
            BuildLeftPanel();
            BuildContentPanel();
            BuildRightPanel();
        }

        private Control BuildBrand()
        {
            var panel = new Panel { Dock = DockStyle.Left, Width = 280 };
            var title = new Label { Text = "枫叶视觉助手", Location = new Point(0, 0), Size = new Size(260, 26) };
            Theme.StyleLabel(title, false, true);
            title.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            var subtitle = new Label { Text = "视觉自动打怪 · 安全原型", Location = new Point(1, 30), Size = new Size(260, 20) };
            Theme.StyleLabel(subtitle, true);
            panel.Controls.Add(subtitle);
            panel.Controls.Add(title);
            return panel;
        }

        private Control BuildTabs()
        {
            var tabs = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(8, 4, 0, 0), BackColor = Theme.Surface };
            foreach (string tab in new[] { "实时画面", "地图标定", "动作配置", "识别模型", "运行日志" })
            {
                var button = new Button { Text = tab, Tag = tab, Width = 96, Height = 34, Margin = new Padding(3, 0, 3, 0) };
                Theme.StyleButton(button);
                button.Click += delegate(object sender, EventArgs args) { SelectTab((string)((Button)sender).Tag); };
                tabs.Controls.Add(button);
            }
            return tabs;
        }

        private void BuildLeftPanel()
        {
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Surface };
            leftPanel.Controls.Add(scroll);
            int y = 0;

            var heading = NewLabel("运行监控", 0, y, 240, 24, false, true); scroll.Controls.Add(heading); y += 28;
            stateBadge.SetBounds(0, y, 250, 30); stateBadge.Text = "已停止"; stateBadge.TextAlign = ContentAlignment.MiddleCenter; Theme.StyleLabel(stateBadge, false, true); stateBadge.BackColor = Theme.AccentDark; scroll.Controls.Add(stateBadge); y += 40;
            targetLabel.SetBounds(0, y, 250, 42); targetLabel.Text = "目标窗口：自动查找中"; targetLabel.AutoEllipsis = true; targetLabel.MaximumSize = new Size(250, 42); Theme.StyleLabel(targetLabel, true); scroll.Controls.Add(targetLabel); y += 50;

            scroll.Controls.Add(NewLabel("生命与魔法", 0, y, 240, 24, false, true)); y += 28;
            hpValue.SetBounds(0, y, 250, 22); Theme.StyleLabel(hpValue); scroll.Controls.Add(hpValue); y += 26;
            mpValue.SetBounds(0, y, 250, 22); Theme.StyleLabel(mpValue); scroll.Controls.Add(mpValue); y += 34;

            scroll.Controls.Add(NewLabel("控制中心", 0, y, 240, 24, false, true)); y += 30;
            var observe = NewButton("开始观察", 0, y, 120, 34); observe.Click += delegate { SetState(SessionState.Observing, "用户开始观察"); }; scroll.Controls.Add(observe);
            var pause = NewButton("暂停", 130, y, 120, 34); pause.Click += delegate { SetState(SessionState.Paused, "用户手动暂停"); }; scroll.Controls.Add(pause); y += 42;
            var emergency = NewButton("紧急停止", 0, y, 250, 36, true); emergency.Click += delegate { SetState(SessionState.EmergencyStop, "用户触发紧急停止"); }; scroll.Controls.Add(emergency); y += 48;

            scroll.Controls.Add(NewLabel("攻击模式", 0, y, 240, 22, false, true)); y += 24;
            modeBox.SetBounds(0, y, 250, 28); modeBox.DropDownStyle = ComboBoxStyle.DropDownList; modeBox.Items.AddRange(new object[] { "单体优先", "自动", "群攻优先" }); modeBox.SelectedIndex = 1; modeBox.BackColor = Theme.Surface2; modeBox.ForeColor = Theme.Text; scroll.Controls.Add(modeBox); y += 40;

            scroll.Controls.Add(NewLabel("血蓝保护阈值", 0, y, 240, 22, false, true)); y += 26;
            hpThreshold.SetBounds(0, y, 108, 26); hpThreshold.Minimum = 1; hpThreshold.Maximum = 99; hpThreshold.Value = 50; hpThreshold.BackColor = Theme.Surface2; hpThreshold.ForeColor = Theme.Text; scroll.Controls.Add(hpThreshold);
            var hpText = NewLabel("HP %", 116, y + 3, 50, 20, true, false); scroll.Controls.Add(hpText);
            mpThreshold.SetBounds(166, y, 84, 26); mpThreshold.Minimum = 1; mpThreshold.Maximum = 99; mpThreshold.Value = 30; mpThreshold.BackColor = Theme.Surface2; mpThreshold.ForeColor = Theme.Text; scroll.Controls.Add(mpThreshold); y += 40;
            scroll.Controls.Add(NewLabel("MP %", 116, y - 37, 50, 20, true, false));

            scroll.Controls.Add(NewLabel("动作设置", 0, y, 240, 22, false, true)); y += 25;
            var jumpLabel = NewLabel("跳跃键", 0, y + 4, 60, 20, true, false); scroll.Controls.Add(jumpLabel);
            jumpKey.SetBounds(66, y, 55, 25); jumpKey.Text = "Alt"; jumpKey.BackColor = Theme.Surface2; jumpKey.ForeColor = Theme.Text; scroll.Controls.Add(jumpKey);
            var moveLabel = NewLabel("移动", 132, y + 4, 42, 20, true, false); scroll.Controls.Add(moveLabel);
            var moveValue = NewLabel("↑ ↓ ← → 固定", 174, y + 4, 76, 20, false, false); moveValue.Font = new Font("Microsoft YaHei UI", 8F); scroll.Controls.Add(moveValue); y += 38;

            pickupEnabled.Text = "启用自动拾取"; pickupEnabled.Checked = true; pickupEnabled.AutoSize = true; pickupEnabled.SetBounds(0, y, 130, 24); pickupEnabled.ForeColor = Theme.Text; scroll.Controls.Add(pickupEnabled);
            pickupKey.SetBounds(180, y, 70, 25); pickupKey.Text = "Z"; pickupKey.BackColor = Theme.Surface2; pickupKey.ForeColor = Theme.Text; scroll.Controls.Add(pickupKey); y += 42;

            hidLabel.SetBounds(0, y, 250, 35); hidLabel.AutoEllipsis = true; Theme.StyleLabel(hidLabel, true); scroll.Controls.Add(hidLabel); y += 44;
            pauseLabel.SetBounds(0, y, 250, 38); pauseLabel.Text = "最近原因：无"; pauseLabel.AutoEllipsis = true; Theme.StyleLabel(pauseLabel, true); scroll.Controls.Add(pauseLabel);
        }

        private void BuildContentPanel()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = Theme.Window };
            mapLabel.Dock = DockStyle.Left; mapLabel.Width = 420; Theme.StyleLabel(mapLabel, false, true); header.Controls.Add(mapLabel);
            var safe = new Label { Text = "演示模式：不会发送按键", Dock = DockStyle.Right, Width = 250, TextAlign = ContentAlignment.MiddleRight }; Theme.StyleLabel(safe, true, true); safe.ForeColor = Theme.Warning; header.Controls.Add(safe);
            contentPanel.Controls.Add(header);
            preview.Dock = DockStyle.Fill;
            contentPanel.Controls.Add(preview);
            logBox.Multiline = true; logBox.ReadOnly = true; logBox.ScrollBars = ScrollBars.Vertical; logBox.Dock = DockStyle.Fill; logBox.BackColor = Color.FromArgb(9, 16, 17); logBox.ForeColor = Theme.Text; logBox.BorderStyle = BorderStyle.None; logBox.Font = new Font("Consolas", 9F); logBox.Visible = false; contentPanel.Controls.Add(logBox);
            statusLabel.Dock = DockStyle.Bottom; statusLabel.Height = 28; Theme.StyleLabel(statusLabel, true); contentPanel.Controls.Add(statusLabel);
            mapLabel.Text = "当前地图：未绑定 · 地图档案：未验证";
            statusLabel.Text = "当前状态：已停止 · 当前动作：无 · 输入锁：原型锁定";
        }

        private void BuildRightPanel()
        {
            var scroll = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Theme.Surface,
                Padding = new Padding(0)
            };
            rightPanel.Controls.Add(scroll);

            var safety = NewRightSection("安全门", 154);
            rightSafetyValue.SetBounds(10, 34, 250, 26);
            rightSafetyValue.Text = "输入已锁定 · 原型安全";
            rightSafetyValue.ForeColor = Theme.Success;
            rightSafetyValue.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            safety.Controls.Add(rightSafetyValue);
            AddRightLine(safety, "前台窗口", "等待检测", 68, out rightFocusValue);
            AddRightLine(safety, "失焦保护", "已启用", 94, out _);
            AddRightLine(safety, "紧急停止", "手动按钮", 120, out _);
            scroll.Controls.Add(safety);

            var confidence = NewRightSection("视觉识别", 142);
            rightConfidenceValue.SetBounds(10, 34, 250, 24);
            rightConfidenceValue.Text = "角色 0.94   野怪 0.88   掉落 0.76";
            Theme.StyleLabel(rightConfidenceValue, false, true);
            confidence.Controls.Add(rightConfidenceValue);
            AddRightLine(confidence, "检测管线", "OpenCV + YOLO（模拟）", 68, out _);
            AddRightLine(confidence, "帧耗时", "等待采集", 94, out _);
            AddRightLine(confidence, "遮挡状态", "未判定", 120, out _);
            scroll.Controls.Add(confidence);

            var map = NewRightSection("地图标定与大模型", 166);
            rightMapValue.SetBounds(10, 34, 250, 25);
            rightMapValue.Text = "未绑定 · 等待视觉录制";
            rightMapValue.ForeColor = Theme.Warning;
            rightMapValue.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            map.Controls.Add(rightMapValue);
            AddRightLine(map, "覆盖率", "68%（演示）", 68, out _);
            AddRightLine(map, "标定误差", "2.4 px（演示）", 94, out _);
            AddRightLine(map, "模型调用", "0 次 · 低频", 120, out rightModelValue);
            AddRightLine(map, "输出", "初始结构标注", 146, out _);
            scroll.Controls.Add(map);

            var events = NewRightSection("最近事件", 160);
            rightLogBox.Multiline = true;
            rightLogBox.ReadOnly = true;
            rightLogBox.ScrollBars = ScrollBars.Vertical;
            rightLogBox.BorderStyle = BorderStyle.None;
            rightLogBox.BackColor = Color.FromArgb(9, 16, 17);
            rightLogBox.ForeColor = Theme.Muted;
            rightLogBox.Font = new Font("Consolas", 8F);
            rightLogBox.SetBounds(10, 34, 260, 116);
            events.Controls.Add(rightLogBox);
            scroll.Controls.Add(events);
        }

        private static Panel NewRightSection(string title, int height)
        {
            var section = new Panel { Width = 270, Height = height, BackColor = Theme.Surface2, Margin = new Padding(0, 0, 0, 10), BorderStyle = BorderStyle.FixedSingle };
            var header = new Label { Text = title, Location = new Point(10, 8), Size = new Size(250, 20) };
            Theme.StyleLabel(header, false, true);
            section.Controls.Add(header);
            return section;
        }

        private static void AddRightLine(Panel section, string name, string value, int y, out Label valueLabel)
        {
            var nameLabel = new Label { Text = name, Location = new Point(10, y), Size = new Size(86, 20) };
            Theme.StyleLabel(nameLabel, true);
            section.Controls.Add(nameLabel);
            valueLabel = new Label { Text = value, Location = new Point(96, y), Size = new Size(164, 20), AutoEllipsis = true, TextAlign = ContentAlignment.MiddleRight };
            Theme.StyleLabel(valueLabel, false);
            section.Controls.Add(valueLabel);
        }

        private Label NewLabel(string text, int x, int y, int width, int height, bool muted, bool bold)
        {
            var label = new Label { Text = text, Location = new Point(x, y), Size = new Size(width, height) };
            Theme.StyleLabel(label, muted, bold);
            return label;
        }

        private Button NewButton(string text, int x, int y, int width, int height, bool danger = false)
        {
            var button = new Button { Text = text, Location = new Point(x, y), Size = new Size(width, height) };
            Theme.StyleButton(button, danger);
            return button;
        }

        private void SelectTab(string tab)
        {
            currentTab = tab;
            bool map = tab == "地图标定";
            preview.Visible = tab == "实时画面" || map;
            logBox.Visible = tab == "运行日志";
            preview.SetMapMode(map);
            if (tab == "动作配置") AppendLog("[配置] 移动键固定 Left/Right/Up/Down；跳跃=" + jumpKey.Text + "；拾取=" + (pickupEnabled.Checked ? pickupKey.Text : "关闭"));
            if (tab == "识别模型") AppendLog("[模型] OpenCV + YOLO/ONNX（原型使用模拟观察数据）");
            if (tab == "地图标定") SetState(SessionState.MapScanning, "打开地图标定页，等待视觉录制");
            statusLabel.Text = "当前页面：" + tab + " · " + (sessionState == SessionState.Stopped ? "已停止" : SessionStateText.ToChinese(sessionState));
        }

        private void SetState(SessionState state, string reason)
        {
            sessionState = state;
            telemetry.PauseReason = state == SessionState.Paused || state == SessionState.EmergencyStop ? reason : "无";
            stateBadge.Text = SessionStateText.ToChinese(state);
            stateBadge.BackColor = StateColor(state);
            pauseLabel.Text = "最近原因：" + telemetry.PauseReason;
            statusLabel.Text = "当前状态：" + SessionStateText.ToChinese(state) + " · 当前动作：" + (state == SessionState.Observing ? "观察画面" : "无") + " · 输入锁：原型锁定";
            AppendLog("[状态] " + SessionStateText.ToChinese(state) + " · " + reason);
        }

        private static Color StateColor(SessionState state)
        {
            switch (state)
            {
                case SessionState.EmergencyStop: return Theme.Danger;
                case SessionState.Paused: return Color.FromArgb(126, 87, 38);
                case SessionState.Observing: return Theme.AccentDark;
                case SessionState.MapScanning:
                case SessionState.MapCalibrating: return Color.FromArgb(51, 86, 91);
                default: return Color.FromArgb(48, 62, 62);
            }
        }

        private void CaptureTimerTick(object sender, EventArgs e)
        {
            RefreshTargetAndFrame();
        }

        private void RefreshTargetAndFrame()
        {
            TargetWindowInfo found;
            if (!WindowCapture.TryFindTarget(out found))
            {
                target = null;
                targetLabel.Text = "目标窗口：未找到“冒险岛怀旧服”";
                hidLabel.Text = "虚拟 HID：原型锁定（未发送）";
                if (sessionState == SessionState.Observing) SetState(SessionState.Paused, "未找到目标窗口");
                preview.SetFrame(null, "未找到目标窗口");
                return;
            }
            target = found;
            targetLabel.Text = "目标：" + found.Title + "\nPID " + found.ProcessId + " · 客户区 " + found.ClientScreenBounds.Width + "×" + found.ClientScreenBounds.Height + " · DPI " + found.Dpi;
            hidLabel.Text = "虚拟 HID：原型锁定 · 不连接、不发送报告";
            hidLabel.ForeColor = Theme.Warning;
            rightFocusValue.Text = found.IsForeground ? "游戏窗口（前台）" : "游戏窗口（非前台）";
            rightFocusValue.ForeColor = found.IsForeground ? Theme.Success : Theme.Warning;
            rightSafetyValue.Text = found.IsForeground ? "输入已锁定 · 可观察" : "输入已锁定 · 失焦暂停";
            rightSafetyValue.ForeColor = found.IsForeground ? Theme.Success : Theme.Warning;
            Bitmap frame = WindowCapture.Capture(found, out string reason);
            if (frame == null)
            {
                preview.SetFrame(null, reason);
                if (sessionState == SessionState.Observing) SetState(SessionState.Paused, reason);
                return;
            }
            if (lastCapture != DateTime.MinValue)
            {
                double elapsed = (DateTime.UtcNow - lastCapture).TotalMilliseconds;
                telemetry.FrameLatencyMs = Math.Max(1, (int)elapsed);
            }
            lastCapture = DateTime.UtcNow;
            captureCount++;
            recognitionCount++;
            lastWidth = found.ClientScreenBounds.Width;
            lastHeight = found.ClientScreenBounds.Height;
            preview.SetFrame(frame, reason);
            hpValue.Text = "HP 99%  · 触发 " + hpThreshold.Value + "%";
            hpValue.ForeColor = Theme.Success;
            mpValue.Text = "MP 35%  · 触发 " + mpThreshold.Value + "%";
            mpValue.ForeColor = Theme.Cyan;
            mapLabel.Text = currentTab == "地图标定" ? "地图扫描：森林入口 · 覆盖率 68% · 标定误差 2.4 px" : "当前地图：森林入口（模拟） · 地图档案：待验证";
        }

        private void TelemetryTimerTick(object sender, EventArgs e)
        {
            telemetry.CaptureFps = Math.Min(60, captureCount); captureCount = 0;
            telemetry.RecognitionFps = Math.Min(30, recognitionCount); recognitionCount = 0;
            telemetry.QueueAgeMs = telemetry.FrameLatencyMs + 8;
            telemetry.DroppedFrames = telemetry.DroppedFrames + (target == null ? 1 : 0);
            telemetry.MemoryMb = Process.GetCurrentProcess().WorkingSet64 / 1024D / 1024D;
            perfLabel.Text = "采集 " + telemetry.CaptureFps + " FPS   识别 " + telemetry.RecognitionFps + " FPS   延迟 " + telemetry.FrameLatencyMs + " ms   队列 " + telemetry.QueueAgeMs + " ms   丢帧 " + telemetry.DroppedFrames + "   内存 " + telemetry.MemoryMb.ToString("0") + " MB   CPU OpenCV模拟   GPU 未启用   HID 原型锁定";
            rightConfidenceValue.Text = "角色 0.94   野怪 0.88   掉落 0.76";
        }

        private void AppendLog(string text)
        {
            if (logBox.TextLength > 12000) logBox.Clear();
            logBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + " " + text + Environment.NewLine);
            if (rightLogBox.IsHandleCreated)
            {
                if (rightLogBox.TextLength > 4000) rightLogBox.Clear();
                rightLogBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + " " + text + Environment.NewLine);
            }
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            captureTimer.Stop(); telemetryTimer.Stop();
            AppendLog("[安全] 原型关闭，输入锁保持禁用");
        }

        internal void WriteSelfTestReport()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "prototype-self-test.txt");
            File.WriteAllText(path, string.Join(Environment.NewLine, new[]
            {
                "PROTOTYPE_MODE=SAFE_OBSERVE_ONLY",
                "INPUT_INJECTION=DISABLED",
                "TARGET_TITLE=冒险岛怀旧服",
                "MOVEMENT_KEYS=Left/Right/Up/Down",
                "JUMP_KEY=Alt",
                "PICKUP=OPTIONAL_DEFAULT_Z",
                "PREVIEW=CLIENT_AREA_ONLY",
                "STATE_MACHINE=Stopped,Observing,MapScanning,MapCalibrating,Paused,EmergencyStop"
            }));
        }

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var form = new PrototypeForm();
            if (args.Length > 0 && args[0] == "--self-test")
            {
                form.WriteSelfTestReport();
                form.Dispose();
                return;
            }
            Application.Run(form);
        }
    }
}

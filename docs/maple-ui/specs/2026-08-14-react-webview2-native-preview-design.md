# React 工作台与高帧率原生预览设计

日期：2026-08-14  
状态：待用户审阅  
关联主规格：`docs/MAPLE_PROJECT_SPEC.md`

## 1. 背景

当前 WinForms 原型在 `Program.cs` 中使用 `Timer.Interval = 300`，采集和绘制都在 UI 线程完成，因此遥测约为 3 FPS。该数值来自原型设置和同步捕获路径，不代表 Windows 或 React 的性能上限。

本次改造的目标是：保持 C# 原生运行能力和安全边界不变，替换工作台 UI，同时让实时画面达到 30 FPS 稳定、60 FPS 目标。

## 2. 方案

采用混合桌面架构：

- React + TypeScript + Vite：工作台 UI、配置、地图编辑、日志和遥测；
- Mantine：可访问的表单、Tabs、Drawer、Badge、Table 和状态组件；
- `lucide-react`：工具按钮和状态图标；
- WebView2：承载 React 静态资源并提供 JSON 消息桥；
- C# Host：窗口生命周期、消息校验、运行时状态和安全急停；
- Native `PreviewSurface`：实时画面帧交换、缩放和识别框绘制；
- `CaptureWorker`：WGC 优先，BitBlt/PrintWindow 仅作兼容或诊断回退。

React 页面不接收逐帧 base64 图像。原生预览与 WebView2 同级布局，React 通过宿主消息获得预览尺寸、状态和可用性。

首版交互必须保持开箱即用：默认配置和模型随包提供，程序自动发现窗口、校准客户区/DPI/HUD ROI、初始化唯一 Self，并在低置信度时自动进入修复/暂停流程。UI 不提供“点击 Self”“填写跟踪编号”或逐个标注人物/怪物的必填步骤；用户看到的是明确的诊断状态，而不是内部识别缺陷。

## 3. 组件边界

### `Maple.Core`

纯 C# 运行核心，包含 `TargetBinding`、`CaptureFrame`、`ObservationSnapshot`、`SessionState`、`SafetyGate` 和 `TelemetrySnapshot`。它不依赖 WinForms 控件，不引用 React 或 WebView2。

### `Maple.Capture`

实现 `ICaptureBackend`：

```csharp
public interface ICaptureBackend : IDisposable
{
    bool TryStart(TargetWindowInfo target, int maxFps, out string reason);
    bool TryGetLatestFrame(out CaptureFrame frame);
    CaptureDiagnostics Diagnostics { get; }
}
```

WGC 后端输出 GPU/CPU 可复用的帧资源；兼容后端输出 pooled bitmap。所有后端都必须支持取消、尺寸变化、失焦停止和 latest-frame-wins。

### `Maple.Preview`

原生控件只做渲染：读取最新帧、按宽高比缩放、绘制仍在 TTL 内的 overlay，并报告 `renderFps` 与 `renderLatencyMs`。它不决定状态、不发送输入。

实时预览只显示三类动态框：

- `Self`：绿色框，标签只包含置信度；Self 只有一个，不向用户显示跟踪编号；
- `Player`：青色框，标签包含置信度和跟踪编号，只观察，不参与目标选择；
- `Monster`：红色框，标签包含类别、置信度和目标编号，目标选择仍需经过距离、平台和拓扑过滤。

`loot`、HP/MP、地图名、技能栏和小地图不画框，只提供给内部识别和右侧状态面板。所有框绑定 `frameId`、模型版本和 stale TTL；过期或丢失的框自动隐藏。Self 置信度不足时不能提示用户点击确认；C# 运行核心必须进入 `Paused/CalibrationRequired`，自动重试窗口绑定、ROI、模型和跟踪器，直到达到高置信度门槛。用户不负责标注 Self，也不需要理解或填写跟踪编号。

### `Maple.Host`

创建 WebView2、加载本地 React bundle、注册 `WebMessageReceived`、验证消息 schema，并在 React 页面刷新或崩溃时保持后端暂停。

### `maple-ui`

按页面拆分 `WorkbenchPage`、`MapCalibrationPage`、`ReplayPage`、`SettingsPage`、`DiagnosticsPage`。状态使用 typed reducer 或 Zustand；所有后端消息先通过 schema 校验再进入 UI store。

## 4. 帧和消息流

```text
WGC/BitBlt -> CaptureWorker -> FrameSlot[2]
                                  |       |
                         Native Preview  VisionWorker
                                  |
                    React receives state/telemetry only
```

帧槽位必须固定为 2，写入完成后再发布版本号。渲染器发现版本变化就读取最新槽位，旧槽位直接复用。识别线程可以跳帧，但不能阻塞预览或安全监控。

识别数据不通过 React 逐帧传输。Native Preview 读取结构化 `OverlaySnapshot`，React 每秒接收聚合后的目标数量、置信度、角色所在层、地图身份和暂停原因。

```ts
type OverlaySnapshot = {
  frameId: number
  capturedAtMonoMs: number
  self?: { box: [number, number, number, number]; confidence: number; freshUntilMonoMs: number }
  players: Array<{ box: [number, number, number, number]; confidence: number; trackId: string; freshUntilMonoMs: number }>
  monsters: Array<{ className: string; box: [number, number, number, number]; confidence: number; targetId: string; freshUntilMonoMs: number }>
}
```

WebView2 消息使用：

```ts
type HostEvent =
  | { schemaVersion: 1; type: 'session.stateChanged'; payload: SessionState }
  | { schemaVersion: 1; type: 'telemetry.updated'; payload: TelemetrySnapshot }
  | { schemaVersion: 1; type: 'target.updated'; payload: TargetBinding }
  | { schemaVersion: 1; type: 'preview.availabilityChanged'; payload: PreviewStatus }

type UiCommand =
  | { schemaVersion: 1; type: 'session.arm' }
  | { schemaVersion: 1; type: 'session.pause' }
  | { schemaVersion: 1; type: 'session.emergencyStop' }
  | { schemaVersion: 1; type: 'config.update'; payload: ConfigPatch }
```

所有命令在 C# 端重新执行窗口、前台、帧 TTL、地图和设备安全检查。前端不能绕过安全门。

动作策略必须使用最新观察结果闭环：C# 根据 Self/Monster 框、相对距离、当前平台、朝向、攻击范围和地图拓扑计算移动方向与本次保持时间；发送 key-down 后持续观察新帧，进入攻击距离、接近边界、目标消失或安全门失败时立即 key-up。释放后重新确认距离、目标状态和角色位移，满足攻击前置条件才发送攻击键。配置中的最短/最大保持时间只是边界，不能被实现成固定按键脚本；实际保持时间、前置/后置条件和输入结果必须记录。

## 5. 性能策略

- 采集线程不使用 WinForms `Timer` 驱动；使用可取消后台循环或 WGC frame-arrived 事件；
- 目标采集 60 FPS，上限由实际窗口刷新和显示器刷新率决定；普通 CPU 下最低验收 30 FPS；
- 预览渲染独立于识别，识别低于 30 FPS 时继续显示最新画面；
- 禁止每帧创建大量 `Bitmap`、`Graphics` 或字符串；使用对象池/复用缓冲；
- 采用 P50/P95/P99 采集到显示延迟、丢帧、队列年龄和内存增长作为验收指标；
- WGC 不可用时记录后端降级原因，不静默宣称达到 60 FPS；
- 页面遥测默认 1Hz，日志按批次更新，避免 React 重渲染影响预览。

识别线程允许低于预览帧率；预览必须继续显示新鲜画面，但不能继续显示过期识别框。`renderFps`、`captureFps` 和 `recognitionFps` 必须分开统计。

## 6. 故障和降级

- WebView2 页面崩溃：C# 保持 `Paused`，显示原生安全提示并允许重新加载 UI；
- 采集后端断开：停止发布新帧，释放资源，进入 `Paused`；
- 目标失焦/最小化/尺寸变化：立即标记预览不可用并清空待执行动作；
- 帧延迟超过 100ms：保留预览但显示 stale 告警；超过 180ms 或连续黑帧则暂停；
- 原生预览控件异常：不影响 EmergencyStop，后端仍可独立执行停止和释放逻辑。

## 7. 验收计划

1. 使用固定录屏回放验证 CaptureWorker 能稳定输出 60 FPS 时间戳；
2. 使用授权测试窗口测量 WGC、BitBlt 和 PrintWindow 的 P50/P95/P99；
3. 验证 React 页面刷新、WebView2 崩溃和窗口失焦不会触发输入；
4. 验证 30 分钟运行内存无持续增长，latest-frame 队列始终为 2；
5. 在真实视觉模块接入前，所有命令仍保持输入禁用。

## 8. 不在本次设计内的内容

本设计不实现真实 OpenCV/YOLO/OCR、地图拓扑、百炼调用或虚拟 HID 报告。它只定义 UI、帧传输、性能和安全边界，确保后续实现可以独立替换视觉和输入模块。

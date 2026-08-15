# Maple 项目统一主规格

> 本文是当前项目唯一的实施入口。后续实现、测试和交接先读本文；其他规格、交接记录和设计草案仅用于历史追溯，不能覆盖本文中的最新决策。

规格版本：1.2
日期：2026-08-14  
状态：Mac 可开发模块已实现并完成离线回归；Windows 原生运行、真实模型准确率、30-60 FPS 和虚拟 HID 保持实机待验
目标平台：Windows 10/11 x64  
适用范围：获得授权的测试客户端

## 1. 项目目标

Maple 是一个独立的 Windows 桌面观察与自动化控制台。它绑定一个指定的“冒险岛怀旧服”窗口，采集客户区画面，展示实时预览、视觉识别结果、地图标定、运行状态和诊断信息。生产自动化必须以视觉反馈和安全状态为依据，未通过门禁的能力不得进入真实动作闭环。

产品闭环的唯一正确理解是：`窗口绑定 -> 原生采集 -> 本地视觉 -> ObservationSnapshot -> 安全门 -> 短动作决策 -> 虚拟 HID -> 新画面反馈 -> 提前释放/继续/暂停`。React 只展示状态并提交用户意图，不能生成动作、发送原始按键或承担逐帧画面链路。低频云端模型只能辅助未知地图结构，不进入实时控制闭环。

旧 WinForms 原型、SendInput 探针、net48 静态测试、历史设计文档和 `dist/` 实验输出已从当前工作树清除，只能通过 Git 历史追溯。新实现以模块化 React/.NET 8 架构为唯一基线，不能把旧程序的行为、性能或输入结论当作验收证据。完整自动打怪程序仍未通过 Windows 实机闭环，任何动作能力都必须重新经过本规格定义的安全和设备验收。

## 2. 已确认边界

### 2.1 允许范围

- 单个指定的窗口化客户端；
- 客户区实时镜像、截图/短录屏回放和诊断报告；
- OpenCV/OCR 处理固定 UI，YOLO/ONNX 处理动态目标；
- 在用户明确开启并可审计的情况下，可增加只读内存作为诊断/性能对照/非控制性辅助观察；
- 多帧地图扫描、平台/梯子候选和 `MapWorld` 坐标标定；
- 运行状态机、暂停、人工接管和紧急停止；
- 在独立输入适配器完成验收后，才评估移动、攻击、补给和可选拾取。

### 2.2 禁止范围

- 写入或修改游戏内存、客户端注入、网络协议修改或后台窗口消息；只读内存辅助不能改变客户端、资源或动作状态；
- 未验证地图、未知弹窗、失焦、黑帧、低置信度或设备状态不明时继续动作；
- 把 `SendInput` 探针当作生产输入路径；
- 未经用户确认的云端图像上传。

只读内存辅助不是默认能力，必须独立于视觉管线、可关闭、可审计、记录读取范围和版本，并在视觉结果与安全状态冲突时以视觉和安全门为准。它不能单独证明角色、地图、HP/MP 或动作后置条件，也不能直接生成或放行动作。

## 3. 当前状态

| 阶段 | 状态 | 说明 |
| --- | --- | --- |
| 产品规格和安全边界 | 已完成 | 统一到本文，旧文档仅作历史记录 |
| 旧 WinForms/输入探针材料 | 未验证、可舍弃 | 仅作历史参考；不作为新实现基线，不承诺兼容 |
| 模块化共享契约 | `DONE (macOS)` | schema v2、JSON Schema、TypeScript 校验、共享夹具和 C# Contracts/Runtime.Tests 已执行；Self 无跟踪编号，Attack/UsePotion profile 已统一 |
| React 工作台骨架 | `DONE (macOS) / WINDOWS_PENDING` | Vite + React + Ant Design 壳层、Maple 视觉令牌和响应式工作区已完成；WebView2 宿主绑定待 Windows 验收 |
| 类型化桥接与模拟宿主 | `DONE (macOS)` | 白名单命令、逐命令 payload 校验、禁止字段递归拦截、宿主事件校验、确定性模拟会话和释放测试已完成；真实 WebView2 事件接线归入 Windows Host 阶段 |
| 实时工作台 UI | `DONE (macOS) / WINDOWS_PENDING` | 三栏实时工作台、中文优先交互、默认配置、诊断/日志/遥测、响应式布局和紧急停止状态已完成；已在 1440×900、1280×720、390×844 浏览器回归，原生画面承载与 Windows Host 接线待验收 |
| 预览与识别框契约 | `DONE (macOS) / WINDOWS_PENDING` | Capture 两槽 latest-frame、Self/其他玩家/怪物颜色语义、过期框隐藏、浏览器模拟画布、OpenCV/OCR/ONNX 后处理和原生 PreviewSurface 已完成；真实帧率/DPI/显卡路径待 Windows 验收 |
| 可移植核心与反馈驱动动作策略 | `DONE (macOS) / WINDOWS_PENDING` | 安全门、状态机、时长估算、生产编排器、最新观察反馈、取消/异常 ReleaseAll 和 Replay L3 已通过 Runtime.Tests；真实输入仍待 Windows |
| 地图拓扑与回放 | `DONE (macOS) / WINDOWS_PENDING` | candidate/validated/archived、拓扑、Replay JSONL 和闭环行为已通过可移植测试；真实地图扫描/校准动作待 Windows |
| 视觉融合与百炼边界 | `DONE (macOS) / MODEL_PENDING` | 百炼固定 endpoint/白名单/凭据边界、真实地图标注 HTTP 客户端、上传同意、图片上限、来源帧校验，以及 OpenCvSharp/OCR/ONNX adapters、manifest SHA-256、TTL/冲突/超时融合均已离线验证；WGC 关键帧内存源与 PNG 编码已在 Windows 自建窗口通过，真实客户端扫描、百炼联网响应和模型准确率仍待验 |
| Null/Replay 输入适配器 | `DONE (macOS) / WINDOWS_PENDING` | Null/Replay/活动键/ReleaseAll 行为已通过便携测试；真实 HID 保持禁用 |
| WebView2 Host 与原生预览交接 | `SOURCE_READY / WINDOWS_PENDING` | `Maple.Host` 已迁移为 .NET 8 win-x64 可执行入口；WebView2 Evergreen、本地资源映射、系统 WGC/D3D11 自检和 BitBlt fallback 已在 Windows 通过，刷新/崩溃故障注入及真实客户端客户区仍待验；严格 bridge 和关闭 ReleaseAll 保持启用 |
| 30-60 FPS 实时预览 | `WINDOWS_PENDING` | 两槽位链路和性能验收指标已定义；必须在 Windows 实测 P50/P95/P99、1280×720 和 1440×900 后才能完成 |
| 虚拟 HID 适配器 | `DRIVER_BUILD_PASS / WINDOWS_EVIDENCE_PENDING` | 项目自有 KMDF/VHF 驱动、Boot Keyboard 报告编码、唯一接口枚举、IOCTL 传输、Neutral/Heartbeat/watchdog、安装/卸载脚本和 WDK/Inf2Cat 零警告构建已完成；测试签名安装、设备路径和设备/Windows 输入/授权客户端三层 PASS 仍待重启实测，生产输入保持禁用 |
| 自动战斗闭环 | `DONE (macOS) / WINDOWS_PENDING` | 生产 C# 编排器已以 Replay/替身验证移动到攻击距离提前释放、profile、补给优先级、低置信度、过期帧和 ReleaseAll；真实 HID/客户端画面反馈待 Windows |
| macOS 页面与可移植回归 | `DONE (macOS)` | `verify-portable` 已通过：npm audit 0 漏洞、ESLint、TypeScript、33 个 Vitest、Vite 构建、桌面/移动 Playwright、38 个 Runtime.Tests、81 个 Host.Tests、30 个 Input.Tests、2 个 Map.Tests、portable contracts/closed-loop，以及 Host Rebuild 0 warning |

状态词只有以下含义：`DONE (macOS)` 表示 Mac 可执行测试和构建证据通过；`SOURCE_READY` 表示源码已可交叉编译但运行依赖真实 Windows；`WINDOWS_PENDING`、`MODEL_PENDING` 均属于目标环境未完成。后续 AI 不得把源码存在、交叉编译、XML 可解析或静态 token 检查改写成 Windows 功能完成。

## 4. 目标技术架构

```text
Maple.exe（.NET 8 Windows Desktop x64 原生宿主）
├── WebView2：React + TypeScript + Ant Design 工作台
├── PreviewSurface：WGC / Direct3D / Direct2D 原生高帧率预览
├── Capture / Vision / Map / Core / Replay：后台工作线程
└── Input：唯一虚拟 HID 边界、ReleaseAll 和独立 watchdog
```

### 4.1 React 工作台

React 只负责界面和用户意图：运行控制、参数配置、地图档案、模型状态、日志、诊断和遥测。组件采用 Ant Design，图标采用 `@ant-design/icons`，再叠加 Maple 自定义视觉令牌。界面使用深色、高对比度、稳定三栏的工作台布局，中文优先，不使用营销式大卡片或无意义装饰动画。

### 4.2 C# 原生运行核心

C# 原生运行核心按全新模块边界实现。旧 `WindowCapture`、`PrototypeState`、输入探针和 WinForms 界面只保留为历史参考，不直接迁移，负责：

- 目标窗口身份绑定：HWND、PID、启动时间、路径/版本、客户区尺寸和 DPI；
- 采集、帧时间戳、帧 TTL、失焦/最小化/黑帧检测；
- OpenCV/ONNX/OCR、地图拓扑、状态机和动作安全门；
- 输入设备状态、全键释放、心跳 watchdog 和 EmergencyStop；
- 将状态和诊断事件以结构化消息发布给 React。

Windows 正式实现统一使用 SDK-style `net8.0-windows10.0.19041.0`、`win-x64`、`SelfContained=true`。发布不是强制单文件；WebView2、ONNX 和原生依赖允许随安装目录部署，以稳定、可诊断和可回滚为先。安装器必须携带或修复已验收版本的 WebView2 Evergreen Runtime，应用启动时检查最低版本。当前工作树只保留 SDK-style .NET 8 工程。

### 4.3 原生预览

中央实时画面不通过 React 的逐帧 JSON 或 base64 图片通信。使用独立的原生 `PreviewSurface` 控件，作为 WebView2 宿主窗口的同级子控件或独立预览层。React 只提供预览区域布局和叠加配置，原生控件负责帧交换、缩放、叠加框和绘制。

### 4.4 开箱即用原则

默认安装包必须自带默认键位、阈值、模型 manifest、采集后端选择和安全策略。首次启动自动发现目标窗口、校准客户区/DPI/HUD ROI、建立唯一 Self 观察并加载默认配置；用户不需要选择 PID、框选 ROI、点击 Self、填写跟踪编号或逐个标注人物/怪物。任何低置信度都由程序自动重试、重标定、切换兼容后端或修复模型/跟踪链路；在达到高置信度前保持暂停并给出可诊断的原因，不把内部缺陷转成用户操作步骤。已验证地图可以直接运行；未知地图仍必须完成一次自动扫描和地图结构验证门禁。

窗口发现规则必须确定：只有一个合格窗口时在 3 秒内自动绑定；没有窗口时保持 `Stopped` 并等待；存在多个合格窗口时只显示窗口缩略图/标题供用户选择，不要求输入 PID/HWND。绑定后 PID、进程启动时间、路径哈希、客户区和客户端版本任一变化都使旧绑定及地图一致性失效。Self 自动修复按 1/2/5 秒退避持续重试，60 秒仍未达到门槛时自动生成诊断包并继续安全暂停，不能让用户点击确认 Self。仅 `CalibrationRequired` 和短暂 `StaleFrame` 可在已 arm 会话中通过连续稳定帧自动恢复；失焦、未知弹窗、设备断连、手动暂停和 EmergencyStop 必须由用户重新恢复或创建会话。

## 5. 30-60 FPS 预览方案

### 5.1 采集链路

优先使用 Windows Graphics Capture（WGC）按窗口客户区采集；在系统版本、权限或目标客户端不兼容时回退到经过测量的 BitBlt/屏幕客户区采集。`PrintWindow` 只保留为低频诊断回退，不作为 30-60 FPS 主路径。

采集线程与 UI 线程完全分离，使用两个可复用帧槽位和 latest-frame-wins 策略：

```text
目标窗口 -> CaptureWorker -> FrameSlot[2]
                              |       |
                    PreviewSurface  VisionWorker
```

新帧到达时丢弃尚未消费的旧帧，禁止无界队列堆积。每帧记录 `frameId`、单调时钟、客户区尺寸、DPI、采集耗时和丢帧原因。

### 5.2 识别对象与实时画面标记

实时画面只绘制动态对象的三类标记，颜色和语义固定：

| 对象 | 颜色 | 标签 | 是否参与目标选择 | 备注 |
| --- | --- | --- | --- | --- |
| 自己角色 `self` | 绿色 | `Self <confidence>` | 是，作为导航参考 | Self 只有一个，不显示跟踪编号；跟踪状态由程序内部维护 |
| 其他玩家 `player` | 青色 | `Player <confidence> #<trackId>` | 否 | 只观察和显示，不攻击、不暂停、不作为导航目标 |
| 怪物 `monster` | 红色 | `<class> <confidence> #<targetId>` | 是 | 目标选择还要经过距离、当前平台和地图拓扑过滤 |

识别框必须绑定 `frameId`、模型版本、置信度、归一化客户区坐标和 stale TTL。识别结果过期、目标被遮挡或跟踪丢失时自动隐藏对应框，不能继续显示过期框造成误导。

掉落物 `loot` 只允许进入内部观察通道，不在实时预览中画框、标签或数量；HP/MP 条、数字、地图名、技能栏、小地图等固定 UI 同样只做内部 OpenCV/OCR 识别，不在客户区镜像上画框。

自己的角色必须完全自动识别：结合 YOLO、角色特征、初始位置和多帧轨迹推断，内部维护唯一 Self 跟踪状态，不向用户显示或要求填写跟踪编号。置信度不足不是用户操作问题，而是程序的模型、ROI、窗口绑定或跟踪链路问题；此时程序必须保持 `Paused/CalibrationRequired`，自动重试采集、重新标定 ROI、刷新模型/跟踪器或进入离线诊断，直到达到配置的高置信度门槛。未达到门槛前不得启动真实动作，也不得要求用户点击角色“帮程序猜”。客户端重启、PID/HWND 变化、地图身份重置或连续丢失后，程序自动重新建立 Self 观察并再次通过门槛。

预览层只展示观察结果，不直接决定按键。任何框都不能绕过状态机、帧新鲜度和安全门。

### 5.3 绘制链路

预览目标为 60 FPS 上限、30 FPS 稳定下限。渲染线程只读取最新完整帧，不等待识别结果；识别框使用最近一次仍在 TTL 内的观察快照。识别变慢时画面仍保持流畅，过期框自动隐藏。

首版可用 GDI+ 原生控件验证帧率；正式版优先采用 Direct3D/WGC 纹理或 Direct2D 绘制，减少 `Bitmap` 分配、像素复制和 GC 抖动。预览帧率与识别帧率分别统计，不能用识别帧率冒充画面帧率。

### 5.4 性能验收

- 采集频率：目标窗口可见且前台时 30-60 FPS；普通 CPU 下不低于 30 FPS；
- 预览绘制：目标 60 FPS，上限受显示器刷新率限制；
- 端到端预览延迟：P95 不超过 100ms，P99 不超过 180ms；
- latest-frame 队列长度固定为 2，队列年龄超过 100ms 时显示告警；
- 任何识别或 React 页面卡顿都不得阻塞采集、安全门或 EmergencyStop；
- 采集后端、分辨率、DPI、CPU/GPU、内存和丢帧原因都写入诊断日志。

当前原型的 `captureTimer.Interval = 300` 仅是低频演示设置，迁移时必须删除同步定时器式采集。

## 6. WebView2 通信契约

C# 与 React 通过版本化 JSON 消息通信。消息只传控制和状态，不传高频原始帧。

示例事件：

```json
{
  "schemaVersion": 2,
  "type": "telemetry.updated",
  "timestamp": "2026-08-14T12:00:00Z",
  "payload": {
    "captureFps": 58,
    "renderFps": 60,
    "recognitionFps": 18,
    "frameLatencyMs": 42,
    "droppedFrames": 3,
    "state": "Observing",
    "queueAgeMs": 12,
    "pauseReason": "None"
  }
}
```

React 只允许发送会话意图和配置更新，例如 `session.arm`、`session.pause`、`session.emergencyStop`、`config.update`。React 不允许提交 `AbstractAction`、原始按键、HID 报告、HWND 消息或任意 URL。后端必须重新检查窗口身份、前台状态、帧新鲜度和设备健康度，不能信任前端状态。

### 6.1 观察快照

视觉层对外发布的 `ObservationSnapshot` 至少包含：

```json
{
  "schemaVersion": 2,
  "frameId": 1842,
  "capturedAtMonoMs": 441238,
  "target": { "hwnd": "0x1234", "pid": 1008, "clientWidth": 1280, "clientHeight": 720, "dpi": 96 },
  "self": { "box": [0.42, 0.51, 0.08, 0.18], "confidence": 0.94, "freshUntilMonoMs": 441338 },
  "players": [],
  "monsters": [{ "class": "snail", "box": [0.66, 0.54, 0.07, 0.13], "confidence": 0.88, "freshUntilMonoMs": 441338, "targetId": "monster-12" }],
  "loot": { "visible": false, "confidence": 0.0, "freshUntilMonoMs": 441338 },
  "hp": { "mode": "percent", "value": 0.99, "confidence": 0.98, "freshUntilMonoMs": 441338 },
  "mp": { "mode": "percent", "value": 0.35, "confidence": 0.96, "freshUntilMonoMs": 441338 },
  "map": { "mapId": "forest-east", "state": "validated", "confidence": 0.91, "freshUntilMonoMs": 441338 },
  "state": "Observing"
}
```

预览可消费 `self`、`players` 和 `monsters`；`self` 对外不包含跟踪编号，只有 `confidence` 和唯一当前框。`loot`、HP/MP、地图和技能状态只能进入状态机与诊断面板。所有观察字段都必须有时间戳、置信度和 TTL，过期值不得触发动作。

## 7. UI 信息架构

- 顶部：应用标识、目标窗口、连接/安全状态、当前会话状态和运行控制；
- 左侧：HP/MP 当前读数及阈值、攻击模式、攻击技能键、移动/跳跃键、拾取开关/按键和地图档案；
- 中央：实时预览、地图标定、回放视图；原生预览区域保持固定比例；
- 右侧：Self/Player/Monster 识别置信度、角色所在层、怪物数量、地图拓扑、窗口焦点、输入设备和事件日志；
- 底部：采集 FPS、渲染 FPS、识别 FPS、固定 UI/OCR 耗时、动态检测耗时、端到端延迟、队列年龄、丢帧、内存和暂停原因。

所有按钮、开关、滑块、下拉框和图标按钮必须有明确的禁用、加载、错误和键盘焦点状态。紧急停止必须始终可见，且不依赖 React 页面刷新。

## 8. 运行状态机

```text
Stopped -> Arming -> Observing
                      ├── validated map -> Navigating <-> Attacking / Looting / UsingPotion
                      └── unknown map -> MapScanning -> MapCalibrating -> Observing
任意活动态 -> Paused / ManualIntervention / EmergencyStop
Paused -> Arming（重新执行全部门禁）；EmergencyStop -> 新会话
```

失焦、窗口身份变化、黑帧、角色丢失、地图未验证、输入设备断连、心跳超时、系统锁屏/睡眠和未知弹窗必须清空动作队列并进入 `Paused` 或 `EmergencyStop`。低置信度自动修复成功后可按 4.4 节自动重新 arm；其他安全中断由用户恢复，且恢复必须重新采集稳定帧、重新执行全部门禁并丢弃旧动作。

### 8.1 地图扫描和验证门禁

未知地图必须先进入 `MapScanning`，采集覆盖不同镜头位置的关键帧组，记录相机位移、覆盖率、未覆盖区域、平台/台阶/梯子候选和标定误差。低频云端视觉只能输出结构化 `InitialMapAnnotation`，不能输出路线或按键。

地图档案状态固定为：

- `candidate`：来源帧、坐标变换和结构候选已保存，但不能驱动动作；
- `validated`：用户确认、本地几何/拓扑校验、跨帧一致性和动作预览/短时验证均通过；
- `archived`：旧版本，仅供回滚和对比。

覆盖率不足、坐标注册冲突、关键平台/梯子未覆盖、标定误差超限或地图身份冲突时，不得进入 `Navigating`。

为避免首次地图验证形成循环依赖，`MapCalibrating` 是唯一例外：输入设备三层验收已经通过时，它可以在当前可见且几何安全的平台范围内执行单次不超过 300ms 的校准移动，只允许 `MoveLeft`、`MoveRight`、`Jump` 和立即 `ReleaseAll`，禁止攻击、补给、拾取和跨未知边界。每次动作后必须重新观察，任何不确定立即暂停。该动作只生成校准证据，不代表 candidate 地图可用于生产导航；没有已验收 HID 时由用户手动移动角色完成扫描。

### 8.2 战斗、补给和拾取规则

- 攻击模式固定为“单体优先 / 自动 / 群攻优先”。自动模式按同屏怪物数量阈值和最短切换间隔选择技能；
- 目标只从 `monster` 中选择，按置信度、距离、当前平台、可达性和地图拓扑过滤；`player` 永不进入目标列表；
- 每个攻击动作必须有前置条件、key-down/hold/key-up、最大持续时间、冷却限制和后置视觉反馈；攻击无反馈或目标丢失超过有限预算时暂停；
- HP/MP 支持 `percent` 和 `absolute` 两种互斥阈值模式，默认优先级 `HP > MP`；读数冲突、过期或补给无反馈时暂停，HP critical 且无法确认补给成功时 EmergencyStop；
- 拾取可随时关闭，默认按键 `Z`。只有怪物威胁低、角色与 loot 同层且内部观察确认掉落可达时才允许 `Pickup`；关闭拾取后不得产生该动作；
- 移动方向固定为 `Left/Right/Up/Down`，跳跃默认 `Alt` 且可修改；未知地形、传送门、移动平台和不可解释边界默认阻止动作。

### 8.3 移动到攻击的感知闭环

基础自动攻击不是预先录制的固定按键序列，而是由每次最新的 `ObservationSnapshot` 驱动：

抽象动作词汇以本节为唯一准绳：`MoveLeft`、`MoveRight`、`Jump`、`ClimbUp`、`ClimbDown`、`Attack`、`UsePotion`、`Pickup`、`Pause`、`Replan`。`Attack` 必须携带 `profileId=singleAttack|areaAttack`，`UsePotion` 必须携带 `profileId=hpPotion|mpPotion`；方向键 `Up/Down` 只是输入映射，不再定义 `MoveUp/MoveDown` 动作。TypeScript/JSON/C# 契约已统一为 schema v2，禁止通过猜测按键来区分攻击或药水类型。

1. 从 Self/Monster 的当前框、相对距离、所在平台、地图拓扑、角色朝向和攻击范围判断是否已经满足攻击前置条件。
2. 如果不在攻击范围，策略层选择 `MoveLeft`、`MoveRight`、`Jump`、`ClimbUp` 或 `ClimbDown` 等抽象动作，并根据当前距离、历史位移速度、平台边界、镜头状态和动作上下限计算本次移动的保持时间。
3. 输入适配器发送 key-down；采集线程持续获取新帧。只要角色进入攻击距离、接近边界、目标消失、窗口失焦、帧过期或安全门失败，就立即 key-up，不等待固定时长结束。
4. key-up 后等待稳定帧，重新计算 Self/Monster 距离、朝向、目标存活和当前平台；只有后置条件满足才进入攻击动作。
5. 攻击键的按下/保持时间同样由技能配置的上下限、冷却、动画/命中反馈和目标状态计算；攻击后重新观察目标数量、目标位置、角色位移和技能反馈，再决定继续攻击、重新移动、切换目标或暂停。

移动时长不是用户填写的固定值，也不能只靠一次距离换算。配置中的最短/最大保持时间只是安全边界，程序必须使用视觉反馈提前结束或在无反馈时触发有限重试。所有 key-down/key-up、前置条件、后置条件和计算出的保持时间都要写入 JSONL，便于回放和调参。

### 8.4 动作节奏和卡死检测

动作由目标几何关系、动画/命中反馈、角色位移、镜头变化和技能冷却驱动，不能使用固定周期作为主循环。每次只规划有限短动作窗口；角色无位移、动作超时、连续无反馈或状态冲突时有限重试，超过预算进入 `Paused`。停止、失焦、异常退出或设备断连都必须执行 `ReleaseAll`。

## 9. 用户工作流、配置、日志和回放

### 9.1 首次配置

1. 自动发现并绑定唯一的可见目标窗口，保存 HWND/PID/进程路径、客户区尺寸和 DPI；用户不需要手动选择进程或填写窗口参数。
2. 自动计算固定 HUD 的 ROI 和客户区比例，不要求用户逐项框选；程序自动选择并验证采集后端。
3. 自动加载默认模型、键位、阈值和安全策略；自动建立唯一 Self 观察，低置信度时进入程序自修复流程，不要求用户点击角色。
4. 创建或选择地图档案。已验证地图直接进入观察；未知地图只能进入 `MapScanning`，不能直接运行。
5. 采集覆盖多个镜头位置的短录屏/关键帧，估计相机位移并注册到 `MapWorld`，显示覆盖率和未覆盖区域。
6. 可选调用百炼生成 `InitialMapAnnotation`；用户只确认地图结构版本，不参与 Self/Monster 逐个标注。
7. 通过本地几何/拓扑校验和动作预览/短时验证后，保存 `validated` 地图版本。
8. 使用默认攻击、药水、跳跃和拾取配置；高级用户可进入设置页修改，程序自动执行键冲突检查。
9. 执行虚拟 HID 单键诊断和人物/怪物离线回放检查。

### 9.2 日常运行与人工接管

1. 选择已验证的地图档案和动作配置，确认客户端可见、未最小化且为前台窗口。
2. 点击启动，通过显式 arm、短倒计时和目标窗口前台确认后进入 `Observing`。
3. 主界面显示 Self/目标/楼层、HP/MP、当前动作、攻击模式、地图版本、预览 FPS 和安全状态。
4. 用户可随时暂停、恢复或紧急停止。恢复前必须重新执行窗口、帧、角色、地图、HP/MP 和设备健康检查；旧动作队列不得复用。

用户手动键盘/鼠标操作、全局停止热键、失焦、锁屏/睡眠、未知弹窗、客户端重启和设备异常都会暂停或紧急停止；程序不自动抢回焦点、不自动换频道、不自动复活、不自动点击未知界面。

### 9.3 配置 schema

配置分为全局设置、地图档案和动作档案三类，均使用带 `schemaVersion` 的 JSON。配置必须支持迁移、默认值校验、原子写入、备份、损坏回退、文件锁和版本冲突检查。

```json
{
  "schemaVersion": 1,
  "window": {
    "titlePattern": "冒险岛怀旧服",
    "clientAreaAspect": 1.7778,
    "binding": { "hwnd": null, "pid": null, "pathHash": null, "dpi": null }
  },
  "keys": {
    "left": "Left", "right": "Right", "up": "Up", "down": "Down",
    "jump": "Alt", "pickup": "Z", "pickupEnabled": true
  },
  "actions": {
    "singleAttack": { "key": "J", "cooldownMs": 800, "minHoldMs": 60, "maxHoldMs": 180 },
    "areaAttack": { "key": "A", "cooldownMs": 1500, "minHoldMs": 60, "maxHoldMs": 180 },
    "hpPotion": { "key": "1", "mode": "percent", "percentBelow": 50, "valueBelow": null },
    "mpPotion": { "key": "2", "mode": "percent", "percentBelow": 30, "valueBelow": null }
  },
  "combat": { "mode": "auto", "areaTargetCount": 3, "switchCooldownMs": 1200 },
  "map": { "mapId": "forest-east", "calibrationVersion": 1, "status": "validated" }
}
```

百分比和绝对值阈值互斥；攻击键、药水键、跳跃键和拾取键必须执行冲突校验。移动键固定为 `Left/Right/Up/Down`，跳跃默认 `Alt` 可修改，拾取默认 `Z` 可关闭或修改。

单位必须在边界处统一：配置文件、React 输入和 bridge 中的百分比均使用 `0..100`（例如 `35` 表示 35%）；`ObservationSnapshot` 和 Core 内部统一使用 `0..1`（例如 `0.35`）。Host 配置适配器负责一次性换算并记录原值/归一化值，禁止在策略层再次猜测单位。绝对值模式保留原始非负数值。

### 9.4 日志与回放

每个会话写入独立目录和 JSONL 事件流，至少记录：时间戳、单调时间、客户端版本/窗口身份、frameId、采集后端、客户区/DPI、采集/渲染/识别 FPS、延迟、丢帧、Self/Player/Monster 结果、HP/MP 读数和置信度、地图/模型/ROI 版本、状态转换、拟执行动作、实际输入结果和暂停原因。

异常时保存前后截图或关键帧组、最后一帧 `ObservationSnapshot`、安全门检查和动作结果。回放模式默认完全禁用真实输入，只重现观察、状态转换和暂停原因。API Key 不得写入日志、地图档案或导出报告，使用 Windows Credential Manager/DPAPI 保存；外部导出必须脱敏。

### 9.5 模型和地图版本

YOLO/ONNX 模型 manifest 必须记录：类别清单（`self`、`player`、`monster`、内部 `loot`）、输入尺寸、置信度阈值/NMS、训练数据版本、权重哈希、推理后端、运行时版本和许可证。ONNX Runtime CPU 是基线，CUDA/TensorRT 为可选 provider；GPU 缺失只降级性能，不改变安全阈值。

地图档案保存地图身份线索、扫描帧索引、相机变换、覆盖率、关键结构覆盖、平台/梯子拓扑、标定误差、本地验证报告、模型/ROI 版本和创建客户端版本。档案固定为 `candidate`、`validated`、`archived` 三态；新候选不得覆盖当前已验证版本，模型、UI 锚点、DPI 或客户端版本变化时必须重新执行一致性检查。

### 9.6 百炼视觉增强

首个交付只支持百炼一个云端厂商，不做通用供应商网关。设置页只提供启用开关、内置模型下拉框、API Key 输入和“测试连接”按钮；服务地址和模型清单由应用版本固定，不接受任意 URL 或远程插件。

允许调用的场景仅为：首次未知地图扫描、用户主动重建、分辨率/UI 版本变化、持续低置信度复核。调用期间输入保持暂停，用户可见上传范围、图片尺寸、留存和成本信息。响应必须通过 JSON schema、来源帧/坐标系校验、本地几何/拓扑约束、覆盖率/误差门禁和用户确认；模型不生成按键序列、完整路线或动作决策。未配置、超时、格式错误或复核失败时保持安全暂停或使用旧的已验证档案，不猜测继续运行。

## 10. 输入适配器契约

生产输入唯一通过独立虚拟 HID 适配器。接入前必须获得精确设备路径、VID/PID、报告描述符和 `KeyDown`、`KeyUp`、`ReleaseAll`、`Heartbeat` 协议；未知设备不得发送报告。每次发送前重新检查 HWND/PID/前台/帧 TTL/设备心跳，任何竞态都以暂停和中性报告为准。

输入适配器是唯一可以产生键盘事件的模块，和视觉、策略、UI、配置存储解耦。上层只能提交抽象动作：

`MoveLeft`、`MoveRight`、`ClimbUp`、`ClimbDown`、`Jump`、`SingleAttack`、`AreaAttack`、`UseHpPotion`、`UseMpPotion`、`Pickup`。

适配器至少提供 `KeyDown`、`KeyUp`、`Press`、`ReleaseAll`、`Heartbeat`、设备状态查询和活动按键注册表。每个动作必须记录请求、实际报告、key-down/key-up 配对、前台检查、设备心跳、最大保持时间和后置视觉观察。动作超时、无反馈、失焦、设备断连、进程退出或异常退出都必须触发 `ReleaseAll`。

输入验收分为设备层、操作系统层和客户端画面响应层，三层证据必须分别记录。未知设备、未确认报告描述符、心跳超时或报告释放能力不明时，状态必须为 `InputUnavailable`，不得尝试发送。`SendInput` 探针只保留历史诊断，不得复用到自动运行路径。

`keybd_event` 只允许用于独立的 Windows 诊断探针（diagnostic-only），并且仅面向获得授权且处于前台的客户端。该探针不是生产自动化，不改变生产输入仅能经过已验收虚拟 HID 的边界，其结果不得作为 L4/L5 生产证据。

### 10.1 输入安装和发布边界

如果虚拟 HID 需要系统组件，安装器必须明确展示管理员权限、驱动签名、Secure Boot、重启、卸载、失败清理、回滚和 Defender/SmartScreen 兼容性。程序不得静默安装、加载或逆向未确认设备的私有协议。运行时主程序应以普通权限启动，并在启动前显示输入适配器版本和状态。

## 11. 测试和量化验收

- 预览：采集/渲染 30-60 FPS，P95 延迟不超过 100ms，latest-frame 队列固定为 2；
- 识别：按地图、分辨率、特效和遮挡分别统计 Self/Player/Monster precision/recall、位置误差、跟踪丢失率和 stale 率；
- 固定 UI：HP/MP、地图名和技能状态的连续帧一致性、OCR 置信度和冲突暂停；
- 地图：覆盖率、跨帧坐标一致性、平台/梯子连接、标定误差和 candidate/validated 回滚；
- 状态机：暂停/恢复、攻击模式切换、药水优先级、拾取开关、动作超时和 EmergencyStop；
- 输入：key-down/key-up 配对、ReleaseAll、失焦阻断、设备断连、页面刷新和 WebView2 崩溃不产生输入；
- 稳定性：30 分钟 UI/预览无持续内存增长，生产候选阶段再做 4/8 小时 soak；
- 视觉基线：固定 UI/OCR 普通 CPU 目标不低于 15 FPS；动态检测 CPU 单帧目标不超过 250ms、GPU 单帧目标不超过 100ms，超时不得阻塞 HP/MP 安全监控；
- 内存基线：无 GPU 模式运行时目标不超过 1GB，并记录模型/缓存/预览各自占用；
- 端到端误动作次数必须为零；未知地图、未知弹窗、不匹配版本和不完整设备合同必须阻止启动。

识别发布门槛按每个受支持地图档案、分辨率和特效组合分别计算：Self precision 不低于 99.9%、recall 不低于 99.5%、中心点误差 P95 不超过 8px；Monster precision 不低于 99%、recall 不低于 97%、中心点误差 P95 不超过 12px；Player 被误判成 Monster 的发布集次数必须为零。HP/MP 百分比误差 P95 不超过 2 个百分点，critical 状态不得被误判为健康。每个组合至少保留 1000 个独立标注帧并包含遮挡、镜头移动和特效；不满足时只能停留在观察/诊断模式。

全量闭环分为五级，必须分别报告，不能用低级别替代高级别：

| 级别 | 范围 | 当前状态 |
| --- | --- | --- |
| L1 | Schema、TypeScript、静态资源、源码边界 | macOS 已执行 |
| L2 | React 单测、浏览器 E2E、规格级离线动作闭环 | macOS 已执行；仅为验收基准 |
| L3 | .NET 8 生产模块行为：采集 -> 视觉 -> 安全门 -> 决策 -> ReplayInput -> 新观察 | macOS 已执行替身/Replay 闭环；不代表真实 Windows 输入 |
| L4 | Windows 原生：WebView2/WGC/Direct2D、真实模型、崩溃/失焦/ReleaseAll | 未执行 |
| L5 | 授权客户端 + 虚拟 HID 三层证据 + 30 分钟/4 小时/8 小时稳定性 | 未执行 |

只有 L1-L5 全部通过，且误动作、卡键和 Player->Monster 误判均为零，才可称为“产品全量闭环完成”。`tests/closed-loop/portable-closed-loop.mjs` 的 PASS 只证明 L2 规格基准，不证明 C# 生产实现。

## 12. 发布与运维

- 发布目标为 Windows x64 自包含 `.exe`；模型、manifest、默认资源、许可证和采集后端能力随包提供；首次启动检查资源完整性和哈希。
- 用户数据写入用户目录，配置、地图、日志和回放按会话分目录保存；导出报告默认脱敏。
- 主程序显示客户端版本、地图档案版本、模型版本、采集后端、WebView2 运行时和输入适配器状态；任一版本/资源不匹配都阻止运行。
- 支持 WebView2 页面刷新/崩溃恢复、原生预览后端降级、日志滚动保留、诊断包导出和失败回滚。
- 生产候选版本完成 Windows 10/11、不同 DPI、不同显示器刷新率和 4/8 小时 soak 后才能评估真实输入。

## 13. 实施阶段

1. **UI 迁移基础**：建立 React/Vite 工程、WebView2 宿主、JSON bridge 和 Ant Design/Maple 视觉令牌；保留当前 WinForms 原型可回退。
2. **高帧率预览**：抽出 CaptureWorker、FrameSlot、PreviewSurface，先用录屏/桌面窗口做 30-60 FPS 基准。
3. **工作台功能迁移**：迁移状态、配置、地图标定演示、日志和遥测；动作保持禁用。
4. **真实本地视觉**：建立离线样本集，接入 OpenCV、OCR、YOLO/ONNX 和回放验证。
5. **地图与状态机**：实现候选地图、拓扑校验、短动作策略和人工确认。
6. **输入适配器**：在确认设备路径、VID/PID、报告描述符和通信协议后，独立完成设备/OS/客户端三层验收。
7. **发布验证**：win-x64 打包、日志脱敏、崩溃恢复、4/8 小时 soak 和回滚。

## 14. 验收命令与证据

Windows 环境执行：

```powershell
node .\tools\verify-portable.mjs
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\build-react-ui.ps1
dotnet publish .\src\Maple.Host\Maple.Host.csproj -c Release -r win-x64 --self-contained true
git diff --check
```

只有真实 HID 三层证据齐全时，才执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\windows\hid_contract.tests.ps1 -RequireEvidence
```

高帧率阶段另需记录：采集后端、显示器刷新率、窗口尺寸/DPI、P50/P95/P99 延迟、采集 FPS、渲染 FPS、识别 FPS、丢帧和内存。

## 15. 文档单一来源

当前工作树只保留三份有效文档：

- `docs/MAPLE_PROJECT_SPEC.md`：唯一产品和实施规格；
- `docs/WINDOWS_IMPLEMENTATION_HANDOFF_2026-08-14.md`：Windows 接手顺序和未完成边界；
- `docs/maple-runtime/VERIFICATION_2026-08-14.md`：最近一次 macOS/交叉编译验证证据。

所有旧设计、旧计划、旧会话交接和 archive 已从工作树清除。需要历史背景时使用 Git 历史，不得把历史版本重新作为当前实施依据。

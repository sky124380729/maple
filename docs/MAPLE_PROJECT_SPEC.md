# Maple 项目统一主规格

> 本文是当前项目唯一的实施入口。后续实现、测试和交接先读本文；其他规格、交接记录和设计草案仅用于历史追溯，不能覆盖本文中的最新决策。

日期：2026-08-14  
状态：React UI 与高帧率预览设计已确认方向，等待实现计划  
目标平台：Windows 10/11 x64  
适用范围：获得授权的测试客户端

## 1. 项目目标

Maple 是一个独立的 Windows 桌面观察与自动化控制台。它绑定一个指定的“冒险岛怀旧服”窗口，采集客户区画面，展示实时预览、视觉识别结果、地图标定、运行状态和诊断信息。生产自动化必须以视觉反馈和安全状态为依据，未通过门禁的能力不得进入真实动作闭环。

当前交付物是输入禁用的观察型原型，不是完整自动打怪程序。现有 `dist/MapleVisualPrototype.exe` 只用于窗口发现、客户区捕获、UI 演示和状态安全门；所有动作按钮不会发送键盘、鼠标或 HID。

## 2. 已确认边界

### 2.1 允许范围

- 单个指定的窗口化客户端；
- 客户区实时镜像、截图/短录屏回放和诊断报告；
- OpenCV/OCR 处理固定 UI，YOLO/ONNX 处理动态目标；
- 多帧地图扫描、平台/梯子候选和 `MapWorld` 坐标标定；
- 运行状态机、暂停、人工接管和紧急停止；
- 在独立输入适配器完成验收后，才评估移动、攻击、补给和可选拾取。

### 2.2 禁止范围

- 修改游戏内存、客户端注入、网络协议修改或后台窗口消息；
- 反检测、规避封禁或隐藏自动化行为；
- 未验证地图、未知弹窗、失焦、黑帧、低置信度或设备状态不明时继续动作；
- 把 `SendInput` 探针当作生产输入路径；
- 未经用户确认的云端图像上传。

交接文档中曾出现“只读内存辅助”的提议。该提议与本主规格的视觉唯一运行依据冲突，当前视为未决事项，不得实现或接入运行闭环。

## 3. 当前状态

| 阶段 | 状态 | 说明 |
| --- | --- | --- |
| 产品规格和安全边界 | 已完成 | 统一到本文，旧文档仅作历史记录 |
| WinForms 观察原型 | 已完成 | 可发现窗口、捕获客户区、显示模拟叠加和遥测 |
| 标准输入探针 | 已完成诊断 | `SendInput` 入队成功，但客户端响应未确认；不进入生产路径 |
| React 工作台 | 待实现 | 采用 WebView2 承载 React |
| 30-60 FPS 实时预览 | 待实现 | 当前原型 `300ms` 定时器只有约 3.3 FPS |
| 真实 OpenCV/YOLO/OCR | 未开始 | 需要先建立离线评测集 |
| 地图拓扑运行时 | 未开始 | 先做候选、校验、用户确认 |
| 虚拟 HID 适配器 | 未开始 | 等待明确设备合同和报告协议 |
| 自动战斗闭环 | 未开始 | 真实输入必须最后分阶段开启 |

## 4. 目标技术架构

```text
React + TypeScript + Mantine + lucide-react
                    |
              WebView2 工作台
                    |
       C# Windows Host / Native Runtime
                    |
     捕获、视觉、状态机、安全门、输入适配器
                    |
          原生高帧率 PreviewSurface
```

### 4.1 React 工作台

React 只负责界面和用户意图：运行控制、参数配置、地图档案、模型状态、日志、诊断和遥测。组件采用 Mantine，图标采用 `lucide-react`。界面使用深色、高对比度、紧凑的工作台布局，不使用营销式大卡片或装饰性动画。

### 4.2 C# 原生运行核心

C# 保留并逐步拆分现有 `WindowCapture`、`PrototypeState` 和安全逻辑，负责：

- 目标窗口身份绑定：HWND、PID、启动时间、路径/版本、客户区尺寸和 DPI；
- 采集、帧时间戳、帧 TTL、失焦/最小化/黑帧检测；
- OpenCV/ONNX/OCR、地图拓扑、状态机和动作安全门；
- 输入设备状态、全键释放、心跳 watchdog 和 EmergencyStop；
- 将状态和诊断事件以结构化消息发布给 React。

### 4.3 原生预览

中央实时画面不通过 React 的逐帧 JSON 或 base64 图片通信。使用独立的原生 `PreviewSurface` 控件，作为 WebView2 宿主窗口的同级子控件或独立预览层。React 只提供预览区域布局和叠加配置，原生控件负责帧交换、缩放、叠加框和绘制。

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

### 5.2 绘制链路

预览目标为 60 FPS 上限、30 FPS 稳定下限。渲染线程只读取最新完整帧，不等待识别结果；识别框使用最近一次仍在 TTL 内的观察快照。识别变慢时画面仍保持流畅，过期框自动隐藏。

首版可用 GDI+ 原生控件验证帧率；正式版优先采用 Direct3D/WGC 纹理或 Direct2D 绘制，减少 `Bitmap` 分配、像素复制和 GC 抖动。预览帧率与识别帧率分别统计，不能用识别帧率冒充画面帧率。

### 5.3 性能验收

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
  "schemaVersion": 1,
  "type": "telemetry.updated",
  "timestamp": "2026-08-14T12:00:00Z",
  "payload": {
    "captureFps": 58,
    "renderFps": 60,
    "recognitionFps": 18,
    "frameLatencyMs": 42,
    "droppedFrames": 3,
    "state": "Observing",
    "pauseReason": "无"
  }
}
```

React 发出的命令只允许抽象动作和配置更新，例如 `session.arm`、`session.pause`、`session.emergencyStop`、`config.update`。后端必须重新检查窗口身份、前台状态、帧新鲜度和设备健康度，不能信任前端状态。

## 7. UI 信息架构

- 顶部：应用标识、目标窗口、连接/安全状态、当前会话状态和运行控制；
- 左侧：HP/MP 阈值、攻击模式、跳跃/拾取配置和地图档案；
- 中央：实时预览、地图标定、回放视图；原生预览区域保持固定比例；
- 右侧：识别置信度、地图拓扑、窗口焦点、输入设备和事件日志；
- 底部：采集 FPS、渲染 FPS、识别 FPS、延迟、队列年龄、丢帧、内存和暂停原因。

所有按钮、开关、滑块、下拉框和图标按钮必须有明确的禁用、加载、错误和键盘焦点状态。紧急停止必须始终可见，且不依赖 React 页面刷新。

## 8. 运行状态机

```text
Stopped -> Arming -> Observing -> MapScanning -> MapCalibrating
                                      |
                                      v
                 Navigating -> Attacking -> Looting -> UsingPotion
                                      |
                         Paused / ManualIntervention / EmergencyStop
```

失焦、窗口身份变化、黑帧、角色丢失、地图未验证、输入设备断连、心跳超时、系统锁屏/睡眠和未知弹窗必须清空动作队列并进入 `Paused` 或 `EmergencyStop`。恢复必须重新采集稳定帧并由用户确认。

## 9. 实施阶段

1. **UI 迁移基础**：建立 React/Vite 工程、WebView2 宿主、JSON bridge 和 Mantine 主题；保留当前 WinForms 原型可回退。
2. **高帧率预览**：抽出 CaptureWorker、FrameSlot、PreviewSurface，先用录屏/桌面窗口做 30-60 FPS 基准。
3. **工作台功能迁移**：迁移状态、配置、地图标定演示、日志和遥测；动作保持禁用。
4. **真实本地视觉**：建立离线样本集，接入 OpenCV、OCR、YOLO/ONNX 和回放验证。
5. **地图与状态机**：实现候选地图、拓扑校验、短动作策略和人工确认。
6. **输入适配器**：在确认设备路径、VID/PID、报告描述符和通信协议后，独立完成设备/OS/客户端三层验收。
7. **发布验证**：win-x64 打包、日志脱敏、崩溃恢复、4/8 小时 soak 和回滚。

## 10. 验收命令与证据

Windows 环境执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\build-prototype.ps1 -Configuration Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\prototype_contract.tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\input_probe_logic.tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\minimal_probe_contract.tests.ps1
git diff --check
```

高帧率阶段另需记录：采集后端、显示器刷新率、窗口尺寸/DPI、P50/P95/P99 延迟、采集 FPS、渲染 FPS、识别 FPS、丢帧和内存。

## 11. 历史资料

以下文件保留用于追溯，内容不再作为实施入口：

- `2026-08-13-maple-auto-hunting-product-spec.md`；
- `2026-08-13-input-compatibility-handoff-spec.md`；
- `docs/2026-08-13-maple-auto-hunting-product-spec.md`；
- `docs/2026-08-13-input-compatibility-handoff-spec.md`；
- `docs/SESSION_HANDOFF_2026-08-14.md`；
- `docs/superpowers/specs/2026-08-12-maple-visual-automation-design.md`。

当历史文档与本文冲突时，以本文为准；需要恢复历史背景时才打开对应文件。


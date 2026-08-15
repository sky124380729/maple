# Maple 自动化工作台

Maple 是面向授权测试客户端的 Windows 桌面观察与自动化架构。唯一产品规格入口是 [`docs/MAPLE_PROJECT_SPEC.md`](docs/MAPLE_PROJECT_SPEC.md)，Windows 接手摘要是 [`docs/WINDOWS_IMPLEMENTATION_HANDOFF_2026-08-14.md`](docs/WINDOWS_IMPLEMENTATION_HANDOFF_2026-08-14.md)。旧 WinForms、SendInput 探针、net48 静态测试和历史设计文档已经从工作树清除，需要追溯时使用 Git 历史。

## 架构

```text
Maple.exe（.NET 8 Windows Desktop x64）
├── WebView2 / React + Ant Design 工作台
├── WGC + Direct2D/Direct3D 原生 PreviewSurface
├── Capture / Vision / Map / Core / Replay
└── BrokerClient -> Maple.InputBroker.exe（按需管理员权限）
```

- React 只负责界面、配置和用户命令，不接收逐帧 base64 画面，也不能发送原始按键。
- 原生预览使用固定两槽位 latest-frame-wins 策略，目标为稳定 30 FPS、争取 60 FPS。
- Self、其他玩家、怪物分别使用绿色、青色、红色识别框；Self 不显示跟踪编号。
- 低置信度由程序自动校准并暂停动作，不要求用户点击角色或确认错误识别。
- 动作保持时长由距离、观测位移速度、攻击范围、地图拓扑、平台边界和反馈自动计算。
- Broker 不可用、窗口失焦、帧过期、地图未验证或 EmergencyStop 时必须阻止输入并执行 `ReleaseAll`。

## 当前状态

| 模块 | 状态 |
| --- | --- |
| React 工作台、模拟宿主、浏览器预览与页面测试 | `DONE (macOS)` |
| TypeScript/JSON 契约和 L2 规格级离线闭环 | `DONE (macOS)` |
| C# Core、Map、Replay、Vision、Cloud、Input | `DONE (macOS) / WINDOWS_PENDING` |
| .NET 8 Windows Host 源码与 Host 单测 | `SOURCE_READY / WINDOWS_PENDING` |
| WebView2 实际运行时、WGC、原生预览性能 | `WINDOWS_PENDING` |
| 生产 Broker、IPC、热键、客户端动作与异常释放 | `SOURCE_READY / WINDOWS_PENDING` |
| 真实 OpenCV/OCR/YOLO 模型准确率 | `WINDOWS_PENDING` |

macOS 已完成 React、共享契约、生产编排器、Replay、视觉适配器、百炼客户端和 Host 平台无关逻辑测试，并可交叉编译 win-x64 Host。它仍不能替代真实 Windows 上的 WGC/WebView2、30-60 FPS、管理员 Broker、DPAPI 和授权客户端验收。

## macOS 验证

```bash
DOTNET_ROOT=/tmp/maple-dotnet node tools/verify-portable.mjs
```

该命令执行 npm audit、ESLint、TypeScript、33 个 Vitest、2 个 Playwright、便携契约/闭环、全部 .NET 测试、Host Rebuild、XML 检查、密钥扫描和 `git diff --check`。

## Windows 交接验证

```powershell
node .\tools\verify-portable.mjs
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\build-react-ui.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\publish-windows.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\windows\production_input_contract.tests.ps1
```

### 前台输入诊断探针

`MapleInputProbe.exe` 只用于授权客户端的 Windows 前台输入兼容性诊断，不接入自动战斗闭环，也不替代生产 Broker 的验收。它只在用户勾选授权并点击开始后，各发送一次 500ms 左键和右键；目标失焦、最小化、权限不匹配或窗口身份异常时立即停止并释放全部按键。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\build-input-probe.ps1 -Configuration Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\windows\input_probe_contract.tests.ps1 -RequirePublished
```

可执行文件输出到 `artifacts\input-probe\MapleInputProbe.exe`。真实测试会请求管理员权限；无输入自检由构建脚本通过 DLL 入口执行，不会调用 `keybd_event`。

### 生产 Broker 实机验收

正常使用打开 `dist\windows-x64\Maple.exe`。只有采集生产输入证据时，才从 PowerShell 显式进入原生验收模式：

```powershell
& .\dist\windows-x64\Maple.exe --input-broker-evidence
```

该模式按固定顺序测试左、右、跳跃、上、下、单体攻击、拾取和全键释放。每项自动等待 3 秒、切回目标客户端、抓取客户区前后帧并执行 `ReleaseAll`，之后必须在审阅窗明确确认；异常时停止，不会自动继续。证据写入 `%LOCALAPPDATA%\Maple\input-broker-evidence`，最终校验命令为：

```powershell
$latest = Get-ChildItem "$env:LOCALAPPDATA\Maple\input-broker-evidence" -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\windows\input_broker_evidence.tests.ps1 -EvidenceRoot $latest.FullName -RequireEvidence
```

只有完成以下实机证据后，Windows 模块才能从 `WINDOWS_PENDING` 改为 `DONE`：

- WebView2 本地资源加载、页面刷新/崩溃恢复和命令白名单；
- WGC 优先、BitBlt 回退，以及 1280×720、1440×900 下的 P50/P95/P99 延迟和 30–60 FPS；
- 普通权限 Host 与管理员 Broker 的完整性级别、IPC 身份、协议版本和心跳全部通过；
- Left/Right/Up/Down/Jump/Attack/Pickup/Potion 的前台客户端视觉反馈全部通过；
- 失焦、进程退出、IPC 断开、心跳超时和 EmergencyStop 后零卡键、零自动恢复。

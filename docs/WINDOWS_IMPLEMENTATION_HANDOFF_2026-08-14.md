# Maple Windows 实施交接

日期：2026-08-14
用途：把当前 macOS 阶段交给真实 Windows 10/11 x64 设备继续开发
产品决策唯一来源：`docs/MAPLE_PROJECT_SPEC.md`

## 1. 一句话上下文

Maple 不是网页自动化，也不是 WinForms 原型续写。最终产品是 `.NET 8` Windows 桌面宿主：WebView2 只承载 React 工作台，WGC/Direct2D 原生层承载 30-60 FPS 预览，本地视觉产生观察快照，Core 经安全门计算短动作和按键时长，唯一的虚拟 HID 输入后必须等待新画面反馈再继续。任何低置信度由程序自动修复，Self 不公开跟踪编号，Player 永不成为攻击目标。

## 2. 当前仓库事实

- `ui/`：React 19 + Ant Design 6 中文工作台，可独立运行，输入始终禁用。
- `schemas/`：bridge、observation、action 已统一为 schemaVersion 2；Attack/UsePotion profile、Self 无跟踪编号和云端命令均有共享校验。
- `src/Maple.*`：Contracts/Core/Runtime/Replay/Cloud/Capture/Vision/Map/Input/Preview/Host 已是 SDK-style .NET 8 工程；Mac 可执行测试覆盖 Host 30、Runtime 37、Input 3、Map 2。
- `src/Maple.Host`：已有 `Program/MainWindow`、win-x64 可执行入口、CoreWebView2 本地资源映射、外部导航拦截、进程失败释放和 strict bridge router；只能在 Mac 交叉编译，不能在 Mac 宣称运行。
- Host EmergencyStop 已使用 schema v2 的 `pauseReason`；router 会递归拒绝未知命令、错误 schema、原始动作/按键/HID/frame/image/url 字段，并逐命令校验严格 payload；刷新/进程失败/非法命令/关闭统一进入 ReleaseAll 安全路径。
- `src/Maple.Cloud/BailianMapHttpClient.cs`：真实百炼视觉地图标注客户端已完成离线协议测试，固定 endpoint/模型，要求上传同意，限制 1-4 张受限图片，验证响应 schema 和来源 frameId；Windows 只需把已编码 WGC 关键帧实现为 `IMapImageSource`。
- `src/Maple.Capture/WindowsGraphicsCaptureBackend.cs`：已支持 WGC source -> 明确 BitBlt source fallback；没有 source 时返回 `WGC_RUNTIME_NOT_BOUND`，不静默伪造画面。
- `src/Maple.Capture/WgcFramePool.cs` 与 `src/Maple.Host/WindowsGraphicsCaptureSession.cs`：已建立固定两槽、尺寸变化诊断、取消/错误释放和 latest-frame 所有权边界；真实 Windows Graphics Capture API adapter 仍待 Windows 绑定与实机验证。
- `src/Maple.Preview/NativePreviewSurface.cs`：GDI+ 原生预览和 Self/Player/Monster 颜色框源码已可交叉编译；不是已测的 Direct2D/Direct3D 30-60 FPS 实现。
- `src/Maple.Input/WindowsVirtualHidAdapter.cs`：只有传输/编码器接口；没有设备路径、VID/PID、报告描述符或驱动协议。
- 旧 `dist/`、WinForms 原型、SendInput 探针、net48 静态测试和历史设计文档已从工作树清除；需要追溯时只查看 Git 历史，不得恢复到生产路径。

## 3. 需求审查结论

主规格 v1.2 评审为 94/100，可作为 Windows 开发入口。

| 维度 | 得分 | 结论 |
| --- | ---: | --- |
| 内容完整性 | 18/20 | 架构、工作流、安全、发布和验收完整；真实设备/数据资产仍未知 |
| 逻辑一致性 | 20/20 | 宿主目标、动作词汇、profile、地图校准循环和 fail-closed 边界已在代码/测试统一 |
| 清晰无歧义 | 14/15 | 百分比单位、窗口多实例、恢复策略已固定 |
| 可测试性 | 14/15 | 五级闭环、视觉超时和模型哈希已量化；Windows/L5 证据为空 |
| 用户场景覆盖 | 9/10 | 首次启动、日常运行、异常恢复已覆盖 |
| 边界与异常处理 | 10/10 | 默认 fail-closed，EmergencyStop/ReleaseAll 明确 |
| 可追溯性 | 8/10 | 12 Task、状态矩阵和测试文件存在；尚无正式需求 ID 到测试 ID 映射 |

已修复的关键模糊点：正式宿主统一为 `net8.0-windows` 自包含 x64；React 不提交动作；动作 v2 使用 `Attack/UsePotion + profileId`；UI 百分比 `0..100` 在 Host 转为 Core `0..1`；多窗口、低置信度恢复和 candidate 地图校准例外均已明确。

| 级别 | 原模糊点 | 主规格 v1.1 的确定结论 |
| --- | --- | --- |
| P0 | “自包含 exe”与旧 net48 类库冲突 | `net8.0-windows`、win-x64、SelfContained 目标；旧工程已从工作树清除 |
| P0 | `Attack/UsePotion` 与 `Single/Area/HP/MP` 两套枚举冲突 | v2 只保留语义动作，并用必需 `profileId` 指定技能/药水档案 |
| P0 | React 发送 `35`，Core 按 `0.35` 比较 | UI/bridge 为 0..100，Host 单点归一化，Core 为 0..1 |
| P1 | candidate 禁止动作但地图验证又要求短动作 | 仅 `MapCalibrating` 可执行不超过 300ms 的有限校准动作 |
| P1 | 低置信度自动修复与“恢复需用户确认”冲突 | CalibrationRequired/短暂 StaleFrame 可自动重新 arm，其他安全中断需用户恢复 |
| P1 | 源码存在被写成“合同完成” | 使用 DONE(macOS)/SOURCE_READY/WINDOWS_PENDING/MODEL_PENDING 和 L1-L5 证据定义 |

仍需外部事实才能关闭的 P0：真实授权客户端的进程路径/版本特征；模型训练与发布数据集；虚拟 HID 的设备接口、VID/PID、报告描述符、签名和安装方式。这些内容不能猜。

## 4. 本轮验证证据

2026-08-14 在 macOS 执行：

- `npm ci`、`npm audit --audit-level=high`：依赖安装成功，0 vulnerability。
- ESLint：PASS，0 warning。
- TypeScript：PASS，0 error。
- Vitest：33/33 PASS，6 个测试文件。
- Vite production build：PASS；JS 795.32 kB，gzip 251.47 kB，有非阻塞的单 chunk 警告；中文字体已打包。
- Playwright：桌面 1440x900、移动 390x844，共 2/2 PASS。
- `tests/portable-contracts.mjs`：PASS；Windows Native/HID 明确为 PENDING。
- `tests/closed-loop/portable-closed-loop.mjs`：L2 规格级闭环 PASS；生产 C# Replay 闭环另由 Runtime.Tests 覆盖。
- `dotnet test Maple.sln -p:EnableWindowsTargeting=true`：Host 30/30、Runtime 37/37、Input 3/3、Map 2/2 PASS。
- `dotnet build src/Maple.Host/Maple.Host.csproj -p:EnableWindowsTargeting=true -t:Rebuild`：win-x64 Host 交叉编译 PASS，0 warning / 0 error；这不是 Windows 运行证据。
- Vision 测试覆盖 7 个管线行为：帧所有权、并行 provider、stale frame、动态超时/critical HP、manifest SHA-256、Self/Player/Monster 后处理和 OpenCV/OCR 像素通路。
- `data-testid`、疑似硬编码密钥：未发现。
- `git diff --check`：PASS。

结论：当前可声明 Mac 可移植构建、离线视觉/Replay/C# 编排器和 UI L1-L3 替身测试通过；不能声明 Windows WGC/WebView2 运行、真实 30-60 FPS、真实模型准确率或虚拟 HID L4/L5 通过。

## 5. Windows 接手顺序

1. 在真实 Windows 上 restore/build/publish `Maple.Host`，确认 WebView2 Evergreen Runtime 最低版本、本地 `ui/index.html` 映射、DPI awareness 和普通权限启动。
2. 在 Host 成功启动后、采集任何真实输入证据前，运行独立的 `keybd_event` 诊断探针（diagnostic-only）验证获得授权且处于前台的客户端；该里程碑不是生产自动化，不改变生产输入仅限已验收虚拟 HID 的边界，也不能作为 L4/L5 证据。
3. 注入真实 `GraphicsCaptureItem`/D3D11 adapter，接入 `WindowsGraphicsCaptureSession` 的两槽 pool；把地图扫描关键帧编码后绑定到 `IMapImageSource`；用 WGC 不可用/失焦/窗口尺寸变化场景验证 BitBlt 只按明确诊断回退。
4. 将 `IWgcFrameEncoder` 绑定到 GPU readback/Direct2D PreviewSurface，使用 1280x720、1440x900、100/125/150% DPI 矩阵记录 capture/render FPS、P50/P95/P99 延迟、队列年龄、丢帧和内存。
5. 将真实模型 manifest/权重放入发布目录，跑 Self/Player/Monster、遮挡、特效和多分辨率离线数据集；达到主规格 precision/recall 和 stale 门槛前保持观察模式。
6. 用户提供真实 HID 设备路径、VID/PID、报告描述符、签名和协议后，实现 `IVirtualHidTransport`/`IVirtualHidReportEncoder`，分别生成设备层、Windows 输入层、授权客户端画面响应层三份 PASS 证据。
7. 在已验收 HID 上执行 MapScanning/MapCalibrating 的不超过 300ms 校准动作；随后做完整反馈闭环、失焦/崩溃/设备断连/EmergencyStop 和 ReleaseAll 验收。
8. 完成安装、普通权限运行、DPAPI 实机读写、凭据/日志脱敏、资源哈希、30 分钟与 4/8 小时 soak，最后才开放生产动作。

每一步必须在主规格状态表中逐项更新证据。不要一次把多个 `WINDOWS_PENDING` 改成 `DONE`。

## 6. 首次 Windows 验证命令

```powershell
Push-Location .\ui
npm ci
npx playwright install chromium
Pop-Location
node .\tools\verify-portable.mjs
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\build-react-ui.ps1
dotnet publish .\src\Maple.Host\Maple.Host.csproj -c Release -r win-x64 --self-contained true
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\windows\hid_contract.tests.ps1 -RequireEvidence
git diff --check
```

`verify-portable` 已包含 restore/build/test 和 Host 交叉重编译；Windows AI 仍必须执行真实 publish、启动、WGC/WebView2、模型、HID 和 L4-L5 验证。当前 Mac 没有 `pwsh`，因此 HID 证据脚本只能在 Windows 执行。

## 7. 不可改变的产品决策

- Self 绿色、Player 青色、Monster 红色；Self 不显示跟踪编号。
- Player 永不进入攻击目标；loot 不在预览画框。
- 低置信度由程序自动重试和诊断，不要求用户确认识别结果。
- 移动和攻击按下时长由距离、速度、边界、冷却和最新画面反馈动态计算。
- React 不接收逐帧 base64，不发送原始按键；预览与 EmergencyStop 不依赖 React 存活。
- 未验证地图、过期帧、失焦、未知弹窗、HP/MP 冲突、设备异常一律 fail-closed。
- 生产输入只能来自已验收虚拟 HID；`SendInput`、`PostMessage` 和旧探针禁止进入生产闭环。

## 8. 文档使用规则

后续 AI 开发时先且只以 `docs/MAPLE_PROJECT_SPEC.md` 决定产品行为，再用本文了解当前工作区和接手顺序，用 `docs/maple-runtime/VERIFICATION_2026-08-14.md` 查看最近证据。其他历史资料已从工作树清除，只能通过 Git 历史追溯。

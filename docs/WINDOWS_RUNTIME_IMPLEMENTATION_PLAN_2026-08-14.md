# Maple Windows Runtime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在真实 Windows 11 x64 上把当前 `SOURCE_READY` 的宿主推进为可发布、可启动、可绑定目标窗口、可安全观察的原生程序，并为后续 WGC、真实模型和 HID 实机验收建立可信基线。

**Architecture:** 保持 `.NET 8 Windows Host + WebView2 React + Native Preview` 边界。第一阶段只接通 Windows 工具链、发布资产、窗口身份、WebView2、DPAPI 和明确诊断的 BitBlt 观察回退；生产输入继续使用 `NullInputAdapter`，WGC/HID/真实模型仍以独立证据解锁。

**Tech Stack:** .NET SDK 8.0.424、WinForms/WebView2、React/Vite/Playwright、xUnit、Node built-in test、PowerShell。

---

### Task 1: 修复 Windows 便携验证入口

**Files:**
- Create: `tools/portable-process.mjs`
- Create: `tests/tools/portable-process.test.mjs`
- Modify: `tools/verify-portable.mjs`

- [x] **Step 1: 写失败测试**

用 Node built-in test 验证 Windows 的 `.cmd` 命令经 `ComSpec /d /s /c` 执行，而原生可执行文件保持直接启动；验证参数引用不会丢失。

- [x] **Step 2: 验证 RED**

Run: `node --test tests/tools/portable-process.test.mjs`

Expected: FAIL，因为 `tools/portable-process.mjs` 尚不存在。

- [x] **Step 3: 实现最小跨平台启动器**

导出 `resolvePortableCommand(command, args, platform, comSpec)` 和 `run(command, args, cwd)`。Windows `.cmd/.bat` 使用 `cmd.exe /d /s /c`，其他命令保持 `spawnSync(command, args)`；失败时保留退出码和原始错误。

- [x] **Step 4: 接入总验证并验证 GREEN**

`verify-portable.mjs` 改用统一启动器，并把 `node --test tests/tools/portable-process.test.mjs` 纳入验证。运行单测后再运行 `node tools/verify-portable.mjs`。

### Task 2: 生成真正可启动的 Windows 发布包

**Files:**
- Create: `global.json`
- Create: `tools/publish-windows.ps1`
- Create: `tests/windows/publish_contract.tests.ps1`
- Modify: `src/Maple.Host/Maple.Host.csproj`

- [x] **Step 1: 写失败的发布契约测试**

测试发布目录必须包含 `Maple.exe`、`Maple.runtimeconfig.json`、`WebView2Loader.dll`、`ui/index.html` 和至少一个 `ui/assets/*.js`。当前发布目录因缺少 `ui/` 应失败。

- [x] **Step 2: 验证 RED**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File tests/windows/publish_contract.tests.ps1 -PublishDirectory src/Maple.Host/bin/Release/net8.0-windows10.0.19041.0/win-x64/publish`

Expected: FAIL，错误为 `ui/index.html` 缺失。

- [x] **Step 3: 实现发布资产与脚本**

固定 SDK `8.0.424`；Host 项目把 `ui/dist/**/*` 映射为发布目录的 `ui/**/*`；发布脚本先执行 React lint/typecheck/test/build，再执行 `dotnet publish -c Release -r win-x64 --self-contained true`，最后运行发布契约。

- [x] **Step 4: 验证 GREEN 与启动存活**

运行发布脚本；启动 `Maple.exe` 后轮询主窗口最多 10 秒，验证进程不因资产目录异常退出，再正常关闭进程。

### Task 3: Windows 目标窗口身份绑定

**Files:**
- Create: `src/Maple.Host/WindowIdentity.cs`
- Create: `src/Maple.Host/WindowsTargetWindowLocator.cs`
- Create: `src/Maple.Host.Tests/WindowsTargetWindowLocatorTests.cs`
- Modify: `src/Maple.Host.Tests/Maple.Host.Tests.csproj`

- [ ] **Step 1: 写失败测试**

通过可替换的 `IWindowSystem` 验证：无窗口返回 `TARGET_NOT_FOUND`；唯一合格窗口自动绑定；多个窗口返回 `TARGET_SELECTION_REQUIRED`；窗口最小化、标题错误、客户区过小被拒绝；身份包含 HWND、PID、进程启动时间、路径哈希、客户区、DPI、前台状态。

- [ ] **Step 2: 验证 RED**

Run: `dotnet test src/Maple.Host.Tests/Maple.Host.Tests.csproj --filter WindowsTargetWindowLocatorTests`

Expected: FAIL，因为 locator 类型不存在。

- [ ] **Step 3: 实现 Win32 发现与不可变身份**

使用 `EnumWindows/GetWindowText/GetClassName/GetClientRect/ClientToScreen/GetDpiForWindow/IsIconic/GetForegroundWindow`；进程路径和启动时间来自 `Process`，路径哈希使用 SHA-256。不得激活、置顶或向窗口发送消息。

- [ ] **Step 4: 验证 GREEN**

运行筛选测试和全部 Host tests；对当前授权客户端只做只读发现，记录诊断结果，不发送输入。

### Task 4: 编译并接通 BitBlt 安全观察回退

**Files:**
- Create: `src/Maple.Capture/WindowsBitBltFrameSource.cs`
- Create: `src/Maple.Host/CaptureCoordinator.cs`
- Create: `src/Maple.Host.Tests/CaptureCoordinatorTests.cs`
- Modify: `src/Maple.Capture/Maple.Capture.csproj`
- Modify: `src/Maple.Host/WebViewHostForm.cs`
- Modify: `src/Maple.Host/HostCompositionRoot.cs`

- [ ] **Step 1: 写失败测试**

测试 coordinator 只在唯一目标、前台、非最小化、客户区稳定时采集；失焦/尺寸变化/黑帧立即暂停并释放；latest-frame 发布到预览 sink；任何异常保持 `NullInputAdapter`。

- [ ] **Step 2: 验证 RED**

Run: `dotnet test src/Maple.Host.Tests/Maple.Host.Tests.csproj --filter CaptureCoordinatorTests`

Expected: FAIL，因为 coordinator 尚不存在。

- [ ] **Step 3: 实现 Windows-only 编译和 BGRA32 帧源**

`Maple.Capture` 多目标构建，在 Windows TFM 编译 Windows 源；BitBlt 只复制客户区到池化 BGRA32 `CapturedFrame`，记录捕获时长并检测黑帧。Host 以 30 FPS 定时器拉取 latest frame，GDI 预览仅作第一阶段兼容回退。

- [ ] **Step 4: 验证 GREEN 与客户端观察测试**

运行 Host tests、发布包，再对当前客户端执行只读客户区预览；验证失焦和最小化均停止采集且无输入调用。

### Task 5: WebView2 与 DPAPI Windows 实机证据

**Files:**
- Create: `src/Maple.Host/WebView2EnvironmentProbe.cs`
- Create: `src/Maple.Host.Tests/WebView2EnvironmentProbeTests.cs`
- Create: `src/Maple.Runtime.Tests/Cloud/WindowsDpapiCredentialStoreTests.cs`
- Create: `tests/windows/windows_runtime_smoke.tests.ps1`
- Modify: `src/Maple.Host/WebView2Runtime.cs`
- Modify: `docs/maple-runtime/VERIFICATION_2026-08-14.md`

- [ ] **Step 1: 写失败测试**

WebView2 probe 对运行时缺失/版本过低返回明确状态；DPAPI Windows 测试在临时目录完成 set/lease/clear，确认密文不含明文且错误用户范围不可伪造。

- [ ] **Step 2: 验证 RED**

运行筛选测试，确认 probe 类型缺失和 Windows DPAPI 证据缺失。

- [ ] **Step 3: 实现 probe 和烟雾证据脚本**

Host 启动先查询 Evergreen 版本，再加载本地 `maple.local`；烟雾脚本输出 OS build、DPI、WebView2 版本、目标绑定、发布资产、DPAPI 和输入适配器状态，严禁把 WGC/HID 标记为 PASS。

- [ ] **Step 4: 完整验证并更新证据**

运行 `node tools/verify-portable.mjs`、Windows 发布契约、runtime smoke、`git diff --check`。只把有本机输出支撑的条目写入验证文档。

### 后续独立阶段（不在本计划伪造完成）

- WGC：真实 `GraphicsCaptureItem + D3D11` readback、Direct2D/Direct3D 预览和 30-60 FPS/DPI/soak 矩阵。
- 地图帧源：WGC 关键帧编码并绑定 `IMapImageSource`，再验百炼地图标定。
- 真实模型：用户提供 manifest/权重/数据集后执行 precision/recall 与 GPU provider 验收。
- 虚拟 HID：只有拿到本项目设备接口、VID/PID、报告描述符、签名和协议后才实现 transport/encoder，并运行 `tests/windows/hid_contract.tests.ps1 -RequireEvidence`；不得借用或逆向第三方 `gvinput` 私有接口。

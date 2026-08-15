# Maple 运行时验证记录

日期：2026-08-14
初始环境：macOS arm64，局部 .NET SDK `/tmp/maple-dotnet` 8.0.424，Node/Vite/Vitest/Playwright。
Windows 补验环境：Windows x64 build 26200，.NET SDK 8.0.424，WebView2 Evergreen Runtime 151.0.4129.78。

## 已通过

| 范围 | 证据 | 结果 |
| --- | --- | --- |
| React 类型与规范 | `cd ui && npm run typecheck && npm run lint` | PASS |
| 前端依赖审计 | `cd ui && npm audit --audit-level=high` | 0 vulnerability |
| React 单测 | `cd ui && npm test -- --run` | 6 个文件、33/33 PASS |
| React 生产构建 | `cd ui && npm run build` | PASS；存在非阻塞 chunk 大小提示 |
| 浏览器回归 | `cd ui && npm run e2e` | desktop 1440x900、mobile 390x844，2/2 PASS |
| .NET solution 测试 | `dotnet test Maple.sln -p:EnableWindowsTargeting=true` | Host 30/30、Runtime 37/37、Input 3/3、Map 2/2 PASS |
| 视觉管线 | Runtime.Tests `VisionPipelineTests` | 9 个相关行为 PASS（含 OpenCV/OCR/ONNX adapter） |
| 便携契约 | `node tests/portable-contracts.mjs` | PASS；Windows Native/HID PENDING |
| 便携闭环 oracle | `node tests/closed-loop/portable-closed-loop.mjs` | PASS；生产真实输入 NOT VERIFIED |
| Windows Host 交叉编译 | `dotnet build src/Maple.Host/Maple.Host.csproj -p:EnableWindowsTargeting=true -t:Rebuild` | win-x64 编译 PASS，0 warning / 0 error |
| 密钥边界 | 百炼测试、portable 密钥扫描 | 未发现真实密钥；API Key 不进入状态/日志 |

## Windows 实机补验

| 范围 | 证据 | 结果 |
| --- | --- | --- |
| Windows 自包含发布 | `tools/publish-windows.ps1 -SkipE2E`、`tests/windows/publish_contract.tests.ps1` | PASS；`Maple.exe`、自包含运行时、WebView2Loader 和 React 静态资源齐全 |
| Windows 全量便携门禁 | `node tools/verify-portable.mjs` | PASS；React 33、Playwright 2、Host 81、Runtime 38、Input 30、Map 2，Host Rebuild 0 warning / 0 error |
| WebView2 环境与宿主启动 | `tests/windows/windows_runtime_smoke.tests.ps1` | PASS；Evergreen 151.0.4129.78 高于应用兼容线 109.0.1518.78；发布版工作台响应并正常关闭 |
| WGC 系统链路自检 | `Maple.exe --wgc-self-test` | PASS；真实 `GraphicsCaptureItem + D3D11` 捕获并裁剪 640×360 客户区，BGRA32 非黑帧；最近一次单帧 readback 约 10.17ms；授权客户端帧仍待窗口前台补验 |
| WGC 地图关键帧源 | Host.Tests `MapScanFrameStoreTests` / `CaptureCoordinatorTests` / `HostCommandDispatcherTests`、发布版 WGC 自检 | 15/15 PASS；扫描会话隔离、32 张有界内存缓存、缺帧/跨地图拒绝、捕获所有权、百炼服务绑定通过；真实 Windows PNG 为 640×360、2039 bytes；真实地图覆盖率和百炼联网响应仍待验 |
| 目标窗口身份 | `Maple.exe --windows-diagnostics` | PASS；唯一 `UnityWndClass` 客户端自动绑定，记录 PID、启动时间、版本、DPI 与路径 SHA-256；当次窗口为最小化/失焦 |
| DPAPI 当前用户存储 | Runtime.Tests `WindowsDpapiCredentialStoreTests` | 1/1 PASS；临时目录完成写入、密文检查、读取、替换、清除 |
| 安全观察回退 | Host.Tests `CaptureCoordinatorTests`、Host 启动烟雾 | 10/10 PASS；缺失/失焦/最小化/黑帧 fail-closed，窗口身份和客户区尺寸/DPI 跨帧锁定，地图观察者异常停止预览并释放帧，销毁时释放捕获后端；真实客户区帧仍待窗口前台补验 |
| 客户端只读捕获证据入口 | Host.Tests `TargetCaptureEvidenceRunnerTests`、`tests/windows/target_capture_evidence.tests.ps1` | runner 3/3 PASS；真实客户端当次为已恢复但非前台，1366×768，正确返回 `TARGET_NOT_FOREGROUND`、0 帧、输入禁用；前台连续帧仍待补验 |
| 捕获卡顿修复 | Host.Tests `CapturePollingPolicyTests` / `FrameSlotTests` / `WgcReadbackResourcePolicyTests`、Windows 进程采样 | PASS（实现/失焦实测）；顶层窗口元数据只在标题/类名命中后读取，失焦轮询降到 1 FPS，旧工作台单核 100% 降至约 0.2%；覆盖 Bitmap 立即释放，WGC staging texture 按尺寸/格式复用。游戏前台主动捕获帧率和长时工作集仍待补验 |
| VHF 驱动构建 | `tests/windows/hid_driver_build.tests.ps1` | PASS；WDK 10.0.28000 x64、KMDF/VHF、INF 隔离/Inf2Cat `Errors: None / Warnings: None`，SYS/CAT 同证书签名，45-byte Boot Keyboard 描述符 SHA-256 `d0adc4c8754c228f1ed84f6d294b17df6e10fc13b684b7807325189b0b3b510e` |
| HID 预安装自检 | `Maple.exe --hid-device-self-test dist/hid-device-self-test-preinstall.json` | 预期门禁 PASS；返回 `HID_DEVICE_NOT_INSTALLED`，未误认系统第三方 HID，也未发送方向键；测试签名安装和三层证据待重启 |
| 输入状态 | `windows-runtime-diagnostic.json` | 默认仍为 `NullInputAdapter` / `INPUT_INJECTION=DISABLED`；HID 三层证据仍未提供 |

## 已实现但不能宣称实机通过

- `Maple.Host` 已有 .NET 8 win-x64 入口、WebView2 本地虚拟域映射、禁止外部导航、关闭生产 DevTools、进程崩溃事件和原生紧急停止；`HostSafetyCoordinator` 已离线验证刷新/失败/关闭的统一 ReleaseAll 语义。
- `Maple.Capture` 已有 `CapturedFrame` 所有权、Replay backend、WGC/BitBlt source boundary 和固定两槽 `WgcFramePool`。
- `Maple.Vision` 已有 OpenCvSharp HUD 像素识别、OCR 预处理/Tesseract adapter、ONNX Runtime adapter、manifest 类别/哈希校验、Self 唯一后处理和超时 fail-closed。
- `HostCommandDispatcher` 已把百炼凭据、固定模型、连接测试和地图标注意图放在 native 侧；`BailianMapHttpClient` 已离线验证真实兼容接口请求、上传同意、图片边界、401/超时/重试、响应 schema 和 frameId 来源一致性。Windows store 使用 DPAPI，Mac 调用会明确抛平台不支持。

## 必须留给 Windows

1. WebView2 刷新/进程失败的 Windows 故障注入与 ReleaseAll 实机证据；Evergreen 检测、本地宿主启动和窗口关闭已补验。
2. 授权客户端 WGC 权限/黑帧/尺寸变化与 BitBlt fallback 实测；系统级 `GraphicsCaptureItem + D3D11` readback 自检已通过。
3. 1280x720、1440x900、DPI 100/125/150% 的 capture/render 30-60 FPS、P50/P95/P99 延迟、队列年龄和内存/稳定性矩阵。
4. 真实 ONNX 权重和数据集的 Self/Player/Monster precision/recall、位置误差、遮挡和 stale 率。
5. 项目 VHF 驱动已完成 WDK 构建、VID/PID/报告描述符和签名包；仍需启用测试签名并重启，完成设备路径、设备层/Windows 输入层/授权客户端三层 PASS。
6. Windows DPAPI 升级/回滚兼容证据；当前用户写入、读取、替换和清除已补验。
7. 把 WGC 地图关键帧编码并绑定到 `IMapImageSource`；未绑定时 Host 明确返回 `MAP_FRAME_SOURCE_UNAVAILABLE`，不得伪造云端地图候选。

## 当前风险

- Vite bundle 约 795 KB（gzip 251 KB），不影响当前启动，但应在 Host 接线后评估首屏加载。
- OpenCvSharp analyzer 版本高于局部 Roslyn 时已在项目中抑制已知 `CS9057`；运行时 native OpenCV 包和 GPU provider 仍需 Windows 发布矩阵验证。
- WGC API 通过 `IWgcRuntimeAdapter` 注入，避免 Mac 直接引用 `.winmd`；Windows AI 必须绑定系统 API，不得把 fake adapter 当成采集完成。
- `Maple.Input.WindowsVirtualHidAdapter` 仍在 `HID_CONTRACT_UNVERIFIED` 时拒绝生产发送；项目自有 VHF 设备身份已固定，但测试签名安装和三层实机证据完成前不得切换默认适配器。
- 百炼地图客户端源码与协议测试已完成，但生产 Host 尚无 WGC `IMapImageSource`；这属于 Windows 帧源绑定，不是百炼协议未实现。

### 2026-08-15 extended-scan-code probe

- Evidence session: `%LOCALAPPDATA%\Maple\input-probe\20260815-122248-925`
- Left: `VK=37`, `scanCode=75 (0x4B)`, `flagsDown=1`, `flagsUp=3`, foreground confirmed, all keys released; avatar moved left from about x=318 to x=194.
- Right: `VK=39`, `scanCode=77 (0x4D)`, `flagsDown=1`, `flagsUp=3`, foreground confirmed, all keys released; avatar moved right from about x=194 to x=318.
- Classification: `CLIENT_MOVEMENT_CONFIRMED` for left/right only.
- Not established: jump, climb, attack, pickup, potion, production Host integration, or soak stability.

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
| 便携契约 | `node tests/portable-contracts.mjs` | PASS；Windows Native/Production Input PENDING |
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
| 已废弃 VHF 实验 | 历史 WDK 构建记录 | 当时构建通过，但该实现未取得客户端三层证据；其源码、驱动、安装工具和验收脚本已于 2026-08-15 从当前产品路径删除，不可作为当前 PASS |
| 输入状态 | `windows-runtime-diagnostic.json` | Host 当前组合 `BrokerInputAdapter`，默认 `BROKER_NOT_ARMED`；生产 Broker 的客户端矩阵和异常释放证据仍待完成 |

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
5. 旧 VHF 试验已废弃并从产品路径移除；后续只验收普通权限 Host + 管理员 Broker 的发布、IPC、前台动作和异常释放。
6. Windows DPAPI 升级/回滚兼容证据；当前用户写入、读取、替换和清除已补验。
7. 把 WGC 地图关键帧编码并绑定到 `IMapImageSource`；未绑定时 Host 明确返回 `MAP_FRAME_SOURCE_UNAVAILABLE`，不得伪造云端地图候选。

## 当前风险

- Vite bundle 约 795 KB（gzip 251 KB），不影响当前启动，但应在 Host 接线后评估首屏加载。
- OpenCvSharp analyzer 版本高于局部 Roslyn 时已在项目中抑制已知 `CS9057`；运行时 native OpenCV 包和 GPU provider 仍需 Windows 发布矩阵验证。
- WGC API 通过 `IWgcRuntimeAdapter` 注入，避免 Mac 直接引用 `.winmd`；Windows AI 必须绑定系统 API，不得把 fake adapter 当成采集完成。
- 生产输入只允许经 `BrokerInputAdapter`/`Maple.InputBroker.exe` 的抽象动作协议；旧 VHF 类型和测试签名工具已删除。
- 百炼地图客户端源码与协议测试已完成，但生产 Host 尚无 WGC `IMapImageSource`；这属于 Windows 帧源绑定，不是百炼协议未实现。

### 2026-08-15 extended-scan-code probe

- Evidence session: `%LOCALAPPDATA%\Maple\input-probe\20260815-122248-925`
- Left: `VK=37`, `scanCode=75 (0x4B)`, `flagsDown=1`, `flagsUp=3`, foreground confirmed, all keys released; avatar moved left from about x=318 to x=194.
- Right: `VK=39`, `scanCode=77 (0x4D)`, `flagsDown=1`, `flagsUp=3`, foreground confirmed, all keys released; avatar moved right from about x=194 to x=318.
- Classification: `CLIENT_MOVEMENT_CONFIRMED` for left/right only.
- Not established: jump, climb, attack, pickup, potion, production Host integration, or soak stability.

### 2026-08-16 Windows offline completion pass

This pass deliberately did not start, activate, capture, or send input to the Maple client. The user requested that client testing wait until the implementation is ready for a supervised acceptance run.

- React lint/typecheck/build: PASS. Vitest 9 files, 54/54 PASS. The workbench now renders live HP/MP, combat strategy and thresholds, editable logical key bindings, map scan/calibration status, native overlay/telemetry state, and model/input safety status without hard-coded map readiness.
- .NET solution tests excluding the sandbox-only current-user DPAPI case: Runtime 64, Input 51, Map 4, Host 160, InputBroker 16; 295/295 PASS. The DPAPI implementation was not changed and still requires its separate real-user-profile evidence.
- Portable contracts and closed-loop specification oracle: PASS. They continue to report Windows native and production-input evidence as PENDING.
- Host rebuild: PASS, 0 errors. NU1900 warnings only reflect unavailable NuGet vulnerability metadata during the restricted run.
- Production input source contract: PASS. It proves the normal-integrity Host / elevated Broker boundary and abstract-action protocol, not client response.
- Windows self-contained publish and publish contract: PASS. `dist/windows-x64` contains `Maple.exe`, `Maple.InputBroker.exe`, WebView2/runtime assets, and the current React build; no driver or bundled ONNX file is present.
- External ONNX inspection through the published `Maple.exe`: PASS. SHA-256 `06c933f9290c5683af26b110ff8c1ba40a4b023de2f3dea07b401def8879310a`, AGPL-3.0 metadata, input `1x3x320x320`, output `1x10x2100`, supported `yoloChannelsFirst`, classes `character/environment/item/mob/npc/ui`. `ModelReady=true`; `CanDriveActions=false` is expected before multi-frame Self resolution and does not count as client accuracy evidence.
- Published-model bootstrap without opening the workbench: PASS. The release-local, hash-pinned `model-manifest.json` was resolved by `Maple.exe --vision-bootstrap-diagnostics`; result was `ready=true`, model `kaelo-maple-yolo`, provider `cpu`, diagnostic `OK`. The external AGPL weight remains outside Git and outside the distribution.
- Windows fixed-UI OCR bootstrap without opening the workbench: PASS. Production composition now creates `Windows.Media.Ocr` with simplified-Chinese preference and feeds it into `AdaptiveFixedUiVisionProvider`; the published diagnostic reported `ocrReady=true`, `ocrProvider=windowsMediaOcr`. This proves runtime availability only. Client map-name accuracy, ROI fit and HP/MP error remain part of supervised acceptance.
- Windows publish reproducibility: PASS. Both React build and portable verification use the repository-local `.cache/npm`; the publish path preserves `ui/package-lock.json` and uses the already-restored .NET assets with `--no-restore`, avoiding dependency on inaccessible user-global npm/NuGet configuration.
- `node tools/verify-portable.mjs` now uses a repository-local npm cache and a Windows-safe lock-file-preserving install path. Its lint, typecheck, unit-test, audit and build stages passed. Playwright browser processes could not launch in the managed sandbox and then hung without executing a page, so E2E was terminated and remains an environment-limited item for the supervised run.
- `git diff --check`: PASS. Generated model inspection reports and npm caches are ignored and are not release inputs.

Remaining acceptance boundary: supervised client WGC/overlay accuracy, unique Self and Monster tracking, HP/MP accuracy, map calibration, production Broker actions and ReleaseAll matrix, F9/F12, and 30-minute/4-hour/8-hour stability. None is marked PASS by this offline run.

### 2026-08-16 production Broker acceptance UI

This pass did not activate or send input to the already-open Maple client. It completed the product-side path needed for the next supervised client run.

- Confirmed editable defaults: arrows for movement, `Alt` jump, `Ctrl` single/area attack, `Z` pickup, `Delete` HP potion and `End` MP potion. Exclusive-key conflicts remain rejected while the confirmed shared `Ctrl` attack binding is allowed.
- Added the native-only `input.test` command. React may submit only one of nine closed abstract intents plus a bounded 50-600 ms duration; raw key, VK, scan-code, HID and report fields remain rejected recursively.
- Added a serialized `InputAcceptanceController`: it performs the existing three-second foreground arm, runs one typed action through `BrokerActionExecutor`, then always calls `ReleaseAll` and pauses. Concurrent tests are rejected and failures are returned as `input.result`.
- Added a Chinese input diagnostics matrix to the workbench for left/right/up/down/jump/attack/pickup/HP potion/MP potion. The latest result is visible in the same control panel.
- Added stable visual minimap fingerprints as a map-identity fallback. A fingerprint is only a candidate lookup key and never bypasses local map validation.
- Added controlled-motion Self confirmation. A unique provisional character may become action-capable only after motion consistent with a bounded calibration action; ambiguous or opposite motion remains fail-closed.
- Focused verification: Host routing/controller/dispatcher 45/45 PASS; React bridge/controls 29/29 PASS.
- Full verification: `node tools/verify-portable.mjs` PASS; React 56/56, Playwright 6/6, Host 172/172, Runtime 78/78, Input 53/53, InputBroker 16/16 and Map 4/4; Host rebuild 0 warning / 0 error.
- Production input source contract: PASS with `WINDOWS_EVIDENCE=PENDING` as required. Windows self-contained publish and publish contract: PASS at `dist/windows-x64`.

Remaining input boundary: the user-supervised run must accept the Broker UAC prompt and visually confirm all nine client actions plus F9/F12 and key release. These are not marked PASS by source tests or publishing.

### 2026-08-16 Windows client read-only acceptance

This pass used the already-open, authorized Maple client on the input desktop. It captured pixels and ran the production OpenCV/OCR/ONNX pipeline. It did not arm the broker and did not send any game input because the final action-safety gates were not satisfied.

- Target binding: PASS. The unique `UnityWndClass` client was bound as PID 5924 with a 2049x1152 client area at 96 DPI.
- Client capture: PASS with fallback. 30/30 foreground frames were captured through the production `WindowsGraphicsCaptureBackend`; WGC did not produce a frame in this desktop context and the explicit BitBlt fallback achieved 28.77 effective FPS, P50 11.64 ms and P95 15.77 ms. The WGC shutdown deadlock was fixed by making a closing FrameArrived callback discard its frame instead of blocking on the lifecycle lock.
- Live vision: PASS for observation, not for actions. Five live frames produced a `Ready` observation in 576 ms total with `Self` present, three monsters, HP 98.1% and MP 97.5%. The display-only character threshold is 0.25 while the action threshold remains 0.60 and Core retains its higher safety thresholds; final `CanDriveActions=false` is therefore expected.
- HUD correction: resource percentage now measures horizontal colored extent instead of colored pixel area. Full 50/50 HP and 5/5 MP no longer read near 45%; the live result is within about 2.5 percentage points.
- Map OCR: NOT PASS. Three-times upscaled Windows OCR returned non-CJK garbage for this stylized mini-map text. The runtime now rejects such output and keeps `mapId=unknown`, confidence 0, instead of publishing a false map name.
- DPI diagnostic capture: PASS. The desktop acceptance helper now opts into Per-Monitor V2 and saved the complete 2049x1152 client frame; the earlier 1366x768 image was DPI-virtualized and incomplete.
- Input: NOT EXECUTED. `INPUT_INJECTION=DISABLED` is recorded in both the 30-frame capture report and live-vision report. Because Self action confidence and map validation were not satisfied, testing production broker actions would have violated the fail-closed contract.
- Evidence: `artifacts/client-acceptance/target-capture-input-desktop.json`, `artifacts/client-acceptance/live-vision-input-desktop.json`, and `artifacts/client-acceptance/live-current-client-dpi.bmp`.
- Verification: Host 164/164 PASS; Runtime 70/70 PASS excluding the sandbox-only current-user DPAPI case; production input source contract PASS (`WINDOWS_EVIDENCE=PENDING`). React lint/typecheck, 54/54 Vitest tests, audit and production build passed. Playwright remained blocked by managed-sandbox browser `spawn EPERM` and was terminated after all six browser launches failed before page execution.

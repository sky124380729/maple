# Maple 运行时验证记录

日期：2026-08-14
环境：macOS arm64，局部 .NET SDK `/tmp/maple-dotnet` 8.0.424，Node/Vite/Vitest/Playwright
范围：本机可执行模块和 Windows 交叉编译；不把交叉编译当作 Windows 实机验收。

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

## 已实现但不能宣称实机通过

- `Maple.Host` 已有 .NET 8 win-x64 入口、WebView2 本地虚拟域映射、禁止外部导航、关闭生产 DevTools、进程崩溃事件和原生紧急停止；`HostSafetyCoordinator` 已离线验证刷新/失败/关闭的统一 ReleaseAll 语义。
- `Maple.Capture` 已有 `CapturedFrame` 所有权、Replay backend、WGC/BitBlt source boundary 和固定两槽 `WgcFramePool`。
- `Maple.Vision` 已有 OpenCvSharp HUD 像素识别、OCR 预处理/Tesseract adapter、ONNX Runtime adapter、manifest 类别/哈希校验、Self 唯一后处理和超时 fail-closed。
- `HostCommandDispatcher` 已把百炼凭据、固定模型、连接测试和地图标注意图放在 native 侧；`BailianMapHttpClient` 已离线验证真实兼容接口请求、上传同意、图片边界、401/超时/重试、响应 schema 和 frameId 来源一致性。Windows store 使用 DPAPI，Mac 调用会明确抛平台不支持。

## 必须留给 Windows

1. 实际 WebView2 Evergreen Runtime 启动、本地资源加载、刷新/进程失败/窗口关闭后的 ReleaseAll。
2. 实际 `GraphicsCaptureItem` + D3D11 readback adapter、WGC 权限/黑帧/尺寸变化与 BitBlt fallback。
3. 1280x720、1440x900、DPI 100/125/150% 的 capture/render 30-60 FPS、P50/P95/P99 延迟、队列年龄和内存/稳定性矩阵。
4. 真实 ONNX 权重和数据集的 Self/Player/Monster precision/recall、位置误差、遮挡和 stale 率。
5. 真实虚拟 HID 设备路径、VID/PID、报告描述符、驱动签名、设备层/Windows 输入层/授权客户端三层 PASS。
6. Windows DPAPI 当前用户写入、读取、清除和升级/回滚实机证据。
7. 把 WGC 地图关键帧编码并绑定到 `IMapImageSource`；未绑定时 Host 明确返回 `MAP_FRAME_SOURCE_UNAVAILABLE`，不得伪造云端地图候选。

## 当前风险

- Vite bundle 约 795 KB（gzip 251 KB），不影响当前启动，但应在 Host 接线后评估首屏加载。
- OpenCvSharp analyzer 版本高于局部 Roslyn 时已在项目中抑制已知 `CS9057`；运行时 native OpenCV 包和 GPU provider 仍需 Windows 发布矩阵验证。
- WGC API 通过 `IWgcRuntimeAdapter` 注入，避免 Mac 直接引用 `.winmd`；Windows AI 必须绑定系统 API，不得把 fake adapter 当成采集完成。
- `Maple.Input.WindowsVirtualHidAdapter` 仍在 `HID_CONTRACT_UNVERIFIED` 时拒绝发送；没有真实设备资料前禁止填写猜测的 VID/PID/报告编码。
- 百炼地图客户端源码与协议测试已完成，但生产 Host 尚无 WGC `IMapImageSource`；这属于 Windows 帧源绑定，不是百炼协议未实现。

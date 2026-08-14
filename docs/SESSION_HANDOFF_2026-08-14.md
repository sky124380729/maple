# 冒险岛视觉自动化项目交接记录

日期：2026-08-14
工作区：`C:\Users\Levi\Desktop\maple`
当前分支：`master`
适用范围：获得授权的测试客户端

## 1. 当前结论

仓库目前已经有一个可运行的 Windows WinForms 观察型原型，以及一个用于历史兼容性排查的 `SendInput` 探针。原型能够自动发现标题包含“冒险岛怀旧服”的窗口，读取客户区并显示三栏工作台、地图标定演示、状态安全门和性能遥测；它明确不发送键盘、鼠标或 HID 输入。

完整自动打怪产品尚未完成。当前没有真实 OpenCV、YOLO/ONNX、OCR、百炼请求链路、地图拓扑运行时、虚拟 HID 写入协议或自动战斗闭环。后续会话必须把原型与生产实现区分开，不要把模拟识别框、模拟地图坐标或 `SendInput` 返回成功当成生产能力。

## 2. 已确认的产品决策

这些决策来自此前讨论，后续实现默认遵守：

1. 只做视觉检测配合输入模拟；不修改游戏内存，不做客户端注入、网络协议修改、后台窗口消息、反检测或规避安全机制。
2. 生产输入唯一采用虚拟 HID。`SendInput` 已被舍弃，只保留旧探针作诊断记录；不能把旧探针接入自动运行路径。
3. 本地高频视觉采用 OpenCV + YOLO/ONNX。CPU provider 是基线，GPU provider 为可选加速；GPU 缺失不能改变安全门和暂停规则。
4. 游戏 UI 相对固定，地图、角色和野怪动态变化。分辨率、DPI 或 UI 版本变化必须触发重新标定。
5. 地图来源只能是游戏客户区截图和短录屏关键帧。未知地图先进入 `MapScanning`/`MapCalibrating`，不能凭单帧直接运行。
6. 百炼视觉模型只在低频场景使用：首次未知地图整图标定、用户主动重建、分辨率/UI 变化、持续低置信度复核。模型识别平台/台阶、梯子/绳索、边界、层级、连接关系和 `MapWorld` 坐标，输出结构化 `InitialMapAnnotation`；不生成按键序列、不生成完整路线、不直接驱动动作。
7. 本地拓扑算法和实时视觉闭环负责短动作规划。每次动作必须有前置条件、按下/保持/释放、后置观察和有限重试。
8. 移动键固定为 `Left/Right/Up/Down`；跳跃键可修改，默认 `Alt`；自动拾取可以关闭，默认拾取键 `Z`。
9. 程序是一个完整桌面程序，预览渲染在程序自己的窗口中，不在游戏上创建透明覆盖层，也不覆盖游戏客户端。
10. HID 是系统级全局输入，不能真正定向到后台窗口。失焦必须暂停并释放键；想同时使用微信或其他游戏，应使用第二台电脑或隔离的 VM/USB 直通方案。
11. 换频道不是自动识别范围。用户决定换频道时手动停止程序；地图仍然相同不能作为频道识别依据。
12. “拟人化”只表示由视觉反馈驱动、带有上下限和可复现约束的动作时序变化，不使用固定周期作为主循环，也不把随机性用于规避检测。

## 3. 代码结构与入口

### 3.1 新观察原型

目录：`src/MaplePrototype/`

- `MaplePrototype.csproj`：.NET Framework 4.8 WinForms，`WinExe`，普通用户权限，PerMonitorV2 DPI。
- `Program.cs`：三栏 UI、顶部标签、运行/暂停/紧急停止状态、HP/MP 阈值、攻击模式、跳跃键、拾取开关、右侧安全/识别/地图/日志信息和底部遥测。所有动作按钮只改状态和日志。
- `WindowCapture.cs`：枚举可见顶层窗口，按标题片段查找目标；读取 HWND、PID、客户区矩形、DPI、前台和最小化状态；前台客户区读取使用 `PrintWindow`，失败时有安全回退，失焦/最小化时暂停。
- `PreviewCanvas.cs`：保持宽高比显示客户区；原型的 Self/Player/Monster 标注和地图标定图是演示数据，不是真实检测结果。
- `PrototypeState.cs`：`Stopped`、`Observing`、`MapScanning`、`MapCalibrating`、`Paused`、`EmergencyStop` 和遥测字段。
- `Theme.cs`：深色工作台配色和 WinForms 控件样式。
- `app.manifest`：`asInvoker`，不要求管理员权限。

构建脚本：`tools/build-prototype.ps1`  生成 `dist/MapleVisualPrototype.exe`。

### 3.2 历史输入探针

目录：`src/MapleInputProbe/`，脚本：`tools/input-probe.ps1`、`tools/input-probe-ui.ps1`。

它使用 `user32.dll!SendInput` 做前台单键诊断，支持扫描码/虚拟键码、前台检查、显式 key-up 和日志。文件中的 `MapleVhfKeyboard` 只是占位探测路径，不代表存在可用的 HID 协议或驱动。生产代码不得复用这一实现。

## 4. 本机输入测试证据

2026-08-14 曾在用户已打开客户端的情况下执行一次低频授权自动测试：

- 先关闭前台的“键盘设置”层；
- 确认游戏窗口成为前台；
- 只发送一次扫描码 `Left`，等待后只发送一次扫描码 `Right`；
- 每个动作都有 `down=1`、`up=1`，结束时 `release-all count=0`；
- 保存了 `dist/auto-before.png`、`dist/auto-after-left.png`、`dist/auto-after-right.png`、`dist/auto-after.png` 和 `dist/input-test.log`。

截图中角色没有可确认的位移，且键盘设置层仍影响画面。因此结论是：

> Windows 输入事件入队成功，但目标客户端响应/角色移动未确认；不能据此声称 SendInput 可用。

这次测试不是生产路径验证，也没有写内存或修改客户端。此前机器上可枚举到 `ROOT\\HIDCLASS\\0000` 的 `gvinput` 键盘设备，但没有公开的用户态写入协议，不能逆向或借用其私有接口。当前没有名为 `MapleVhfKeyboard` 的已确认设备协议。

## 5. 当前验证命令

在仓库根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\build-prototype.ps1 -Configuration Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\prototype_contract.tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\input_probe_logic.tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\minimal_probe_contract.tests.ps1
git diff --check
```

原型自检是 WinExe，不依赖控制台输出：

```powershell
dist\MapleVisualPrototype.exe --self-test
Get-Content dist\prototype-self-test.txt -Encoding UTF8
```

自检应包含：

```text
PROTOTYPE_MODE=SAFE_OBSERVE_ONLY
INPUT_INJECTION=DISABLED
TARGET_TITLE=冒险岛怀旧服
MOVEMENT_KEYS=Left/Right/Up/Down
JUMP_KEY=Alt
PICKUP=OPTIONAL_DEFAULT_Z
```

## 6. 下一会话建议顺序

### 第一优先级：确定虚拟 HID 合同

先让用户提供已配置虚拟 HID 的精确设备接口路径、VID/PID、报告描述符、通信方式和 `KeyDown/KeyUp/ReleaseAll/Heartbeat` 协议。未知设备不要发送报告。先做设备状态查询和离线报告编码测试，再做记事本/Raw Input 测试器的单键验证，最后才在授权客户端做一次左右键验证。结果要分三层记录：设备安装、操作系统收到报告、客户端画面响应。

### 第二优先级：冻结安全原型并加强窗口身份

加入 PID、规范化进程路径、启动时间、文件版本/哈希、HWND、客户区尺寸和 DPI 的绑定；窗口重建或身份变化立即解除绑定。把采集后端、帧 TTL、有界 latest-frame 队列和离线回放接口抽出来。原型的模拟识别和模拟地图只能用于 UI/回放测试。

### 第三优先级：真实本地视觉

先用离线截图/短录屏建立评测集：OpenCV 负责固定 UI、HP/MP、HUD、颜色/模板/几何校验；YOLO/ONNX 负责 `self/player/monster`，`loot` 只走内部观察通道。先跑 CPU baseline，再接可选 GPU provider。模型 manifest 要记录输入尺寸、阈值、权重哈希和许可证。

### 第四优先级：多帧地图扫描与低频大模型

采集不同镜头位置的关键帧，估计相机位移并注册到 `MapWorld`；显示覆盖率、未覆盖区域、关键结构覆盖和标定误差。百炼输入是关键帧组或拼接/分块图像，输出 `InitialMapAnnotation`。本地几何/拓扑校验、动作预览和用户确认通过后才进入 `validated` 地图档案。

### 第五优先级：离线状态机和动作策略

先在回放模式实现统一状态机：`Stopped -> Arming -> Observing -> MapScanning -> MapCalibrating -> Navigating -> Attacking -> Looting -> UsingPotion -> Paused -> ManualIntervention -> EmergencyStop`。用视觉反馈决定动作结束，做有限重试和卡死检测；在此阶段保持真实输入禁用。

### 第六优先级：HID 接入、配置、日志和发布

将抽象动作映射到 HID 适配器，加入心跳 watchdog、全键释放、设备断连和失焦急停；配置使用带 schemaVersion 的 JSON，事件使用 JSONL，API Key 使用 DPAPI/Credential Manager。最后再做 self-contained win-x64 打包、签名、安装/回滚和 4/8 小时 soak。

## 7. 交接注意事项

- 不要把 `dist/MapleVisualPrototype.exe` 误称为自动打怪程序，它是输入禁用的观察原型。
- 不要把 `dist/MapleInputProbe.exe` 或 `SendInput` 接入生产闭环。
- 不要向 `gvinput`、Nefarius 或其他未确认设备写私有报告。
- 不要在失焦、未知地图、低置信度、黑帧、角色丢失、未知弹窗或输入设备状态不明时重试发送。
- 每次修改输入层后都要重新做“设备层 / OS 层 / 客户端视觉层”三层验收，并保留截图和日志。
- `.vs/`、`bin/`、`obj/` 已加入 `.gitignore`，不应提交。

## 8. 本次提交内容

本次交接提交应包含：两份已修订 SPEC、README、观察原型源码和项目文件、原型构建脚本、三份测试脚本、原型 EXE、历史输入测试证据以及本交接文档。不包含 IDE 缓存和编译中间目录。

# Maple 开源输入路径与视觉架构设计

日期：2026-08-15  
状态：已获用户批准，待书面复核  
适用范围：获得授权的测试客户端

## 1. 目标

先完成一个最小输入验证程序，确认开源项目使用的 Windows 用户态键盘路径能否让当前客户端响应左右移动；验证通过后，再把同一输入适配器接入 Maple 的视觉反馈闭环。

本设计借鉴 `KenYu910645/MapleStoryAutoLevelUp` 的公开架构：WGC 截图、小地图定位、地图路线档案、OpenCV 模板、状态机、血蓝监控和显式按键释放。上游仓库使用 MIT 许可证；PyAutoGUI 的 Windows 实现使用 BSD 许可证。复用代码时保留相应版权和许可证声明。

本设计不复制未知来源的 VMProtect 二进制、商业版授权逻辑、卖家改造代码、模型权重或游戏资源包。游戏截图和怪物模板只作为用户本地测试资产，不默认随产品分发。

## 2. 范围与非目标

### 2.1 包含

- 自动捕获唯一的“冒险岛怀旧服”窗口；
- 管理员完整性与前台窗口确认；
- `keybd_event` 按下、保持、释放和全键释放；
- 最小左右移动探针和结构化证据；
- OpenCV 小地图/固定 UI 识别、YOLO 动态目标识别和本地地图拓扑；
- 视觉反馈驱动的短动作、暂停和异常恢复；
- 可选的未知地图多帧扫描与低频地图结构标定。

### 2.2 不包含

- SendInput、PostMessage、Interception、虚拟 HID 或内存写入；
- 绕过 GPK、隐藏驱动、反检测或规避封禁；
- 后台窗口定向输入；
- 在未验证地图、失焦、黑帧、过期帧或低置信度状态下继续动作；
- 直接复制第三方程序的闭源资源或地图包。

## 3. 目标架构

```text
Maple.exe（.NET 8 Windows x64）
├─ React/WebView2 工作台
├─ WindowBinding：HWND/PID/路径/启动时间/DPI/完整性
├─ WGC Capture：latest-frame、TTL、黑帧和失焦门禁
├─ Vision
│  ├─ OpenCV：小地图、HP/MP、UI 和模板匹配
│  ├─ ONNX/YOLO：Self/Player/Monster/Loot/NPC/UI
│  └─ ObservationSnapshot：统一帧观察
├─ Map
│  ├─ 本地地图档案：平台、梯子、路线和模板
│  ├─ 小地图坐标定位
│  └─ MapScanning/MapCalibration
├─ Core：状态机、短动作、补给、拾取和异常恢复
└─ Input
   ├─ KeybdEventInputAdapter（首个可用后端）
   ├─ ActiveKeyRegistry 和 ReleaseAll
   └─ RP2040/VirtualHid 仅作为可选后端
```

## 4. 输入适配器设计

### 4.1 开源路径复用

输入实现按上游 `KeyBoardController` 的行为移植，而不是继续使用 SendInput：

- Windows 键名映射沿用 PyAutoGUI 的 VK 映射；
- `key_down` 调用 `user32.keybd_event(vk, 0, 0, 0)`；
- `key_up` 调用 `user32.keybd_event(vk, 0, KEYEVENTF_KEYUP, 0)`；
- 每个动作都显式成对发送 down/up；
- 左右、上下互斥，切换方向前先释放相反方向；
- 线程退出、暂停、失焦、异常和窗口关闭统一执行 `ReleaseAll`。

该路径是全局用户态输入，只能作用于当前前台桌面。程序不承诺后台控制；微信或其他程序获得焦点后必须立即暂停并释放全部按键。

### 4.2 权限与前台门禁

程序启动时读取目标进程和自身完整性等级。若目标高于自身，提示用户以管理员权限重启；未达到同级权限时禁止发送。

每次动作前必须同时满足：

- 目标 HWND 仍然有效且唯一；
- `GetForegroundWindow()` 等于目标 HWND；
- 目标未最小化，客户区尺寸与 DPI 未异常变化；
- 最新帧未过期且视觉安全门为 Armed；
- 输入 watchdog 正常，当前按键集合为空或与动作一致。

任何门禁失败都只执行 `ReleaseAll`，不重试发送。

### 4.3 可插拔接口

```text
IInputBackend
├─ KeybdEventInputAdapter      // 当前首选
├─ NullInputAdapter            // 默认观察/回放
├─ ReplayInputAdapter          // 离线验证
└─ VirtualHidInputAdapter      // 未来可选，未验收前禁用
```

Core 只提交语义动作（Move、Jump、Attack、UsePotion、Pickup），不直接依赖 Win32 API。适配器负责键名映射、时长、释放和诊断。

## 5. 最小输入验证程序

### 5.1 入口

提供独立的 `MapleInputProbe.exe` 诊断入口，不注册全局热键，不修改游戏内存，不常驻挂机。生产主程序后续复用同一个适配器。

### 5.2 流程

1. 枚举标题包含“冒险岛怀旧服”且类名为 `UnityWndClass` 的顶层窗口；多于一个时停止并要求选择。
2. 记录 HWND、PID、路径、启动时间、客户区尺寸、DPI、完整性等级和前台状态。
3. 采集动作前基线截图和角色/小地图定位信息。
4. 显示三秒倒计时，尝试把目标窗口置于前台并轮询确认最终 HWND。
5. 执行 `Left down -> hold 500ms -> Left up`。
6. 等待并采集释放后观察帧。
7. 执行同样的右移动作，最多两轮后自动结束。
8. `finally` 中释放所有方向键、Alt、Ctrl、Space、Z 和当前动作键。

### 5.3 兼容性矩阵

探针只在授权测试客户端中比较两种实现：

1. 上游完全一致：VK + `scan=0`；
2. 兼容补充：VK + `MapVirtualKey` 扫描码 + 扩展键标志。

不把 `keybd_event` 返回值当作成功证据。结果必须区分：

- `FOCUS_CONFIRMED`：目标确实获得前台；
- `INPUT_ATTEMPTED`：发送了 down/up；
- `MOVED_LEFT/RIGHT`：视觉确认角色或小地图发生同向位移；
- `NO_OBSERVED_TRANSLATION`：输入已尝试但未观察到位移；
- `UNKNOWN`：失焦、加载、遮挡、锚点丢失或帧不稳定。

## 6. 视觉与地图复用

### 6.1 检测层

- WGC 负责客户区帧；
- OpenCV 负责小地图边界、玩家颜色点、HP/MP、模板匹配和静态 UI；
- ONNX/YOLO 负责动态对象，不把单次检测直接转成动作；
- `ObservationSnapshot` 统一时间戳、置信度、坐标和帧年龄。

### 6.2 地图层

常用地图保存为本地档案，字段包括平台、梯子、层级、连接、路线、怪物模板和小地图变换。地图档案只能来自用户视觉录制或用户明确导入的合法资产。

未知地图进入 `MapScanning`：采集多个镜头位置的关键帧，注册到 `MapWorld`，必要时低频调用百炼识别平台/梯子结构；本地几何校验和短时观察通过后才变为 `validated`。

### 6.3 状态机

```text
Observe
  -> MapScanning / MapCalibrating
  -> Hunting
  -> Attack / Move / Pickup
  -> Reobserve
  -> Restock / Pause / EmergencyStop
```

每个动作必须等待新的观察帧，不采用无限循环或固定周期连发。卡住、多人遮挡、地图变化、HP/MP 冲突和失焦都进入暂停或重新观察。

## 7. 验收

第一阶段只验收输入探针：

- Windows 10/11 x64；
- 测试签名关闭；
- 目标窗口唯一、前台确认和权限等级记录完整；
- 左右各一次视觉位移证据；
- 任何退出路径没有卡键；
- 微信/记事本获得焦点后不再发送；
- JSONL 诊断报告可复现。

第二阶段再验收真实 WGC、YOLO、地图档案和视觉反馈闭环。没有第一阶段证据，不开放自动战斗。

## 8. 需要同步到主规格的变更

实现前必须更新 `docs/MAPLE_PROJECT_SPEC.md`：

- 将生产输入边界从“仅虚拟 HID”改为“已验收的前台输入适配器；首选 `KeybdEventInputAdapter`，虚拟 HID 为可选后端”；
- 明确禁止 SendInput、PostMessage、内存写入和后台定向输入；
- 增加管理员完整性、前台确认、ReleaseAll 和 `keybd_event` 证据要求；
- 保留“获得授权的测试客户端”范围。


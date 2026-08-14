# Maple VHF Input Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 构建 Maple 自有、可审计的 Windows 虚拟 HID 键盘链路，并以设备层、Windows 输入层和授权客户端画面层三份证据解锁真实输入。

**Architecture:** 使用 KMDF HID source driver 调用 Microsoft Virtual HID Framework。驱动创建标准 8 字节 boot-keyboard collection，并通过独立设备接口接收完整键盘状态和 heartbeat；内核 watchdog 超时立即提交 neutral report。`Maple.Input` 只负责报告编码、设备接口发现和 `DeviceIoControl`，React 永远不接触按键或报告。

**Tech Stack:** WDK 10.0.28000、KMDF、VHF (`vhf.h`/`vhfkm.lib`)、.NET 8、xUnit、PowerShell、Windows PnP/SetupAPI。

---

### Task 1: 固定 HID 报告与动作映射

**Files:**
- Create: `src/Maple.Input/BootKeyboardReportEncoder.cs`
- Create: `src/Maple.Input.Tests/BootKeyboardReportEncoderTests.cs`
- Modify: `src/Maple.Input/WindowsVirtualHidAdapter.cs`

- [ ] **Step 1: 写失败测试**

测试 `Left/Right/Up/Down/Z/Alt/Ctrl/Space` 到 HID usage；组合键保留所有活动 usage；重复 key-down 不重复；neutral 为 8 个零字节；超过 6 个普通键拒绝。

```csharp
byte[] report = encoder.EncodeState(["Left", "Alt"], contract);
Assert.Equal(0x04, report[0]);
Assert.Contains((byte)0x50, report[2..]);
Assert.Equal(new byte[8], encoder.EncodeState([], contract));
```

- [ ] **Step 2: 验证 RED**

Run: `dotnet test src/Maple.Input.Tests/Maple.Input.Tests.csproj --filter BootKeyboardReportEncoderTests`

Expected: FAIL，`BootKeyboardReportEncoder` 不存在。

- [ ] **Step 3: 最小实现**

编码器只产生完整状态，不产生时序；`WindowsVirtualHidAdapter` 先计算 next active-key set，写成功后再提交 registry，失败保持原状态并进入 fail-closed。

```csharp
public interface IVirtualHidReportEncoder
{
    byte[] EncodeState(IReadOnlyCollection<string> activeKeys, VirtualHidDeviceContract contract);
}
```

- [ ] **Step 4: 验证 GREEN**

Run: `dotnet test src/Maple.Input.Tests/Maple.Input.Tests.csproj`

Expected: PASS，现有 adapter 生命周期测试不回归。

### Task 2: Windows 设备接口发现与 IOCTL transport

**Files:**
- Create: `src/Maple.Input/WindowsVirtualHidTransport.cs`
- Create: `src/Maple.Input/MapleHidProtocol.cs`
- Create: `src/Maple.Input.Tests/MapleHidProtocolTests.cs`
- Modify: `src/Maple.Input/VirtualHidDiagnostics.cs`

- [ ] **Step 1: 写失败测试**

验证固定协议版本、设备接口 GUID、报告长度、sequence 单调递增、heartbeat 和 neutral 命令；未知版本、错误长度和非 neutral 初始状态必须拒绝。

```csharp
Assert.Equal(8, MapleHidProtocol.KeyboardReportLength);
Assert.Equal(MapleHidCommand.SubmitReport, MapleHidProtocol.Decode(frame).Command);
```

- [ ] **Step 2: 验证 RED**

Run: `dotnet test src/Maple.Input.Tests/Maple.Input.Tests.csproj --filter MapleHidProtocolTests`

Expected: FAIL，协议类型不存在。

- [ ] **Step 3: 实现 transport**

使用 SetupAPI 枚举 `{6E6E6F4A-21A5-4DD2-86E5-7DB4C7E8A101}`，只允许唯一设备；`CreateFile` 打开后先读取协议状态并提交 neutral，再允许 `IOCTL_MAPLE_HID_SUBMIT_REPORT`。任何 Win32 错误关闭句柄并返回稳定错误码。

- [ ] **Step 4: 验证 GREEN**

Run: `dotnet test src/Maple.Input.Tests/Maple.Input.Tests.csproj`

Expected: PASS；没有 Maple 设备时返回 `HID_DEVICE_NOT_FOUND`，不得回退到其他 HID。

### Task 3: KMDF/VHF 键盘驱动

**Files:**
- Create: `driver/MapleVhfKeyboard/MapleVhfKeyboard.vcxproj`
- Create: `driver/MapleVhfKeyboard/MapleVhfKeyboard.inf`
- Create: `driver/MapleVhfKeyboard/driver.c`
- Create: `driver/MapleVhfKeyboard/device.c`
- Create: `driver/MapleVhfKeyboard/protocol.h`
- Create: `driver/MapleVhfKeyboard/public.h`
- Create: `tests/windows/hid_driver_build.tests.ps1`

- [ ] **Step 1: 写失败的构建契约**

脚本要求 x64 Release 输出 `.sys/.inf/.cat`，INF 包含 `Root\\MapleVhfKeyboard`、`LowerFilters=vhf` 和项目设备接口 GUID；报告描述符 SHA-256 写入证据。

- [ ] **Step 2: 验证 RED**

Run: `powershell -File tests/windows/hid_driver_build.tests.ps1`

Expected: FAIL，驱动工程不存在。

- [ ] **Step 3: 实现驱动**

`EvtDeviceAdd` 创建顺序队列、VHF 设备和 2 秒 passive-level WDF timer。IOCTL 只接受 8 字节完整状态；每次有效报告刷新 watchdog；stop/remove/timeout 提交 neutral 后 `VhfDelete`。不注册 ready-for-next callback，采用 VHF 默认缓冲。

- [ ] **Step 4: 构建驱动**

Run: `MSBuild.exe driver/MapleVhfKeyboard/MapleVhfKeyboard.vcxproj /p:Configuration=Release /p:Platform=x64`

Expected: `.sys` 构建成功，0 error；Inf2Cat 生成 Windows 10/11 x64 catalog。

### Task 4: 同一 EXE 的 HID 自检入口

**Files:**
- Create: `src/Maple.Host/WindowsHidSelfTest.cs`
- Create: `src/Maple.Host.Tests/WindowsHidSelfTestTests.cs`
- Modify: `src/Maple.Host/Program.cs`
- Modify: `src/Maple.Host/HostCompositionRoot.cs`
- Create: `tests/windows/hid_device_evidence.tests.ps1`

- [ ] **Step 1: 写失败测试**

测试 `--hid-self-test <json>` 在设备缺失、合同不匹配、neutral 失败、heartbeat 失败时不发非 neutral 报告；成功时只做 neutral/heartbeat 并生成设备层证据。

- [ ] **Step 2: 实现诊断 CLI**

诊断模式不注册全局热键，不发送方向键。`--hid-key-test <left|right> <holdMs> <json>` 仅在设备层和 OS 层已 PASS、目标窗口前台且 `holdMs<=300` 时执行一次 down/up，并在 finally 中 neutral。

- [ ] **Step 3: 验证**

Run: `tests/windows/hid_device_evidence.tests.ps1`

Expected: 未安装时明确 `HID_DEVICE_NOT_FOUND`；已安装后 `hid-device-report.json` 为 PASS。

### Task 5: 签名、安装和三层证据

**Files:**
- Create: `tools/install-maple-vhf-driver.ps1`
- Create: `tools/uninstall-maple-vhf-driver.ps1`
- Modify: `tests/windows/hid_contract.tests.ps1`
- Modify: `docs/maple-runtime/VERIFICATION_2026-08-14.md`

- [ ] **Step 1: 安装前门禁**

脚本必须检测管理员、catalog 签名、Secure Boot、testsigning/HVCI、现有设备和回滚路径。任何不满足只输出诊断，不修改启动策略。

- [ ] **Step 2: 用户明确允许后安装**

仅接受 Microsoft 签名包，或用户明确批准的开发测试签名模式。脚本 staging/创建设备后核对 VID/PID、descriptor hash 和 neutral；失败立即卸载新设备/驱动包。

- [ ] **Step 3: OS 层证据**

Raw Input 监听器记录一次 left/right key-down/up、ReleaseAll 后零卡键、heartbeat 超时 neutral，写 `dist/hid-os-report.json`。

- [ ] **Step 4: 客户端层证据**

冒险岛前台时每个方向只发送一次不超过 300ms；WGC 比较动作前后 Self/背景位移，验证失焦、进程退出和 EmergencyStop 均 neutral，写 `dist/hid-client-response.json`。

- [ ] **Step 5: 全量门禁**

Run: `node tools/verify-portable.mjs`

Run: `powershell -File tests/windows/hid_contract.tests.ps1 -RequireEvidence`

Expected: 全部 PASS 后才在 `HostCompositionRoot` 选择 `WindowsVirtualHidAdapter`；否则继续使用 `NullInputAdapter`。

# Production Input Broker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the virtual-HID production path with a guarded elevated `Maple.InputBroker.exe`, wire it into the existing Host/runtime, and ship a foreground-only input flow with F9 pause/resume and F12 emergency stop.

**Architecture:** `Maple.exe` remains an `asInvoker` WebView2/vision host. It launches a minimal `requireAdministrator` broker and communicates over a current-user named pipe; only abstract actions cross IPC, while the broker owns the fixed VK/scan-code map, foreground gate, active-key registry, watchdog, and `ReleaseAll`. The probe remains a diagnostic executable, while Replay/Null remain portable test backends.

**Tech Stack:** .NET 8, Windows Forms Host, named pipes, Win32 `keybd_event`, `RegisterHotKey`, System.Text.Json, xUnit, PowerShell contract tests, React 19/TypeScript/Ant Design.

---

## File Structure

Create these focused units:

- `src/Maple.Input/BrokerProtocol.cs`: versioned IPC envelopes and abstract action allowlist.
- `src/Maple.Input/BrokerKeyProfile.cs`: logical action/profile to fixed key encoding.
- `src/Maple.InputBroker/Program.cs`: command-line validation and broker lifetime.
- `src/Maple.InputBroker/BrokerServer.cs`: one-client named-pipe server, PID verification, heartbeat, and dispatch.
- `src/Maple.InputBroker/BrokerSafetyGate.cs`: independent target/foreground/integrity checks.
- `src/Maple.InputBroker/WindowsKeybdEventSender.cs`: the sole production `keybd_event` P/Invoke.
- `src/Maple.InputBroker/BrokerInputSession.cs`: active-key lifecycle and watchdog cleanup.
- `src/Maple.Host/BrokerProcessLauncher.cs`: UAC launch, random session identifiers, and process ownership.
- `src/Maple.Host/BrokerClient.cs`: typed request/response pipe client.
- `src/Maple.Host/BrokerInputAdapter.cs`: `IInputAdapter` implementation that delegates every input lifecycle operation to Broker.
- `src/Maple.Host/BrokerActionExecutor.cs`: `IActionExecutor` bridge from runtime actions to broker messages.
- `src/Maple.Host/GlobalHotKeyManager.cs`: F9/F12 registration and dispatch.
- `src/Maple.Host/ForegroundSessionController.cs`: start/resume countdown, target activation, and loss-of-focus pause.
- `src/Maple.InputBroker.Tests/BrokerServerTests.cs` and `BrokerInputSessionTests.cs`: broker protocol, safety, watchdog, and dispatch tests.
- `src/Maple.Host.Tests/BrokerProcessLauncherTests.cs`, `BrokerClientTests.cs`, `BrokerActionExecutorTests.cs`, `GlobalHotKeyManagerTests.cs`, and `ForegroundSessionControllerTests.cs`: Host-side process, IPC, hotkey, and session tests.
- `tests/windows/production_input_contract.tests.ps1`: static production boundary and publish checks.

Delete the virtual-HID driver/runtime surface only after its replacement tests pass:

- `src/Maple.Input/WindowsVirtualHidAdapter.cs`
- `src/Maple.Input/WindowsVirtualHidTransport.cs`
- `src/Maple.Input/WindowsMapleHidDeviceLocator.cs`
- `src/Maple.Input/BootKeyboardReportEncoder.cs`
- `src/Maple.Input/MapleHidProtocol.cs`
- `src/Maple.Input/VirtualHidDiagnostics.cs`
- corresponding HID tests, `tests/windows/hid_*`, driver sources, fixtures, and install/reboot/rollback tools.

### Task 1: Preserve the Proven Diagnostic Input Evidence

**Files:**
- Modify: `src/Maple.Input/KeybdEventInputAdapter.cs`
- Modify: `src/Maple.Input/VirtualKeyMap.cs`
- Modify: `src/Maple.Input.Tests/KeybdEventInputAdapterTests.cs`
- Modify: `src/Maple.InputProbe/ProbeRunOptions.cs`
- Modify: `src/Maple.InputProbe/ProbeRunner.cs`
- Modify: `src/Maple.InputProbe/ProbeEvidence.cs`
- Modify: `src/Maple.InputProbe/ProbeForm.cs`
- Modify: `src/Maple.InputProbe/Program.cs`
- Modify: `tests/windows/input_probe_contract.tests.ps1`
- Modify: `docs/maple-runtime/VERIFICATION_2026-08-14.md`

- [ ] **Step 1: Run the focused encoding tests against the current working tree**

Run:

```powershell
dotnet test .\src\Maple.Input.Tests\Maple.Input.Tests.csproj --filter "FullyQualifiedName~KeybdEventInputAdapterTests"
```

Expected: `23` or more focused tests pass, including `ExtendedScanCodeModeEncodesLeftArrowAsExtendedKey` and `ExtendedScanCodeModeUsesRightArrowScanCodeDuringReleaseAll`.

- [ ] **Step 2: Add the real-client evidence record without copying sensitive images into Git**

Append a dated section to `docs/maple-runtime/VERIFICATION_2026-08-14.md` containing this exact classification:

```markdown
### 2026-08-15 extended-scan-code probe

- Evidence session: `%LOCALAPPDATA%\Maple\input-probe\20260815-122248-925`
- Left: `VK=37`, `scanCode=75 (0x4B)`, `flagsDown=1`, `flagsUp=3`, foreground confirmed, all keys released; avatar moved left from about x=318 to x=194.
- Right: `VK=39`, `scanCode=77 (0x4D)`, `flagsDown=1`, `flagsUp=3`, foreground confirmed, all keys released; avatar moved right from about x=194 to x=318.
- Classification: `CLIENT_MOVEMENT_CONFIRMED` for left/right only.
- Not established: jump, climb, attack, pickup, potion, production Host integration, or soak stability.
```

- [ ] **Step 3: Build and verify the published diagnostic probe**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\build-input-probe.ps1 -Configuration Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\windows\input_probe_contract.tests.ps1 -RequirePublished
dotnet test .\src\Maple.Input.Tests\Maple.Input.Tests.csproj
```

Expected: `INPUT_PROBE_BUILD=PASS`, `INPUT_PROBE_CONTRACT=PASS`, and all Input tests pass.

- [ ] **Step 4: Commit the probe and evidence as one bounded baseline**

```powershell
git add src/Maple.Input src/Maple.Input.Tests src/Maple.InputProbe tests/windows/input_probe_contract.tests.ps1 docs/maple-runtime/VERIFICATION_2026-08-14.md
git commit -m "feat: verify extended scan code input"
```

### Task 2: Migrate the Product Source of Truth

**Files:**
- Create: `tests/windows/production_input_contract.tests.ps1`
- Modify: `docs/MAPLE_PROJECT_SPEC.md`
- Modify: `docs/WINDOWS_IMPLEMENTATION_HANDOFF_2026-08-14.md`
- Modify: `tests/portable-contracts.mjs`

- [ ] **Step 1: Write a failing production input contract**

Create `tests/windows/production_input_contract.tests.ps1` with these checks:

```powershell
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$spec = Get-Content (Join-Path $root 'docs\MAPLE_PROJECT_SPEC.md') -Raw -Encoding UTF8
$handoff = Get-Content (Join-Path $root 'docs\WINDOWS_IMPLEMENTATION_HANDOFF_2026-08-14.md') -Raw -Encoding UTF8

foreach ($token in @('Maple.InputBroker.exe', '扩展扫描码', '前台窗口', 'F9', 'F12', 'ReleaseAll')) {
    if ($spec -notmatch [regex]::Escape($token)) { throw "Spec missing production input token: $token" }
}
if ($spec -match '生产输入唯一通过独立虚拟 HID') { throw 'Spec still declares virtual HID as the production input path.' }
if ($handoff -match '生产输入只能来自已验收虚拟 HID') { throw 'Handoff still declares virtual HID as the production input path.' }
Write-Output 'PRODUCTION_INPUT_SPEC=PASS'
```

- [ ] **Step 2: Run the contract and verify it fails on the old source of truth**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\windows\production_input_contract.tests.ps1
```

Expected: FAIL because the current spec still declares virtual HID as the only production path.

- [ ] **Step 3: Update the source-of-truth documents**

Change the production loop to:

```text
窗口绑定 -> 原生采集 -> 本地视觉 -> ObservationSnapshot -> 安全门 -> 短动作决策 -> InputBroker 扩展扫描码 -> 新画面反馈
```

Replace HID status/acceptance text with these explicit rules:

```markdown
- `Maple.exe` remains normal-integrity; `Maple.InputBroker.exe` is the only elevated/input-producing process.
- Production input is foreground-only and uses fixed, separately verified extended scan codes.
- React and Host cannot submit raw VK/scan-code/flag values.
- Target loss, foreground loss, stale frames, IPC loss, heartbeat timeout, shutdown, and any exception call `ReleaseAll`.
- F9 pauses/resumes through a three-second re-arm flow; F12 performs native emergency stop.
```

Update `tests/portable-contracts.mjs` to require `BrokerProtocol`, `BrokerClient`, and `ReleaseAll` tokens instead of `WindowsVirtualHidAdapter` tokens.

- [ ] **Step 4: Run the contract and portable contract tests**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\windows\production_input_contract.tests.ps1
node .\tests\portable-contracts.mjs
```

Expected: `PRODUCTION_INPUT_SPEC=PASS` and portable contracts pass.

- [ ] **Step 5: Commit the product decision migration**

```powershell
git add docs/MAPLE_PROJECT_SPEC.md docs/WINDOWS_IMPLEMENTATION_HANDOFF_2026-08-14.md tests/windows/production_input_contract.tests.ps1 tests/portable-contracts.mjs
git commit -m "docs: adopt brokered scan code input"
```

### Task 3: Define the Broker Protocol and Fixed Key Profiles

**Files:**
- Create: `src/Maple.Input/BrokerProtocol.cs`
- Create: `src/Maple.Input/BrokerKeyProfile.cs`
- Create: `src/Maple.Input.Tests/BrokerProtocolTests.cs`
- Create: `src/Maple.Input.Tests/BrokerKeyProfileTests.cs`

- [ ] **Step 1: Write failing protocol and profile tests**

Add tests that assert only abstract actions are serializable and the four arrows use the verified encodings:

```csharp
[Fact]
public void ArrowProfilesUseVerifiedExtendedScanCodes()
{
    Assert.Equal(new BrokerKeyEncoding(0x25, 0x4B, true), BrokerKeyProfile.For(BrokerActionKind.MoveLeft));
    Assert.Equal(new BrokerKeyEncoding(0x27, 0x4D, true), BrokerKeyProfile.For(BrokerActionKind.MoveRight));
    Assert.Equal(new BrokerKeyEncoding(0x26, 0x48, true), BrokerKeyProfile.For(BrokerActionKind.ClimbUp));
    Assert.Equal(new BrokerKeyEncoding(0x28, 0x50, true), BrokerKeyProfile.For(BrokerActionKind.ClimbDown));
}

[Fact]
public void RawKeyboardFieldsAreNotPartOfActionRequest()
{
    string json = JsonSerializer.Serialize(new BrokerRequest(
        BrokerProtocol.Version, 1, BrokerRequestKind.KeyDownAction,
        new BrokerActionPayload("a-1", BrokerActionKind.MoveLeft, null, 120, 300)));
    Assert.DoesNotContain("virtualKey", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("scanCode", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("flags", json, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run:

```powershell
dotnet test .\src\Maple.Input.Tests\Maple.Input.Tests.csproj --filter "FullyQualifiedName~Broker"
```

Expected: compile failure because broker protocol types do not exist.

- [ ] **Step 3: Implement the protocol and fixed profiles**

Define the public contract in `BrokerProtocol.cs`:

```csharp
public static class BrokerProtocol { public const int Version = 1; }
public enum BrokerRequestKind { ArmTarget, KeyDownAction, KeyUpAction, PressAction, Heartbeat, ReleaseAll, Shutdown }
public enum BrokerActionKind { MoveLeft, MoveRight, Jump, ClimbUp, ClimbDown, SingleAttack, AreaAttack, Pickup, HpPotion, MpPotion }
public sealed record BrokerRequest(int Version, long Sequence, BrokerRequestKind Kind, object? Payload);
public sealed record BrokerResponse(int Version, long Sequence, bool Accepted, string Code, string[] ReleasedKeys);
public sealed record ArmTargetPayload(long Hwnd, int Pid, long StartedAtUtcTicks, string ExecutablePath);
public sealed record BrokerActionPayload(string ActionId, BrokerActionKind Action, string? LogicalKey, int HoldMs, int MaximumDurationMs);
public sealed record BrokerKeyEncoding(ushort VirtualKey, uint ScanCode, bool Extended);
```

`BrokerKeyProfile.For` must contain the verified arrow values and default logical keys (`Alt`, `Ctrl`, `Z`) without accepting numeric VK or scan-code input from callers. Reject unsupported keys and action/profile conflicts.

- [ ] **Step 4: Run focused and full Input tests**

```powershell
dotnet test .\src\Maple.Input.Tests\Maple.Input.Tests.csproj --filter "FullyQualifiedName~Broker"
dotnet test .\src\Maple.Input.Tests\Maple.Input.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/Maple.Input src/Maple.Input.Tests
git commit -m "feat: define input broker protocol"
```

### Task 4: Build the Elevated Broker and Watchdog

**Files:**
- Create: `src/Maple.InputBroker/Maple.InputBroker.csproj`
- Create: `src/Maple.InputBroker/app.manifest`
- Create: `src/Maple.InputBroker/Program.cs`
- Create: `src/Maple.InputBroker/BrokerServer.cs`
- Create: `src/Maple.InputBroker/BrokerClientIdentity.cs`
- Create: `src/Maple.InputBroker/BrokerSafetyGate.cs`
- Create: `src/Maple.InputBroker/WindowsKeybdEventSender.cs`
- Create: `src/Maple.InputBroker/BrokerInputSession.cs`
- Create: `src/Maple.InputBroker.Tests/Maple.InputBroker.Tests.csproj`
- Create: `src/Maple.InputBroker.Tests/BrokerServerTests.cs`
- Create: `src/Maple.InputBroker.Tests/BrokerInputSessionTests.cs`
- Modify: `Maple.sln`

- [ ] **Step 1: Write failing broker session tests**

Use fakes for Win32, clock, and sender. Cover these exact behaviors:

```csharp
[Fact]
public async Task HeartbeatTimeoutReleasesEveryActiveKey()
{
    var sender = new RecordingSender();
    var clock = new FakeClock(1_000);
    await using var session = TestSession(sender, clock, heartbeatTimeoutMs: 500);
    await session.HandleAsync(KeyDown(BrokerActionKind.MoveLeft, 200));
    clock.Now = 1_501;

    await session.CheckWatchdogAsync();

    Assert.Equal(new[] { "Left" }, session.LastReleasedKeys);
    Assert.Equal(0x0001u | 0x0002u, sender.Events[^1].Flags);
}

[Theory]
[InlineData(false, true, true, "TARGET_NOT_FOREGROUND")]
[InlineData(true, false, true, "TARGET_IDENTITY_CHANGED")]
[InlineData(true, true, false, "FRAME_STALE")]
public async Task SafetyFailureRejectsAndReleases(bool foreground, bool identity, bool fresh, string code)
{
    var sender = new RecordingSender();
    await using var session = TestSession(sender, new FakeClock(1_000));
    await session.HandleAsync(KeyDown(BrokerActionKind.MoveLeft, 200));
    session.Safety.Foreground = foreground;
    session.Safety.IdentityMatches = identity;
    session.Safety.FrameFresh = fresh;

    BrokerResponse response = await session.HandleAsync(KeyDown(BrokerActionKind.MoveRight, 200));

    Assert.False(response.Accepted);
    Assert.Equal(code, response.Code);
    Assert.Empty(session.ActiveKeys);
    Assert.Contains(sender.Events, item => item.Flags == 0x0003u);
}
```

- [ ] **Step 2: Run tests and verify they fail**

```powershell
dotnet test .\src\Maple.InputBroker.Tests\Maple.InputBroker.Tests.csproj
```

Expected: project/types missing.

- [ ] **Step 3: Create the broker project and elevation manifest**

Use:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <PlatformTarget>x64</PlatformTarget>
    <SelfContained>true</SelfContained>
    <ApplicationManifest>app.manifest</ApplicationManifest>
  </PropertyGroup>
  <ItemGroup><ProjectReference Include="..\Maple.Input\Maple.Input.csproj" /></ItemGroup>
</Project>
```

The manifest must contain `requestedExecutionLevel level="requireAdministrator" uiAccess="false"`.

- [ ] **Step 4: Implement one-client pipe identity and broker dispatch**

`BrokerServer` must:

```csharp
await pipe.WaitForConnectionAsync(token);
int clientPid = BrokerClientIdentity.GetClientProcessId(pipe.SafePipeHandle);
if (clientPid != expectedParentPid) throw new BrokerRejectedException("CLIENT_PID_MISMATCH");
while (!token.IsCancellationRequested) {
    BrokerRequest request = await codec.ReadRequestAsync(pipe, token);
    await codec.WriteResponseAsync(pipe, await session.HandleAsync(request, token), token);
}
```

Create the named pipe with current-user-only ACL, a single server instance, byte/message size caps, JSON depth caps, monotonic sequence enforcement, and protocol-version enforcement. Reject a second client.

- [ ] **Step 5: Implement the production sender and session cleanup**

`WindowsKeybdEventSender` is the only `user32!keybd_event` declaration in production code. `BrokerInputSession` must apply `KEYEVENTF_EXTENDEDKEY` and `KEYEVENTF_KEYUP`, release opposite directions first, enforce `HoldMs <= MaximumDurationMs <= 5_000`, and call `ReleaseAll` on EOF, cancellation, exception, watchdog, and disposal.

- [ ] **Step 6: Run broker and Input tests**

```powershell
dotnet test .\src\Maple.InputBroker.Tests\Maple.InputBroker.Tests.csproj
dotnet test .\src\Maple.Input.Tests\Maple.Input.Tests.csproj
```

Expected: all tests pass with no real input sent because every test injects a fake sender.

- [ ] **Step 7: Commit**

```powershell
git add Maple.sln src/Maple.InputBroker src/Maple.InputBroker.Tests
git commit -m "feat: add elevated input broker"
```

### Task 5: Add the Host Broker Client and Runtime Action Executor

**Files:**
- Create: `src/Maple.Host/BrokerProcessLauncher.cs`
- Create: `src/Maple.Host/BrokerClient.cs`
- Create: `src/Maple.Host/BrokerInputAdapter.cs`
- Create: `src/Maple.Host/BrokerActionExecutor.cs`
- Create: `src/Maple.Host.Tests/BrokerProcessLauncherTests.cs`
- Create: `src/Maple.Host.Tests/BrokerClientTests.cs`
- Create: `src/Maple.Host.Tests/BrokerInputAdapterTests.cs`
- Create: `src/Maple.Host.Tests/BrokerActionExecutorTests.cs`
- Modify: `src/Maple.Host/HostCompositionRoot.cs`
- Modify: `src/Maple.Host/Maple.Host.csproj`

- [ ] **Step 1: Write failing Host tests**

Cover process launch and action translation:

```csharp
[Fact]
public void LauncherUsesRunAsAndNeverElevatesMapleHost()
{
    ProcessStartInfo info = launcher.CreateStartInfo("Maple.InputBroker.exe", pipeName, parentPid);
    Assert.Equal("runas", info.Verb);
    Assert.False(info.UseShellExecute is false);
    Assert.DoesNotContain("token", info.Arguments, StringComparison.OrdinalIgnoreCase);
}

[Theory]
[InlineData(ActionType.MoveLeft, null, BrokerActionKind.MoveLeft)]
[InlineData(ActionType.Jump, null, BrokerActionKind.Jump)]
[InlineData(ActionType.Attack, ActionProfileId.SingleAttack, BrokerActionKind.SingleAttack)]
[InlineData(ActionType.UsePotion, ActionProfileId.HpPotion, BrokerActionKind.HpPotion)]
public async Task ExecutorMapsOnlySupportedAbstractActions(ActionType type, ActionProfileId? profile, BrokerActionKind expected)
{
    var client = new RecordingBrokerClient();
    var executor = new BrokerActionExecutor(client);
    var action = new AbstractAction
    {
        ActionId = "action-1",
        Type = type,
        ProfileId = profile,
        IssuedAtMonoMs = 100,
        HoldMs = 120,
        MaxDurationMs = 300
    };

    await executor.KeyDownAsync(action, CancellationToken.None);

    Assert.Equal(expected, Assert.Single(client.ActionRequests).Action);
}
```

- [ ] **Step 2: Run tests and verify they fail**

```powershell
dotnet test .\src\Maple.Host.Tests\Maple.Host.Tests.csproj --filter "FullyQualifiedName~Broker"
```

Expected: missing launcher/client/executor types.

- [ ] **Step 3: Implement launcher and client**

`BrokerProcessLauncher` generates a cryptographically random pipe suffix, publishes only pipe name/parent PID/protocol version to command line, launches with `Verb="runas"`, waits for broker connection with a bounded timeout, and terminates a child it owns on Host shutdown. Cancellation/UAC denial returns `INPUT_BROKER_ELEVATION_CANCELLED` without retry loops.

`BrokerClient` serializes one request at a time, verifies response sequence/version, sends heartbeat on a timer, and sends `ReleaseAll` with `CancellationToken.None` during disposal. `BrokerInputAdapter` implements the existing synchronous `IInputAdapter` contract over a dedicated Broker client worker; it never performs pipe I/O on the UI synchronization context, and its `GetStatus()` mirrors the last acknowledged Broker state.

- [ ] **Step 4: Implement `BrokerActionExecutor`**

`BrokerActionExecutor` wraps `BrokerInputAdapter` and maps `AbstractAction` to the logical key/profile used by `KeyDown` and `KeyUp`; preserve action ID/hold/max duration. `Pause` and `Replan` never cross IPC. A rejected or malformed response throws a typed `InputUnavailableException` so `ProductionOrchestrator` reaches its existing `finally ReleaseAll` path.

- [ ] **Step 5: Wire Broker artifacts into publish output but keep runtime disabled**

Add the project reference/build dependency and copy broker publish files next to `Maple.exe`. Do not replace `NullInputAdapter` in `HostCompositionRoot` until Task 7 has complete foreground/hotkey gates.

- [ ] **Step 6: Run Host tests and build**

```powershell
dotnet test .\src\Maple.Host.Tests\Maple.Host.Tests.csproj
dotnet build .\src\Maple.Host\Maple.Host.csproj -c Release
```

Expected: all tests pass and both Host/Broker artifacts build.

- [ ] **Step 7: Commit**

```powershell
git add src/Maple.Host src/Maple.Host.Tests src/Maple.InputBroker
git commit -m "feat: connect host to input broker"
```

### Task 6: Add Feedback-Based Timing Variation

**Files:**
- Create: `src/Maple.Core/ActionTimingRandomizer.cs`
- Create: `src/Maple.Runtime.Tests/Runtime/ActionTimingRandomizerTests.cs`
- Modify: `src/Maple.Runtime/ProductionOrchestrator.cs`
- Modify: `src/Maple.Runtime/IRuntimeJournal.cs`

- [ ] **Step 1: Write failing deterministic randomization tests**

```csharp
[Fact]
public void SameSeedProducesSameBoundedDuration()
{
    var first = new ActionTimingRandomizer(42, maximumFraction: 0.08);
    var second = new ActionTimingRandomizer(42, maximumFraction: 0.08);
    Assert.Equal(first.Apply(500, 100, 700), second.Apply(500, 100, 700));
}

[Theory]
[InlineData(100, 100, 300)]
[InlineData(500, 120, 520)]
public void ResultNeverExceedsSafetyBounds(int baseline, int minimum, int maximum)
{
    int value = new ActionTimingRandomizer(7, 0.08).Apply(baseline, minimum, maximum);
    Assert.InRange(value, minimum, maximum);
}
```

- [ ] **Step 2: Run and verify failure**

```powershell
dotnet test .\src\Maple.Runtime.Tests\Maple.Runtime.Tests.csproj --filter "FullyQualifiedName~ActionTimingRandomizer"
```

Expected: missing type.

- [ ] **Step 3: Implement bounded deterministic variation**

Use a per-session seed. Clamp the sampled result to action-specific minimum and `MaxDurationMs`; never randomize `ReleaseAll`, safety pauses, health actions beyond their configured urgency cap, or platform-edge releases. Journal `seed`, `baselineHoldMs`, `variationMs`, and `finalHoldMs`.

- [ ] **Step 4: Run Runtime tests**

```powershell
dotnet test .\src\Maple.Runtime.Tests\Maple.Runtime.Tests.csproj
```

Expected: all Runtime tests pass, including deterministic replay.

- [ ] **Step 5: Commit**

```powershell
git add src/Maple.Core src/Maple.Runtime src/Maple.Runtime.Tests
git commit -m "feat: add bounded action timing variation"
```

### Task 7: Implement Foreground Start/Resume and Native Hotkeys

**Files:**
- Create: `src/Maple.Host/GlobalHotKeyManager.cs`
- Create: `src/Maple.Host/ForegroundSessionController.cs`
- Create: `src/Maple.Host.Tests/GlobalHotKeyManagerTests.cs`
- Create: `src/Maple.Host.Tests/ForegroundSessionControllerTests.cs`
- Modify: `src/Maple.Host/WebViewHostForm.cs`
- Modify: `src/Maple.Host/HostCompositionRoot.cs`
- Modify: `src/Maple.Host/HostSafetyCoordinator.cs`

- [ ] **Step 1: Write failing hotkey and focus tests**

```csharp
[Fact]
public async Task ResumeCountsDownThenRequiresConfirmedGameForeground()
{
    target.ActivationResult = true;
    await controller.ResumeAsync(CancellationToken.None);
    Assert.Equal(new[] { 3, 2, 1 }, countdown.Values);
    Assert.True(broker.Armed);
}

[Fact]
public async Task ForegroundLossReleasesAndPausesOnce()
{
    await controller.OnForegroundChangedAsync(otherHwnd);
    Assert.Equal(1, broker.ReleaseAllCalls);
    Assert.Equal(PauseReason.WindowNotForeground, controller.PauseReason);
}

[Fact]
public void F12EmergencyStopDoesNotDependOnWebView()
{
    hotkeys.Dispatch(GlobalHotKeyId.EmergencyStop);
    Assert.Equal(1, safety.EmergencyStopCalls);
}
```

- [ ] **Step 2: Run and verify failure**

```powershell
dotnet test .\src\Maple.Host.Tests\Maple.Host.Tests.csproj --filter "FullyQualifiedName~HotKey|FullyQualifiedName~ForegroundSession"
```

Expected: missing types.

- [ ] **Step 3: Implement F9/F12 registration**

Use `RegisterHotKey`/`UnregisterHotKey` in a focused wrapper. Default F9 toggles pause/resume; F12 always emergency-stops. Registration failure publishes `InputUnavailable` and prevents arming. Dispose unregisters both hotkeys.

- [ ] **Step 4: Implement start/resume state flow**

`ForegroundSessionController` performs target identity validation, Broker start/arm, `3,2,1` countdown events, `SetForegroundWindow`, a bounded foreground poll, and final Broker status confirmation. It never sends input when activation fails. A WinEvent foreground hook or bounded foreground poll detects loss and calls Broker `ReleaseAll` before publishing paused state.

- [ ] **Step 5: Replace the formal Host Null input path**

In `HostCompositionRoot`, compose `BrokerProcessLauncher`, `BrokerClient`, `BrokerInputAdapter`, `BrokerActionExecutor`, `ForegroundSessionController`, and the existing safety coordinator. Pass `BrokerInputAdapter` to `MainWindow` so native close/crash/content-reset paths release through Broker. Keep Null/Replay only in tests and explicit observe mode. The navigation/combat plan will connect `BrokerActionExecutor` to the production `ProductionOrchestrator` after its live observation source is available.

- [ ] **Step 6: Run Host tests and a no-input smoke launch**

```powershell
dotnet test .\src\Maple.Host.Tests\Maple.Host.Tests.csproj
dotnet run --project .\src\Maple.Host\Maple.Host.csproj -- --windows-diagnostics .\artifacts\windows-runtime\diagnostics.json
```

Expected: tests pass; diagnostics exits without starting Broker or sending input.

- [ ] **Step 7: Commit**

```powershell
git add src/Maple.Host src/Maple.Host.Tests
git commit -m "feat: guard input with foreground hotkeys"
```

### Task 8: Expose Broker State and Interaction in React

**Files:**
- Modify: `schemas/bridge.schema.json`
- Modify: `src/Maple.Contracts/DomainContracts.cs`
- Modify: `ui/src/contracts/bridge.ts`
- Modify: `ui/src/features/workbench/SessionControls.tsx`
- Modify: `ui/src/features/workbench/TargetStatus.tsx`
- Modify: `ui/src/features/workbench/WorkbenchPage.tsx`
- Modify: `ui/src/mock/mockSession.ts`
- Create: `ui/src/features/workbench/SessionControls.test.tsx`
- Create: `ui/src/features/workbench/TargetStatus.test.tsx`
- Modify: `ui/tests/playwright/workbench.spec.ts`

- [ ] **Step 1: Write failing contract/UI tests**

Require a host event payload shaped as:

```ts
{
  provider: 'inputBroker',
  status: 'disconnected' | 'starting' | 'ready' | 'paused' | 'faulted',
  integrity: 'unknown' | 'medium' | 'high',
  activeKeys: string[],
  lastReleaseSucceeded: boolean,
  hotkeys: { pauseResume: 'F9', emergencyStop: 'F12' },
  errorCode: string | null
}
```

UI tests must assert “开始运行”, “恢复并切回游戏”, F9/F12 labels, Broker status, and disabled auto-run when status is not ready.

- [ ] **Step 2: Run and verify tests fail**

```powershell
Push-Location .\ui
npm test -- --run
Pop-Location
```

Expected: schema/UI assertions fail because input Broker status is absent.

- [ ] **Step 3: Add strict shared contracts and Host event publishing**

Add `input.status.updated` to C#, JSON Schema, and Zod with identical closed fields. Do not add raw action/key/scan-code command fields. Host publishes countdown, paused reason, Broker health, active keys, and release result.

- [ ] **Step 4: Update the workbench controls**

Replace “开始观察” with mode-aware “开始运行”; show the 3-second countdown and “恢复并切回游戏”. Keep the native emergency button. Settings interactions cause a visible “修改设置时已暂停” state. Display hotkeys as compact status text, not as explanatory feature cards.

- [ ] **Step 5: Run UI tests and Playwright**

```powershell
Push-Location .\ui
npm run lint
npm run typecheck
npm test -- --run
npm run build
npx playwright test
Pop-Location
```

Expected: all checks pass at desktop and mobile viewports with no text overlap.

- [ ] **Step 6: Commit**

```powershell
git add schemas src/Maple.Contracts src/Maple.Host ui
git commit -m "feat: show production input controls"
```

### Task 9: Remove Virtual HID and Driver Operations

**Files:**
- Delete: virtual-HID runtime/test/driver/tool files listed in File Structure
- Modify: `Maple.sln`
- Modify: `tools/verify-portable.mjs`
- Modify: `tools/publish-windows.ps1`
- Modify: `tests/windows/publish_contract.tests.ps1`
- Modify: `tests/windows/production_input_contract.tests.ps1`
- Modify: `README.md`

- [ ] **Step 1: Strengthen the failing production boundary test**

Add checks that production source/publish scripts contain none of:

```powershell
$forbidden = @('WindowsVirtualHidAdapter', 'MapleVhf', 'TESTSIGNING', 'enable-maple-driver-test-mode', 'hid_contract.tests.ps1')
```

Also require `Maple.InputBroker.exe`, `requireAdministrator` only in the Broker manifest, `asInvoker` in the Host manifest, and exactly one production `keybd_event` P/Invoke under `src/Maple.InputBroker`.

- [ ] **Step 2: Run and verify failure**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\windows\production_input_contract.tests.ps1
```

Expected: FAIL because HID files/tools still exist.

- [ ] **Step 3: Delete HID runtime, driver, tests, fixtures, and scripts**

Use explicit `git rm` paths identified by `git ls-files` matching `Hid`, `hid_`, `vhf`, and the listed driver tools. Review the list before deletion. Preserve `IInputAdapter`, Null/Replay, `KeybdEventInputAdapter` diagnostic code, InputBroker code, and generic active-key tests.

- [ ] **Step 4: Update publish and verification scripts**

Publish both `Maple.exe` and `Maple.InputBroker.exe`. Remove driver installation, test-mode, restart, HID evidence, and device self-test steps. Replace them with production Broker static contract and an explicit real-client input evidence command.

- [ ] **Step 5: Run contracts and full portable verification**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\windows\production_input_contract.tests.ps1
node .\tools\verify-portable.mjs
git diff --check
```

Expected: production input contract and portable verification pass; no virtual-HID production references remain.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "refactor: remove virtual hid production path"
```

### Task 10: Publish and Capture Windows Input Evidence

**Files:**
- Create: `tests/windows/input_broker_evidence.tests.ps1`
- Create: `tests/fixtures/windows-input/broker-client-response.template.json`
- Modify: `tools/publish-windows.ps1`
- Modify: `docs/maple-runtime/VERIFICATION_2026-08-14.md`
- Modify: `.gitignore`

- [ ] **Step 1: Write the evidence validator before collecting evidence**

Require separate JSONL records for left, right, jump, climb-up, climb-down, single attack, pickup, and release-all. Every action record must contain:

```json
{
  "actionId": "...",
  "targetHwnd": 0,
  "targetPid": 0,
  "foregroundConfirmed": true,
  "hostIntegrity": 8192,
  "brokerIntegrity": 12288,
  "targetIntegrity": 12288,
  "vk": 0,
  "scanCode": 0,
  "flagsDown": 0,
  "flagsUp": 0,
  "screenshotBefore": "...",
  "screenshotAfter": "...",
  "classification": "CLIENT_EFFECT_CONFIRMED",
  "allKeysReleased": true
}
```

The validator must fail on missing screenshots, unconfirmed foreground, unknown classification, or any unreleased key.

- [ ] **Step 2: Run validator against the template and verify it fails**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\windows\input_broker_evidence.tests.ps1 -EvidenceRoot .\tests\fixtures\windows-input
```

Expected: FAIL because the template is not real evidence.

- [ ] **Step 3: Publish the Windows product**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\build-react-ui.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\publish-windows.ps1 -Configuration Release
```

Expected: publish output contains `Maple.exe`, `Maple.InputBroker.exe`, React assets, and no driver/INF/CAT files.

- [ ] **Step 4: Perform one authorized real-client matrix**

Run each unverified key once from a dedicated diagnostics page, with at least three seconds between actions. Capture client-area before/after screenshots and explicit `ReleaseAll`. Stop immediately on foreground loss or unexpected effect. Do not classify API return alone as success.

- [ ] **Step 5: Validate and document evidence**

Run:

```powershell
$latestEvidence = Get-ChildItem "$env:LOCALAPPDATA\Maple\input-broker-evidence" -Directory |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if ($null -eq $latestEvidence) { throw 'No input broker evidence session was found.' }
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\windows\input_broker_evidence.tests.ps1 -EvidenceRoot $latestEvidence.FullName -RequireEvidence
node .\tools\verify-portable.mjs
git diff --check
```

Expected: evidence validator passes only for confirmed effects and all keys released. Update the verification document with the exact session path and per-action result; do not commit screenshots containing user content.

- [ ] **Step 6: Commit**

```powershell
git add tests/windows/input_broker_evidence.tests.ps1 tests/fixtures/windows-input tools/publish-windows.ps1 docs/maple-runtime/VERIFICATION_2026-08-14.md .gitignore
git commit -m "test: verify production input broker"
```

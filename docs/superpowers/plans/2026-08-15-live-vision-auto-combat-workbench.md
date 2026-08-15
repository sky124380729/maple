# Live Vision Auto-Combat Workbench Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a Windows workbench whose central native preview shows real Self/Player/Monster detections and in-frame telemetry, whose side panels expose working combat/input configuration, and whose first supervised automatic-combat loop can seek and attack a same-platform snail through the production input broker.

**Architecture:** React owns the full-window three-column layout and reports only the central preview rectangle to the Host. The Host positions the native preview surface over that rectangle, fans captured frames into independent preview and latest-frame vision consumers, publishes typed observations to React, and feeds the existing production orchestrator. The local external ONNX model is inspected and hash-bound before use; unsupported/missing classes keep actions fail-closed.

**Tech Stack:** .NET 8 Windows Forms Host, WebView2, Windows Graphics Capture/BitBlt, Maple.Preview GDI+ overlay, OpenCvSharp, ONNX Runtime CPU/DirectML provider selection, React 19, TypeScript, Ant Design, Zustand, Vitest, Playwright, xUnit, production `Maple.InputBroker.exe`.

---

## File Structure

New focused units:

- `ui/src/features/preview/useNativePreviewBounds.ts`: observes the center placeholder and submits bounded layout intent.
- `src/Maple.Host/PreviewLayout.cs`: validates CSS-pixel preview bounds before touching native controls.
- `src/Maple.Preview/PreviewTelemetrySnapshot.cs`: immutable native HUD values and warning severity.
- `src/Maple.Vision/OnnxModelInspector.cs`: reads tensor metadata and Ultralytics class metadata without running actions.
- `src/Maple.Vision/YoloTensorDecoder.cs`: decodes supported fixed-row and YOLO output layouts with NMS.
- `src/Maple.Vision/SelfIdentityTracker.cs`: resolves exactly one Self from multi-frame character candidates and exposes other stable character tracks as Players.
- `src/Maple.Host/LatestVisionFrameQueue.cs`: bounded latest-frame copy owned by the vision worker.
- `src/Maple.Host/CompositeCaptureFrameObserver.cs`: fans a frame into map and vision copy observers without transferring ownership.
- `src/Maple.Host/VisionRuntimeService.cs`: processes latest frames, publishes overlays/observations/telemetry, and implements the runtime observation source.
- `src/Maple.Host/CombatConfigurationStore.cs`: validates and atomically persists native combat configuration.
- `src/Maple.Host/AutomaticCombatController.cs`: owns orchestrator cancellation, arming gates, state publication, and guaranteed release.

Existing units remain responsible for their current boundaries; `WebViewHostForm` coordinates controls and lifetimes but does not contain inference or combat policy logic.

### Task 1: Define Preview Layout And Runtime Status Contracts

**Files:**
- Modify: `ui/src/contracts/bridge.ts`
- Modify: `schemas/ui-command.schema.json`
- Modify: `schemas/host-event.schema.json`
- Modify: `src/Maple.Contracts/DomainContracts.cs`
- Modify: `src/Maple.Host/BridgeMessageRouter.cs`
- Test: `ui/src/contracts/bridge.test.ts`
- Test: `src/Maple.Host.Tests/BridgeMessageRouterTests.cs`
- Test: `src/Maple.Runtime.Tests/Contracts/ContractV2Tests.cs`

- [ ] **Step 1: Write failing contract tests**

Add tests that accept this layout intent and reject negative, oversized, non-finite, or raw-input-bearing payloads:

```ts
expect(uiCommandSchema.parse({
  schemaVersion: 2,
  type: 'preview.boundsChanged',
  payload: { left: 276, top: 82, width: 844, height: 720, devicePixelRatio: 1.25 },
}).type).toBe('preview.boundsChanged')
```

Add host-event tests for explicit model state and richer telemetry:

```ts
expect(hostEventSchema.parse({
  schemaVersion: 2,
  type: 'vision.status.updated',
  payload: { status: 'notConfigured', modelId: null, provider: 'none', diagnostic: 'MODEL_MANIFEST_MISSING' },
}).payload.status).toBe('notConfigured')
```

- [ ] **Step 2: Run tests and verify RED**

Run:

```powershell
Push-Location ui
npm test -- --run src/contracts/bridge.test.ts
Pop-Location
dotnet test src/Maple.Host.Tests/Maple.Host.Tests.csproj --filter BridgeMessageRouterTests
dotnet test src/Maple.Runtime.Tests/Maple.Runtime.Tests.csproj --filter ContractV2Tests
```

Expected: failures because `preview.boundsChanged`, `vision.status.updated`, detector latency, process memory, model provider, and last action are absent.

- [ ] **Step 3: Implement the typed contracts**

Add the strict UI payload:

```ts
z.object({
  ...commandEnvelope,
  type: z.literal('preview.boundsChanged'),
  payload: z.object({
    left: z.number().finite().min(0).max(10_000),
    top: z.number().finite().min(0).max(10_000),
    width: z.number().finite().min(320).max(10_000),
    height: z.number().finite().min(180).max(10_000),
    devicePixelRatio: z.number().finite().min(0.5).max(4),
  }).strict(),
}).strict()
```

Extend telemetry with `detectorLatencyMs`, `processMemoryMb`, `inferenceProvider`, `captureBackend`, `lastAction`, and `warningCode`. Define vision status values `notConfigured | inspecting | ready | repairing | faulted`. Mirror the same fields in JSON Schema and C# records. Route only the typed layout intent; continue recursive rejection of `vk`, `scanCode`, `flags`, raw reports, and action sequences.

- [ ] **Step 4: Run tests and verify GREEN**

Run the three commands from Step 2. Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add ui/src/contracts/bridge.ts schemas src/Maple.Contracts/DomainContracts.cs src/Maple.Host/BridgeMessageRouter.cs ui/src/contracts/bridge.test.ts src/Maple.Host.Tests/BridgeMessageRouterTests.cs src/Maple.Runtime.Tests/Contracts/ContractV2Tests.cs
git commit -m "feat: define native preview layout contract"
```

### Task 2: Restore The Full Three-Column Workbench

**Files:**
- Create: `ui/src/features/preview/useNativePreviewBounds.ts`
- Create: `src/Maple.Host/PreviewLayout.cs`
- Modify: `ui/src/features/preview/PreviewRegion.tsx`
- Modify: `ui/src/features/workbench/WorkbenchPage.tsx`
- Modify: `ui/src/features/workbench/TelemetryStrip.tsx`
- Modify: `ui/src/app/app.css`
- Modify: `src/Maple.Host/WebViewHostForm.cs`
- Modify: `src/Maple.Host.Tests/Maple.Host.Tests.csproj`
- Test: `ui/src/app/App.test.tsx`
- Test: `ui/src/features/preview/MockPreviewCanvas.test.tsx`
- Create: `src/Maple.Host.Tests/PreviewLayoutTests.cs`
- Modify: `ui/tests/workbench.spec.ts`

- [ ] **Step 1: Write failing layout tests**

Add a Vitest assertion that a `ResizeObserver` report sends `preview.boundsChanged` and that the full left/center/right regions remain in the DOM. Add an xUnit test for clamping:

```csharp
[Fact]
public void Resolve_clamps_preview_to_browser_client_area()
{
    var result = PreviewLayout.Resolve(
        new PreviewBoundsIntent(260, 64, 900, 700, 1.25),
        new Size(1200, 760));
    Assert.Equal(new Rectangle(260, 64, 900, 696), result);
}
```

Update Playwright to assert at 1440x900 and 1280x720 that `运行控制`, `实时预览`, `识别概览`, and the preview placeholder are simultaneously visible with no horizontal scroll.

- [ ] **Step 2: Run tests and verify RED**

```powershell
Push-Location ui
npm test -- --run src/app/App.test.tsx src/features/preview/MockPreviewCanvas.test.tsx
npm run e2e -- --grep "complete workbench"
Pop-Location
dotnet test src/Maple.Host.Tests/Maple.Host.Tests.csproj --filter PreviewLayoutTests
```

Expected: the Host still uses `SplitContainer`, no bounds intent is sent, and the new layout unit is missing.

- [ ] **Step 3: Implement browser-first layout and native overlay positioning**

`useNativePreviewBounds` observes the center stage and throttles reports to one animation frame:

```ts
export function useNativePreviewBounds(
  element: React.RefObject<HTMLElement | null>,
  sendCommand: (command: UiCommand) => void,
) {
  useLayoutEffect(() => {
    const node = element.current
    if (!node) return
    const publish = () => {
      const rect = node.getBoundingClientRect()
      sendCommand({ schemaVersion: 2, type: 'preview.boundsChanged', payload: {
        left: rect.left, top: rect.top, width: rect.width, height: rect.height,
        devicePixelRatio: window.devicePixelRatio,
      } })
    }
    const observer = new ResizeObserver(publish)
    observer.observe(node)
    publish()
    return () => observer.disconnect()
  }, [element, sendCommand])
}
```

Replace `SplitContainer` with a full-window `browserPanel`. Add `preview` as a sibling overlay, initially hidden. For accepted bounds, call `PreviewLayout.Resolve`, translate from browser client coordinates to form coordinates, set `preview.Bounds`, `preview.Visible = true`, and `preview.BringToFront()`. Keep the native emergency button above both. Invalid/stale bounds hide the surface and pause input.

Remove the normal `TelemetryStrip` render from `WorkbenchPage`; retain the component only for diagnostics/replay until removed separately. The center React stage becomes a transparent native-surface aperture with its headers and controls outside the reported rectangle.

- [ ] **Step 4: Run tests and verify GREEN**

Run Step 2. Expected: PASS at both desktop viewports and no horizontal scroll.

- [ ] **Step 5: Build and visually inspect the Windows shell**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/publish-windows.ps1 -SkipE2E
Start-Process -FilePath "$PWD\dist\windows-x64\Maple.exe"
```

Expected: original three-column controls are all visible and the real client frame occupies only the center stage.

- [ ] **Step 6: Commit**

```powershell
git add ui/src src/Maple.Host src/Maple.Host.Tests ui/tests
git commit -m "fix: embed native preview in workbench center"
```

### Task 3: Draw Detection Targets And Performance HUD Natively

**Files:**
- Create: `src/Maple.Preview/PreviewTelemetrySnapshot.cs`
- Create: `src/Maple.Preview/PreviewRenderModel.cs`
- Modify: `src/Maple.Preview/OverlaySnapshot.cs`
- Modify: `src/Maple.Preview/NativePreviewSurface.cs`
- Create: `src/Maple.Host.Tests/NativePreviewRenderModelTests.cs`
- Modify: `src/Maple.Host.Tests/Maple.Host.Tests.csproj`
- Modify: `ui/src/features/preview/OverlayLegend.tsx`
- Test: `ui/src/features/preview/overlay.test.ts`

- [ ] **Step 1: Write failing render-model tests**

Test fresh-only boxes, selected-target emphasis, telemetry severity, and collision-free HUD corner selection:

```csharp
[Fact]
public void Build_hides_expired_boxes_and_emphasizes_selected_monster()
{
    PreviewRenderModel model = PreviewRenderModel.Build(snapshot, telemetry, nowMonoMs: 500);
    Assert.Single(model.Monsters);
    Assert.Equal("monster-7", model.Monsters[0].TargetId);
    Assert.True(model.Monsters[0].Selected);
    Assert.DoesNotContain(model.Markers, marker => marker.Kind == "loot");
}
```

- [ ] **Step 2: Run test and verify RED**

```powershell
dotnet test src/Maple.Host.Tests/Maple.Host.Tests.csproj --filter NativePreviewRenderModelTests
```

Expected: missing render model and telemetry snapshot.

- [ ] **Step 3: Implement native HUD and overlay rendering**

Define:

```csharp
public sealed record PreviewTelemetrySnapshot(
    double CaptureFps, double RenderFps, double RecognitionFps,
    double FrameLatencyMs, double DetectorLatencyMs,
    string CaptureBackend, string InferenceProvider,
    long DroppedFrames, double ProcessMemoryMb,
    string SessionState, string LastAction, string? WarningCode);
```

Extend `OverlaySnapshot` with `SelectedTargetId` and `ModelVersion`. `PreviewRenderModel.Build` filters by TTL and validates normalized boxes before drawing. `NativePreviewSurface` tracks paint timestamps for render FPS and draws four compact translucent HUD bands inside the fitted frame. Use green Self, cyan Player, red Monster, and a 3 px selected-target border plus corner marker. Use amber when queue age/latency exceeds thresholds and red for stale/safety warnings. Do not draw loot or fixed UI ROIs.

- [ ] **Step 4: Run tests and verify GREEN**

Run Step 2 and the UI overlay test. Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Maple.Preview src/Maple.Host.Tests ui/src/features/preview
git commit -m "feat: draw native detections and telemetry hud"
```

### Task 4: Inspect And Validate The External ONNX Model

**Files:**
- Create: `src/Maple.Vision/OnnxModelInspector.cs`
- Create: `src/Maple.Vision/YoloTensorDecoder.cs`
- Create: `src/Maple.Vision/SelfIdentityTracker.cs`
- Modify: `src/Maple.Vision/ModelManifest.cs`
- Modify: `src/Maple.Vision/OnnxRuntimeInferenceEngine.cs`
- Modify: `src/Maple.Host/Program.cs`
- Create: `src/Maple.Runtime.Tests/Vision/OnnxModelInspectorTests.cs`
- Create: `src/Maple.Runtime.Tests/Vision/YoloTensorDecoderTests.cs`
- Create: `src/Maple.Runtime.Tests/Vision/SelfIdentityTrackerTests.cs`
- Modify: `tools/publish-windows.ps1`
- Modify: `tests/windows/publish_contract.tests.ps1`

- [ ] **Step 1: Write failing decoder and manifest tests**

Cover fixed `[N,6]`, YOLO `[1,4+C,N]`, YOLO `[1,N,4+C]`, invalid dimensions, class-role mapping, NMS, hash mismatch, and absent character/monster roles. Cover the Self resolver for a unique stable character, temporary occlusion, multiple ambiguous characters, and Player exposure. Example:

```csharp
[Fact]
public void Decode_yolo_channels_first_maps_snail_to_monster_role()
{
    var result = YoloTensorDecoder.Decode(
        tensor, new[] { 1, 6, 2 },
        new ModelClassMap(new[] { "character", "snail" },
            new Dictionary<string, DetectionRole> {
                ["character"] = DetectionRole.CharacterCandidate,
                ["snail"] = DetectionRole.Monster,
            }), 0.6, 0.45);
    Assert.Contains(result, item => item.Role == DetectionRole.Monster);
}
```

- [ ] **Step 2: Run tests and verify RED**

```powershell
dotnet test src/Maple.Runtime.Tests/Maple.Runtime.Tests.csproj --filter "OnnxModelInspectorTests|YoloTensorDecoderTests|SelfIdentityTrackerTests"
```

Expected: inspector, decoder, and role mapping do not exist.

- [ ] **Step 3: Implement inspection, decoding, and safe manifest generation**

`OnnxModelInspector` opens a session, records input/output names and shapes, reads Ultralytics `names` custom metadata when present, and classifies output layout. It returns a report without creating a detector if the output is unsupported. Extend the manifest with explicit class-to-role mappings and `OutputLayout`; require at least one CharacterCandidate class and at least one Monster class before model inference is ready.

The real external model is expected to report input `320x320`, Ultralytics YOLO output without embedded NMS, and classes `character`, `environment`, `item`, `mob`, `npc`, and `ui`. Map `character` to CharacterCandidate, `mob` to Monster, and ignore the four non-dynamic classes in the live overlay. Do not map every character directly to Self.

`SelfIdentityTracker` associates character candidates by IoU/center distance across frames. One stable candidate observed for the configured warm-up window becomes Self; additional stable character candidates become Players. If multiple candidates are equally plausible, the tracker returns `SELF_AMBIGUOUS` and actions stay disabled. A short occlusion preserves identity only until its TTL. Client identity/map reset clears all tracks. `CanDriveActions` becomes true only when this resolver currently exposes exactly one fresh high-confidence Self in addition to a valid Monster role.

Add a Host CLI:

```powershell
Maple.exe --inspect-model "C:\Users\Levi\Desktop\辅助\Kaelo_ok_sp\Kaelo_ok_sp\weights\best.onnx" --output "$env:LOCALAPPDATA\Maple\model-inspection.json"
```

The report includes the SHA-256, tensor metadata, embedded classes, supported layout, available providers, and `canDriveActions`; it never copies the model into the repository or distribution. `OnnxRuntimeInferenceEngine` selects the matching decoder and returns role-bearing candidates.

- [ ] **Step 4: Run tests and verify GREEN**

Run Step 2. Expected: PASS.

- [ ] **Step 5: Inspect the real external model**

```powershell
& .\dist\windows-x64\Maple.exe --inspect-model "C:\Users\Levi\Desktop\辅助\Kaelo_ok_sp\Kaelo_ok_sp\weights\best.onnx" --output "$env:LOCALAPPDATA\Maple\model-inspection.json"
Get-Content "$env:LOCALAPPDATA\Maple\model-inspection.json"
```

Expected: a concrete supported/unsupported report containing model metadata license `AGPL-3.0`, classes `character/environment/item/mob/npc/ui`, input `320x320`, and the actual output shape. If CharacterCandidate or Monster roles cannot be established, status remains `MODEL_CLASSES_INVALID`. If the model is valid but Self remains ambiguous, preview evaluation may continue while Task 8 stays blocked with `SELF_AMBIGUOUS`.

- [ ] **Step 6: Commit**

```powershell
git add src/Maple.Vision src/Maple.Host/Program.cs src/Maple.Runtime.Tests/Vision tools/publish-windows.ps1 tests/windows/publish_contract.tests.ps1
git commit -m "feat: inspect and decode yolo onnx models"
```

### Task 5: Add A Bounded Latest-Frame Vision Worker

**Files:**
- Create: `src/Maple.Host/LatestVisionFrameQueue.cs`
- Create: `src/Maple.Host/CompositeCaptureFrameObserver.cs`
- Create: `src/Maple.Host/VisionRuntimeService.cs`
- Modify: `src/Maple.Host/CaptureCoordinator.cs`
- Modify: `src/Maple.Host/Maple.Host.csproj`
- Modify: `src/Maple.Host.Tests/Maple.Host.Tests.csproj`
- Create: `src/Maple.Host.Tests/LatestVisionFrameQueueTests.cs`
- Create: `src/Maple.Host.Tests/CompositeCaptureFrameObserverTests.cs`
- Create: `src/Maple.Host.Tests/VisionRuntimeServiceTests.cs`

- [ ] **Step 1: Write failing ownership/backpressure tests**

Test that observing a capture copies pixels before the preview sink disposes the source, that publishing a third unconsumed frame replaces/disposes the oldest, and that cancellation/disposal releases all buffers.

```csharp
[Fact]
public void Observe_keeps_only_latest_owned_copy()
{
    using var queue = new LatestVisionFrameQueue(capacity: 1);
    using CapturedFrame first = Frames.Bgra(frameId: 1);
    using CapturedFrame second = Frames.Bgra(frameId: 2);
    queue.Observe(first);
    queue.Observe(second);
    using CapturedFrame read = queue.TakeLatest(CancellationToken.None);
    Assert.Equal(2, read.Metadata.FrameId);
    Assert.Equal(1, queue.DroppedFrames);
}
```

- [ ] **Step 2: Run tests and verify RED**

```powershell
dotnet test src/Maple.Host.Tests/Maple.Host.Tests.csproj --filter "LatestVisionFrameQueueTests|CompositeCaptureFrameObserverTests|VisionRuntimeServiceTests"
```

Expected: missing worker units.

- [ ] **Step 3: Implement the frame fan-out and worker**

`LatestVisionFrameQueue` uses a one-slot channel and `MemoryPool<byte>` ownership. `CompositeCaptureFrameObserver` invokes map storage and vision copying in deterministic order; each observer may read but not dispose the original. `VisionRuntimeService` owns a background loop:

```csharp
while (await frames.WaitToReadAsync(cancellationToken))
{
    using CapturedFrame frame = frames.TakeLatest(cancellationToken);
    long started = clock();
    VisionPipelineResult result = await pipeline.ProcessAsync(frame, target, clock(), cancellationToken);
    Publish(result, detectorLatencyMs: clock() - started);
}
```

It catches model/inference exceptions, publishes a faulted status, clears the latest observation, and invokes `HostSafetyCoordinator.PauseAndRelease` once per fault transition. It never blocks the capture/UI thread.

- [ ] **Step 4: Run tests and verify GREEN**

Run Step 2. Expected: PASS with disposal/backpressure assertions.

- [ ] **Step 5: Commit**

```powershell
git add src/Maple.Host src/Maple.Host.Tests
git commit -m "feat: process latest capture frames in vision worker"
```

### Task 6: Publish Real Observations To Native Preview And React

**Files:**
- Create: `src/Maple.Host/ObservationEventPublisher.cs`
- Create: `src/Maple.Host/RuntimeTelemetryCollector.cs`
- Modify: `src/Maple.Host/WebViewHostForm.cs`
- Modify: `src/Maple.Host/HostCompositionRoot.cs`
- Modify: `src/Maple.Host/NativePreviewFrameSink.cs`
- Modify: `src/Maple.Host/WindowsRuntimeDiagnostics.cs`
- Create: `src/Maple.Host.Tests/ObservationEventPublisherTests.cs`
- Create: `src/Maple.Host.Tests/RuntimeTelemetryCollectorTests.cs`
- Modify: `ui/src/store/sessionStore.ts`
- Modify: `ui/src/features/workbench/HealthPanel.tsx`
- Modify: `ui/src/features/preview/PreviewRegion.tsx`
- Test: `ui/src/app/App.test.tsx`

- [ ] **Step 1: Write failing publication tests**

Assert one vision result produces matching native overlay, `observation.updated`, `vision.status.updated`, and telemetry events with the same `frameId`; expired observations must produce an empty overlay and a repairing status.

- [ ] **Step 2: Run tests and verify RED**

```powershell
dotnet test src/Maple.Host.Tests/Maple.Host.Tests.csproj --filter "ObservationEventPublisherTests|RuntimeTelemetryCollectorTests"
Push-Location ui
npm test -- --run src/app/App.test.tsx
Pop-Location
```

- [ ] **Step 3: Implement real event and HUD publication**

`ObservationEventPublisher` serializes only structured state to WebView2 while calling `NativePreviewSurface.PublishOverlay` and `PublishTelemetry` directly. `RuntimeTelemetryCollector` uses rolling one-second windows for FPS, `Stopwatch.GetTimestamp` for latency, `Process.WorkingSet64` for memory, and provider/backend labels from the actual runtime. Replace all misleading ready/default labels: no model becomes `模型未配置`; no observation becomes `等待首帧`; stale becomes `正在重新识别`.

Compose `Maple.Vision` into `Maple.Host.csproj` and `HostCompositionRoot`; create the model only after manifest validation. Configure capture with a composite map/vision observer. Do not use `MockVisionProvider` in production composition.

- [ ] **Step 4: Run tests and verify GREEN**

Run Step 2. Expected: PASS.

- [ ] **Step 5: Run observation-only Windows acceptance**

Start Maple with the client foreground and verify the center HUD shows non-zero capture/render/recognition rates. Capture a screenshot and JSONL session under `%LOCALAPPDATA%\Maple\evidence\vision-<timestamp>`; no input is armed in this step.

- [ ] **Step 6: Commit**

```powershell
git add src/Maple.Host src/Maple.Host.Tests ui/src
git commit -m "feat: publish live vision observations"
```

### Task 7: Make Combat And Key Configuration Real

**Files:**
- Create: `src/Maple.Host/CombatConfiguration.cs`
- Create: `src/Maple.Host/CombatConfigurationStore.cs`
- Modify: `src/Maple.Host/HostCommandDispatcher.cs`
- Modify: `src/Maple.Host/BrokerActionExecutor.cs`
- Modify: `src/Maple.Input/BrokerKeyProfile.cs`
- Modify: `ui/src/features/workbench/SessionControls.tsx`
- Create: `ui/src/features/workbench/KeyBindingEditor.tsx`
- Modify: `ui/src/store/sessionStore.ts`
- Test: `src/Maple.Host.Tests/HostCommandDispatcherTests.cs`
- Create: `src/Maple.Host.Tests/CombatConfigurationStoreTests.cs`
- Modify: `src/Maple.Host.Tests/BrokerActionExecutorTests.cs`
- Modify: `ui/src/features/workbench/SessionControls.test.tsx`

- [ ] **Step 1: Write failing persistence and UI tests**

Test default bindings, atomic persistence, corrupt-file fallback, percentage normalization, pause-before-change, profile conflict rejection, and action executor use of the active native profile.

```csharp
[Fact]
public async Task Save_and_load_preserves_validated_combat_configuration()
{
    CombatConfiguration config = CombatConfiguration.Default with { AttackKey = "Ctrl", PickupEnabled = true };
    await store.SaveAsync(config, CancellationToken.None);
    Assert.Equal(config, await store.LoadAsync(CancellationToken.None));
}
```

- [ ] **Step 2: Run tests and verify RED**

```powershell
dotnet test src/Maple.Host.Tests/Maple.Host.Tests.csproj --filter "CombatConfigurationStoreTests|BrokerActionExecutorTests|HostCommandDispatcherTests"
Push-Location ui
npm test -- --run src/features/workbench/SessionControls.test.tsx
Pop-Location
```

- [ ] **Step 3: Implement native configuration and complete controls**

Persist `%LOCALAPPDATA%\Maple\config\combat-v2.json` using write-to-temp plus atomic replace. Defaults are direction keys for movement, Alt jump, Z pickup, J single attack, A area attack, 1 HP potion, and 2 MP potion. Validate supported logical keys and reject duplicates that create ambiguous simultaneous actions.

The left panel displays live HP/MP values, HP and MP thresholds, attack mode, attack key/profile, potion keys, jump, pickup, preferred distance, and pickup toggle. Opening the key editor pauses and releases keys. `BrokerActionExecutor` resolves the configured logical key through the native profile; React still cannot submit scan codes or flags.

- [ ] **Step 4: Run tests and verify GREEN**

Run Step 2. Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Maple.Host src/Maple.Input ui/src
git commit -m "feat: persist combat and key configuration"
```

### Task 8: Connect The Production Orchestrator With Fail-Closed Arming

**Files:**
- Create: `src/Maple.Host/AutomaticCombatController.cs`
- Create: `src/Maple.Host/LiveObservationSource.cs`
- Create: `src/Maple.Host/ValidatedMapPlatformResolver.cs`
- Modify: `src/Maple.Host/HostCompositionRoot.cs`
- Modify: `src/Maple.Host/WebViewHostForm.cs`
- Modify: `src/Maple.Host/ForegroundSessionController.cs`
- Modify: `src/Maple.Runtime/ProductionOrchestrator.cs`
- Create: `src/Maple.Host.Tests/AutomaticCombatControllerTests.cs`
- Create: `src/Maple.Host.Tests/LiveObservationSourceTests.cs`
- Modify: `src/Maple.Runtime.Tests/Runtime/ProductionOrchestratorTests.cs`

- [ ] **Step 1: Write failing lifecycle and same-platform tests**

Test that arm is rejected for missing/unsupported model, no unique Self, stale frame, unvalidated map, unresolved platform, unhealthy HP/MP, or broker not ready. Test one valid sequence: move toward same-platform snail, release at attack distance, attack, consume next observation. Every cancellation/fault must call `ReleaseAll`.

```csharp
[Fact]
public async Task Valid_same_platform_target_moves_then_attacks_and_releases()
{
    await controller.ArmAsync(CancellationToken.None);
    await observations.PublishAsync(Fixtures.SnailFarRight(frameId: 10));
    await observations.PublishAsync(Fixtures.SnailInRange(frameId: 11));
    await observations.PublishAsync(Fixtures.SnailHit(frameId: 12));
    Assert.Equal(new[] { "MoveRight.Down", "MoveRight.Up", "Attack.Down", "Attack.Up", "ReleaseAll" }, executor.Trace);
}
```

- [ ] **Step 2: Run tests and verify RED**

```powershell
dotnet test src/Maple.Host.Tests/Maple.Host.Tests.csproj --filter "AutomaticCombatControllerTests|LiveObservationSourceTests"
dotnet test src/Maple.Runtime.Tests/Maple.Runtime.Tests.csproj --filter ProductionOrchestratorTests
```

- [ ] **Step 3: Implement controller ownership and arming gates**

`LiveObservationSource` is a bounded channel of newest fused observations. `ValidatedMapPlatformResolver` maps Self and monsters to validated platform intervals. `AutomaticCombatController` owns one run task and cancellation source, maps session arm/pause/emergency/foreground events to orchestrator lifecycle, publishes state transitions, and always awaits `ReleaseAll` in a `finally` block.

The controller sets `Navigating`, `Attacking`, `Looting`, or `UsingPotion` only after the corresponding abstract action is accepted. It sets `Observing` while safely watching and `Paused` with the exact gate reason otherwise. It does not auto-arm after manual pause, foreground loss, or EmergencyStop.

- [ ] **Step 4: Run tests and verify GREEN**

Run Step 2. Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Maple.Host src/Maple.Runtime src/Maple.Host.Tests src/Maple.Runtime.Tests
git commit -m "feat: connect live automatic combat controller"
```

### Task 9: Add Recorded-Frame Accuracy And Performance Evidence

**Files:**
- Create: `tests/fixtures/client-snail-map/README.md`
- Create: `tests/windows/live_vision_evidence.tests.ps1`
- Create: `tests/windows/live_combat_evidence.tests.ps1`
- Modify: `tests/windows/publish_contract.tests.ps1`
- Modify: `tools/publish-windows.ps1`
- Modify: `docs/maple-runtime/VERIFICATION_2026-08-14.md`
- Modify: `docs/WINDOWS_IMPLEMENTATION_HANDOFF_2026-08-14.md`

- [ ] **Step 1: Write failing Windows evidence contracts**

Require a session directory containing `session.json`, `telemetry.jsonl`, `observations.jsonl`, `before.png`, `overlay.png`, and `after.png`. Validate real PNG signatures, monotonic frame IDs, non-zero FPS, P95 preview latency, model SHA, one unique Self, at least one snail, no Player selected as target, release evidence, and explicit human confirmation for the supervised action result.

- [ ] **Step 2: Run evidence scripts and verify RED**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/windows/live_vision_evidence.tests.ps1 -RequireEvidence
powershell -NoProfile -ExecutionPolicy Bypass -File tests/windows/live_combat_evidence.tests.ps1 -RequireEvidence
```

Expected: fail because no real evidence session exists yet.

- [ ] **Step 3: Capture a supervised current-client evidence session**

With the game foreground and the user-authorized test map open:

1. Run observation-only for 60 seconds and record capture/render/recognition FPS and latency.
2. Confirm the green Self box follows the controlled character and red boxes follow snails for at least 300 frames.
3. Arm one same-platform seek-and-attack cycle with pickup disabled.
4. Trigger pause, foreground loss, and F12 separately and confirm all keys released.
5. Record screenshots and JSONL under `%LOCALAPPDATA%\Maple\evidence\client-snail-<timestamp>`.

- [ ] **Step 4: Run evidence scripts and verify GREEN**

Run Step 2. Expected: `LIVE_VISION_EVIDENCE=PASS` and `LIVE_COMBAT_EVIDENCE=PASS` only if all recorded criteria are met.

- [ ] **Step 5: Update evidence docs without overstating scope**

Record exact model hash, client resolution/DPI, providers, P50/P95/P99, detection counts, action matrix, and remaining cross-platform/map risks. Mark only the verified snail-map same-platform path as passed.

- [ ] **Step 6: Commit**

```powershell
git add tests/windows tests/fixtures/client-snail-map tools/publish-windows.ps1 docs
git commit -m "test: record live vision combat evidence"
```

### Task 10: Full Verification, Visual QA, And Release Build

**Files:**
- Modify only files required by failures found in this task.

- [ ] **Step 1: Run the full portable gate**

```powershell
node tools/verify-portable.mjs
```

Expected: `PORTABLE_VERIFICATION=PASS`, with no lint, type, test, build, audit, or diff-check failure.

- [ ] **Step 2: Run production input and publish gates**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/windows/production_input_contract.tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/publish-windows.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/windows/publish_contract.tests.ps1 -PublishDirectory dist/windows-x64
```

Expected: production input and publish contracts pass, no driver artifacts appear, Host remains `asInvoker`, and Broker remains `requireAdministrator`.

- [ ] **Step 3: Perform native visual QA**

Launch `dist\windows-x64\Maple.exe` at 1440x900 and 1280x720. Capture screenshots proving:

- complete left controls, center native preview, and right status are visible together;
- FPS/latency/provider/memory values are drawn inside the client frame;
- Self is green, Player cyan, Monster red, and selected Monster emphasized;
- HP/MP and keys show real values outside the preview;
- model missing/fault states do not claim automatic operation;
- no overlap, clipped text, unexpected scroll, or stale box remains.

- [ ] **Step 4: Run soak and release checks**

Run observation-only for 30 minutes, then the supervised auto-combat profile for the approved duration. Confirm bounded memory, no stuck keys, responsive F9/F12, and no unbounded frame queue. Preserve the logs in the evidence session.

- [ ] **Step 5: Review the final diff and commit fixes**

```powershell
git diff --check
git status --short
git diff --stat
```

Commit only verified fixes with scoped messages. Do not mark cross-platform navigation, broad model accuracy, or long-duration production readiness complete unless their separate evidence passes.

---

## Plan Self-Review

- The plan covers the approved design's layout repair, native HUD, real overlay, model gate, frame ownership, observation events, combat configuration, runtime closed loop, safety failure states, Windows evidence, and final verification.
- The external model remains outside Git and cannot arm actions without an explicit Self/Monster role mapping and real-frame accuracy evidence.
- React never receives raw frames or sends raw input. The production path remains normal-integrity Host plus elevated `Maple.InputBroker.exe` with abstract actions only.
- The first Windows action scope is a supervised same-platform snail cycle. Ladder/cross-platform behavior remains a later evidence milestone.
- No task treats simulated boxes, source compilation, or model loading alone as product completion.

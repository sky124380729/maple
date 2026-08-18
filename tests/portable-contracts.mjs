import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const read = (relative) => fs.readFileSync(path.join(root, relative), 'utf8')
const requireTokens = (relative, tokens) => {
  const source = read(relative)
  for (const token of tokens) {
    if (!source.includes(token)) throw new Error(`${relative} 缺少 ${token}`)
  }
  return source
}

requireTokens('src/Maple.Core/SafetyGate.cs', ['CalibrationRequired', 'WindowNotForeground', 'InputUnavailable'])
requireTokens('src/Maple.Core/ActionPolicy.cs', ['MoveLeft', 'MoveRight', 'Attack', 'Replan', 'UsePotion'])
requireTokens('src/Maple.Contracts/DomainContracts.cs', ['MaxAttackDurationMs', 'CombatRhythmSnapshot', 'CombatRhythmUpdated'])
requireTokens('src/Maple.Core/ActionJournal.cs', ['Precondition', 'KeyDown', 'Observe', 'EarlyReleaseOrTimeout', 'KeyUp', 'Postcondition'])
requireTokens('src/Maple.Map/MapWorld.cs', ['CanProduceActions', 'Candidate', 'Validated', 'Archived'])
requireTokens('src/Maple.Map/TopologyValidator.cs', ['MinimumCoverage', 'MaximumCalibrationErrorPx'])
requireTokens('src/Maple.Replay/ReplayClock.cs', ['Pause()', 'Resume()', 'SetSpeed', 'Step('])
requireTokens('src/Maple.Vision/ObservationFusion.cs', ['FreshUntilMonoMs', 'ResourceConflictTolerance', 'HealthUnknown'])
requireTokens('src/Maple.Cloud/MockBailianMapClient.cs', ['UploadNotApproved', 'Offline', 'Timeout', 'MalformedResponse'])
requireTokens('src/Maple.Input/NullInputAdapter.cs', ['INPUT_INJECTION=DISABLED'])
requireTokens('src/Maple.Input/ReplayInputAdapter.cs', ['REPLAY_ONLY', 'ActiveKeyRegistry'])
const hidSource = requireTokens('src/Maple.Input/WindowsVirtualHidAdapter.cs', ['HID_CONTRACT_UNVERIFIED', 'IVirtualHidTransport', 'IVirtualHidReportEncoder'])
for (const forbidden of ['CreateFile', 'WriteFile', 'DeviceIoControl', 'VID_', 'PID_']) {
  if (hidSource.includes(forbidden)) throw new Error(`HID 合同包含未验证原生细节：${forbidden}`)
}
requireTokens('src/Maple.Host/BridgeMessageRouter.cs', ['session.emergencyStop', 'UNKNOWN_COMMAND_REJECTED', 'ContainsForbiddenField', 'ValidatePayload'])
requireTokens('schemas/bridge.schema.json', ['combat.rhythm.updated', 'attackHolding', 'movementGap', 'resting'])
requireTokens('src/Maple.Host/HostSafetyCoordinator.cs', ['PauseAndRelease', 'EmergencyStop', 'ReleaseForShutdown'])
requireTokens('src/Maple.Cloud/BailianMapHttpClient.cs', ['BailianHttpClient.Endpoint', 'CloudUploadApproved', 'HasMatchingProvenance', '不得输出路线、按键或动作'])
requireTokens('src/Maple.Preview/NativePreviewSurface.cs', ['FrameSlot<Bitmap>', 'OverlayColors.Self', 'OverlayColors.Player', 'OverlayColors.Monster'])
requireTokens('src/Maple.Capture/WindowsGraphicsCaptureBackend.cs', ['WGC_RUNTIME_NOT_BOUND'])

const mapFixture = JSON.parse(read('tests/fixtures/forest-east/map-candidate.json'))
if (mapFixture.state !== 'candidate' || mapFixture.coverage < 0.85 || mapFixture.unresolvedStructures.length !== 0) throw new Error('地图候选夹具不满足验证前提')
const replayFixture = JSON.parse(read('tests/fixtures/forest-east/replay-events.json'))
for (let index = 1; index < replayFixture.length; index += 1) {
  if (replayFixture[index].timestampMonoMs < replayFixture[index - 1].timestampMonoMs) throw new Error('回放时间戳不是单调递增')
}
for (const relative of ['hid-device-report.template.json', 'hid-os-report.template.json', 'hid-client-response.template.json']) {
  if (JSON.parse(read(`tests/fixtures/windows-hid/${relative}`)).status !== 'PENDING') throw new Error(`${relative} 不得伪造 PASS 证据`)
}

console.log('PORTABLE_CONTRACTS=PASS')
console.log('WINDOWS_NATIVE_EVIDENCE=PENDING')
console.log('WINDOWS_HID_EVIDENCE=PENDING')

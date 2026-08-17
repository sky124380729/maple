import { CONTRACT_SCHEMA_VERSION, uiCommandSchema, type HostEvent, type UiCommand } from '../contracts/bridge'
import { createMockSessionEvents, createMockTelemetryEvent, mockCombatConfiguration } from '../mock/mockSession'
import type { BridgeResult, HostBridge, HostEventListener } from './HostBridge'

export interface MockHostBridgeOptions {
  telemetryIntervalMs?: number
}

export function createMockHostBridge(options: MockHostBridgeOptions = {}): HostBridge {
  const listeners = new Set<HostEventListener>()
  const telemetryIntervalMs = options.telemetryIntervalMs ?? 1_000
  let disposed = false
  let tick = 0
  let cloudStatus: Extract<HostEvent, { type: 'cloud.status.updated' }>['payload'] = {
    provider: 'bailian',
    enabled: false,
    credentialConfigured: false,
    modelId: 'qwen3-vl-plus',
    connectionStatus: 'notConfigured',
    requestInFlight: false,
    lastErrorCode: null,
  }
  let inputStatus: Extract<HostEvent, { type: 'input.status.updated' }>['payload'] = {
    provider: 'inputBroker',
    status: 'disconnected',
    integrity: 'unknown',
    activeKeys: [],
    lastReleaseSucceeded: true,
    hotkeys: { pauseResume: 'F9', emergencyStop: 'F12' },
    errorCode: null,
  }
  let combatConfiguration: Extract<HostEvent, { type: 'config.updated' }>['payload'] = { ...mockCombatConfiguration }
  let mapScan: Extract<HostEvent, { type: 'map.scan.updated' }>['payload'] = { scanning: false, frameIds: [] }
  let mapStatus: Extract<HostEvent, { type: 'map.status.updated' }>['payload'] | undefined

  const emit = (event: HostEvent) => {
    if (disposed) return
    listeners.forEach((listener) => listener(event))
  }

  const emitSessionState = (state: Extract<HostEvent, { type: 'session.stateChanged' }>['payload']['state'], pauseReason: Extract<HostEvent, { type: 'session.stateChanged' }>['payload']['pauseReason']) => {
    emit({ schemaVersion: CONTRACT_SCHEMA_VERSION, type: 'session.stateChanged', payload: { state, pauseReason } })
  }
  const emitCloudStatus = () => emit({ schemaVersion: CONTRACT_SCHEMA_VERSION, type: 'cloud.status.updated', payload: cloudStatus })
  const emitInputStatus = () => emit({ schemaVersion: CONTRACT_SCHEMA_VERSION, type: 'input.status.updated', payload: inputStatus })
  const emitMapScan = () => emit({ schemaVersion: CONTRACT_SCHEMA_VERSION, type: 'map.scan.updated', payload: mapScan })
  const emitMapStatus = () => { if (mapStatus) emit({ schemaVersion: CONTRACT_SCHEMA_VERSION, type: 'map.status.updated', payload: mapStatus }) }

  let interval: ReturnType<typeof setInterval> | undefined

  const ensureTelemetryStarted = () => {
    if (!interval) interval = setInterval(() => emit(createMockTelemetryEvent(++tick)), telemetryIntervalMs)
  }

  const requestSnapshot = (): BridgeResult => {
    if (disposed) return { ok: false, reason: 'disposed' }
    createMockSessionEvents(tick).forEach(emit)
    emitMapScan()
    emitMapStatus()
    return { ok: true }
  }

  return {
    kind: 'mock',
    send(command: UiCommand) {
      if (disposed) return { ok: false, reason: 'disposed' }
      const result = uiCommandSchema.safeParse(command)
      if (!result.success) return { ok: false, reason: 'invalid-command' }

      switch (result.data.type) {
        case 'snapshot.request': return requestSnapshot()
        case 'session.arm':
          inputStatus = { ...inputStatus, status: 'starting', integrity: 'unknown' }; emitInputStatus()
          emitSessionState('Arming', 'None')
          inputStatus = { ...inputStatus, status: 'ready', integrity: 'high' }; emitInputStatus()
          emitSessionState('Observing', 'None')
          break
        case 'combat.trial.start':
          inputStatus = { ...inputStatus, status: 'ready', integrity: 'high' }; emitInputStatus()
          emitSessionState('Observing', 'None')
          break
        case 'stationary.attack.set':
          inputStatus = { ...inputStatus, status: result.data.payload.enabled ? 'ready' : 'paused', integrity: 'high', activeKeys: [] }; emitInputStatus()
          emitSessionState(result.data.payload.enabled ? 'Attacking' : 'Paused', result.data.payload.enabled ? 'None' : 'OperatorRequested')
          break
        case 'session.pause':
          inputStatus = { ...inputStatus, status: 'paused', activeKeys: [], lastReleaseSucceeded: true }; emitInputStatus()
          emitSessionState('Paused', 'OperatorRequested')
          break
        case 'session.resume':
          inputStatus = { ...inputStatus, status: 'starting' }; emitInputStatus()
          emitSessionState('Arming', 'None')
          inputStatus = { ...inputStatus, status: 'ready', integrity: 'high' }; emitInputStatus()
          emitSessionState('Observing', 'None')
          break
        case 'session.emergencyStop':
          inputStatus = { ...inputStatus, status: 'paused', activeKeys: [], lastReleaseSucceeded: true }; emitInputStatus()
          emitSessionState('EmergencyStop', 'OperatorRequested')
          break
        case 'map.scan.start':
          mapScan = { scanning: true, frameIds: [] }
          emitMapScan()
          emitSessionState('MapScanning', 'None')
          break
        case 'map.calibration.start':
          mapScan = { scanning: false, frameIds: [100, 101, 102, 103] }
          emitMapScan()
          emitSessionState('MapCalibrating', 'None')
          break
        case 'map.calibration.confirm':
          {
            const mapId = (result.data as Extract<UiCommand, { type: 'map.calibration.confirm' }>).payload.mapId
            mapStatus = { mapId, state: 'validated', coverage: 0.92, calibrationErrorPx: 2, platformCount: 8, ladderCount: 3, errors: [], canProduceActions: true }
            emitMapStatus()
            break
          }
        case 'config.update':
          combatConfiguration = { ...combatConfiguration, ...result.data.payload }
          emit({ schemaVersion: CONTRACT_SCHEMA_VERSION, type: 'config.updated', payload: combatConfiguration })
          emit({ schemaVersion: CONTRACT_SCHEMA_VERSION, type: 'log.appended', payload: { level: 'info', code: 'CONFIG_UPDATED', message: '模拟宿主已接收配置更新' } })
          break
        case 'cloud.credential.set':
          cloudStatus = { ...cloudStatus, credentialConfigured: true, connectionStatus: 'notConfigured', lastErrorCode: null }
          emitCloudStatus()
          break
        case 'cloud.credential.clear':
          cloudStatus = { ...cloudStatus, enabled: false, credentialConfigured: false, connectionStatus: 'notConfigured', requestInFlight: false, lastErrorCode: null }
          emitCloudStatus()
          break
        case 'cloud.config.update':
          cloudStatus = { ...cloudStatus, enabled: result.data.payload.enabled, modelId: result.data.payload.modelId }
          emitCloudStatus()
          break
        case 'cloud.connection.test':
          cloudStatus = { ...cloudStatus, connectionStatus: 'checking', requestInFlight: true }
          emitCloudStatus()
          cloudStatus = { ...cloudStatus, connectionStatus: cloudStatus.credentialConfigured ? 'ready' : 'notConfigured', requestInFlight: false }
          emitCloudStatus()
          break
        case 'cloud.map.annotate':
          mapStatus = {
            mapId: result.data.payload.mapId,
            state: 'candidate',
            coverage: 0.92,
            calibrationErrorPx: 2,
            platformCount: 8,
            ladderCount: 3,
            errors: [],
            canProduceActions: false,
          }
          emitMapStatus()
          emit({ schemaVersion: CONTRACT_SCHEMA_VERSION, type: 'log.appended', payload: { level: 'info', code: 'CLOUD_MAP_ANNOTATED', message: '模拟百炼已完成初始地图结构标注' } })
          break
      }
      return { ok: true }
    },
    subscribe(listener) {
      if (disposed) return () => undefined
      listeners.add(listener)
      ensureTelemetryStarted()
      return () => {
        listeners.delete(listener)
        if (listeners.size === 0 && interval) {
          clearInterval(interval)
          interval = undefined
        }
      }
    },
    requestSnapshot,
    dispose() {
      if (disposed) return
      disposed = true
      if (interval) clearInterval(interval)
      listeners.clear()
    },
  }
}

import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest'
import type { HostEvent, UiCommand } from '../contracts/bridge'
import { createHostBridge } from './HostBridge'
import { createMockHostBridge } from './MockHostBridge'
import { createSessionStore } from '../store/sessionStore'
import { createTelemetryStore } from '../store/telemetryStore'

const pauseCommand = { schemaVersion: 2, type: 'session.pause', payload: {} } as const
const emergencyCommand = { schemaVersion: 2, type: 'session.emergencyStop', payload: { message: '用户请求停止' } } as const

describe('typed host bridge', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  test('posts only validated commands and requests a typed snapshot', () => {
    const posted: unknown[] = []
    const listeners = new Set<(event: MessageEvent<unknown>) => void>()
    const webview = {
      postMessage: (message: unknown) => posted.push(message),
      addEventListener: (_type: 'message', listener: (event: MessageEvent<unknown>) => void) => listeners.add(listener),
      removeEventListener: (_type: 'message', listener: (event: MessageEvent<unknown>) => void) => listeners.delete(listener),
    }
    const bridge = createHostBridge({ chrome: { webview } })

    expect(bridge.send(pauseCommand)).toEqual({ ok: true })
    expect(bridge.requestSnapshot()).toEqual({ ok: true })
    expect(bridge.send({ schemaVersion: 1, type: 'raw.key', payload: { key: 'A' } } as unknown as UiCommand)).toEqual({ ok: false, reason: 'invalid-command' })
    expect(posted).toEqual([
      pauseCommand,
      { schemaVersion: 2, type: 'snapshot.request', payload: {} },
    ])
  })

  test('validates incoming host events and detaches listeners on dispose', () => {
    const listeners = new Set<(event: MessageEvent<unknown>) => void>()
    const webview = {
      postMessage: vi.fn(),
      addEventListener: (_type: 'message', listener: (event: MessageEvent<unknown>) => void) => listeners.add(listener),
      removeEventListener: (_type: 'message', listener: (event: MessageEvent<unknown>) => void) => listeners.delete(listener),
    }
    const bridge = createHostBridge({ chrome: { webview } })
    const received: HostEvent[] = []
    bridge.subscribe((event) => received.push(event))

    const validEvent = { schemaVersion: 2, type: 'session.stateChanged', payload: { state: 'Paused', pauseReason: 'OperatorRequested' } }
    listeners.forEach((listener) => listener({ data: validEvent } as MessageEvent<unknown>))
    listeners.forEach((listener) => listener({ data: { schemaVersion: 1, type: 'raw.key', payload: {} } } as MessageEvent<unknown>))

    expect(received).toEqual([validEvent])
    bridge.dispose()
    expect(listeners.size).toBe(0)
  })

  test('returns an explicit unavailable result outside WebView2', () => {
    const bridge = createHostBridge({})

    expect(bridge.send(pauseCommand)).toEqual({ ok: false, reason: 'unavailable' })
    expect(bridge.requestSnapshot()).toEqual({ ok: false, reason: 'unavailable' })
  })

  test('mock host emits deterministic safe events without input results', () => {
    const bridge = createMockHostBridge()
    const received: HostEvent[] = []
    bridge.subscribe((event) => received.push(event))

    expect(bridge.requestSnapshot()).toEqual({ ok: true })
    expect(bridge.send(pauseCommand)).toEqual({ ok: true })
    expect(bridge.send(emergencyCommand)).toEqual({ ok: true })

    expect(received.map((event) => event.type)).toEqual(expect.arrayContaining([
      'target.updated',
      'session.stateChanged',
      'telemetry.updated',
      'preview.availabilityChanged',
      'observation.updated',
      'log.appended',
      'input.status.updated',
    ]))
    expect(received.some((event) => event.type === 'input.result')).toBe(false)
    expect(received.some((event) => event.type === 'log.appended' && event.payload.code === 'INPUT_BROKER_STANDBY')).toBe(true)
    expect(received.filter((event) => event.type === 'session.stateChanged').at(-1)?.payload.state).toBe('EmergencyStop')
  })

  test('disposal stops mock timers and clears subscribers', () => {
    const bridge = createMockHostBridge({ telemetryIntervalMs: 100 })
    const listener = vi.fn()
    bridge.subscribe(listener)
    bridge.requestSnapshot()
    const countBeforeDispose = listener.mock.calls.length

    bridge.dispose()
    vi.advanceTimersByTime(1_000)

    expect(listener).toHaveBeenCalledTimes(countBeforeDispose)
    expect(bridge.send(pauseCommand)).toEqual({ ok: false, reason: 'disposed' })
  })

  test('session and telemetry stores reduce typed events independently', () => {
    const sessionStore = createSessionStore()
    const telemetryStore = createTelemetryStore({ historyLimit: 2 })
    const stateEvent = { schemaVersion: 2, type: 'session.stateChanged', payload: { state: 'Paused', pauseReason: 'OperatorRequested' } } as const
    const telemetryEvent = {
      schemaVersion: 2,
      type: 'telemetry.updated',
      payload: {
        schemaVersion: 2,
        timestamp: '2026-08-14T12:00:00Z',
        captureFps: 60,
        renderFps: 60,
        recognitionFps: 30,
        frameLatencyMs: 18,
        detectorLatencyMs: 12,
        droppedFrames: 0,
        queueAgeMs: 2,
        processMemoryMb: 256,
        inferenceProvider: 'directml',
        captureBackend: 'WGC',
        lastAction: null,
        warningCode: null,
        state: 'Observing',
        pauseReason: 'None',
      },
    } as const

    sessionStore.getState().applyHostEvent(stateEvent)
    telemetryStore.getState().applyHostEvent(telemetryEvent)
    telemetryStore.getState().applyHostEvent({ ...telemetryEvent, payload: { ...telemetryEvent.payload, captureFps: 59 } })
    telemetryStore.getState().applyHostEvent({ ...telemetryEvent, payload: { ...telemetryEvent.payload, captureFps: 58 } })

    expect(sessionStore.getState()).toMatchObject({
      sessionState: 'Paused',
      pauseReason: 'OperatorRequested',
      inputStatus: { provider: 'inputBroker', status: 'disconnected' },
    })
    expect(telemetryStore.getState().latest?.captureFps).toBe(58)
    expect(telemetryStore.getState().history.map((item) => item.captureFps)).toEqual([59, 58])
  })
})

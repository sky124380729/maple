import { createStore } from 'zustand/vanilla'
import type { HostEvent, InputBrokerStatus, ObservationSnapshot, PauseReason, SessionState, TargetBinding, VisionStatus } from '../contracts/bridge'

type PreviewAvailability = Extract<HostEvent, { type: 'preview.availabilityChanged' }>['payload']
type LogEntry = Extract<HostEvent, { type: 'log.appended' }>['payload']
type CloudStatus = Extract<HostEvent, { type: 'cloud.status.updated' }>['payload']

export interface SessionStoreState {
  target?: TargetBinding
  sessionState: SessionState
  pauseReason: PauseReason
  resumeCountdown: number | null
  preview: PreviewAvailability
  observation?: ObservationSnapshot
  logs: LogEntry[]
  cloudStatus: CloudStatus
  inputStatus: InputBrokerStatus
  visionStatus: VisionStatus
  applyHostEvent(event: HostEvent): void
  reset(): void
}

const initialState = {
  sessionState: 'Stopped' as SessionState,
  pauseReason: 'None' as PauseReason,
  resumeCountdown: null,
  preview: { available: false, reason: '等待宿主连接' },
  logs: [] as LogEntry[],
  inputStatus: {
    provider: 'inputBroker' as const,
    status: 'disconnected' as const,
    integrity: 'unknown' as const,
    activeKeys: [],
    lastReleaseSucceeded: true,
    hotkeys: { pauseResume: 'F9' as const, emergencyStop: 'F12' as const },
    errorCode: null,
  },
  visionStatus: { status: 'notConfigured' as const, modelId: null, provider: 'none' as const, diagnostic: 'MODEL_NOT_CONFIGURED' },
  cloudStatus: {
    provider: 'bailian' as const,
    enabled: false,
    credentialConfigured: false,
    modelId: 'qwen3-vl-plus' as const,
    connectionStatus: 'notConfigured' as const,
    requestInFlight: false,
    lastErrorCode: null,
  },
}

export function createSessionStore() {
  return createStore<SessionStoreState>((set) => ({
    ...initialState,
    applyHostEvent(event) {
      switch (event.type) {
        case 'target.updated': set({ target: event.payload }); break
        case 'session.stateChanged': set({ sessionState: event.payload.state, pauseReason: event.payload.pauseReason, resumeCountdown: event.payload.resumeCountdown ?? null }); break
        case 'preview.availabilityChanged': set({ preview: event.payload }); break
        case 'observation.updated': set({ observation: event.payload }); break
        case 'log.appended': set((state) => ({ logs: [...state.logs, event.payload].slice(-200) })); break
        case 'cloud.status.updated': set({ cloudStatus: event.payload }); break
        case 'input.status.updated': set({ inputStatus: event.payload }); break
        case 'vision.status.updated': set((state) => ({
          visionStatus: event.payload,
          observation: event.payload.status === 'ready' ? state.observation : undefined,
        })); break
      }
    },
    reset() { set(initialState) },
  }))
}

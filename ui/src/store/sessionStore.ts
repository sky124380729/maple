import { createStore } from 'zustand/vanilla'
import type { HostEvent, ObservationSnapshot, PauseReason, SessionState, TargetBinding } from '../contracts/bridge'

type PreviewAvailability = Extract<HostEvent, { type: 'preview.availabilityChanged' }>['payload']
type LogEntry = Extract<HostEvent, { type: 'log.appended' }>['payload']
type CloudStatus = Extract<HostEvent, { type: 'cloud.status.updated' }>['payload']

export interface SessionStoreState {
  target?: TargetBinding
  sessionState: SessionState
  pauseReason: PauseReason
  preview: PreviewAvailability
  observation?: ObservationSnapshot
  logs: LogEntry[]
  cloudStatus: CloudStatus
  inputInjection: 'DISABLED'
  applyHostEvent(event: HostEvent): void
  reset(): void
}

const initialState = {
  sessionState: 'Stopped' as SessionState,
  pauseReason: 'None' as PauseReason,
  preview: { available: false, reason: '等待宿主连接' },
  logs: [] as LogEntry[],
  inputInjection: 'DISABLED' as const,
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
        case 'session.stateChanged': set({ sessionState: event.payload.state, pauseReason: event.payload.pauseReason }); break
        case 'preview.availabilityChanged': set({ preview: event.payload }); break
        case 'observation.updated': set({ observation: event.payload }); break
        case 'log.appended': set((state) => ({ logs: [...state.logs, event.payload].slice(-200) })); break
        case 'cloud.status.updated': set({ cloudStatus: event.payload }); break
      }
    },
    reset() { set(initialState) },
  }))
}

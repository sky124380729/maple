import { createStore } from 'zustand/vanilla'
import type { HostEvent, TelemetrySnapshot } from '../contracts/bridge'

export interface TelemetryStoreState {
  latest?: TelemetrySnapshot
  history: TelemetrySnapshot[]
  applyHostEvent(event: HostEvent): void
  reset(): void
}

export function createTelemetryStore(options: { historyLimit?: number } = {}) {
  const historyLimit = options.historyLimit ?? 120
  return createStore<TelemetryStoreState>((set) => ({
    history: [],
    applyHostEvent(event) {
      if (event.type !== 'telemetry.updated') return
      set((state) => ({ latest: event.payload, history: [...state.history, event.payload].slice(-historyLimit) }))
    },
    reset() { set({ latest: undefined, history: [] }) },
  }))
}

import { describe, expect, it } from 'vitest'
import type { HostEvent } from '../contracts/bridge'
import { createMockSessionEvents } from '../mock/mockSession'
import { createSessionStore } from './sessionStore'

describe('sessionStore vision freshness', () => {
  it.each(['repairing', 'faulted', 'notConfigured'] as const)('clears the last observation when vision becomes %s', (status) => {
    const store = createSessionStore()
    for (const event of createMockSessionEvents()) store.getState().applyHostEvent(event)
    expect(store.getState().observation).toBeDefined()

    const event: HostEvent = {
      schemaVersion: 2,
      type: 'vision.status.updated',
      payload: { status, modelId: status === 'notConfigured' ? null : 'model', provider: status === 'notConfigured' ? 'none' : 'cpu', diagnostic: 'STALE' },
    }
    store.getState().applyHostEvent(event)

    expect(store.getState().observation).toBeUndefined()
  })
})

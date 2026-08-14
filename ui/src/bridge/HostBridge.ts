import { CONTRACT_SCHEMA_VERSION, hostEventSchema, uiCommandSchema, type HostEvent, type UiCommand } from '../contracts/bridge'

export type BridgeFailureReason = 'unavailable' | 'invalid-command' | 'disposed'
export type BridgeResult = { ok: true } | { ok: false; reason: BridgeFailureReason }
export type HostEventListener = (event: HostEvent) => void

export interface HostBridge {
  readonly kind: 'webview' | 'unavailable' | 'mock'
  send(command: UiCommand): BridgeResult
  subscribe(listener: HostEventListener): () => void
  requestSnapshot(): BridgeResult
  dispose(): void
}

interface WebViewPort {
  postMessage(message: unknown): void
  addEventListener(type: 'message', listener: (event: MessageEvent<unknown>) => void): void
  removeEventListener(type: 'message', listener: (event: MessageEvent<unknown>) => void): void
}

export interface HostBridgeEnvironment {
  chrome?: { webview?: WebViewPort }
}

export function createHostBridge(environment?: HostBridgeEnvironment): HostBridge {
  const resolvedEnvironment = environment ?? (typeof window === 'undefined' ? {} : window as unknown as HostBridgeEnvironment)
  const webview = resolvedEnvironment.chrome?.webview
  const listeners = new Set<HostEventListener>()
  let disposed = false

  const onMessage = (message: MessageEvent<unknown>) => {
    const result = hostEventSchema.safeParse(message.data)
    if (!result.success || disposed) return
    listeners.forEach((listener) => listener(result.data))
  }

  webview?.addEventListener('message', onMessage)

  const send = (command: UiCommand): BridgeResult => {
    if (disposed) return { ok: false, reason: 'disposed' }
    const result = uiCommandSchema.safeParse(command)
    if (!result.success) return { ok: false, reason: 'invalid-command' }
    if (!webview) return { ok: false, reason: 'unavailable' }
    webview.postMessage(result.data)
    return { ok: true }
  }

  return {
    kind: webview ? 'webview' : 'unavailable',
    send,
    subscribe(listener) {
      if (disposed) return () => undefined
      listeners.add(listener)
      return () => listeners.delete(listener)
    },
    requestSnapshot() {
      return send({ schemaVersion: CONTRACT_SCHEMA_VERSION, type: 'snapshot.request', payload: {} })
    },
    dispose() {
      if (disposed) return
      disposed = true
      webview?.removeEventListener('message', onMessage)
      listeners.clear()
    },
  }
}

import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

afterEach(() => cleanup())

if (!window.matchMedia) {
  window.matchMedia = (query: string): MediaQueryList => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => undefined,
    removeListener: () => undefined,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    dispatchEvent: () => false,
  })
}

if (!window.ResizeObserver) {
  class ResizeObserverStub {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
  window.ResizeObserver = ResizeObserverStub as unknown as typeof ResizeObserver
}

if (window.getComputedStyle) {
  window.getComputedStyle = ((...args: Parameters<typeof window.getComputedStyle>) => {
    void args
    return { getPropertyValue: () => '' }
  }) as unknown as typeof window.getComputedStyle
}

if (typeof HTMLCanvasElement !== 'undefined') {
  Object.defineProperty(HTMLCanvasElement.prototype, 'getContext', {
    configurable: true,
    value: () => ({
      arc: () => undefined,
      beginPath: () => undefined,
      clearRect: () => undefined,
      fillRect: () => undefined,
      fillText: () => undefined,
      lineTo: () => undefined,
      measureText: () => ({ width: 120 }),
      moveTo: () => undefined,
      stroke: () => undefined,
      strokeRect: () => undefined,
    }),
  })
}

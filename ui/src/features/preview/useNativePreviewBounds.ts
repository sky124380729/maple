import { useLayoutEffect, type RefObject } from 'react'
import type { UiCommand } from '../../contracts/bridge'

export function useNativePreviewBounds(
  element: RefObject<HTMLElement | null>,
  sendCommand: (command: UiCommand) => void,
) {
  useLayoutEffect(() => {
    const node = element.current
    if (!node) return

    let frameHandle: number | undefined
    const publish = () => {
      frameHandle = undefined
      const rect = node.getBoundingClientRect()
      if (rect.width < 320 || rect.height < 180) return
      sendCommand({
        schemaVersion: 2,
        type: 'preview.boundsChanged',
        payload: {
          left: rect.left,
          top: rect.top,
          width: rect.width,
          height: rect.height,
          devicePixelRatio: window.devicePixelRatio,
        },
      })
    }
    const schedule = () => {
      if (frameHandle !== undefined) return
      frameHandle = window.requestAnimationFrame(publish)
    }
    const observer = new ResizeObserver(schedule)
    observer.observe(node)
    schedule()

    return () => {
      observer.disconnect()
      if (frameHandle !== undefined) window.cancelAnimationFrame(frameHandle)
    }
  }, [element, sendCommand])
}

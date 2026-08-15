import { useEffect, useMemo, useRef } from 'react'
import type { OverlaySnapshot } from '../../contracts/bridge'
import { OverlayLegend } from './OverlayLegend'
import { buildOverlayRenderItems, formatCanvasOverlayLabel, type OverlayRenderItem } from './overlay'

const CANVAS_WIDTH = 1280
const CANVAS_HEIGHT = 720

function requestFrame(callback: FrameRequestCallback) {
  if (typeof window.requestAnimationFrame === 'function') return window.requestAnimationFrame(callback)
  return window.setTimeout(() => callback(performance.now()), 16) as unknown as number
}

function cancelFrame(handle: number) {
  if (typeof window.cancelAnimationFrame === 'function') window.cancelAnimationFrame(handle)
  else window.clearTimeout(handle)
}

function drawOverlay(context: CanvasRenderingContext2D, item: OverlayRenderItem, compact: boolean) {
  const [x, y, width, height] = item.box
  const left = x * CANVAS_WIDTH
  const top = y * CANVAS_HEIGHT
  const boxWidth = width * CANVAS_WIDTH
  const boxHeight = height * CANVAS_HEIGHT

  context.strokeStyle = item.color
  context.lineWidth = 3
  context.strokeRect(left, top, boxWidth, boxHeight)
  context.font = '600 18px "Noto Sans SC", sans-serif'
  const label = formatCanvasOverlayLabel(item, compact)
  const labelWidth = context.measureText(label).width + 16
  const labelTop = Math.max(0, top - 28)
  context.fillStyle = 'rgba(8, 13, 18, 0.9)'
  context.fillRect(left, labelTop, labelWidth, 26)
  context.fillStyle = item.color
  context.fillText(label, left + 8, labelTop + 19)
}

function drawScene(context: CanvasRenderingContext2D, items: OverlayRenderItem[], compact: boolean) {
  context.clearRect(0, 0, CANVAS_WIDTH, CANVAS_HEIGHT)
  context.fillStyle = '#080d12'
  context.fillRect(0, 0, CANVAS_WIDTH, CANVAS_HEIGHT)

  context.strokeStyle = 'rgba(80, 125, 165, 0.12)'
  context.lineWidth = 1
  for (let x = 0; x <= CANVAS_WIDTH; x += 64) {
    context.beginPath()
    context.moveTo(x, 0)
    context.lineTo(x, CANVAS_HEIGHT)
    context.stroke()
  }
  for (let y = 0; y <= CANVAS_HEIGHT; y += 48) {
    context.beginPath()
    context.moveTo(0, y)
    context.lineTo(CANVAS_WIDTH, y)
    context.stroke()
  }

  context.strokeStyle = 'rgba(77, 163, 255, 0.15)'
  context.lineWidth = 2
  context.beginPath()
  context.arc(CANVAS_WIDTH / 2, CANVAS_HEIGHT / 2, 210, 0, Math.PI * 2)
  context.stroke()

  for (const item of items) drawOverlay(context, item, compact)
}

export function MockPreviewCanvas({ snapshot, nowMonoMs = snapshot.generatedAtMonoMs }: { snapshot: OverlaySnapshot; nowMonoMs?: number }) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const items = useMemo(() => buildOverlayRenderItems(snapshot, nowMonoMs), [nowMonoMs, snapshot])

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return undefined

    let context: CanvasRenderingContext2D | null = null
    try {
      context = canvas.getContext('2d')
    } catch {
      return undefined
    }
    if (!context) return undefined

    let active = true
    let frameHandle = 0
    const draw = () => {
      if (!active || !context) return
      drawScene(context, items, canvas.clientWidth < 520)
      frameHandle = requestFrame(draw)
    }
    draw()

    return () => {
      active = false
      if (frameHandle) cancelFrame(frameHandle)
    }
  }, [items])

  return (
    <div className="mock-preview-canvas">
      <canvas ref={canvasRef} width={CANVAS_WIDTH} height={CANVAS_HEIGHT} aria-label="实时模拟预览画布" />
      <div className="mock-preview-canvas__hud">结构化模拟画面 · 不产生输入</div>
      <OverlayLegend items={items} />
    </div>
  )
}

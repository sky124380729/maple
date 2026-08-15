import '@testing-library/jest-dom/vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, test } from 'vitest'
import { createHostBridge } from '../bridge/HostBridge'
import type { HostBridge } from '../bridge/HostBridge'
import { createMockHostBridge } from '../bridge/MockHostBridge'
import type { UiCommand } from '../contracts/bridge'
import { App } from './App'

describe('Maple real-time workbench', () => {
  test('keeps all three workbench regions visible and reports the native preview aperture', async () => {
    const sent: UiCommand[] = []
    let notifyResize: (() => void) | undefined
    const OriginalResizeObserver = window.ResizeObserver
    window.ResizeObserver = class {
      constructor(callback: ResizeObserverCallback) {
        notifyResize = () => callback([], this as unknown as ResizeObserver)
      }
      observe() {}
      unobserve() {}
      disconnect() {}
    } as unknown as typeof ResizeObserver
    const mock = createMockHostBridge({ telemetryIntervalMs: 10_000 })
    const bridge: HostBridge = {
      ...mock,
      send(command) {
        sent.push(command)
        return mock.send(command)
      },
    }
    const originalBounds = HTMLElement.prototype.getBoundingClientRect
    HTMLElement.prototype.getBoundingClientRect = function () {
      if (this.getAttribute('aria-label') === '原生预览画面区域') {
        return { x: 278, y: 112, left: 278, top: 112, width: 812, height: 620, right: 1090, bottom: 732, toJSON: () => ({}) }
      }
      return originalBounds.call(this)
    }

    render(<App bridge={bridge} />)

    expect(await screen.findByText('运行控制')).toBeInTheDocument()
    expect(screen.getByText('实时预览')).toBeInTheDocument()
    expect(screen.getByText('识别概览')).toBeInTheDocument()
    expect(screen.getByLabelText('原生预览画面区域')).toBeInTheDocument()
    sent.length = 0
    notifyResize?.()
    await waitFor(() => expect(sent).toContainEqual({
        schemaVersion: 2,
        type: 'preview.boundsChanged',
        payload: { left: 278, top: 112, width: 812, height: 620, devicePixelRatio: window.devicePixelRatio },
      }))

    HTMLElement.prototype.getBoundingClientRect = originalBounds
    window.ResizeObserver = OriginalResizeObserver
  })

  test('hydrates the workbench from typed mock-host events', async () => {
    const bridge = createMockHostBridge({ telemetryIntervalMs: 10_000 })
    render(<App bridge={bridge} />)

    expect(await screen.findByText('冒险岛怀旧服')).toBeInTheDocument()
    expect(screen.getByText('森林东部')).toBeInTheDocument()
    expect(screen.getAllByText('输入服务待连接').length).toBeGreaterThan(0)
    expect(screen.getByText('94', { selector: '.identity-card__score' })).toHaveTextContent('94%')
    expect(screen.queryByLabelText('性能遥测')).not.toBeInTheDocument()
    expect(screen.getByText('模拟画面已连接')).toBeInTheDocument()
    expect(screen.getByLabelText('实时模拟预览画布')).toBeInTheDocument()
    expect(screen.getAllByText('自己 94%').length).toBeGreaterThan(0)
    expect(screen.getByText(/采集 60 FPS · 识别 30 FPS/)).toBeInTheDocument()
    expect(screen.getByText('HP 99%')).toBeInTheDocument()
    expect(screen.getByText('MP 35%')).toBeInTheDocument()
    expect(screen.getByText('maple-yolo-demo')).toBeInTheDocument()
    expect(screen.getByText('其他玩家 81% #player-7')).toBeInTheDocument()
    expect(screen.getByText('snail 88% #monster-1')).toBeInTheDocument()
  })

  test('sends safe session controls through the bridge', async () => {
    const bridge = createMockHostBridge({ telemetryIntervalMs: 10_000 })
    render(<App bridge={bridge} />)
    await screen.findByText('冒险岛怀旧服')

    fireEvent.click(screen.getByRole('button', { name: '开始运行' }))
    expect(screen.getAllByText('观察中').length).toBeGreaterThan(0)

    fireEvent.click(screen.getByRole('button', { name: '暂停并释放按键' }))
    expect(screen.getAllByText('已暂停').length).toBeGreaterThan(0)

    fireEvent.click(screen.getByRole('button', { name: '紧急停止' }))
    expect(screen.getAllByText('紧急停止').length).toBeGreaterThan(1)
  })

  test('keeps emergency stop enabled when the native host is unavailable', () => {
    const bridge = createHostBridge({})
    render(<App bridge={bridge} />)

    expect(screen.getAllByText('宿主未连接').length).toBeGreaterThan(0)
    expect(screen.getByRole('button', { name: '紧急停止' })).toBeEnabled()
  })

  test('loads safe defaults without exposing Self tracking controls', () => {
    const bridge = createMockHostBridge({ telemetryIntervalMs: 10_000 })
    render(<App bridge={bridge} />)

    expect(screen.getByRole('radio', { name: '自动' })).toBeChecked()
    expect(screen.getByRole('spinbutton', { name: '生命值下限百分比' })).toHaveValue('35')
    expect(screen.getByRole('switch', { name: '自动拾取' })).not.toBeChecked()
    expect(screen.queryByLabelText(/编号|跟踪/i)).not.toBeInTheDocument()
  })

  test('keeps the interface Chinese-first and free of legacy labels', () => {
    const bridge = createMockHostBridge({ telemetryIntervalMs: 10_000 })
    render(<App bridge={bridge} />)

    for (const legacyLabel of ['Session', 'Health', 'Native Preview Surface', 'SAFE OBSERVE', 'Capture', 'Render', 'Recognition']) {
      expect(screen.queryByText(legacyLabel)).not.toBeInTheDocument()
    }
  })

  test('opens Bailian settings without leaving the real-time workbench', async () => {
    const bridge = createMockHostBridge({ telemetryIntervalMs: 10_000 })
    render(<App bridge={bridge} />)
    await screen.findByText('冒险岛怀旧服')

    fireEvent.click(screen.getByRole('button', { name: '系统设置' }))

    expect(screen.getByRole('heading', { name: '百炼' })).toBeInTheDocument()
    expect(screen.getByText('未保存密钥')).toBeInTheDocument()
    expect(screen.getByText('实时预览')).toBeInTheDocument()
  })

  test('pauses the running session before saving system settings', async () => {
    const bridge = createMockHostBridge({ telemetryIntervalMs: 10_000 })
    render(<App bridge={bridge} />)
    await screen.findByText('冒险岛怀旧服')
    fireEvent.click(screen.getByRole('button', { name: '开始运行' }))
    fireEvent.click(screen.getByRole('button', { name: '系统设置' }))

    fireEvent.change(screen.getByLabelText('百炼 API Key'), { target: { value: 'test-key-1234567890' } })
    fireEvent.click(screen.getByRole('button', { name: '保存密钥' }))

    expect(screen.getByRole('status')).toHaveTextContent('修改设置时已暂停')
    expect(screen.getAllByText('已暂停').length).toBeGreaterThan(0)
  })
})

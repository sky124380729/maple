import '@testing-library/jest-dom/vitest'
import { render, screen } from '@testing-library/react'
import { expect, test } from 'vitest'
import type { InputBrokerStatus, TargetBinding } from '../../contracts/bridge'
import { TargetStatus } from './TargetStatus'

test('shows the bound game and broker readiness without exposing raw keys', () => {
  const target: TargetBinding = { schemaVersion: 2, hwnd: '0x1234', pid: 7, clientWidth: 1280, clientHeight: 720, dpi: 96 }
  const inputStatus: InputBrokerStatus = {
    provider: 'inputBroker', status: 'ready', integrity: 'high', activeKeys: [], lastReleaseSucceeded: true,
    hotkeys: { pauseResume: 'F9', emergencyStop: 'F12' }, errorCode: null,
  }

  render(<TargetStatus target={target} bridgeKind="webview" inputStatus={inputStatus} />)

  expect(screen.getByText('冒险岛怀旧服')).toBeInTheDocument()
  expect(screen.getByText('输入服务已就绪')).toBeInTheDocument()
  expect(screen.queryByText(/scan|VK|扫描码/i)).not.toBeInTheDocument()
})

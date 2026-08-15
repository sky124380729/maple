import { DesktopOutlined, SettingOutlined, StopOutlined, ThunderboltFilled } from '@ant-design/icons'
import { Button, Drawer, Tag, Tooltip } from 'antd'
import { useCallback, useEffect, useRef, useState } from 'react'
import { useStore } from 'zustand'
import { createHostBridge, type HostBridge, type HostBridgeEnvironment } from '../../bridge/HostBridge'
import { createMockHostBridge } from '../../bridge/MockHostBridge'
import type { UiCommand } from '../../contracts/bridge'
import { createSessionStore } from '../../store/sessionStore'
import { createTelemetryStore } from '../../store/telemetryStore'
import { PreviewRegion } from '../preview/PreviewRegion'
import { SettingsPage } from '../settings/SettingsPage'
import { HealthPanel } from './HealthPanel'
import { SessionControls } from './SessionControls'
import { StatusPill } from './presentation'
import { TargetStatus } from './TargetStatus'

function createDevelopmentBridge(): HostBridge {
  const nativeBridge = createHostBridge()
  if (nativeBridge.kind === 'webview') return nativeBridge
  nativeBridge.dispose()
  return createMockHostBridge()
}

function getDefaultBridgeKind(): HostBridge['kind'] {
  if (typeof window === 'undefined') return 'unavailable'
  const environment = window as unknown as HostBridgeEnvironment
  return environment.chrome?.webview ? 'webview' : 'mock'
}

export function WorkbenchPage({ bridge: suppliedBridge }: { bridge?: HostBridge }) {
  const [sessionStore] = useState(() => createSessionStore())
  const [telemetryStore] = useState(() => createTelemetryStore())
  const runtimeBridgeRef = useRef<HostBridge | undefined>(suppliedBridge)
  const session = useStore(sessionStore, (state) => state)
  const [settingsOpen, setSettingsOpen] = useState(false)
  const [pausedForSettings, setPausedForSettings] = useState(false)

  useEffect(() => {
    const bridge = suppliedBridge ?? createDevelopmentBridge()
    runtimeBridgeRef.current = bridge
    sessionStore.getState().reset()
    telemetryStore.getState().reset()
    const unsubscribe = bridge.subscribe((event) => {
      sessionStore.getState().applyHostEvent(event)
      telemetryStore.getState().applyHostEvent(event)
    })
    bridge.requestSnapshot()
    return () => {
      unsubscribe()
      if (!suppliedBridge) bridge.dispose()
      if (runtimeBridgeRef.current === bridge) runtimeBridgeRef.current = suppliedBridge
    }
  }, [sessionStore, suppliedBridge, telemetryStore])

  const sendCommand = useCallback((command: UiCommand) => {
    runtimeBridgeRef.current?.send(command)
  }, [])
  const sendSettingsCommand = (command: UiCommand) => {
    const mutatesSettings = command.type === 'cloud.credential.set'
      || command.type === 'cloud.credential.clear'
      || command.type === 'cloud.config.update'
    if (mutatesSettings && session.sessionState !== 'Stopped' && session.sessionState !== 'Paused' && session.sessionState !== 'EmergencyStop') {
      sendCommand({ schemaVersion: 2, type: 'session.pause', payload: {} })
      setPausedForSettings(true)
    } else if (mutatesSettings && session.sessionState === 'Paused') {
      setPausedForSettings(true)
    }
    sendCommand(command)
  }
  const requestSnapshot = () => runtimeBridgeRef.current?.requestSnapshot()
  const bridgeKind = suppliedBridge?.kind ?? getDefaultBridgeKind()

  return (
    <div className="workbench-shell">
      <header className="topbar" role="banner">
        <div className="topbar__brand"><div className="brand-mark"><ThunderboltFilled /></div><div><div className="brand-name">Maple <span>自动化工作台</span></div><div className="brand-subtitle">视觉识别 · 安全控制</div></div></div>
        <TargetStatus target={session.target} bridgeKind={bridgeKind} inputStatus={session.inputStatus} />
        <div className="topbar__actions">
          <Tag className="preview-tag"><DesktopOutlined />{bridgeKind === 'mock' ? '模拟宿主' : bridgeKind === 'webview' ? '原生宿主' : '宿主未连接'}</Tag>
          <StatusPill state={session.sessionState} />
          <Tooltip title="系统设置"><Button type="text" shape="circle" aria-label="系统设置" icon={<SettingOutlined />} onClick={() => { setPausedForSettings(false); setSettingsOpen(true) }} /></Tooltip>
          <Button className="emergency-button" danger type="default" aria-label="紧急停止" icon={<StopOutlined />} onClick={() => sendCommand({ schemaVersion: 2, type: 'session.emergencyStop', payload: { message: '用户请求紧急停止' } })}>紧急停止</Button>
        </div>
      </header>
      <div className="workbench-grid">
        <SessionControls sessionState={session.sessionState} inputStatus={session.inputStatus} resumeCountdown={session.resumeCountdown} sendCommand={sendCommand} />
        <PreviewRegion preview={session.preview} observation={session.observation} onRequestSnapshot={requestSnapshot} sendCommand={sendCommand} />
        <HealthPanel sessionState={session.sessionState} pauseReason={session.pauseReason} observation={session.observation} preview={session.preview} inputStatus={session.inputStatus} logs={session.logs} onRefresh={requestSnapshot} />
      </div>
      <Drawer
        className="settings-drawer"
        title="系统设置"
        placement="right"
        size={420}
        open={settingsOpen}
        onClose={() => setSettingsOpen(false)}
      >
        <SettingsPage cloudStatus={session.cloudStatus} pausedForSettings={pausedForSettings} sendCommand={sendSettingsCommand} />
      </Drawer>
    </div>
  )
}

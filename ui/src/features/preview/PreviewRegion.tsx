import { AimOutlined, DesktopOutlined, LinkOutlined, SettingOutlined } from '@ant-design/icons'
import { Button, Space, Tag, Tooltip, Typography } from 'antd'
import { useRef } from 'react'
import type { HostEvent, ObservationSnapshot, UiCommand } from '../../contracts/bridge'
import { MockPreviewCanvas } from './MockPreviewCanvas'
import { useNativePreviewBounds } from './useNativePreviewBounds'

const { Text, Title } = Typography
type PreviewAvailability = Extract<HostEvent, { type: 'preview.availabilityChanged' }>['payload']

export function PreviewRegion({ preview, observation, onRequestSnapshot, sendCommand }: { preview: PreviewAvailability; observation?: ObservationSnapshot; onRequestSnapshot(): void; sendCommand(command: UiCommand): void }) {
  const nativePreviewRef = useRef<HTMLDivElement>(null)
  useNativePreviewBounds(nativePreviewRef, sendCommand)
  const connected = preview.available
  const overlay = observation ? {
    schemaVersion: observation.schemaVersion,
    frameId: observation.frameId,
    generatedAtMonoMs: observation.capturedAtMonoMs,
    self: observation.self,
    players: observation.players,
    monsters: observation.monsters,
  } : undefined
  const showMockCanvas = connected && preview.backend === 'browser-mock' && overlay !== undefined
  const sourceLabel = preview.backend === 'native' ? '原生画面已连接' : preview.backend === 'browser-mock' ? '模拟画面已连接' : '等待目标画面'
  return (
    <main className="preview-panel">
      <div className="preview-panel__header">
        <div><Text className="section-heading__kicker">画面中心</Text><Title level={3} className="preview-title">实时预览</Title></div>
        <Space size={8}><Tag className="preview-tag"><DesktopOutlined /> {preview.backend === 'browser-mock' ? '模拟预览' : '原生画面'}</Tag><Tooltip title="预览设置"><Button type="text" shape="circle" icon={<SettingOutlined />} aria-label="预览设置" /></Tooltip></Space>
      </div>
      <div ref={nativePreviewRef} className={`preview-stage ${connected ? 'preview-stage--connected' : ''}`} aria-label="原生预览画面区域">
        <div className="preview-stage__grid" />
        <div className="preview-stage__corner preview-stage__corner--tl" /><div className="preview-stage__corner preview-stage__corner--tr" /><div className="preview-stage__corner preview-stage__corner--bl" /><div className="preview-stage__corner preview-stage__corner--br" />
        <div className="preview-hud preview-hud--top"><span><i className={`hud-dot ${connected ? 'hud-dot--good' : ''}`} />{sourceLabel}</span><span>{connected ? '预览通道正常' : '画面源未连接'}</span></div>
        {showMockCanvas && overlay ? <MockPreviewCanvas snapshot={overlay} nowMonoMs={overlay.generatedAtMonoMs} /> : <div className="preview-empty">
          <div className="preview-empty__orb"><AimOutlined /></div>
          <Title level={4} className="preview-empty__title">{connected ? '预览通道已就绪' : '连接目标窗口开始'}</Title>
          <Text className="preview-empty__copy">{connected ? '当前显示结构化模拟状态；Windows Host 接入后由原生预览面承载实时画面。' : '绑定后将以 30–60 FPS 显示画面，并叠加人物与怪物识别框。'}</Text>
          {!connected && <Button className="bind-button" type="primary" aria-label="绑定目标窗口" icon={<LinkOutlined />} onClick={onRequestSnapshot}>绑定目标窗口</Button>}
        </div>}
        <div className="preview-hud preview-hud--bottom"><span>识别框预览</span><span className="hud-chip hud-chip--green">自己 {observation ? Math.round(observation.self.confidence * 100) : 0}%</span><span className="hud-chip hud-chip--red">怪物 {observation?.monsters.length ?? 0}</span><span className="hud-chip hud-chip--cyan">其他玩家 {observation?.players.length ?? 0}</span></div>
      </div>
      <div className="preview-panel__footer"><div className="frame-status"><span className="frame-status__line" /><span>{connected ? '通道可用 · 自动隐藏过期框' : '等待绑定 · 不显示过期框'}</span></div><Text className="preview-note">只显示动态目标</Text></div>
    </main>
  )
}

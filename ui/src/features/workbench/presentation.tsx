import type { ReactNode } from 'react'
import { Tag, Typography } from 'antd'
import type { SessionState } from '../../contracts/bridge'

const { Text, Title } = Typography

export const sessionStateLabels: Record<SessionState, string> = {
  Stopped: '已停止',
  Arming: '准备中',
  Observing: '观察中',
  MapScanning: '地图扫描',
  MapCalibrating: '地图校准',
  Navigating: '导航中',
  Attacking: '攻击中',
  Looting: '拾取中',
  UsingPotion: '补给中',
  Paused: '已暂停',
  ManualIntervention: '需要处理',
  EmergencyStop: '紧急停止',
}

const stateClassNames: Record<SessionState, string> = {
  Stopped: 'status-pill--muted',
  Arming: 'status-pill--warn',
  Observing: 'status-pill--good',
  MapScanning: 'status-pill--warn',
  MapCalibrating: 'status-pill--warn',
  Navigating: 'status-pill--good',
  Attacking: 'status-pill--good',
  Looting: 'status-pill--good',
  UsingPotion: 'status-pill--warn',
  Paused: 'status-pill--warn',
  ManualIntervention: 'status-pill--danger',
  EmergencyStop: 'status-pill--danger',
}

export function StatusPill({ state }: { state: SessionState }) {
  return <Tag className={`status-pill ${stateClassNames[state]}`}><span className="status-pill__dot" />{sessionStateLabels[state]}</Tag>
}

export function SectionHeading({ icon, kicker, title, action }: { icon: ReactNode; kicker: string; title: string; action?: ReactNode }) {
  return (
    <div className="section-heading">
      <div className="section-heading__lead">
        <span className="section-heading__icon">{icon}</span>
        <div><Text className="section-heading__kicker">{kicker}</Text><Title level={4} className="section-heading__title">{title}</Title></div>
      </div>
      {action}
    </div>
  )
}

export function MetricRow({ label, value, detail, tone = 'default' }: { label: string; value: string; detail?: string; tone?: 'default' | 'good' | 'warn' }) {
  return <div className="metric-row"><div><span className="metric-row__label">{label}</span>{detail && <span className="metric-row__detail">{detail}</span>}</div><strong className={`metric-row__value metric-row__value--${tone}`}>{value}</strong></div>
}

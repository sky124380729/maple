import { DesktopOutlined } from '@ant-design/icons'
import type { TargetBinding } from '../../contracts/bridge'
import type { HostBridge } from '../../bridge/HostBridge'

export function TargetStatus({ target, bridgeKind }: { target?: TargetBinding; bridgeKind: HostBridge['kind'] }) {
  const label = target ? '冒险岛怀旧服' : bridgeKind === 'unavailable' ? '宿主未连接' : '等待目标窗口'
  return (
    <div className="target-status">
      <DesktopOutlined />
      <div><span>目标窗口</span><strong>{label}</strong></div>
      <span className={`connection-dot ${target ? 'connection-dot--good' : ''}`} />
    </div>
  )
}

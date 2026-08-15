import { DesktopOutlined } from '@ant-design/icons'
import type { InputBrokerStatus, TargetBinding } from '../../contracts/bridge'
import type { HostBridge } from '../../bridge/HostBridge'

export function TargetStatus({ target, bridgeKind, inputStatus }: { target?: TargetBinding; bridgeKind: HostBridge['kind']; inputStatus: InputBrokerStatus }) {
  const label = target ? '冒险岛怀旧服' : bridgeKind === 'unavailable' ? '宿主未连接' : '等待目标窗口'
  const inputLabel = inputStatus.status === 'ready' ? '输入服务已就绪' : inputStatus.status === 'starting' ? '输入服务连接中' : inputStatus.status === 'paused' ? '输入服务已暂停' : inputStatus.status === 'faulted' ? '输入服务异常' : '输入服务待连接'
  return (
    <div className="target-status">
      <DesktopOutlined />
      <div><span>{inputLabel}</span><strong>{label}</strong></div>
      <span className={`connection-dot ${target && inputStatus.status === 'ready' ? 'connection-dot--good' : ''}`} />
    </div>
  )
}

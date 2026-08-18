import type { CombatRhythmSnapshot, SessionState } from '../../contracts/bridge'

const phaseLabels: Record<CombatRhythmSnapshot['phase'], string> = {
  idle: '等待下一轮',
  attackHolding: '攻击键按住中',
  moveLeft: '左移',
  moveRight: '右移',
  movementGap: '动作间隔',
  resting: '休息中',
}

const inactiveLabels: Partial<Record<SessionState, string>> = {
  Stopped: '已停止',
  Paused: '已暂停',
  ManualIntervention: '等待人工处理',
  EmergencyStop: '紧急停止',
}

const formatSeconds = (milliseconds: number) => `${(milliseconds / 1_000).toFixed(2)} 秒`

export function RhythmCountdown({ rhythm, sessionState }: { rhythm?: CombatRhythmSnapshot; sessionState: SessionState }) {
  const inactiveLabel = inactiveLabels[sessionState]
  if (inactiveLabel || !rhythm) {
    return (
      <section className="rhythm-card" aria-label="随机节奏倒计时">
        <span className="rhythm-card__eyebrow">随机节奏</span>
        <strong className="rhythm-card__phase">{inactiveLabel ?? '等待节奏'}</strong>
        <span className="rhythm-card__hint">由后端安全状态机控制</span>
      </section>
    )
  }

  return (
    <section className="rhythm-card rhythm-card--active" aria-label="随机节奏倒计时">
      <div className="rhythm-card__header">
        <span className="rhythm-card__eyebrow">随机节奏 · 第 {rhythm.cycleId} 轮</span>
        <span className="rhythm-card__live"><i />实时</span>
      </div>
      <strong className="rhythm-card__phase">{phaseLabels[rhythm.phase]}</strong>
      <div className="rhythm-card__remaining">剩余 {formatSeconds(rhythm.remainingMs)}</div>
      <div className="rhythm-card__total">本轮 {formatSeconds(rhythm.sampledDurationMs)}</div>
      {rhythm.earlyReleaseReason ? <div className="rhythm-card__reason">提前释放：{rhythm.earlyReleaseReason}</div> : null}
    </section>
  )
}

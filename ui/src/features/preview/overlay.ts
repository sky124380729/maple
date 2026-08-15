import type { OverlaySnapshot } from '../../contracts/bridge'

export const OVERLAY_COLORS = {
  self: '#42d392',
  player: '#55c7f7',
  monster: '#ff6474',
} as const

export type OverlayKind = keyof typeof OVERLAY_COLORS

export interface OverlayRenderItem {
  kind: OverlayKind
  box: readonly [number, number, number, number]
  confidence: number
  freshUntilMonoMs: number
  label: string
  color: string
}

export function formatCanvasOverlayLabel(item: OverlayRenderItem, compact: boolean) {
  if (!compact) return item.label
  const kindLabel = item.kind === 'self' ? '自己' : item.kind === 'player' ? '玩家' : '怪物'
  return `${kindLabel} ${formatConfidence(item.confidence)}`
}

function formatConfidence(value: number) {
  return `${Math.round(value * 100)}%`
}

export function buildOverlayRenderItems(snapshot: OverlaySnapshot, nowMonoMs: number): OverlayRenderItem[] {
  const items: OverlayRenderItem[] = []

  if (snapshot.self && snapshot.self.freshUntilMonoMs > nowMonoMs) {
    items.push({
      kind: 'self',
      box: snapshot.self.box,
      confidence: snapshot.self.confidence,
      freshUntilMonoMs: snapshot.self.freshUntilMonoMs,
      label: `自己 ${formatConfidence(snapshot.self.confidence)}`,
      color: OVERLAY_COLORS.self,
    })
  }

  for (const player of snapshot.players) {
    if (player.freshUntilMonoMs <= nowMonoMs) continue
    items.push({
      kind: 'player',
      box: player.box,
      confidence: player.confidence,
      freshUntilMonoMs: player.freshUntilMonoMs,
      label: `其他玩家 ${formatConfidence(player.confidence)} #${player.trackId}`,
      color: OVERLAY_COLORS.player,
    })
  }

  for (const monster of snapshot.monsters) {
    if (monster.freshUntilMonoMs <= nowMonoMs) continue
    items.push({
      kind: 'monster',
      box: monster.box,
      confidence: monster.confidence,
      freshUntilMonoMs: monster.freshUntilMonoMs,
      label: `${monster.class} ${formatConfidence(monster.confidence)} #${monster.targetId}`,
      color: OVERLAY_COLORS.monster,
    })
  }

  return items
}

import type { OverlayRenderItem } from './overlay'

export function OverlayLegend({ items }: { items: OverlayRenderItem[] }) {
  return (
    <div className="overlay-legend" aria-label="识别框图例" role="list">
      {items.map((item) => (
        <span className={`overlay-legend__item overlay-legend__item--${item.kind} ${item.selected ? 'overlay-legend__item--selected' : ''}`} key={`${item.kind}-${item.label}`} role="listitem" aria-current={item.selected ? 'true' : undefined}>
          <i className="overlay-legend__swatch" style={{ backgroundColor: item.color }} />
          <span>{item.label}</span>
          {item.selected && <strong className="overlay-legend__target">攻击目标</strong>}
        </span>
      ))}
    </div>
  )
}

import type { OverlayRenderItem } from './overlay'

export function OverlayLegend({ items }: { items: OverlayRenderItem[] }) {
  return (
    <div className="overlay-legend" aria-label="识别框图例" role="list">
      {items.map((item) => (
        <span className={`overlay-legend__item overlay-legend__item--${item.kind}`} key={`${item.kind}-${item.label}`} role="listitem">
          <i className="overlay-legend__swatch" style={{ backgroundColor: item.color }} />
          <span>{item.label}</span>
        </span>
      ))}
    </div>
  )
}

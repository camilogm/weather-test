import type { WeatherIconName } from '../api/types'

interface Props {
  name: WeatherIconName
  /** Human-readable condition, used as the accessible label. */
  label: string
  className?: string
}

/**
 * Open-Meteo ships no artwork, so the API hands over a stable slug and the icons
 * live here. Drawn with currentColor plus two theme tokens, which means they
 * follow the palette in both light and dark without a second asset set.
 */
export function WeatherIcon({ name, label, className = 'size-10' }: Props) {
  return (
    <svg
      viewBox="0 0 48 48"
      fill="none"
      strokeLinecap="round"
      strokeLinejoin="round"
      role="img"
      aria-label={label}
      className={className}
    >
      {renderGlyph(name)}
    </svg>
  )
}

const sun = (cx: number, cy: number, r: number) => (
  <g className="text-warm" stroke="currentColor" strokeWidth={2.4}>
    <circle cx={cx} cy={cy} r={r} fill="currentColor" fillOpacity={0.22} />
    {[0, 45, 90, 135, 180, 225, 270, 315].map((angle) => {
      const radians = (angle * Math.PI) / 180
      const inner = r + 3.5
      const outer = r + 7
      return (
        <line
          key={angle}
          x1={cx + Math.cos(radians) * inner}
          y1={cy + Math.sin(radians) * inner}
          x2={cx + Math.cos(radians) * outer}
          y2={cy + Math.sin(radians) * outer}
        />
      )
    })}
  </g>
)

const cloud = (x: number, y: number, scale = 1, muted = false) => (
  <path
    d={`M ${x - 12 * scale} ${y + 6 * scale}
        a ${7 * scale} ${7 * scale} 0 0 1 ${1 * scale} ${-13.6 * scale}
        a ${9.5 * scale} ${9.5 * scale} 0 0 1 ${18 * scale} ${-2 * scale}
        a ${7.5 * scale} ${7.5 * scale} 0 0 1 ${2 * scale} ${15.6 * scale} Z`}
    fill="currentColor"
    fillOpacity={muted ? 0.16 : 0.24}
    stroke="currentColor"
    strokeWidth={2.2}
    className={muted ? 'text-ink-muted' : 'text-cool'}
  />
)

const drops = (count: number, y: number) => (
  <g className="text-cool" stroke="currentColor" strokeWidth={2.6}>
    {Array.from({ length: count }, (_, index) => {
      const x = 17 + index * 7
      return <line key={x} x1={x} y1={y} x2={x - 2.5} y2={y + 7} />
    })}
  </g>
)

function renderGlyph(name: WeatherIconName) {
  switch (name) {
    case 'clear':
      return sun(24, 24, 9)

    case 'mostly-clear':
      return (
        <>
          {sun(30, 18, 7)}
          {cloud(22, 30, 0.9)}
        </>
      )

    case 'partly-cloudy':
      return (
        <>
          {sun(31, 17, 6)}
          {cloud(21, 30, 1)}
        </>
      )

    case 'overcast':
      return (
        <>
          {cloud(20, 22, 0.85, true)}
          {cloud(26, 30, 1)}
        </>
      )

    case 'fog':
      return (
        <>
          {cloud(24, 22, 1, true)}
          <g className="text-ink-muted" stroke="currentColor" strokeWidth={2.4}>
            <line x1={13} y1={33} x2={35} y2={33} />
            <line x1={16} y1={39} x2={32} y2={39} />
          </g>
        </>
      )

    case 'drizzle':
      return (
        <>
          {cloud(24, 21, 1)}
          {drops(3, 31)}
        </>
      )

    case 'rain':
      return (
        <>
          {cloud(24, 20, 1)}
          {drops(4, 30)}
        </>
      )

    case 'freezing-rain':
      return (
        <>
          {cloud(24, 20, 1)}
          {drops(2, 30)}
          <g className="text-cool" stroke="currentColor" strokeWidth={2.4}>
            <line x1={30} y1={30} x2={36} y2={38} />
            <line x1={36} y1={30} x2={30} y2={38} />
          </g>
        </>
      )

    case 'snow':
      return (
        <>
          {cloud(24, 20, 1)}
          <g className="text-cool" stroke="currentColor" strokeWidth={2.4}>
            {[17, 24, 31].map((x) => (
              <g key={x}>
                <line x1={x - 3} y1={33} x2={x + 3} y2={33} />
                <line x1={x} y1={30} x2={x} y2={36} />
              </g>
            ))}
          </g>
        </>
      )

    case 'thunderstorm':
      return (
        <>
          {cloud(24, 20, 1)}
          <path
            d="M26 28 L19 37 h6 l-3 8 10 -11 h-6 l4 -6 Z"
            fill="currentColor"
            fillOpacity={0.9}
            className="text-warm"
          />
        </>
      )

    default:
      return (
        <g className="text-ink-muted" stroke="currentColor" strokeWidth={2.4}>
          <circle cx={24} cy={24} r={11} fill="currentColor" fillOpacity={0.12} />
          <path d="M20 20 a4 4 0 1 1 4.5 5.2 V28" fill="none" />
          <line x1={24.5} y1={32} x2={24.5} y2={32.5} strokeWidth={3.2} />
        </g>
      )
  }
}

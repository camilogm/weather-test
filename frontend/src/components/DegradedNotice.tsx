import type { Provenance } from '../api/types'
import { formatObservedAt } from './format'

/**
 * The backend tells us when an answer did not come from a live call. Passing
 * that on is the whole point — a stale forecast presented as current is worse
 * than an honest warning.
 */
export function DegradedNotice({ provenance }: { provenance: Provenance }) {
  if (!provenance.degraded) {
    return null
  }

  const explanation =
    provenance.source === 'StaleCache'
      ? 'The weather service is unreachable, so this is the last reading we cached.'
      : 'The weather service is unreachable, so this is the last forecast we stored.'

  return (
    <div
      role="status"
      className="flex items-start gap-3 rounded-xl border border-warning/40 bg-warning/10 px-4 py-3 text-sm text-warning-ink"
    >
      <svg viewBox="0 0 20 20" className="mt-0.5 size-5 shrink-0" aria-hidden="true">
        <path
          d="M10 2.5 18.5 17H1.5L10 2.5Z"
          fill="none"
          stroke="currentColor"
          strokeWidth={1.6}
          strokeLinejoin="round"
        />
        <path d="M10 8v4" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" />
        <circle cx={10} cy={14.6} r={0.9} fill="currentColor" />
      </svg>

      <p className="leading-relaxed">
        <span className="font-semibold">Showing older data.</span> {explanation} Last updated{' '}
        {formatObservedAt(provenance.retrievedAt)}.
      </p>
    </div>
  )
}

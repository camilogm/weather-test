import type { Provenance } from '../api/types'
import { t } from '../i18n'
import { formatObservedAt } from './format'

/**
 * The backend tells us when an answer did not come from a live call. Passing
 * that on is the whole point — a stale forecast presented as current is worse
 * than an honest warning.
 */
export function DegradedNotice({ provenance }: Readonly<{ provenance: Provenance }>) {
  if (!provenance.degraded) {
    return null
  }

  const explanation =
    provenance.source === 'StaleCache' ? t('degraded.staleCache') : t('degraded.historical')

  return (
    /*
      A pale wash of the warning hue carries the signal; the prose stays in the
      ink colour so it clears contrast in both schemes without a second token
      invented to hold a darker version of the same orange.
    */
    <div
      role="status"
      className="flex items-start gap-3 rounded-card bg-warning/10 px-5 py-4 text-body text-ink"
    >
      <svg viewBox="0 0 20 20" className="mt-1 size-5 shrink-0 text-warning" aria-hidden="true">
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

      <p>
        <span className="font-semibold">{t('degraded.title')}</span> {explanation}{' '}
        <span className="text-ink-muted">
          {t('degraded.updatedAt', { when: formatObservedAt(provenance.retrievedAt) })}
        </span>
      </p>
    </div>
  )
}

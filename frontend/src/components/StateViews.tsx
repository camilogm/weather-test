import type { WeatherApiError, WeatherErrorKind } from '../api/weather'
import { t, type CopyKey } from '../i18n'

/**
 * One key per failure kind. Written out rather than built by string
 * concatenation so that adding a kind without adding its copy is a compile
 * error instead of a blank paragraph in front of a user.
 */
const MESSAGE_FOR: Record<WeatherErrorKind, CopyKey> = {
  notFound: 'error.notFound',
  unavailable: 'error.unavailable',
  unreachable: 'error.unreachable',
  unexpected: 'error.unexpected',
}

export function LoadingState() {
  return (
    <div className="space-y-8" aria-busy="true" aria-live="polite">
      <span className="sr-only">{t('loading.announcement')}</span>

      {/* The skeletons hold the finished layout's exact shape, so nothing shifts on arrival. */}
      <div className="h-56 animate-pulse rounded-card bg-surface-muted" />

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4 xl:grid-cols-7">
        {Array.from({ length: 7 }, (_, index) => (
          <div key={index} className="h-24 animate-pulse rounded-card bg-surface-muted sm:h-56" />
        ))}
      </div>
    </div>
  )
}

interface ErrorProps {
  error: WeatherApiError
  /** What the person searched for, so a "not found" can name it. */
  city: string
  onRetry: () => void
}

export function ErrorState({ error, city, onRetry }: ErrorProps) {
  // The server's own wording is English and meant for a log; the interface
  // chooses its own words from the kind of failure it was told about.
  const explanation = t(MESSAGE_FOR[error.kind], { city })

  return (
    /*
      The failure sits on the same surface as everything else, and red is spent
      on the glyph alone. A tinted panel with a coloured bar down its side made
      the error louder than the forecast it was standing in for.
    */
    <div role="alert" className="rounded-card bg-surface px-6 py-12 text-center sm:px-10 sm:py-16">
      <svg viewBox="0 0 24 24" className="mx-auto size-10 text-danger" aria-hidden="true">
        <circle cx={12} cy={12} r={9} fill="none" stroke="currentColor" strokeWidth={1.7} />
        <path d="M12 7.5v5.5" stroke="currentColor" strokeWidth={1.9} strokeLinecap="round" />
        <circle cx={12} cy={16.4} r={1} fill="currentColor" />
      </svg>

      <h2 className="mt-5 text-heading font-semibold">{t('error.title')}</h2>
      <p className="mx-auto mt-2 max-w-md text-body text-ink-muted">{explanation}</p>

      {error.retryable && (
        <button
          type="button"
          onClick={onRetry}
          className="mt-7 h-11 cursor-pointer rounded-pill bg-accent px-6 text-body font-medium text-white transition-opacity hover:opacity-85 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent"
        >
          {t('error.retry')}
        </button>
      )}
    </div>
  )
}

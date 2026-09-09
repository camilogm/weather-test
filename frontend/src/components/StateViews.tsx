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
      <div className="h-44 animate-pulse border border-line bg-surface-muted" />

      <div className="grid grid-cols-1 border-t border-line sm:grid-cols-2 sm:border-l lg:grid-cols-4 xl:grid-cols-7">
        {Array.from({ length: 7 }, (_, index) => (
          <div
            key={index}
            className="h-24 animate-pulse border-b border-line bg-surface-muted sm:h-48 sm:border-r"
          />
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
    <div
      role="alert"
      className="border-l-2 border-danger bg-danger/6 px-6 py-8 text-center sm:px-10 sm:py-12"
    >
      <svg viewBox="0 0 24 24" className="mx-auto size-10 text-danger" aria-hidden="true">
        <circle cx={12} cy={12} r={9} fill="none" stroke="currentColor" strokeWidth={1.7} />
        <path d="M12 7.5v5.5" stroke="currentColor" strokeWidth={1.9} strokeLinecap="round" />
        <circle cx={12} cy={16.4} r={1} fill="currentColor" />
      </svg>

      <h2 className="mt-4 font-display text-2xl font-medium tracking-tight">{t('error.title')}</h2>
      <p className="mx-auto mt-2 max-w-md text-sm leading-relaxed text-ink-muted">{explanation}</p>

      {error.retryable && (
        <button
          type="button"
          onClick={onRetry}
          className="mt-6 cursor-pointer border border-ink bg-ink px-5 py-2.5 font-mono text-xs uppercase tracking-[0.16em] text-canvas transition-colors hover:bg-canvas hover:text-ink focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent"
        >
          {t('error.retry')}
        </button>
      )}
    </div>
  )
}

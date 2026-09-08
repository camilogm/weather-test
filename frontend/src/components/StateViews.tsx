import type { WeatherApiError } from '../api/weather'

export function LoadingState() {
  return (
    <div className="space-y-6" aria-busy="true" aria-live="polite">
      <span className="sr-only">Loading the forecast…</span>

      <div className="h-40 animate-pulse rounded-2xl border border-line bg-surface-muted" />

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4 xl:grid-cols-7">
        {Array.from({ length: 7 }, (_, index) => (
          <div
            key={index}
            className="h-24 animate-pulse rounded-xl border border-line bg-surface-muted sm:h-44"
          />
        ))}
      </div>
    </div>
  )
}

interface ErrorProps {
  error: WeatherApiError
  onRetry: () => void
}

export function ErrorState({ error, onRetry }: ErrorProps) {
  return (
    <div
      role="alert"
      className="rounded-2xl border border-danger/40 bg-danger/8 p-6 text-center sm:p-10"
    >
      <svg viewBox="0 0 24 24" className="mx-auto size-10 text-danger" aria-hidden="true">
        <circle cx={12} cy={12} r={9} fill="none" stroke="currentColor" strokeWidth={1.7} />
        <path d="M12 7.5v5.5" stroke="currentColor" strokeWidth={1.9} strokeLinecap="round" />
        <circle cx={12} cy={16.4} r={1} fill="currentColor" />
      </svg>

      <h2 className="mt-4 text-lg font-semibold">The forecast could not be loaded</h2>
      <p className="mx-auto mt-2 max-w-md text-sm leading-relaxed text-ink-muted">{error.message}</p>

      {error.retryable && (
        <button
          type="button"
          onClick={onRetry}
          className="mt-5 rounded-lg bg-accent px-4 py-2 text-sm font-medium text-canvas transition-opacity hover:opacity-90 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent"
        >
          Try again
        </button>
      )}
    </div>
  )
}

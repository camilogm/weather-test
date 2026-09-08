import { useState, type FormEvent } from 'react'

import { DEFAULT_CITY } from './api/weather'
import { t, translateSource } from './i18n'
import { useWeather } from './hooks/useWeather'
import { CurrentConditions } from './components/CurrentConditions'
import { DegradedNotice } from './components/DegradedNotice'
import { ForecastWeek } from './components/ForecastWeek'
import { ErrorState, LoadingState } from './components/StateViews'

export default function App() {
  const [city, setCity] = useState(DEFAULT_CITY)
  const [draft, setDraft] = useState(DEFAULT_CITY)
  const { status, forecast, current, error, refresh } = useWeather(city)

  function onSearch(event: FormEvent) {
    event.preventDefault()
    const trimmed = draft.trim()

    // Re-searching the city already shown should still re-fetch, so an empty
    // change falls through to a refresh instead of doing nothing.
    if (trimmed && trimmed !== city) {
      setCity(trimmed)
    } else {
      refresh()
    }
  }

  return (
    <div className="mx-auto flex min-h-full max-w-6xl flex-col gap-6 px-4 py-6 sm:px-6 sm:py-10">
      <header className="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <p className="text-sm font-medium text-accent">{t('app.eyebrow')}</p>
          <h1 className="mt-1 text-3xl font-semibold tracking-tight sm:text-4xl">
            {forecast?.location.name ?? city}
          </h1>
        </div>

        <form onSubmit={onSearch} className="flex gap-2">
          <label htmlFor="city" className="sr-only">
            {t('search.label')}
          </label>
          <input
            id="city"
            name="city"
            value={draft}
            onChange={(event) => setDraft(event.target.value)}
            placeholder={t('search.placeholder')}
            autoComplete="address-level2"
            className="min-w-0 flex-1 rounded-lg border border-line bg-surface px-3 py-2 text-sm placeholder:text-ink-muted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent sm:w-56 sm:flex-none"
          />
          <button
            type="submit"
            className="shrink-0 rounded-lg bg-accent px-4 py-2 text-sm font-medium text-canvas transition-opacity hover:opacity-90 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent"
          >
            {t('search.submit')}
          </button>
          <button
            type="button"
            onClick={refresh}
            aria-label={t('search.refresh')}
            className="shrink-0 rounded-lg border border-line bg-surface px-3 py-2 transition-colors hover:bg-surface-muted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent"
          >
            <svg viewBox="0 0 20 20" className="size-4" aria-hidden="true">
              <path
                d="M16.5 10a6.5 6.5 0 1 1-1.9-4.6"
                fill="none"
                stroke="currentColor"
                strokeWidth={1.8}
                strokeLinecap="round"
              />
              <path
                d="M16.6 2.6v3.6H13"
                fill="none"
                stroke="currentColor"
                strokeWidth={1.8}
                strokeLinecap="round"
                strokeLinejoin="round"
              />
            </svg>
          </button>
        </form>
      </header>

      <main className="flex-1 space-y-6">
        {status === 'loading' && <LoadingState />}

        {status === 'error' && error && <ErrorState error={error} city={city} onRetry={refresh} />}

        {status === 'ready' && forecast && (
          <>
            <DegradedNotice provenance={forecast.provenance} />
            {current && <CurrentConditions current={current} />}
            <ForecastWeek days={forecast.days} />
          </>
        )}
      </main>

      <footer className="border-t border-line pt-4 text-xs text-ink-muted">
        {t('footer.credit')}
        {forecast && (
          <>
            {' · '}
            {t('footer.servedFrom', { source: translateSource(forecast.provenance.source) })}
          </>
        )}
      </footer>
    </div>
  )
}

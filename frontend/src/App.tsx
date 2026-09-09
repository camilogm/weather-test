import { useState } from 'react'

import type { SelectedLocation } from './api/types'
import { DEFAULT_LOCATION } from './api/weather'
import { t, translateSource } from './i18n'
import { useWeather } from './hooks/useWeather'
import { CityPicker } from './components/CityPicker'
import { CurrentConditions } from './components/CurrentConditions'
import { DegradedNotice } from './components/DegradedNotice'
import { ForecastWeek } from './components/ForecastWeek'
import { formatCoordinates } from './components/format'
import { ErrorState, LoadingState } from './components/StateViews'

export default function App() {
  const [location, setLocation] = useState<SelectedLocation>(DEFAULT_LOCATION)
  const { status, forecast, current, error, refresh } = useWeather(location)

  const place = forecast?.location ?? location

  return (
    <div className="mx-auto flex min-h-full max-w-6xl flex-col gap-6 px-4 py-6 sm:px-6 sm:py-10">
      <header className="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <p className="text-sm font-medium text-accent">{t('app.eyebrow')}</p>
          <h1 className="mt-1 text-3xl font-semibold tracking-tight sm:text-4xl">
            {place.name}
          </h1>

          <p className="mt-2 font-mono text-xs text-ink-muted">
            {formatCoordinates(place.latitude, place.longitude)}
          </p>
        </div>

        <div className="flex items-start gap-2">
          <CityPicker selected={location} onSelect={setLocation} />

          <button
            type="button"
            onClick={refresh}
            aria-label={t('picker.refresh')}
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
        </div>
      </header>

      <main className="flex-1 space-y-6">
        {status === 'loading' && <LoadingState />}

        {status === 'error' && error && (
          <ErrorState error={error} city={location.name} onRetry={refresh} />
        )}

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

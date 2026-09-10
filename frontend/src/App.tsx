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
  const { status, forecast, current, error, refresh, isRefreshing } = useWeather(location)

  /*
    The name comes from the picker, not from the response. The API answers a
    coordinate pair and its cache is keyed on those coordinates alone, so the
    name it echoes back is whatever the first caller for that point happened to
    send — "Custom location" when a caller sent none at all. The picker already
    knows which city the person chose; that is the name worth printing.
    Coordinates still come from the answer, because those are the ones actually
    forecast.
  */
  const coordinates = forecast?.location ?? location

  return (
    <div className="mx-auto flex min-h-full max-w-6xl flex-col px-5 py-8 sm:px-8 sm:py-12">
      {/*
        A masthead, not a toolbar: kicker, name, coordinates, then the heavy
        rule that every printed bulletin puts under its title.
      */}
      <header className="flex flex-col gap-6 border-b-2 border-ink pb-5 sm:flex-row sm:items-end sm:justify-between">
        <div className="min-w-0">
          <p className="font-mono text-[0.7rem] font-medium uppercase tracking-[0.22em] text-warm">
            {t('app.eyebrow')}
          </p>

          <h1 className="mt-2 font-display text-5xl font-medium leading-[0.95] tracking-tight sm:text-6xl">
            {location.name}
          </h1>

          <p className="mt-2 font-mono text-xs text-ink-muted">
            {formatCoordinates(coordinates.latitude, coordinates.longitude)}
          </p>
        </div>

        <div className="flex items-start gap-2">
          <CityPicker selected={location} onSelect={setLocation} />

          {/*
            A refresh no longer blanks the page to reload what is already on it,
            which leaves the click with nothing to show for itself. The spinning
            glyph is that acknowledgement, and aria-busy is the same news for
            anyone not looking at it.
          */}
          <button
            type="button"
            onClick={refresh}
            aria-label={t('picker.refresh')}
            aria-busy={isRefreshing}
            // size-11 is 44px: it lines the button up with the input beside it
            // and clears the minimum touch target in the same stroke.
            className="flex size-11 shrink-0 cursor-pointer items-center justify-center border border-ink bg-surface transition-colors hover:bg-ink hover:text-canvas focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent"
          >
            <svg
              viewBox="0 0 20 20"
              className={['size-4', isRefreshing ? 'motion-safe:animate-spin' : ''].join(' ')}
              aria-hidden="true"
            >
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

      <main className="flex-1 space-y-8 py-8">
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

      {/* The colophon: where the page came from, set small in mono like a credit line. */}
      <footer className="border-t border-line pt-4 font-mono text-[0.7rem] leading-relaxed text-ink-muted">
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

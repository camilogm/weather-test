import { useState } from 'react'

import type { SelectedLocation } from './api/types'
import { DEFAULT_LOCATION } from './api/weather'
import { t, translateSource } from './i18n'
import { useWeather } from './hooks/useWeather'
import { CityPicker } from './components/CityPicker'
import { CurrentConditions } from './components/CurrentConditions'
import { DegradedNotice } from './components/DegradedNotice'
import { ForecastCarousel } from './components/ForecastCarousel'
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
    <div className="mx-auto flex min-h-full max-w-7xl flex-col px-5 py-6 sm:px-8 sm:py-8">
      {/*
        The header separates itself with air, not with a rule. A heavy border
        under a title is a masthead; a store page just leaves room and lets the
        size of the type do the work.
      */}
      <header className="pb-6">
        <p className="text-label font-medium uppercase text-ink-muted">{t('app.eyebrow')}</p>

        {/*
          The search shares the headline's line and centres on it. Two earlier
          layouts got this wrong in opposite directions: aligning to the bottom
          of the whole title block put the field level with the coordinates
          rather than the name, and giving it a row of its own spent 84px of
          height on a single 44px control. Centred against the headline it is
          level with the city, and the row costs 2px over the headline alone.
        */}
        <div className="mt-2 flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between sm:gap-8">
          <h1 className="text-title font-semibold">{location.name}</h1>

          <div className="flex shrink-0 items-center gap-3">
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
              className="flex size-11 shrink-0 cursor-pointer items-center justify-center rounded-full bg-surface text-ink-muted shadow-control transition-colors hover:bg-surface-muted hover:text-ink focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent"
            >
              <svg
                viewBox="0 0 20 20"
                className={['size-5', isRefreshing ? 'motion-safe:animate-spin' : ''].join(' ')}
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
        </div>

        <p className="mt-2 text-caption tabular-nums text-ink-muted">
          {formatCoordinates(coordinates.latitude, coordinates.longitude)}
        </p>
      </header>

      <main className="flex-1 space-y-5">
        {status === 'loading' && <LoadingState />}

        {status === 'error' && error && (
          <ErrorState error={error} city={location.name} onRetry={refresh} />
        )}

        {status === 'ready' && forecast && (
          <>
            <DegradedNotice provenance={forecast.provenance} />
            {current && <CurrentConditions current={current} />}
            <ForecastCarousel days={forecast.days} />
          </>
        )}
      </main>

      <footer className="mt-8 border-t border-line pt-4 text-caption text-ink-muted">
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

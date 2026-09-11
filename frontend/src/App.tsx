import { useCallback, useState } from 'react'

import type { SelectedLocation } from './api/types'
import { DEFAULT_LOCATION } from './api/weather'
import { t, translateSource } from './i18n'
import { useWeather } from './hooks/useWeather'
import { CityPicker } from './components/CityPicker'
import { CurrentConditions } from './components/CurrentConditions'
import { DegradedNotice } from './components/DegradedNotice'
import { ForecastCarousel } from './components/ForecastCarousel'
import { formatCoordinates } from './components/format'
import { headingBetween, type Heading } from './components/heading'
import { ErrorState, LoadingState } from './components/StateViews'

/** Where the page is, and how it got there. */
interface Journey {
  location: SelectedLocation
  /** 'none' on the first load: there was nothing on screen to replace. */
  heading: Heading
}

/**
 * A heading picks the animation, and a `Record` is what makes adding a heading
 * without an animation a compile error rather than a class string of
 * "undefined" quietly doing nothing.
 */
const ARRIVAL: Record<Heading, string> = {
  east: 'motion-safe:animate-arrive-east',
  west: 'motion-safe:animate-arrive-west',
  none: 'motion-safe:animate-arrive',
}

export default function App() {
  /*
    Place and heading are ONE piece of state, not two.

    Kept apart they can disagree for a render — the heading of the previous move
    paired with the location of the current one — and that render is the one the
    animation reads. Updated together, through the functional form, the previous
    longitude is always the one actually on screen and the callback never goes
    stale, so it needs no dependencies at all.
  */
  const [journey, setJourney] = useState<Journey>({
    location: DEFAULT_LOCATION,
    heading: 'none',
  })

  const travelTo = useCallback((destination: SelectedLocation) => {
    setJourney((from) => ({
      location: destination,
      heading: headingBetween(from.location.longitude, destination.longitude),
    }))
  }, [])

  const { location, heading } = journey
  const { status, forecast, current, error, refresh, isRefreshing } = useWeather(location)

  /*
    The key the arrival animation hangs on, and the whole of the motion logic.

    It moves when the PLACE changes and again when that place's answer lands, so
    the skeleton slides in and the forecast slides in behind it. It deliberately
    does not carry the revalidation flag: a refresh is the same place with the
    same answer coming, and re-running the animation for it would throw the page
    sideways under somebody who only asked for fresher numbers.
  */
  const arrival = `${location.latitude},${location.longitude}:${status}`

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
    /*
      overflow-x-clip, and clip rather than hidden on purpose: `hidden` would
      force the other axis to `auto` and turn this into a scroll container,
      which would trap the city popover inside it. `clip` is the one value that
      lets overflow-y stay `visible`, so the popover still hangs below the
      field while the 40px of arrival travel is cut off at the gutter instead
      of widening the document.

      It clips at the PADDING box, which is why the carousel arrows survive:
      they hang 18px into a 32px gutter and never reach the edge.
    */
    <div className="mx-auto flex min-h-full max-w-7xl flex-col overflow-x-clip px-5 py-6 sm:px-8 sm:py-8">
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
            <CityPicker selected={location} onSelect={travelTo} />

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

      <main className="flex-1">
        {/*
          One wrapper carries the key, so React remounts this subtree and the
          browser runs the arrival animation on it — a CSS animation fires once
          per element, and a keyed remount IS a new element. No animation
          library, no exit choreography, no list of in-flight transitions to
          keep in state.

          The heading is chosen once per move and applies to everything that
          arrives during it: the skeleton comes in from the same side the
          forecast will, so the two read as one continuous movement west or
          east rather than as two unrelated flourishes.

          data-heading states that DECISION; the class is only how it is carried
          out. Written down it is visible in devtools while tuning a curve, and
          it lets a test assert which way the page thought it was going without
          pinning itself to the name of a utility class.
        */}
        <div key={arrival} data-heading={heading} className={`space-y-5 ${ARRIVAL[heading]}`}>
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
        </div>
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

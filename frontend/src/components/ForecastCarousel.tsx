import { useState } from 'react'

import type { DailyForecast } from '../api/types'
import { useCarousel } from '../hooks/useCarousel'
import { t, translateCondition } from '../i18n'
import { WeatherIcon } from './WeatherIcon'
import { dayAndMonthOf, formatTemperature, isToday, weekdayOf } from './format'

/**
 * Ranges the control offers: one week, two weeks, and everything there is.
 *
 * Seven leads because it is what the endpoint answers when nobody asks, so the
 * page opens on the forecast it has always shown. Anything past the last of
 * these is not a range this data can fill.
 */
const RANGES = [7, 14] as const

const DEFAULT_RANGE = 7

export function ForecastCarousel({ days }: { days: DailyForecast[] }) {
  const [requested, setRequested] = useState<number>(DEFAULT_RANGE)

  // Clamped rather than corrected in an effect. A degraded answer can carry
  // fewer days than the horizon, and a range wider than the data is simply the
  // data — writing that back into state would only be the same number twice.
  const range = Math.min(requested, days.length)
  const visible = days.slice(0, range)

  const { ref, overflows, canScrollBack, canScrollForward, back, forward } =
    useCarousel<HTMLOListElement>(visible.length)

  // One scale for the whole visible range, so the bars stay comparable across
  // days rather than each card becoming its own private chart. It is recomputed
  // per range on purpose: a fortnight has a wider spread than a week, and a
  // scale drawn from days nobody is looking at would flatten the ones they are.
  const coldest = Math.min(...visible.map((day) => day.minTemperatureC))
  const warmest = Math.max(...visible.map((day) => day.maxTemperatureC))

  return (
    <section aria-labelledby="forecast-heading">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 id="forecast-heading" className="text-label font-medium uppercase text-ink-muted">
          {t('forecast.heading', { days: visible.length })}
        </h2>

        <div className="flex items-center gap-2">
          <RangePicker available={days.length} value={range} onChange={setRequested} />

          {/*
            Pointer convenience, and nothing more. They appear only when there is
            something off screen, and they are gone below sm where the gesture is
            a swipe — the track itself stays focusable and arrow-key scrollable
            at every size, so nobody depends on these to get through the week.
          */}
          {overflows && (
            <div className="hidden shrink-0 items-center gap-1 sm:flex">
              <Arrow label={t('forecast.previous')} onClick={back} disabled={!canScrollBack} back />
              <Arrow label={t('forecast.next')} onClick={forward} disabled={!canScrollForward} />
            </div>
          )}
        </div>
      </div>

      {/*
        A real overflow container, not a transformed strip with an index. The
        browser already knows how to do momentum, trackpad gestures, page keys
        and bringing a focused card into view; re-implementing any of that would
        be re-implementing it worse.

        tabIndex makes the region reachable for anyone not using a pointer — a
        scrollable area that cannot be focused is unreachable by keyboard, which
        is the whole of WCAG 2.1.1 in one attribute.
      */}
      <ol
        ref={ref}
        tabIndex={0}
        aria-label={t('forecast.track')}
        className="mt-4 flex snap-x snap-mandatory gap-3 overflow-x-auto pb-2 motion-safe:scroll-smooth focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent"
      >
        {visible.map((day) => (
          <ForecastDay key={day.date} day={day} coldest={coldest} warmest={warmest} />
        ))}
      </ol>
    </section>
  )
}

interface RangeProps {
  available: number
  value: number
  onChange: (days: number) => void
}

/**
 * Native radios under the styling, not buttons carrying aria-pressed.
 *
 * The choice IS exclusive, and radios say so without a line of JavaScript:
 * arrow keys move between them, the group is one tab stop, and a screen reader
 * announces "2 of 3" rather than three unrelated toggles that happen to look
 * related. A roving tabindex would be more code to arrive somewhere worse.
 */
function RangePicker({ available, value, onChange }: RangeProps) {
  // Whatever is left over always closes the list, so the widest option shows
  // every day there is — ten when a degraded answer carries ten — instead of a
  // control that promises sixteen and quietly delivers what it has.
  const offered = [...RANGES.filter((range) => range < available), available]

  if (offered.length < 2) {
    return null
  }

  return (
    <fieldset className="flex shrink-0 items-center gap-1 rounded-pill bg-surface p-1 shadow-control">
      <legend className="sr-only">{t('forecast.rangeLegend')}</legend>

      {offered.map((option) => (
        <label key={option} className="relative cursor-pointer">
          <input
            type="radio"
            name="forecast-range"
            value={option}
            checked={option === value}
            onChange={() => onChange(option)}
            className="peer sr-only"
          />
          {/*
            h-9 inside p-1 makes the whole control 44px, level with the search
            field and the refresh button beside it, while each segment stays
            well past the 24px minimum a pointer target owes.
          */}
          <span className="flex h-9 items-center rounded-pill px-3 text-caption tabular-nums text-ink-muted transition-colors peer-hover:text-ink peer-checked:bg-accent peer-checked:text-white peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-accent">
            {t('forecast.rangeOption', { days: option })}
          </span>
        </label>
      ))}
    </fieldset>
  )
}

interface ArrowProps {
  label: string
  onClick: () => void
  disabled: boolean
  back?: boolean
}

function Arrow({ label, onClick, disabled, back = false }: ArrowProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      aria-label={label}
      className="flex size-9 cursor-pointer items-center justify-center rounded-full bg-surface text-ink-muted shadow-control transition-colors hover:bg-surface-muted hover:text-ink focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent disabled:cursor-default disabled:opacity-35 disabled:hover:bg-surface disabled:hover:text-ink-muted"
    >
      <svg viewBox="0 0 20 20" className="size-4" aria-hidden="true">
        <path
          d={back ? 'M12.5 4 6.5 10l6 6' : 'M7.5 4l6 6-6 6'}
          fill="none"
          stroke="currentColor"
          strokeWidth={2}
          strokeLinecap="round"
          strokeLinejoin="round"
        />
      </svg>
    </button>
  )
}

interface DayProps {
  day: DailyForecast
  coldest: number
  warmest: number
}

function ForecastDay({ day, coldest, warmest }: DayProps) {
  const today = isToday(day.date)
  const condition = translateCondition(day.condition)

  const span = Math.max(warmest - coldest, 1)
  const offset = ((day.minTemperatureC - coldest) / span) * 100
  const width = ((day.maxTemperatureC - day.minTemperatureC) / span) * 100

  return (
    // flex: 1 0 8.75rem — grow into whatever is spare, never shrink below 140px.
    //
    // One declaration covering both halves of this component. When the range
    // fits, the free space is shared out and the row reaches the same right edge
    // as the panel above it, instead of stopping short and reading as a
    // misalignment. When it does not fit, shrink-0 pins every card at 140px, the
    // sum overflows the container, and that overflow IS the carousel — with the
    // last visible card half cut off, which is the cheapest signal there is that
    // the row keeps going.
    //
    // The cap is for the short answer. A degraded snapshot can carry four days,
    // and four cards sharing 1216px would be 295px each: an icon and two numbers
    // marooned in the middle of a billboard.
    <li className="flex max-w-56 shrink-0 grow basis-35 snap-start flex-col gap-2 rounded-card bg-surface px-4 py-4 text-center">
      {/*
        Today is marked by setting its name in the accent, not by a coloured rule
        across the tile. One accent, used the same way everywhere.
      */}
      <div>
        <p className={['text-caption font-semibold', today ? 'text-accent' : 'text-ink'].join(' ')}>
          {today ? t('forecast.today') : weekdayOf(day.date)}
        </p>
        <p className="mt-0.5 text-caption tabular-nums text-ink-muted">{dayAndMonthOf(day.date)}</p>
      </div>

      <WeatherIcon name={day.icon} label={condition} className="mx-auto size-10 shrink-0" />

      <p className="text-caption leading-snug text-ink-muted">{condition}</p>

      {/*
        mt-auto, not a min-height on the condition above it. Every card is a flex
        item in the same row, so they already share a height; pushing the
        temperature to the bottom of it aligns the numbers across the range
        whether the condition wraps or not.
      */}
      <p className="mt-auto tabular-nums">
        <span className="text-heading font-semibold">{formatTemperature(day.maxTemperatureC)}</span>
        <span className="ml-2 text-body text-ink-muted">{formatTemperature(day.minTemperatureC)}</span>
      </p>

      {/*
        The track is the range's whole spread, tinted cold to hot, and the solid
        bar is this day's slice of it. The gradient belongs on the track: on the
        bar it would run across whatever width each day happened to need, so the
        same colour would sit at a different temperature in every card and the
        scale would say nothing at all.
      */}
      <div
        className="h-1.5 overflow-hidden rounded-pill bg-gradient-to-r from-cool/25 to-warm/25"
        aria-hidden="true"
      >
        <div
          className="h-full rounded-pill bg-ink"
          style={{ marginInlineStart: `${offset}%`, width: `${Math.max(width, 6)}%` }}
        />
      </div>
    </li>
  )
}

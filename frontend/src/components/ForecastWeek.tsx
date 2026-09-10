import type { DailyForecast } from '../api/types'
import { t, translateCondition } from '../i18n'
import { WeatherIcon } from './WeatherIcon'
import { dayAndMonthOf, formatTemperature, isToday, weekdayOf } from './format'

export function ForecastWeek({ days }: { days: DailyForecast[] }) {
  // One scale for the whole week, so the bars below are comparable across days
  // rather than each card being its own private chart.
  const coldest = Math.min(...days.map((day) => day.minTemperatureC))
  const warmest = Math.max(...days.map((day) => day.maxTemperatureC))

  return (
    <section aria-labelledby="forecast-heading">
      <h2 id="forecast-heading" className="text-label font-medium uppercase text-ink-muted">
        {t('forecast.heading', { days: days.length })}
      </h2>

      {/*
        Separate rounded tiles with a gap, not a ruled table. The gap is what
        separates them, which means the grid stays correct at every column
        count: seven days over four columns leaves one slot empty, and an empty
        slot in a gap grid is simply empty. In a ruled grid it was a hole with
        two hairlines hanging off it.
      */}
      <ol className="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4 xl:grid-cols-7">
        {days.map((day) => (
          <ForecastDay key={day.date} day={day} coldest={coldest} warmest={warmest} />
        ))}
      </ol>
    </section>
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
    <li
      className={[
        'flex items-center gap-4 rounded-card bg-surface px-4 py-4',
        'sm:flex-col sm:items-stretch sm:gap-2 sm:px-4 sm:py-4 sm:text-center',
      ].join(' ')}
    >
      {/*
        Today is marked by setting its name in the accent, not by a coloured
        rule across the tile. One accent, used the same way everywhere.
      */}
      <div className="sm:order-1">
        <p
          className={[
            'text-caption font-semibold',
            today ? 'text-accent' : 'text-ink',
          ].join(' ')}
        >
          {today ? t('forecast.today') : weekdayOf(day.date)}
        </p>
        <p className="mt-0.5 text-caption tabular-nums text-ink-muted">{dayAndMonthOf(day.date)}</p>
      </div>

      <WeatherIcon
        name={day.icon}
        label={condition}
        className="size-10 shrink-0 sm:order-2 sm:mx-auto"
      />

      {/*
        mt-auto, not a min-height on the condition above it. Every tile is a
        grid item in the same row, so they already share a height; pushing the
        temperature to the bottom of that height aligns the numbers across the
        week whether the condition wraps or not. The min-height only ever
        guessed at how many lines the condition would need, and reserved 40px
        to use 19 on the days it guessed wrong.
      */}
      <div className="ml-auto text-right sm:order-4 sm:ml-0 sm:mt-auto sm:text-center">
        <p className="tabular-nums">
          <span className="text-heading font-semibold">
            {formatTemperature(day.maxTemperatureC)}
          </span>
          <span className="ml-2 text-body text-ink-muted">
            {formatTemperature(day.minTemperatureC)}
          </span>
        </p>
      </div>

      <p className="sr-only sm:not-sr-only sm:order-3 sm:text-caption sm:leading-snug sm:text-ink-muted">
        {condition}
      </p>

      {/*
        The track is the week's whole range, tinted cold to hot, and the solid
        bar is this day's slice of it. The gradient used to live on the bar
        instead, which meant it ran across whatever width that day happened to
        need — so the same colour sat at a different temperature in every cell
        and the scale said nothing. On the track it is a scale.
      */}
      <div
        className="hidden h-1.5 overflow-hidden rounded-pill bg-gradient-to-r from-cool/25 to-warm/25 sm:order-5 sm:block"
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

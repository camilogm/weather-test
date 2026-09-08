import type { DailyForecast } from '../api/types'
import { WeatherIcon } from './WeatherIcon'
import {
  dayAndMonthOf,
  formatTemperature,
  humanizeCondition,
  isToday,
  weekdayOf,
} from './format'

export function ForecastWeek({ days }: { days: DailyForecast[] }) {
  // One scale for the whole week, so the bars below are comparable across days
  // rather than each card being its own private chart.
  const coldest = Math.min(...days.map((day) => day.minTemperatureC))
  const warmest = Math.max(...days.map((day) => day.maxTemperatureC))

  return (
    <section aria-labelledby="forecast-heading">
      <h2 id="forecast-heading" className="mb-3 text-sm font-medium text-ink-muted">
        Next {days.length} days
      </h2>

      <ol className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4 xl:grid-cols-7">
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
  const condition = humanizeCondition(day.condition)

  const span = Math.max(warmest - coldest, 1)
  const offset = ((day.minTemperatureC - coldest) / span) * 100
  const width = ((day.maxTemperatureC - day.minTemperatureC) / span) * 100

  return (
    <li
      className={[
        'flex items-center gap-4 rounded-xl border p-4 transition-colors',
        'sm:flex-col sm:items-stretch sm:gap-3 sm:text-center',
        today ? 'border-accent/50 bg-accent/8' : 'border-line bg-surface',
      ].join(' ')}
    >
      <div className="sm:order-1">
        <p className="font-semibold">{today ? 'Today' : weekdayOf(day.date)}</p>
        <p className="text-xs text-ink-muted">{dayAndMonthOf(day.date)}</p>
      </div>

      <WeatherIcon
        name={day.icon}
        label={condition}
        className="size-10 shrink-0 sm:order-2 sm:mx-auto sm:size-12"
      />

      <div className="ml-auto text-right sm:order-4 sm:ml-0 sm:text-center">
        <p className="tabular-nums">
          <span className="text-lg font-semibold">{formatTemperature(day.maxTemperatureC)}</span>
          <span className="ml-2 text-ink-muted">{formatTemperature(day.minTemperatureC)}</span>
        </p>
      </div>

      <p className="sr-only sm:not-sr-only sm:order-3 sm:text-xs sm:text-ink-muted">{condition}</p>

      {/* Where this day's range sits inside the week's range. */}
      <div
        className="hidden h-1.5 overflow-hidden rounded-full bg-surface-muted sm:order-5 sm:block"
        aria-hidden="true"
      >
        <div
          className="h-full rounded-full bg-gradient-to-r from-cool to-warm"
          style={{ marginInlineStart: `${offset}%`, width: `${Math.max(width, 6)}%` }}
        />
      </div>
    </li>
  )
}

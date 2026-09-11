import type { CurrentWeather } from '../api/types'
import { t, translateCondition } from '../i18n'
import { WeatherIcon } from './WeatherIcon'
import { formatObservedAt, formatTemperature } from './format'

export function CurrentConditions({ current }: Readonly<{ current: CurrentWeather }>) {
  const condition = translateCondition(current.condition)

  return (
    /*
      No border. White on #f5f5f7 — and #1d1d1f on black in the dark scheme —
      already reads as a raised surface, so drawing a hairline around it only
      adds a line the eye has to process. The radius does the rest.
    */
    <section aria-labelledby="current-heading" className="rounded-card bg-surface">
      <h2 id="current-heading" className="sr-only">
        {t('current.heading')}
      </h2>

      {/*
        The columns are sized by what is in them. flex-1 on both halves split
        the card down the middle regardless of content, which left 143px of
        dead air between the end of the condition and the rule — the hero side
        needed 321px and the stats side needed 504. `auto 1fr` gives the hero
        exactly what it asks for and hands the remainder to the stats.
      */}
      <div className="flex flex-col gap-8 p-6 sm:grid sm:grid-cols-[auto_1fr] sm:items-center sm:gap-0 sm:p-6">
        <div className="flex items-center gap-6 sm:pr-10">
          <WeatherIcon name={current.icon} label={condition} className="size-16 sm:size-22" />

          <div className="min-w-0">
            {/* The temperature is the headline of the page, so it is set like one. */}
            <p className="flex items-baseline gap-1.5">
              <span className="text-hero font-semibold tabular-nums">
                {formatTemperature(current.temperatureC)}
              </span>
              <span className="text-body text-ink-muted">C</span>
            </p>

            <p className="mt-2 text-heading text-ink-muted">{condition}</p>
          </div>
        </div>

        {/*
          Apparent temperature used to be buried in a sentence. As a third
          column it is scannable and it gives the rail enough weight to hold
          its half of the block.
        */}
        <dl className="flex justify-between gap-6 border-t border-line pt-6 sm:items-center sm:self-stretch sm:border-t-0 sm:border-l sm:pt-0 sm:pl-10">
          <Stat
            label={t('current.apparent')}
            value={formatTemperature(current.apparentTemperatureC)}
          />
          <Stat
            label={t('current.humidity')}
            value={t('current.humidityValue', { percent: current.relativeHumidityPercent })}
          />
          <Stat
            label={t('current.wind')}
            value={t('current.windValue', { speed: Math.round(current.windSpeedKph) })}
          />
        </dl>
      </div>

      <p className="border-t border-line px-6 py-2.5 text-caption text-ink-muted sm:px-6">
        {t('current.observedAt', { when: formatObservedAt(current.observedAt) })}
      </p>
    </section>
  )
}

function Stat({ label, value }: Readonly<{ label: string; value: string }>) {
  return (
    <div>
      <dt className="text-label font-medium uppercase text-ink-muted">{label}</dt>
      <dd className="mt-2 text-heading font-semibold tabular-nums">{value}</dd>
    </div>
  )
}

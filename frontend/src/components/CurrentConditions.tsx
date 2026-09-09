import type { CurrentWeather } from '../api/types'
import { t, translateCondition } from '../i18n'
import { WeatherIcon } from './WeatherIcon'
import { formatObservedAt, formatTemperature } from './format'

export function CurrentConditions({ current }: { current: CurrentWeather }) {
  const condition = translateCondition(current.condition)

  return (
    <section aria-labelledby="current-heading" className="border border-line bg-surface">
      <h2 id="current-heading" className="sr-only">
        {t('current.heading')}
      </h2>

      {/*
        Two halves with the rule down the middle. Letting the headline and the
        stats sit at opposite edges of a 1152px container left more than half
        the block empty, which reads as a layout that broke rather than as
        whitespace anyone chose.
      */}
      <div className="flex flex-col gap-6 p-5 sm:flex-row sm:items-center sm:gap-0 sm:p-8">
        <div className="flex flex-1 items-center gap-5 sm:pr-10">
          <WeatherIcon name={current.icon} label={condition} className="size-16 sm:size-24" />

          <div className="min-w-0">
            {/* The temperature is the headline of the page, so it is set like one. */}
            <p className="flex items-baseline gap-1.5">
              <span className="font-display text-6xl font-medium leading-none tracking-tight tabular-nums sm:text-8xl">
                {formatTemperature(current.temperatureC)}
              </span>
              <span className="font-mono text-sm text-ink-muted sm:text-base">C</span>
            </p>

            {/* Italic serif, the same caption voice the week below uses. */}
            <p className="mt-2 font-display text-lg italic text-ink-muted">{condition}</p>
          </div>
        </div>

        {/*
          Apparent temperature used to be buried in a sentence. As a third
          column it is scannable and it gives the rail enough weight to hold
          its half of the block.
        */}
        <dl className="flex flex-1 justify-between gap-6 border-t border-line pt-5 sm:border-t-0 sm:border-l sm:pt-0 sm:pl-10">
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

      <p className="border-t border-line px-5 py-3 font-mono text-[0.7rem] text-ink-muted sm:px-8">
        {t('current.observedAt', { when: formatObservedAt(current.observedAt) })}
      </p>
    </section>
  )
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="font-mono text-[0.7rem] uppercase tracking-[0.16em] text-ink-muted">
        {label}
      </dt>
      <dd className="mt-1.5 font-display text-2xl leading-none tabular-nums">{value}</dd>
    </div>
  )
}

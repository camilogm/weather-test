import type { CurrentWeather } from '../api/types'
import { t, translateCondition } from '../i18n'
import { WeatherIcon } from './WeatherIcon'
import { formatObservedAt, formatTemperature } from './format'

export function CurrentConditions({ current }: { current: CurrentWeather }) {
  const condition = translateCondition(current.condition)

  return (
    <section
      aria-labelledby="current-heading"
      className="rounded-2xl border border-line bg-surface p-5 sm:p-6"
    >
      <h2 id="current-heading" className="sr-only">
        {t('current.heading')}
      </h2>

      <div className="flex flex-col gap-5 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-center gap-4">
          <WeatherIcon name={current.icon} label={condition} className="size-16 sm:size-20" />

          <div>
            <p className="flex items-baseline gap-1.5">
              <span className="text-5xl font-semibold tracking-tight tabular-nums sm:text-6xl">
                {formatTemperature(current.temperatureC)}
              </span>
              <span className="text-lg text-ink-muted">C</span>
            </p>
            <p className="mt-0.5 text-sm text-ink-muted">
              {t('current.summary', {
                condition,
                apparent: formatTemperature(current.apparentTemperatureC),
              })}
            </p>
          </div>
        </div>

        <dl className="grid grid-cols-2 gap-x-8 gap-y-3 text-sm sm:text-right">
          <div className="sm:contents">
            <dt className="text-ink-muted">{t('current.humidity')}</dt>
            <dd className="font-medium tabular-nums">
              {t('current.humidityValue', { percent: current.relativeHumidityPercent })}
            </dd>
          </div>
          <div className="sm:contents">
            <dt className="text-ink-muted">{t('current.wind')}</dt>
            <dd className="font-medium tabular-nums">
              {t('current.windValue', { speed: Math.round(current.windSpeedKph) })}
            </dd>
          </div>
        </dl>
      </div>

      <p className="mt-4 border-t border-line pt-3 text-xs text-ink-muted">
        {t('current.observedAt', { when: formatObservedAt(current.observedAt) })}
      </p>
    </section>
  )
}

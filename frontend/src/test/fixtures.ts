import type { CurrentWeather, Forecast, LocationSuggestion, SelectedLocation } from '../api/types'
import { FORECAST_HORIZON_DAYS } from '../api/weather'

const PROVENANCE = { source: 'Provider', degraded: false, retrievedAt: '2026-09-07T12:00:00Z' } as const

export function aPlace(name: string, latitude = 13.69, longitude = -89.22): SelectedLocation {
  return { name, latitude, longitude }
}

/**
 * "YYYY-MM-DD" in local time, the way the API sends calendar dates.
 *
 * Built from the parts rather than from toISOString, which is UTC and so writes
 * the day before for anyone west of Greenwich — the exact bug parseCalendarDate
 * exists to avoid on the way back in.
 */
function calendarDate(year: number, month: number, day: number): string {
  const date = new Date(year, month, day)

  return [
    date.getFullYear(),
    String(date.getMonth() + 1).padStart(2, '0'),
    String(date.getDate()).padStart(2, '0'),
  ].join('-')
}

/**
 * A forecast starting today by default, because "today" is what the interface
 * marks and it cannot be pinned to a literal without the fixture going stale.
 * Day arithmetic goes through the Date constructor, which rolls months over and
 * survives a DST boundary that adding milliseconds would not.
 */
export function aForecastFor(
  place: SelectedLocation,
  { length = FORECAST_HORIZON_DAYS, from = new Date() }: { length?: number; from?: Date } = {},
): Forecast {
  return {
    location: place,
    provenance: { ...PROVENANCE },
    days: Array.from({ length }, (_, offset) => ({
      date: calendarDate(from.getFullYear(), from.getMonth(), from.getDate() + offset),
      minTemperatureC: 21 + offset,
      maxTemperatureC: 31 + offset,
      condition: 'PartlyCloudy',
      icon: 'partly-cloudy' as const,
    })),
  }
}

export function aCurrentFor(place: SelectedLocation): CurrentWeather {
  return {
    location: place,
    provenance: { ...PROVENANCE },
    temperatureC: 28.5,
    apparentTemperatureC: 32.1,
    windSpeedKph: 11.2,
    relativeHumidityPercent: 74,
    condition: 'PartlyCloudy',
    icon: 'partly-cloudy',
    observedAt: '2026-09-07T12:00:00Z',
  }
}

export function aSuggestion(name: string): LocationSuggestion {
  return {
    name,
    region: 'San Salvador',
    country: 'El Salvador',
    countryCode: 'SV',
    latitude: 13.69,
    longitude: -89.22,
  }
}

/**
 * A promise whose settling this test decides, so a slow response and a fast one
 * can be resolved in whatever order the race being described requires.
 */
export function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void

  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })

  return { promise, resolve, reject }
}

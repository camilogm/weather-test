import type { CurrentWeather, Forecast, LocationSuggestion, SelectedLocation } from '../api/types'

const PROVENANCE = { source: 'Provider', degraded: false, retrievedAt: '2026-09-07T12:00:00Z' } as const

export function aPlace(name: string, latitude = 13.69, longitude = -89.22): SelectedLocation {
  return { name, latitude, longitude }
}

export function aForecastFor(place: SelectedLocation): Forecast {
  return {
    location: place,
    provenance: { ...PROVENANCE },
    days: Array.from({ length: 7 }, (_, offset) => ({
      date: `2026-09-0${offset + 1}`,
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

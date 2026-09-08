import type { CurrentWeather, WeeklyForecast } from './types'

/**
 * Where the backend lives.
 *
 * Vite bakes env vars in at BUILD time, not at runtime — so a Docker image
 * built for one host cannot be repointed by restarting it with a different
 * variable. That is why the compose file passes this as a build argument.
 */
const API_BASE_URL = (
  import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:8080'
).replace(/\/+$/, '')

/** The city this application exists for. */
export const DEFAULT_CITY = 'San Salvador'

/**
 * What went wrong, as a category rather than a sentence.
 *
 * The API answers RFC 7807 problem documents whose `detail` is written in
 * English for whoever is debugging. Rendering that straight into a Spanish UI
 * would leak the server's language into the product, so the kind travels and
 * the interface picks its own words.
 */
export type WeatherErrorKind = 'notFound' | 'unavailable' | 'unreachable' | 'unexpected'

export class WeatherApiError extends Error {
  readonly kind: WeatherErrorKind

  readonly status: number | undefined

  constructor(kind: WeatherErrorKind, status?: number) {
    // English on purpose: this message is for a console and a stack trace,
    // never for a person using the product.
    super(`Weather API request failed (${kind}${status ? `, HTTP ${status}` : ''})`)
    this.name = 'WeatherApiError'
    this.kind = kind
    this.status = status
  }

  /** A missing city will still be missing on the next attempt; an outage may not be. */
  get retryable(): boolean {
    return this.kind !== 'notFound'
  }
}

function kindFor(status: number): WeatherErrorKind {
  if (status === 404) return 'notFound'
  if (status === 503) return 'unavailable'
  return 'unexpected'
}

async function get<T>(path: string, signal?: AbortSignal): Promise<T> {
  let response: Response

  try {
    response = await fetch(`${API_BASE_URL}${path}`, {
      signal,
      headers: { Accept: 'application/json' },
    })
  } catch (cause) {
    // An aborted request is the caller changing their mind, not a failure.
    if (cause instanceof DOMException && cause.name === 'AbortError') {
      throw cause
    }

    throw new WeatherApiError('unreachable')
  }

  if (!response.ok) {
    throw new WeatherApiError(kindFor(response.status), response.status)
  }

  return (await response.json()) as T
}

function locationQuery(city: string): string {
  return `?city=${encodeURIComponent(city)}`
}

export function fetchForecast(city: string, signal?: AbortSignal): Promise<WeeklyForecast> {
  return get<WeeklyForecast>(`/weather/forecast${locationQuery(city)}`, signal)
}

export function fetchCurrent(city: string, signal?: AbortSignal): Promise<CurrentWeather> {
  return get<CurrentWeather>(`/weather/current${locationQuery(city)}`, signal)
}

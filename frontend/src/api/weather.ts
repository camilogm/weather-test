import type {
  CurrentWeather,
  Forecast,
  LocationSearchResponse,
  LocationSuggestion,
  SelectedLocation,
} from './types'

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

/**
 * The city this application exists for, with its coordinates already known.
 *
 * Shipping the coordinates means the very first page load needs no geocoding
 * call at all — one less thing between opening the page and seeing a forecast,
 * and one less thing that can be down.
 */
export const DEFAULT_LOCATION: SelectedLocation = {
  name: 'San Salvador',
  latitude: 13.6929,
  longitude: -89.2182,
}

/** Below this the query matches too much to be worth asking the server about. */
export const MIN_QUERY_LENGTH = 2

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

/**
 * Coordinates decide the place; the name only decides what the response is
 * labelled with. Sending both removes the ambiguity a name alone carries.
 */
function locationQuery(location: SelectedLocation): string {
  return (
    `?city=${encodeURIComponent(location.name)}` +
    `&latitude=${location.latitude}` +
    `&longitude=${location.longitude}`
  )
}

export function fetchForecast(
  location: SelectedLocation,
  signal?: AbortSignal,
): Promise<Forecast> {
  return get<Forecast>(`/weather/forecast${locationQuery(location)}`, signal)
}

export function fetchCurrent(
  location: SelectedLocation,
  signal?: AbortSignal,
): Promise<CurrentWeather> {
  return get<CurrentWeather>(`/weather/current${locationQuery(location)}`, signal)
}

/** Candidate places for a partial name, straight from our own API. */
export async function searchLocations(
  query: string,
  signal?: AbortSignal,
): Promise<LocationSuggestion[]> {
  const body = await get<LocationSearchResponse>(
    `/locations?query=${encodeURIComponent(query)}&limit=8`,
    signal,
  )

  return body.results
}

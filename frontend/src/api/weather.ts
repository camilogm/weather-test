import type { CurrentWeather, ProblemDetails, WeeklyForecast } from './types'

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

/** A failure the UI can actually explain to a person. */
export class WeatherApiError extends Error {
  readonly status: number | undefined

  /** True when the backend says its upstream is down — a retry may well work. */
  readonly retryable: boolean

  constructor(message: string, status: number | undefined, retryable: boolean) {
    super(message)
    this.name = 'WeatherApiError'
    this.status = status
    this.retryable = retryable
  }
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

    throw new WeatherApiError(
      `Could not reach the weather service at ${API_BASE_URL}.`,
      undefined,
      true,
    )
  }

  if (!response.ok) {
    const problem = await readProblem(response)

    throw new WeatherApiError(
      problem?.detail ?? problem?.title ?? `The service answered ${response.status}.`,
      response.status,
      response.status === 503 || response.status >= 500,
    )
  }

  return (await response.json()) as T
}

async function readProblem(response: Response): Promise<ProblemDetails | null> {
  try {
    return (await response.json()) as ProblemDetails
  } catch {
    return null
  }
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

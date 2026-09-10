import { useMemo } from 'react'

import { useAsyncResource } from './useAsyncResource'
import { FORECAST_HORIZON_DAYS, WeatherApiError, fetchCurrent, fetchForecast } from '../api/weather'
import type { SelectedLocation } from '../api/types'

type Status = 'loading' | 'ready' | 'error'

/**
 * Loads the forecast and current conditions for a chosen place.
 *
 * The request bookkeeping — one in flight at a time, aborts on the way out, and
 * an answer proven to belong to the place currently selected — belongs to
 * useAsyncResource. What is left here is the part specific to weather: the
 * forecast is the page, current conditions are a bonus, so a failing forecast
 * is an error state while failing current conditions just leave that panel out.
 * Losing the extra should never blank out what people came for.
 */
export function useWeather(location: SelectedLocation) {
  // Keyed on the primitive fields rather than the object: a caller that builds
  // `{ name, latitude, longitude }` inline would hand us a new identity on
  // every render, and a resource keyed on that would never settle.
  const { name, latitude, longitude } = location
  const key = `${name}@${latitude},${longitude}`

  const resource = useAsyncResource(key, async (signal) => {
    const target: SelectedLocation = { name, latitude, longitude }

    // Two independent endpoints, so they go out together. Awaited in sequence
    // the page would wait for the sum of both round trips to show either.
    const [forecast, current] = await Promise.all([
      fetchForecast(target, FORECAST_HORIZON_DAYS, signal),
      // Best effort: the page still works without it.
      fetchCurrent(target, signal).catch(() => null),
    ])

    return { forecast, current }
  })

  // Memoised so a normalised error keeps one identity across renders rather
  // than becoming a new object every time something else re-renders.
  const error = useMemo(() => asWeatherError(resource.error), [resource.error])

  return {
    // There is always a place to load, so the resource is never idle.
    status: (resource.status === 'idle' ? 'loading' : resource.status) as Status,
    forecast: resource.data?.forecast ?? null,
    current: resource.data?.current ?? null,
    error,
    refresh: resource.revalidate,
    isRefreshing: resource.isRevalidating,
  }
}

function asWeatherError(cause: unknown): WeatherApiError | null {
  if (cause === null || cause === undefined) {
    return null
  }

  return cause instanceof WeatherApiError ? cause : new WeatherApiError('unexpected')
}

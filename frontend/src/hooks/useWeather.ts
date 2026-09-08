import { useCallback, useEffect, useState } from 'react'

import { WeatherApiError, fetchCurrent, fetchForecast } from '../api/weather'
import type { CurrentWeather, WeeklyForecast } from '../api/types'

type Status = 'loading' | 'ready' | 'error'

/** The outcome of one specific request, tagged with which request it answered. */
interface Settled {
  requestId: string
  forecast: WeeklyForecast | null
  current: CurrentWeather | null
  error: WeatherApiError | null
}

/**
 * Loads the forecast and current conditions for a city.
 *
 * `status` is DERIVED during render rather than written from inside the effect.
 * Setting "loading" in the effect would mean every city change renders twice —
 * once with the previous city's data still on screen, once after the state
 * lands. Comparing the settled result's request id against the current one
 * gives the same answer in a single pass, and makes a stale response
 * structurally impossible to display: it simply does not match.
 *
 * The forecast is the page; current conditions are a bonus. So a failing
 * forecast is an error state, while failing current conditions just leave that
 * panel out — losing the extra should never blank out what people came for.
 */
export function useWeather(city: string) {
  const [reloadToken, setReloadToken] = useState(0)
  const [settled, setSettled] = useState<Settled | null>(null)

  const requestId = `${city}#${reloadToken}`

  const refresh = useCallback(() => setReloadToken((token) => token + 1), [])

  useEffect(() => {
    const controller = new AbortController()
    let active = true

    async function load() {
      try {
        const forecast = await fetchForecast(city, controller.signal)

        // Best effort: the page still works without it.
        const current = await fetchCurrent(city, controller.signal).catch(() => null)

        if (active) {
          setSettled({ requestId, forecast, current, error: null })
        }
      } catch (cause) {
        if (!active || (cause instanceof DOMException && cause.name === 'AbortError')) {
          return
        }

        setSettled({
          requestId,
          forecast: null,
          current: null,
          error: cause instanceof WeatherApiError ? cause : new WeatherApiError('unexpected'),
        })
      }
    }

    void load()

    return () => {
      active = false
      controller.abort()
    }
  }, [city, requestId])

  const isCurrent = settled?.requestId === requestId

  const status: Status = !isCurrent || !settled ? 'loading' : settled.error ? 'error' : 'ready'

  return {
    status,
    forecast: isCurrent ? (settled?.forecast ?? null) : null,
    current: isCurrent ? (settled?.current ?? null) : null,
    error: isCurrent ? (settled?.error ?? null) : null,
    refresh,
  }
}

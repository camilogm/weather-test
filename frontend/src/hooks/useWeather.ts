import { useCallback, useEffect, useState } from 'react'

import { WeatherApiError, fetchCurrent, fetchForecast } from '../api/weather'
import type { CurrentWeather, WeeklyForecast } from '../api/types'

type Status = 'loading' | 'ready' | 'error'

interface WeatherState {
  status: Status
  forecast: WeeklyForecast | null
  current: CurrentWeather | null
  error: WeatherApiError | null
}

const initialState: WeatherState = {
  status: 'loading',
  forecast: null,
  current: null,
  error: null,
}

/**
 * Loads the forecast and current conditions for a city.
 *
 * The forecast is the page; current conditions are a bonus. So a failing
 * forecast is an error state, while failing current conditions just leave that
 * panel out — losing the extra should never blank out the thing people came for.
 */
export function useWeather(city: string) {
  const [state, setState] = useState<WeatherState>(initialState)
  const [reloadToken, setReloadToken] = useState(0)

  const refresh = useCallback(() => setReloadToken((token) => token + 1), [])

  useEffect(() => {
    const controller = new AbortController()
    let active = true

    setState((previous) => ({ ...previous, status: 'loading', error: null }))

    async function load() {
      try {
        const forecast = await fetchForecast(city, controller.signal)

        // Best effort: the page still works without it.
        const current = await fetchCurrent(city, controller.signal).catch(() => null)

        if (active) {
          setState({ status: 'ready', forecast, current, error: null })
        }
      } catch (cause) {
        if (!active || (cause instanceof DOMException && cause.name === 'AbortError')) {
          return
        }

        setState({
          status: 'error',
          forecast: null,
          current: null,
          error:
            cause instanceof WeatherApiError
              ? cause
              : new WeatherApiError('Something went wrong loading the forecast.', undefined, true),
        })
      }
    }

    void load()

    return () => {
      active = false
      controller.abort()
    }
  }, [city, reloadToken])

  return { ...state, refresh }
}

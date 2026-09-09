import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { aCurrentFor, aForecastFor, aPlace, deferred } from '../test/fixtures'
import type { WeeklyForecast } from '../api/types'

// Partial mock: WeatherApiError has to stay the real class, because the hook
// narrows on `instanceof` and a stubbed one would never match.
vi.mock('../api/weather', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/weather')>()),
  fetchForecast: vi.fn(),
  fetchCurrent: vi.fn(),
}))

const { WeatherApiError, fetchCurrent, fetchForecast } = await import('../api/weather')
const { useWeather } = await import('./useWeather')

const forecastOf = vi.mocked(fetchForecast)
const currentOf = vi.mocked(fetchCurrent)

const sanSalvador = aPlace('San Salvador')
const guatemala = aPlace('Guatemala City', 14.63, -90.51)

beforeEach(() => {
  vi.clearAllMocks()
  currentOf.mockImplementation((place) => Promise.resolve(aCurrentFor(place)))
})

describe('useWeather', () => {
  it('serves the forecast once it arrives', async () => {
    forecastOf.mockResolvedValue(aForecastFor(sanSalvador))

    const { result } = renderHook(() => useWeather(sanSalvador))

    expect(result.current.status).toBe('loading')

    await waitFor(() => expect(result.current.status).toBe('ready'))
    expect(result.current.forecast?.location.name).toBe('San Salvador')
    expect(result.current.current).not.toBeNull()
  })

  it('never shows the previous location while the new one is still loading', async () => {
    // The guarantee is structural, not cosmetic: a settled result is tagged
    // with the request it answered, so one belonging to a place nobody asked
    // for any more cannot be rendered at all.
    forecastOf.mockResolvedValue(aForecastFor(sanSalvador))

    const { result, rerender } = renderHook((place) => useWeather(place), {
      initialProps: sanSalvador,
    })
    await waitFor(() => expect(result.current.status).toBe('ready'))

    const pending = deferred<WeeklyForecast>()
    forecastOf.mockReturnValue(pending.promise)
    rerender(guatemala)

    expect(result.current.status).toBe('loading')
    expect(result.current.forecast).toBeNull()
  })

  it('discards a slow answer for a place that was already left behind', async () => {
    const slow = deferred<WeeklyForecast>()
    const fast = deferred<WeeklyForecast>()

    forecastOf.mockReturnValueOnce(slow.promise).mockReturnValueOnce(fast.promise)

    const { result, rerender } = renderHook((place) => useWeather(place), {
      initialProps: sanSalvador,
    })
    rerender(guatemala)

    // Guatemala answers first, then San Salvador finally comes back. Out of
    // order on purpose — this is the race a fast typist on a slow link creates.
    await act(async () => {
      fast.resolve(aForecastFor(guatemala))
      slow.resolve(aForecastFor(sanSalvador))
    })

    await waitFor(() => expect(result.current.status).toBe('ready'))
    expect(result.current.forecast?.location.name).toBe('Guatemala City')
  })

  it('still renders the page when only current conditions fail', async () => {
    // The forecast is the page; current conditions are a bonus. Losing the
    // bonus must not blank out what people came for.
    forecastOf.mockResolvedValue(aForecastFor(sanSalvador))
    currentOf.mockRejectedValue(new WeatherApiError('unavailable', 503))

    const { result } = renderHook(() => useWeather(sanSalvador))

    await waitFor(() => expect(result.current.status).toBe('ready'))
    expect(result.current.forecast).not.toBeNull()
    expect(result.current.current).toBeNull()
  })

  it('reports a failed forecast as an error, keeping the kind intact', async () => {
    forecastOf.mockRejectedValue(new WeatherApiError('notFound', 404))

    const { result } = renderHook(() => useWeather(sanSalvador))

    await waitFor(() => expect(result.current.status).toBe('error'))
    expect(result.current.error?.kind).toBe('notFound')
  })

  it('aborts the request in flight when it unmounts', async () => {
    const pending = deferred<WeeklyForecast>()
    forecastOf.mockReturnValue(pending.promise)

    const { unmount } = renderHook(() => useWeather(sanSalvador))
    const signal = forecastOf.mock.calls[0]?.[1]

    expect(signal?.aborted).toBe(false)
    unmount()
    expect(signal?.aborted).toBe(true)
  })

  it('goes back to the network when asked to refresh', async () => {
    forecastOf.mockResolvedValue(aForecastFor(sanSalvador))

    const { result } = renderHook(() => useWeather(sanSalvador))
    await waitFor(() => expect(result.current.status).toBe('ready'))
    expect(forecastOf).toHaveBeenCalledTimes(1)

    await act(async () => result.current.refresh())

    await waitFor(() => expect(forecastOf).toHaveBeenCalledTimes(2))
  })
})

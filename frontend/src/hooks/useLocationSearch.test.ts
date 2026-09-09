import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { aSuggestion, deferred } from '../test/fixtures'
import type { LocationSuggestion } from '../api/types'

vi.mock('../api/weather', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/weather')>()),
  searchLocations: vi.fn(),
}))

const { searchLocations } = await import('../api/weather')
const { useLocationSearch } = await import('./useLocationSearch')

const search = vi.mocked(searchLocations)

/** Longer than the hook's debounce, so a settled timer is unambiguous. */
const PAST_THE_DEBOUNCE = 300

async function letTheDebounceElapse() {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(PAST_THE_DEBOUNCE)
  })
}

beforeEach(() => {
  vi.clearAllMocks()
  vi.useFakeTimers()
  search.mockResolvedValue([aSuggestion('San Salvador')])
})

afterEach(() => {
  vi.useRealTimers()
})

describe('useLocationSearch', () => {
  it('stays idle and asks for nothing while the term is too short', async () => {
    const { result } = renderHook(() => useLocationSearch('S'))

    await letTheDebounceElapse()

    expect(result.current.status).toBe('idle')
    expect(search).not.toHaveBeenCalled()
  })

  it('collapses a burst of keystrokes into one request for the final term', async () => {
    // Firing per keystroke would spend eight round trips to answer a
    // five-letter word, and every one counts toward the circuit breaker that
    // guards the geocoder.
    const { rerender } = renderHook((term) => useLocationSearch(term), {
      initialProps: 'Sa',
    })

    rerender('San')
    rerender('San ')
    rerender('San Sal')

    await letTheDebounceElapse()

    expect(search).toHaveBeenCalledTimes(1)
    expect(search).toHaveBeenCalledWith('San Sal', expect.anything())
  })

  it('reports results for the term that is actually in the box', async () => {
    const { result } = renderHook(() => useLocationSearch('San Sal'))

    await letTheDebounceElapse()

    expect(result.current.status).toBe('ready')
    expect(result.current.results).toHaveLength(1)
  })

  it('goes back to searching the moment the term moves on', async () => {
    // The subtle one. Status is derived by comparing the settled term against
    // the LIVE one, so the instant someone types another letter the previous
    // term's results stop being an answer — even though they are still the
    // newest thing that arrived.
    const { result, rerender } = renderHook((term) => useLocationSearch(term), {
      initialProps: 'San',
    })
    await letTheDebounceElapse()
    expect(result.current.status).toBe('ready')

    rerender('San S')

    expect(result.current.status).toBe('searching')
    expect(result.current.results).toHaveLength(0)
  })

  it('discards a slow answer for a term that was already typed past', async () => {
    const slow = deferred<LocationSuggestion[]>()
    const fast = deferred<LocationSuggestion[]>()
    search.mockReturnValueOnce(slow.promise).mockReturnValueOnce(fast.promise)

    const { result, rerender } = renderHook((term) => useLocationSearch(term), {
      initialProps: 'San',
    })
    await letTheDebounceElapse()

    rerender('San Salv')
    await letTheDebounceElapse()

    await act(async () => {
      fast.resolve([aSuggestion('San Salvador')])
      slow.resolve([aSuggestion('Santiago'), aSuggestion('Santa Ana')])
    })

    expect(result.current.status).toBe('ready')
    expect(result.current.results).toHaveLength(1)
    expect(result.current.results[0]?.name).toBe('San Salvador')
  })

  it('surfaces a failed search as an error rather than an empty result', async () => {
    // "We could not search" and "there is nothing named that" are different
    // answers, and a picker that confuses them lies to the person typing.
    search.mockRejectedValue(new Error('network is down'))

    const { result } = renderHook(() => useLocationSearch('San'))
    await letTheDebounceElapse()

    expect(result.current.status).toBe('error')
    expect(result.current.results).toHaveLength(0)
  })

  it('aborts the request in flight when it unmounts', async () => {
    const pending = deferred<LocationSuggestion[]>()
    search.mockReturnValue(pending.promise)

    const { unmount } = renderHook(() => useLocationSearch('San'))
    await letTheDebounceElapse()

    const signal = search.mock.calls[0]?.[1]
    expect(signal?.aborted).toBe(false)
    unmount()
    expect(signal?.aborted).toBe(true)
  })
})

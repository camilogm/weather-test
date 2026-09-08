import { useEffect, useState } from 'react'

import { MIN_QUERY_LENGTH, searchLocations } from '../api/weather'
import type { LocationSuggestion } from '../api/types'

export type SearchStatus = 'idle' | 'searching' | 'ready' | 'error'

/** How long the typing has to pause before a request goes out. */
const DEBOUNCE_MS = 250

/** Shared so "no results" keeps a stable identity across renders. */
const NONE: LocationSuggestion[] = []

interface Settled {
  term: string
  results: LocationSuggestion[]
  failed: boolean
}

/**
 * Suggests places for whatever is being typed.
 *
 * Debounced, because firing a request per keystroke would spend eight round
 * trips to answer a five-letter word — and every one of those counts toward the
 * circuit breaker guarding the geocoder.
 *
 * Status is derived from whether the settled result matches the current term,
 * so a slow response for "Sa" can never overwrite a fast one for "San Salv":
 * it simply does not match, and is discarded.
 */
export function useLocationSearch(term: string) {
  const [settled, setSettled] = useState<Settled | null>(null)

  const trimmed = term.trim()
  const tooShort = trimmed.length < MIN_QUERY_LENGTH

  useEffect(() => {
    if (tooShort) {
      return
    }

    const controller = new AbortController()
    let active = true

    const timer = setTimeout(async () => {
      try {
        const results = await searchLocations(trimmed, controller.signal)
        if (active) {
          setSettled({ term: trimmed, results, failed: false })
        }
      } catch (cause) {
        if (!active || (cause instanceof DOMException && cause.name === 'AbortError')) {
          return
        }

        setSettled({ term: trimmed, results: [], failed: true })
      }
    }, DEBOUNCE_MS)

    return () => {
      active = false
      clearTimeout(timer)
      controller.abort()
    }
  }, [trimmed, tooShort])

  const isCurrent = settled?.term === trimmed

  const status: SearchStatus = tooShort
    ? 'idle'
    : !isCurrent || !settled
      ? 'searching'
      : settled.failed
        ? 'error'
        : 'ready'

  return {
    status,
    results: status === 'ready' && settled ? settled.results : NONE,
  }
}

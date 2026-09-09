import { useAsyncResource } from './useAsyncResource'
import { MIN_QUERY_LENGTH, searchLocations } from '../api/weather'
import type { LocationSuggestion } from '../api/types'

export type SearchStatus = 'idle' | 'searching' | 'ready' | 'error'

/** How long the typing has to pause before a request goes out. */
const DEBOUNCE_MS = 250

/** Shared so "no results" keeps a stable identity across renders. */
const NONE: LocationSuggestion[] = []

/**
 * Suggests places for whatever is being typed.
 *
 * Debounced, because firing a request per keystroke would spend eight round
 * trips to answer a five-letter word — and every one of those counts toward the
 * circuit breaker guarding the geocoder.
 *
 * A term shorter than the minimum is a null key, which is how the resource is
 * told there is nothing to ask for yet.
 */
export function useLocationSearch(term: string) {
  const trimmed = term.trim()
  const key = trimmed.length < MIN_QUERY_LENGTH ? null : trimmed

  const { status, data } = useAsyncResource(
    key,
    (signal) => searchLocations(trimmed, signal),
    { debounceMs: DEBOUNCE_MS },
  )

  return {
    // "Searching" rather than "loading": this hook speaks the picker's
    // vocabulary, not the generic resource's.
    status: (status === 'loading' ? 'searching' : status) as SearchStatus,
    results: status === 'ready' ? (data ?? NONE) : NONE,
  }
}

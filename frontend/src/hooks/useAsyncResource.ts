import { useCallback, useEffect, useEffectEvent, useState } from 'react'

export type ResourceStatus = 'idle' | 'loading' | 'ready' | 'error'

/** The outcome of one specific request, tagged with which request it answered. */
interface Settled<T> {
  key: string
  nonce: number
  data: T | null
  error: unknown
}

interface Options {
  /** Wait this long after the key last changed before going to the network. */
  debounceMs?: number
}

const isAbort = (cause: unknown) => cause instanceof DOMException && cause.name === 'AbortError'

/**
 * One request at a time for a keyed resource, with the answer that arrives
 * proven to belong to the question currently being asked.
 *
 * `status` is DERIVED during render rather than written from inside the effect.
 * Setting "loading" in the effect would mean every change renders twice — once
 * with the previous key's data still on screen, once after the state lands.
 * Comparing the settled result's key against the current one gives the same
 * answer in a single pass, and makes a stale response structurally impossible
 * to display: it simply does not match.
 *
 * Two details in here are load-bearing, and both are easy to lose:
 *
 * The debounce lives INSIDE, delaying the request while the key stays live. The
 * obvious alternative — debounce the value, pass the delayed one as the key —
 * breaks the guarantee above, because the settled result would match the key
 * for as long as the delay lasts and the previous answer would read as current.
 *
 * The revalidation counter is deliberately NOT part of the key. Fold it in and
 * asking for fresh data invalidates what is already on screen, so a refresh
 * blanks the page to load the very thing it is replacing.
 */
export function useAsyncResource<T>(
  key: string | null,
  load: (signal: AbortSignal) => Promise<T>,
  { debounceMs = 0 }: Options = {},
) {
  const [settled, setSettled] = useState<Settled<T> | null>(null)
  const [nonce, setNonce] = useState(0)

  // Non-reactive: the loader closes over whatever the caller has this render,
  // but changing identity must not re-fire the request. Only the key does that.
  const run = useEffectEvent(load)

  useEffect(() => {
    if (key === null) {
      return
    }

    const controller = new AbortController()
    let active = true

    const fire = async () => {
      try {
        const data = await run(controller.signal)

        if (active) {
          setSettled({ key, nonce, data, error: null })
        }
      } catch (cause) {
        if (!active || isAbort(cause)) {
          return
        }

        setSettled({ key, nonce, data: null, error: cause })
      }
    }

    const timer = debounceMs > 0 ? setTimeout(() => void fire(), debounceMs) : undefined

    if (timer === undefined) {
      void fire()
    }

    return () => {
      active = false
      clearTimeout(timer)
      controller.abort()
    }
  }, [key, nonce, debounceMs])

  const isCurrent = settled !== null && settled.key === key

  const status: ResourceStatus =
    key === null ? 'idle' : !isCurrent ? 'loading' : settled.error ? 'error' : 'ready'

  return {
    status,
    data: isCurrent ? settled.data : null,
    error: isCurrent ? settled.error : null,
    /** Fresh data is on its way, but what is on screen is still worth showing. */
    isRevalidating: isCurrent && settled.nonce !== nonce,
    revalidate: useCallback(() => setNonce((token) => token + 1), []),
  }
}

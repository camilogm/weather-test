import { type RefObject, useCallback, useEffect, useRef, useState } from 'react'

/**
 * Sub-pixel layout means scrollLeft, clientWidth and scrollWidth never land
 * exactly on each other. Without a pixel of slack a track sitting a third of a
 * pixel short of its end would keep offering a forward step that moves nothing,
 * and an element one rounding error wider than its content would grow arrows it
 * has no use for.
 */
const SLACK = 1

interface Carousel<T extends HTMLElement> {
  /** Attach to the scrolling element itself, not to a wrapper around it. */
  ref: RefObject<T | null>
  /** False when the whole track already fits; the controls have nothing to do. */
  overflows: boolean
  canScrollBack: boolean
  canScrollForward: boolean
  back: () => void
  forward: () => void
}

interface Reach {
  overflows: boolean
  canScrollBack: boolean
  canScrollForward: boolean
}

const NOTHING_TO_SCROLL: Reach = {
  overflows: false,
  canScrollBack: false,
  canScrollForward: false,
}

/**
 * Pointer controls for a natively scrolling track.
 *
 * The scrolling itself is the browser's, which is the whole point. A carousel
 * built out of transforms and an index has to re-implement touch momentum,
 * trackpad gestures, keyboard paging and the focus ring following a card into
 * view — and gets at least one of them wrong. Real overflow gets all of that for
 * nothing, and this hook only adds what native scrolling has no opinion about:
 * whether either direction is worth offering, and what one step means.
 *
 * `itemCount` is not used for arithmetic. It is the signal that the content
 * changed — a wider range, a different city — so the measurements are taken
 * again rather than describing a track that is no longer there.
 */
export function useCarousel<T extends HTMLElement>(itemCount: number): Carousel<T> {
  const ref = useRef<T>(null)
  const [reach, setReach] = useState<Reach>(NOTHING_TO_SCROLL)

  const measure = useCallback(() => {
    const track = ref.current

    if (!track) {
      return
    }

    const { scrollLeft, clientWidth, scrollWidth } = track
    const hidden = scrollWidth - clientWidth

    setReach({
      overflows: hidden > SLACK,
      canScrollBack: scrollLeft > SLACK,
      canScrollForward: scrollLeft < hidden - SLACK,
    })
  }, [])

  useEffect(() => {
    const track = ref.current

    if (!track) {
      return
    }

    measure()

    // Passive: this listener only reads, so the browser is free to keep
    // scrolling at sixty frames a second without waiting to see whether it
    // cancels the event.
    track.addEventListener('scroll', measure, { passive: true })
    window.addEventListener('resize', measure)

    return () => {
      track.removeEventListener('scroll', measure)
      window.removeEventListener('resize', measure)
    }
  }, [measure, itemCount])

  /**
   * One step is one visible page, whatever that happens to be. Stepping by a
   * fixed number of cards would mean the hook guessing at a width the
   * stylesheet owns, and guessing differently at every breakpoint.
   *
   * No `behavior` is named on purpose. Left unset it resolves to the element's
   * computed scroll-behavior, so a `motion-safe:` rule in the stylesheet is what
   * decides — and a person who asked their system to stop moving things gets a
   * jump instead of a glide, without this file knowing anything about it.
   */
  const step = useCallback((direction: 1 | -1) => {
    const track = ref.current

    if (track) {
      track.scrollBy({ left: direction * track.clientWidth })
    }
  }, [])

  return {
    ref,
    ...reach,
    back: useCallback(() => step(-1), [step]),
    forward: useCallback(() => step(1), [step]),
  }
}

import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useCarousel } from './useCarousel'

/** jsdom implements no scrolling, so the one method the hook calls is supplied. */
const scrollBy = vi.fn()
Element.prototype.scrollBy = scrollBy

beforeEach(() => vi.clearAllMocks())

/**
 * A track whose measurements the test decides.
 *
 * jsdom reports zero for every layout property, which would leave the hook
 * permanently believing nothing overflows. Stubbing the three values it reads
 * is what makes the arithmetic — and only the arithmetic — observable.
 */
function measured(element: HTMLElement, scrollLeft: number, clientWidth: number, scrollWidth: number) {
  Object.defineProperty(element, 'clientWidth', { value: clientWidth, configurable: true })
  Object.defineProperty(element, 'scrollWidth', { value: scrollWidth, configurable: true })
  Object.defineProperty(element, 'scrollLeft', { value: scrollLeft, writable: true, configurable: true })
}

function Track({ items = 3 }: { items?: number }) {
  const { ref, overflows, canScrollBack, canScrollForward, back, forward } =
    useCarousel<HTMLDivElement>(items)

  return (
    <div>
      <button type="button" onClick={back} disabled={!canScrollBack}>
        back
      </button>
      <div data-testid="track" ref={ref}>
        {overflows ? 'overflows' : 'fits'}
      </div>
      <button type="button" onClick={forward} disabled={!canScrollForward}>
        forward
      </button>
    </div>
  )
}

/** Renders, applies the layout the test wants, and lets the hook re-measure. */
function renderTrack(scrollLeft: number, clientWidth: number, scrollWidth: number, items = 3) {
  const view = render(<Track items={items} />)
  measured(screen.getByTestId('track'), scrollLeft, clientWidth, scrollWidth)
  fireEvent.scroll(screen.getByTestId('track'))
  return view
}

const back = () => screen.getByRole('button', { name: 'back' })
const forward = () => screen.getByRole('button', { name: 'forward' })

describe('useCarousel', () => {
  it('reports no overflow when the whole track already fits', () => {
    renderTrack(0, 400, 400)

    expect(screen.getByTestId('track').textContent).toBe('fits')
    expect(back()).toHaveProperty('disabled', true)
    expect(forward()).toHaveProperty('disabled', true)
  })

  it('offers only the forward direction at the start of an overflowing track', () => {
    renderTrack(0, 400, 1200)

    expect(screen.getByTestId('track').textContent).toBe('overflows')
    expect(back()).toHaveProperty('disabled', true)
    expect(forward()).toHaveProperty('disabled', false)
  })

  it('offers both directions once the track has been scrolled into', () => {
    renderTrack(400, 400, 1200)

    expect(back()).toHaveProperty('disabled', false)
    expect(forward()).toHaveProperty('disabled', false)
  })

  it('stops offering forward once the end is reached', () => {
    renderTrack(800, 400, 1200)

    expect(back()).toHaveProperty('disabled', false)
    expect(forward()).toHaveProperty('disabled', true)
  })

  /**
   * Sub-pixel layout means these values never land exactly on each other. A
   * track one third of a pixel short of its end would otherwise keep offering a
   * forward step that moves nothing.
   */
  it('treats a sub-pixel remainder as the end of the track', () => {
    renderTrack(799.6, 400, 1200)

    expect(forward()).toHaveProperty('disabled', true)
  })

  it('advances by one visible page rather than by a guessed number of cards', async () => {
    renderTrack(0, 400, 1200)

    await userEvent.click(forward())

    expect(scrollBy).toHaveBeenCalledWith({ left: 400 })
  })

  it('steps back by the same page', async () => {
    renderTrack(400, 400, 1200)

    await userEvent.click(back())

    expect(scrollBy).toHaveBeenCalledWith({ left: -400 })
  })

  /**
   * Leaves the behaviour to CSS on purpose: passing behavior:'smooth' here
   * would animate the jump for people who have asked their system to stop
   * moving things, which a motion-safe stylesheet rule cannot then undo.
   */
  it('never names a scroll behaviour of its own', async () => {
    renderTrack(0, 400, 1200)

    await userEvent.click(forward())

    expect(scrollBy.mock.calls[0]?.[0]).not.toHaveProperty('behavior')
  })

  it('measures again when the number of items changes', () => {
    const { rerender } = renderTrack(0, 400, 400)
    expect(forward()).toHaveProperty('disabled', true)

    // The range grew: same element, more content in it.
    measured(screen.getByTestId('track'), 0, 400, 1600)
    rerender(<Track items={16} />)

    expect(forward()).toHaveProperty('disabled', false)
  })

  it('measures again when the viewport changes size', () => {
    renderTrack(0, 400, 1200)
    expect(forward()).toHaveProperty('disabled', false)

    measured(screen.getByTestId('track'), 0, 1200, 1200)
    fireEvent(window, new Event('resize'))

    expect(forward()).toHaveProperty('disabled', true)
  })
})

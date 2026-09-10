import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { aForecastFor, aPlace } from '../test/fixtures'
import { ForecastCarousel } from './ForecastCarousel'

/** jsdom implements no scrolling, so the method the carousel calls is supplied. */
const scrollBy = vi.fn()
Element.prototype.scrollBy = scrollBy

beforeEach(() => vi.clearAllMocks())

const sanSalvador = aPlace('San Salvador')

function renderCarousel(length?: number) {
  const forecast = length === undefined ? aForecastFor(sanSalvador) : aForecastFor(sanSalvador, { length })
  return render(<ForecastCarousel days={forecast.days} />)
}

const cards = () => screen.getAllByRole('listitem')
const rangeOption = (days: number) => screen.getByRole('radio', { name: `${days} días` })
const track = () => screen.getByRole('list')

/** jsdom measures everything as zero, so overflow has to be described to it. */
function overflowing(element: HTMLElement) {
  Object.defineProperty(element, 'clientWidth', { value: 400, configurable: true })
  Object.defineProperty(element, 'scrollWidth', { value: 2000, configurable: true })
  Object.defineProperty(element, 'scrollLeft', { value: 0, writable: true, configurable: true })
  fireEvent.scroll(element)
}

describe('ForecastCarousel', () => {
  it('opens on a week, which is what the endpoint answers by default', () => {
    renderCarousel()

    expect(cards()).toHaveLength(7)
    expect(screen.getByRole('heading', { level: 2 }).textContent).toBe('Próximos 7 días')
  })

  it('extends to the whole horizon when a longer range is chosen', async () => {
    renderCarousel()

    await userEvent.click(rangeOption(16))

    expect(cards()).toHaveLength(16)
    expect(screen.getByRole('heading', { level: 2 }).textContent).toBe('Próximos 16 días')
  })

  it('narrows again without ever going back to the network', async () => {
    // Nothing here fetches. The range is a slice of days the page already has,
    // which is the reason the control can feel instant at all.
    renderCarousel()

    await userEvent.click(rangeOption(16))
    await userEvent.click(rangeOption(7))

    expect(cards()).toHaveLength(7)
  })

  it('marks the first day as today', () => {
    renderCarousel()

    expect(cards()[0]?.textContent).toContain('Hoy')
  })

  /**
   * A degraded answer can carry fewer days than the horizon. Offering a range
   * the data cannot fill would put a control on screen that quietly does
   * nothing, and a heading that disagrees with the cards under it.
   */
  it('offers only the ranges the data can actually fill', () => {
    renderCarousel(10)

    expect(rangeOption(7)).toBeDefined()
    expect(rangeOption(10)).toBeDefined()
    expect(screen.queryByRole('radio', { name: '16 días' })).toBeNull()
  })

  it('drops the range control entirely when there is only one range left', () => {
    renderCarousel(5)

    expect(screen.queryAllByRole('radio')).toHaveLength(0)
    expect(cards()).toHaveLength(5)
  })

  it('exposes the range as one exclusive choice rather than a row of toggles', () => {
    renderCarousel()

    expect(rangeOption(7).getAttribute('type')).toBe('radio')
    expect(rangeOption(7)).toHaveProperty('checked', true)
    expect(rangeOption(16)).toHaveProperty('checked', false)
  })

  /**
   * The track scrolls, so it has to be reachable and named for anyone who is
   * not using a pointer — the arrows are a convenience, never the only way
   * through.
   */
  it('makes the scrolling track a named tab stop', () => {
    renderCarousel()

    expect(track().getAttribute('tabindex')).toBe('0')
    expect(track().getAttribute('aria-label')).not.toBeNull()
  })

  it('keeps the arrows out of the way while the whole range fits', () => {
    renderCarousel()

    expect(screen.queryByRole('button', { name: 'Días siguientes' })).toBeNull()
  })

  it('offers the arrows once the track overflows, and steps a page at a time', async () => {
    renderCarousel()
    overflowing(track())

    const next = screen.getByRole('button', { name: 'Días siguientes' })
    expect(screen.getByRole('button', { name: 'Días anteriores' })).toHaveProperty('disabled', true)

    await userEvent.click(next)

    expect(scrollBy).toHaveBeenCalledWith({ left: 400 })
  })
})

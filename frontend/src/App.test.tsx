import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import App from './App'
import type { Forecast, LocationSuggestion, SelectedLocation } from './api/types'
import { aCurrentFor, aForecastFor, deferred } from './test/fixtures'

/*
  The network is mocked at the module boundary rather than at `fetch`, because
  what these tests are about is the journey — which longitude the page compares
  against when it picks a direction. Going through URL building and response
  parsing to get there would only add ways for the test to fail for reasons that
  have nothing to do with the thing under test.
*/
vi.mock('./api/weather', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./api/weather')>()

  return {
    ...actual,
    fetchForecast: vi.fn(),
    fetchCurrent: vi.fn(),
    searchLocations: vi.fn(),
  }
})

const { fetchCurrent, fetchForecast, searchLocations } = await import('./api/weather')

/** Longitudes far enough apart that the heading between them is unambiguous. */
const MADRID = { name: 'Madrid', latitude: 40.42, longitude: -3.7 }
const LIMA = { name: 'Lima', latitude: -12.05, longitude: -77.04 }

function suggest(place: SelectedLocation): LocationSuggestion {
  return { ...place, region: '', country: '', countryCode: 'XX' }
}

/** jsdom implements no layout, so scrollIntoView has to be supplied. */
Element.prototype.scrollIntoView = vi.fn()

beforeEach(() => {
  vi.mocked(fetchForecast).mockImplementation((place) => Promise.resolve(aForecastFor(place)))
  vi.mocked(fetchCurrent).mockImplementation((place) => Promise.resolve(aCurrentFor(place)))
  vi.mocked(searchLocations).mockResolvedValue([])
})

/** Types into the city field and picks the one suggestion offered back. */
async function travelTo(place: SelectedLocation) {
  vi.mocked(searchLocations).mockResolvedValue([suggest(place)])

  const user = userEvent.setup()
  await user.type(screen.getByRole('combobox'), place.name)

  const option = await screen.findByRole('option', { name: new RegExp(place.name) })
  await user.click(option)
}

/** The element the arrival animation is applied to. */
const arrival = () => document.querySelector('[data-heading]')

// Read as a plain attribute rather than through jest-dom: this suite has no
// custom matchers and is not about to grow a dependency for one assertion.
const headingNow = () => arrival()?.getAttribute('data-heading')

describe('App arrival heading', () => {
  it('has no heading on the first load, when nothing was replaced', async () => {
    render(<App />)

    await waitFor(() => expect(headingNow()).toBe('none'))
  })

  it('heads east when the chosen city lies east of the one on screen', async () => {
    render(<App />)
    await screen.findByText('San Salvador')

    await travelTo(MADRID) // -3.70, east of San Salvador

    await waitFor(() => expect(headingNow()).toBe('east'))
  })

  /*
    The case that catches a stale closure. The second move must be measured from
    MADRID — the place actually on screen — and not from the default the page
    started on. Comparing Lima against San Salvador would answer "east"; against
    Madrid it is "west", and only one of those is what a person just did.
  */
  it('measures the next move from the city on screen, not from the one it started on', async () => {
    render(<App />)
    await screen.findByText('San Salvador')

    await travelTo(MADRID)
    await waitFor(() => expect(headingNow()).toBe('east'))

    await travelTo(LIMA)
    await waitFor(() => expect(headingNow()).toBe('west'))
  })

  /*
    A refresh is the same place with fresher numbers on the way, so nothing
    arrives from anywhere. If this ever fails, someone has folded the
    revalidation flag into the key and the whole page now lurches sideways every
    time somebody asks for an update.

    The refresh is held OPEN rather than resolved straight away, and that is the
    whole point of the test. Answered immediately, the request and its response
    land in one commit — the in-flight render never gets painted, so a key that
    does carry the revalidation flag would still look identical from out here
    and the test would pass against the very bug it is meant to catch.
  */
  it('does not re-run the arrival while a refresh revalidates the same place', async () => {
    render(<App />)
    await waitFor(() => expect(headingNow()).toBe('none'))

    const before = arrival()
    const refresh = screen.getByRole('button', { name: /actualizar/i })

    const pending = deferred<Forecast>()
    vi.mocked(fetchForecast).mockReturnValueOnce(pending.promise)

    const user = userEvent.setup()
    await user.click(refresh)

    // aria-busy is the page saying, in its own markup, that it is revalidating.
    await waitFor(() => expect(refresh.getAttribute('aria-busy')).toBe('true'))

    // Same element, not a replacement: React never unmounted it, so no CSS
    // animation was restarted and nothing moved under the reader.
    expect(arrival()).toBe(before)

    pending.resolve(aForecastFor(MADRID))

    await waitFor(() => expect(refresh.getAttribute('aria-busy')).toBe('false'))
    expect(arrival()).toBe(before)
  })
})

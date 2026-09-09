import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi, type Mock } from 'vitest'

import { aSuggestion } from '../test/fixtures'
import type { SelectedLocation } from '../api/types'

vi.mock('../api/weather', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/weather')>()),
  searchLocations: vi.fn(),
}))

const { searchLocations } = await import('../api/weather')
const { CityPicker } = await import('./CityPicker')

const search = vi.mocked(searchLocations)

const selected = { name: 'San Salvador', latitude: 13.69, longitude: -89.22 }

const CITIES = [
  { ...aSuggestion('San Salvador'), latitude: 13.69, longitude: -89.22 },
  { ...aSuggestion('San Salvador de Jujuy'), latitude: -24.19, longitude: -65.3 },
  { ...aSuggestion('San Miguel'), latitude: 13.48, longitude: -88.18 },
]

let onSelect: Mock<(location: SelectedLocation) => void>

/** jsdom implements no layout, so scrollIntoView has to be supplied. */
const scrollIntoView = vi.fn()
Element.prototype.scrollIntoView = scrollIntoView

beforeEach(() => {
  vi.clearAllMocks()
  onSelect = vi.fn<(location: SelectedLocation) => void>()
  search.mockResolvedValue(CITIES)
})

/**
 * Real timers, deliberately. The hook debounces for 250ms and userEvent runs
 * its own clock; driving both from fake timers deadlocks on the first
 * keystroke, and waiting a quarter of a second is cheaper than the workaround.
 */
function aUser() {
  return userEvent.setup()
}

async function searchFor(user: ReturnType<typeof aUser>, term: string) {
  await user.type(screen.getByRole('combobox'), term)
  await screen.findAllByRole('option')
}

function renderPicker() {
  return render(
    <>
      <CityPicker selected={selected} onSelect={onSelect} />
      <button type="button">somewhere else</button>
    </>,
  )
}

const activeOptionOf = (input: HTMLElement) => input.getAttribute('aria-activedescendant')

describe('CityPicker', () => {
  it('offers the search results as options', async () => {
    const user = aUser()
    renderPicker()

    await searchFor(user, 'San')

    expect(screen.getByRole('listbox')).toBeDefined()
    expect(screen.getAllByRole('option')).toHaveLength(3)
  })

  it('shows enough context to tell two cities of the same name apart', async () => {
    // "San Salvador" is ambiguous between El Salvador's capital and San
    // Salvador de Jujuy. Resolving that before the request is the whole reason
    // this component exists instead of a plain text input.
    const user = aUser()
    renderPicker()

    await searchFor(user, 'San')

    const options = screen.getAllByRole('option')
    expect(options[0]?.textContent).toContain('El Salvador')
  })

  it('walks the options with the arrow keys, wrapping at both ends', async () => {
    const user = aUser()
    renderPicker()
    await searchFor(user, 'San')
    const input = screen.getByRole('combobox')

    await user.keyboard('{ArrowDown}')
    const first = activeOptionOf(input)
    expect(first).not.toBeNull()
    expect(screen.getAllByRole('option')[0]?.getAttribute('aria-selected')).toBe('true')

    // Past the last one and back to the first: holding a key never dead-ends.
    await user.keyboard('{ArrowDown}{ArrowDown}{ArrowDown}')
    expect(activeOptionOf(input)).toBe(first)

    await user.keyboard('{ArrowUp}')
    expect(screen.getAllByRole('option')[2]?.getAttribute('aria-selected')).toBe('true')
  })

  it('chooses the highlighted option with Enter, and never leaves the text field', async () => {
    const user = aUser()
    renderPicker()
    await searchFor(user, 'San')
    const input = screen.getByRole('combobox')

    await user.keyboard('{ArrowDown}{ArrowDown}{Enter}')

    expect(onSelect).toHaveBeenCalledWith({
      name: 'San Salvador de Jujuy',
      latitude: -24.19,
      longitude: -65.3,
    })
    expect(document.activeElement).toBe(input)
  })

  it('chooses an option that is pointed at', async () => {
    const user = aUser()
    renderPicker()
    await searchFor(user, 'San')

    const option = screen.getAllByRole('option')[2]
    await user.pointer({ target: option!, keys: '[MouseLeft>]' })

    expect(onSelect).toHaveBeenCalledWith({
      name: 'San Miguel',
      latitude: 13.48,
      longitude: -88.18,
    })
  })

  it('empties the box once a city is chosen, so the next search starts clean', async () => {
    const user = aUser()
    renderPicker()
    await searchFor(user, 'San')

    await user.keyboard('{ArrowDown}{Enter}')

    expect(screen.getByRole('combobox')).toHaveProperty('value', '')
    expect(screen.queryByRole('listbox')).toBeNull()
  })

  it('closes on Escape without choosing anything', async () => {
    const user = aUser()
    renderPicker()
    await searchFor(user, 'San')

    await user.keyboard('{Escape}')

    expect(screen.queryByRole('listbox')).toBeNull()
    expect(onSelect).not.toHaveBeenCalled()
  })

  it('closes the listbox when focus leaves the picker', async () => {
    // Pointerdown outside only covers the mouse. Someone tabbing to the next
    // control leaves an absolutely positioned list floating over the page,
    // dismissable only with Escape or a click.
    const user = aUser()
    renderPicker()
    await searchFor(user, 'San')

    await user.tab()

    expect(document.activeElement).toBe(screen.getByRole('button'))
    expect(screen.queryByRole('listbox')).toBeNull()
  })

  it('brings the highlighted option into view', async () => {
    // The list caps at 288px and the API returns up to eight suggestions, so
    // arrowing past the fifth points aria-activedescendant at something the
    // person cannot see.
    const user = aUser()
    renderPicker()
    await searchFor(user, 'San')

    await user.keyboard('{ArrowDown}{ArrowDown}{ArrowDown}')

    const options = screen.getAllByRole('option')
    expect(scrollIntoView).toHaveBeenCalled()
    expect(scrollIntoView.mock.instances.at(-1)).toBe(options[2])
  })

  it('always points at an option that is really on the page', async () => {
    // The highlight is remembered with the term it belongs to, and the term can
    // leave and come back. Rather than guess which sequence could leave the
    // pointer dangling, this walks one and checks the invariant at every step:
    // if the attribute is set, the element it names exists.
    const user = aUser()
    renderPicker()
    await searchFor(user, 'San')
    const input = screen.getByRole('combobox')

    const pointsAtSomethingReal = () => {
      const pointed = input.getAttribute('aria-activedescendant')
      return pointed === null || document.getElementById(pointed) !== null
    }

    await user.keyboard('{ArrowDown}{ArrowDown}{ArrowDown}')
    expect(pointsAtSomethingReal()).toBe(true)

    // Narrow the term: the highlight belongs to the old one and must go.
    await user.keyboard(' Sal')
    expect(input.getAttribute('aria-activedescendant')).toBeNull()

    // Widen it back to a term whose answers are already known.
    await user.keyboard('{Backspace}{Backspace}{Backspace}{Backspace}')
    expect(pointsAtSomethingReal()).toBe(true)
  })

  it('announces how many cities were found, in the right number', async () => {
    // "1 ciudades encontradas" is what a screen reader used to read out. The
    // count is the whole content of the announcement, so getting its grammar
    // wrong is getting the announcement wrong.
    const user = aUser()
    search.mockResolvedValue([CITIES[0]!])
    const { container } = renderPicker()

    await searchFor(user, 'San')

    const announcement = container.querySelector('[aria-live]')
    expect(announcement?.textContent).toBe('1 ciudad encontrada')
  })

  it('announces a search that failed', async () => {
    // The live region only ever spoke on success, so "no se pudo buscar" never
    // reached anyone who could not see the list.
    const user = aUser()
    search.mockRejectedValue(new Error('the geocoder is down'))
    const { container } = renderPicker()

    await user.type(screen.getByRole('combobox'), 'San')

    await waitFor(() =>
      expect(container.querySelector('[aria-live]')?.textContent).toBe(
        'No se pudo buscar en este momento',
      ),
    )
  })

  it('does not pass off its status rows as choices', async () => {
    // A listbox may only own options and groups. A bare row of prose inside one
    // is not a thing that can be chosen, and must not be counted as one.
    const user = aUser()
    search.mockResolvedValue([])
    const { container } = renderPicker()

    await user.type(screen.getByRole('combobox'), 'San')
    await waitFor(() =>
      expect(container.querySelector('[aria-live]')?.textContent).toBe(
        'No encontramos ninguna ciudad con ese nombre',
      ),
    )

    // Not an option, and not a list item either: inside a listbox those are the
    // only roles a child may carry, so a row of prose has to opt out of both.
    expect(screen.queryAllByRole('option')).toHaveLength(0)
    expect(screen.queryAllByRole('listitem')).toHaveLength(0)
  })

  it('asks for nothing until the term is worth searching for', async () => {
    const user = aUser()
    renderPicker()

    await user.type(screen.getByRole('combobox'), 'S')

    // Long enough that a debounced request would have gone out by now.
    await new Promise((resume) => setTimeout(resume, 400))

    expect(search).not.toHaveBeenCalled()
    expect(screen.queryByRole('listbox')).toBeNull()
  })
})

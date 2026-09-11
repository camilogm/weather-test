import { useId, useState } from 'react'

import type { LocationSuggestion, SelectedLocation } from '../api/types'
import { MIN_QUERY_LENGTH } from '../api/weather'
import { useCombobox } from '../hooks/useCombobox'
import { useLocationSearch, type SearchStatus } from '../hooks/useLocationSearch'
import { plural, t } from '../i18n'

interface Props {
  selected: SelectedLocation
  onSelect: (location: SelectedLocation) => void
}

/**
 * Picks a city from real search results instead of guessing at a typed name.
 *
 * A plain <select> was the obvious idea and does not work: there is no "list
 * every city" API anywhere, so its options could only ever be a list somebody
 * hardcoded. This is that select, with the options supplied by the search.
 *
 * What it buys is the thing a bare text input cannot: you SEE which place you
 * are choosing. "San Salvador" is ambiguous between El Salvador's capital and
 * San Salvador de Jujuy, and the region and country lines resolve that before
 * the request is made rather than after the wrong forecast arrives.
 *
 * The keyboard and focus rules of the ARIA combobox pattern live in
 * useCombobox; what is left here is what a suggestion looks like.
 */
export function CityPicker({ selected, onSelect }: Readonly<Props>) {
  const [term, setTerm] = useState('')
  const { status, results } = useLocationSearch(term)

  const trimmed = term.trim()

  const { isOpen, open, activeIndex, containerRef, inputHandlers, optionHandlers } = useCombobox({
    items: results,
    term: trimmed,
    onChoose: (suggestion: LocationSuggestion) => {
      onSelect({
        name: suggestion.name,
        latitude: suggestion.latitude,
        longitude: suggestion.longitude,
      })

      setTerm('')
    },
  })

  const listboxId = useId()
  const optionId = (index: number) => `${listboxId}-option-${index}`

  const showPopup = isOpen && trimmed.length >= MIN_QUERY_LENGTH

  const announcement = showPopup ? announce(status, results.length) : ''
  const message = showPopup ? messageFor(status, results.length) : null

  return (
    <div ref={containerRef} className="relative w-full sm:w-72">
      <label htmlFor={`${listboxId}-input`} className="sr-only">
        {t('picker.label')}
      </label>

      <input
        id={`${listboxId}-input`}
        type="text"
        role="combobox"
        aria-expanded={showPopup}
        aria-controls={listboxId}
        aria-autocomplete="list"
        aria-activedescendant={activeIndex >= 0 ? optionId(activeIndex) : undefined}
        autoComplete="off"
        value={term}
        placeholder={selected.name}
        onChange={(event) => {
          setTerm(event.target.value)
          open()
        }}
        {...inputHandlers}
        // A pill that floats: no border, because the shadow is already telling
        // you where the field ends, and two edges saying the same thing is one
        // edge too many.
        className="h-11 w-full rounded-pill bg-surface px-5 text-body shadow-control placeholder:text-ink-muted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent"
      />

      {/* Screen readers are told what happened; sighted users can see it. */}
      <span aria-live="polite" className="sr-only">
        {announcement}
      </span>

      {showPopup && (
        <div className="absolute z-10 mt-2 w-full overflow-hidden rounded-card border border-line bg-surface shadow-popover">
          {/*
            Outside the listbox, not a presentational row inside it. A listbox
            may own only options and groups, and prose about what the search is
            doing is neither — so it lives beside the list rather than
            pretending to be part of it.
          */}
          {message !== null && (
            <p className="px-5 py-2.5 text-caption text-ink-muted">{message}</p>
          )}

          <ul
            id={listboxId}
            role="listbox"
            aria-label={t('picker.label')}
            className="max-h-72 overflow-y-auto"
          >
            {results.map((suggestion, index) => (
            <li
              key={`${suggestion.name}-${suggestion.latitude}-${suggestion.longitude}`}
              id={optionId(index)}
              role="option"
              aria-selected={index === activeIndex}
              {...optionHandlers(index, suggestion)}
              // The accent fill is the selection colour every desktop list uses,
              // and it is the same accent as the focus ring — so the highlight
              // costs the palette nothing new in either colour scheme.
              className={[
                'cursor-pointer px-5 py-2.5 text-body transition-colors',
                index === activeIndex ? 'bg-accent text-white' : '',
              ].join(' ')}
            >
                <span className="block font-medium">{suggestion.name}</span>
                <span
                  className={[
                    'block text-caption',
                    index === activeIndex ? 'text-white/75' : 'text-ink-muted',
                  ].join(' ')}
                >
                  {describe(suggestion)}
                </span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  )
}

/**
 * What a screen reader hears when the list changes under it.
 *
 * "Searching" is deliberately absent: it would fire on every keystroke, and a
 * live region that talks over itself is worse than one that waits until it has
 * something to say.
 */
function announce(status: SearchStatus, count: number): string {
  if (status === 'error') {
    return t('picker.failed')
  }

  if (status !== 'ready') {
    return ''
  }

  if (count === 0) {
    return t('picker.noResults')
  }

  return plural({ one: 'picker.resultsCountOne', many: 'picker.resultsCountMany' }, count)
}

/** The same news, written down for whoever is looking at the list. */
function messageFor(status: SearchStatus, count: number): string | null {
  if (status === 'searching') {
    return t('picker.searching')
  }

  if (status === 'error') {
    return t('picker.failed')
  }

  return status === 'ready' && count === 0 ? t('picker.noResults') : null
}

/** "San Salvador · El Salvador" — whichever of the two parts came back. */
function describe(suggestion: LocationSuggestion): string {
  return [suggestion.region, suggestion.country].filter(Boolean).join(' · ')
}

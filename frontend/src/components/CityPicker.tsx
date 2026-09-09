import { useId, useState } from 'react'

import type { LocationSuggestion, SelectedLocation } from '../api/types'
import { MIN_QUERY_LENGTH } from '../api/weather'
import { useCombobox } from '../hooks/useCombobox'
import { useLocationSearch } from '../hooks/useLocationSearch'
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
export function CityPicker({ selected, onSelect }: Props) {
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

  // What a screen reader hears when the list changes under it. "Searching" is
  // deliberately absent: it would fire on every keystroke, and a live region
  // that talks over itself is worse than one that waits for something to say.
  const announcement = !showPopup
    ? ''
    : status === 'error'
      ? t('picker.failed')
      : status === 'ready'
        ? results.length === 0
          ? t('picker.noResults')
          : plural(
              { one: 'picker.resultsCountOne', many: 'picker.resultsCountMany' },
              results.length,
            )
        : ''

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
        className="h-11 w-full border border-ink bg-surface px-3 text-sm placeholder:text-ink-muted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent"
      />

      {/* Screen readers are told what happened; sighted users can see it. */}
      <span aria-live="polite" className="sr-only">
        {announcement}
      </span>

      {showPopup && (
        <ul
          id={listboxId}
          role="listbox"
          aria-label={t('picker.label')}
          className="absolute z-10 mt-1 max-h-72 w-full overflow-y-auto border border-ink bg-surface shadow-[0.25rem_0.25rem_0_0_var(--color-line)]"
        >
          {status === 'searching' && <Message text={t('picker.searching')} />}
          {status === 'error' && <Message text={t('picker.failed')} />}
          {status === 'ready' && results.length === 0 && (
            <Message text={t('picker.noResults')} />
          )}

          {results.map((suggestion, index) => (
            <li
              key={`${suggestion.name}-${suggestion.latitude}-${suggestion.longitude}`}
              id={optionId(index)}
              role="option"
              aria-selected={index === activeIndex}
              {...optionHandlers(index, suggestion)}
              // Inverted rather than tinted: ink on paper flips to paper on ink,
              // which reads as a highlight in both colour schemes without needing
              // a second token for each one.
              className={[
                'cursor-pointer px-3 py-2 text-sm transition-colors',
                index === activeIndex ? 'bg-ink text-canvas' : '',
              ].join(' ')}
            >
              <span className="block font-medium">{suggestion.name}</span>
              <span
                className={[
                  'block font-mono text-xs',
                  index === activeIndex ? 'text-canvas/70' : 'text-ink-muted',
                ].join(' ')}
              >
                {describe(suggestion)}
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

/**
 * A row of prose inside the list — searching, failed, nothing found.
 *
 * Marked presentational because a listbox may only own options and groups, and
 * a status message is neither. Its text still reaches assistive technology,
 * through the live region above rather than as something choosable.
 */
function Message({ text }: { text: string }) {
  return (
    <li role="presentation" className="px-3 py-2 text-sm text-ink-muted">
      {text}
    </li>
  )
}

/** "San Salvador · El Salvador" — whichever of the two parts came back. */
function describe(suggestion: LocationSuggestion): string {
  return [suggestion.region, suggestion.country].filter(Boolean).join(' · ')
}

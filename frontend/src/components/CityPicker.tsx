import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react'

import type { LocationSuggestion, SelectedLocation } from '../api/types'
import { MIN_QUERY_LENGTH } from '../api/weather'
import { useLocationSearch } from '../hooks/useLocationSearch'
import { t } from '../i18n'

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
 * Built to the ARIA combobox pattern — the input owns the listbox, arrow keys
 * move a virtual cursor via aria-activedescendant, and focus never leaves the
 * text field.
 */
export function CityPicker({ selected, onSelect }: Props) {
  const [term, setTerm] = useState('')
  const [isOpen, setIsOpen] = useState(false)

  // The highlight is stored WITH the term it belongs to, and read back only
  // when they still match. Resetting it from an effect keyed on `results`
  // looked equivalent and was not: a new array arrives on every render, so any
  // unrelated re-render would silently clear whatever was highlighted.
  const [active, setActive] = useState({ term: '', index: -1 })

  const { status, results } = useLocationSearch(term)

  const trimmed = term.trim()
  const activeIndex = active.term === trimmed ? active.index : -1

  const listboxId = useId()
  const optionId = (index: number) => `${listboxId}-option-${index}`
  const containerRef = useRef<HTMLDivElement>(null)

  // A click anywhere else means the person moved on without choosing.
  useEffect(() => {
    if (!isOpen) {
      return
    }

    function onPointerDown(event: PointerEvent) {
      if (!containerRef.current?.contains(event.target as Node)) {
        setIsOpen(false)
      }
    }

    document.addEventListener('pointerdown', onPointerDown)
    return () => document.removeEventListener('pointerdown', onPointerDown)
  }, [isOpen])

  function choose(suggestion: LocationSuggestion) {
    onSelect({
      name: suggestion.name,
      latitude: suggestion.latitude,
      longitude: suggestion.longitude,
    })

    setTerm('')
    setIsOpen(false)
    setActive({ term: '', index: -1 })
  }

  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'Escape') {
      setIsOpen(false)
      setActive({ term: '', index: -1 })
      return
    }

    if (event.key === 'Enter') {
      const highlighted = results[activeIndex]
      if (isOpen && highlighted) {
        event.preventDefault()
        choose(highlighted)
      }
      return
    }

    if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') {
      return
    }

    if (results.length === 0) {
      return
    }

    event.preventDefault()
    setIsOpen(true)

    // Wraps at both ends, so holding a key never dead-ends on a boundary.
    const step = event.key === 'ArrowDown' ? 1 : -1
    const next = activeIndex + step
    const wrapped = next < 0 ? results.length - 1 : next >= results.length ? 0 : next

    setActive({ term: trimmed, index: wrapped })
  }

  const showPopup = isOpen && trimmed.length >= MIN_QUERY_LENGTH

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
          setIsOpen(true)
        }}
        onFocus={() => setIsOpen(true)}
        onKeyDown={onKeyDown}
        className="w-full rounded-lg border border-line bg-surface px-3 py-2 text-sm placeholder:text-ink-muted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent"
      />

      {/* Screen readers are told how many options appeared; sighted users see them. */}
      <span aria-live="polite" className="sr-only">
        {showPopup && status === 'ready'
          ? t('picker.resultsCount', { count: results.length })
          : ''}
      </span>

      {showPopup && (
        <ul
          id={listboxId}
          role="listbox"
          aria-label={t('picker.label')}
          className="absolute z-10 mt-1 max-h-72 w-full overflow-y-auto rounded-lg border border-line bg-surface py-1 shadow-lg"
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
              // pointerdown, not click: the outside-click handler would close
              // the list on mousedown and the click would never land.
              onPointerDown={(event) => {
                event.preventDefault()
                choose(suggestion)
              }}
              onPointerEnter={() => setActive({ term: trimmed, index })}
              className={[
                'cursor-pointer px-3 py-2 text-sm',
                index === activeIndex ? 'bg-accent/12' : '',
              ].join(' ')}
            >
              <span className="block font-medium">{suggestion.name}</span>
              <span className="block text-xs text-ink-muted">{describe(suggestion)}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

function Message({ text }: { text: string }) {
  return <li className="px-3 py-2 text-sm text-ink-muted">{text}</li>
}

/** "San Salvador · El Salvador" — whichever of the two parts came back. */
function describe(suggestion: LocationSuggestion): string {
  return [suggestion.region, suggestion.country].filter(Boolean).join(' · ')
}

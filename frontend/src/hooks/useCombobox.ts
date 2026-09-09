import { useEffect, useRef, useState, type FocusEvent, type KeyboardEvent } from 'react'

interface Params<T> {
  /** The options currently on offer, in the order they are rendered. */
  items: T[]
  /** The live search term. The highlight is remembered against it. */
  term: string
  onChoose: (item: T) => void
}

/**
 * The behaviour half of the ARIA combobox pattern: open state, a virtual cursor
 * over the options, and the ways out — Escape, a click elsewhere, focus leaving.
 *
 * Split from the markup because the two answer to different things. Every
 * keyboard and focus rule below is a rule of the pattern, identical for any
 * listbox; what a suggestion looks like is not. Interleaved, the rules were
 * hard to find among the class names and impossible to test without rendering
 * the whole component.
 */
export function useCombobox<T>({ items, term, onChoose }: Params<T>) {
  const [isOpen, setIsOpen] = useState(false)

  // The highlight is stored WITH the term it belongs to, and read back only
  // when they still match. Resetting it from an effect keyed on the items
  // looked equivalent and was not: a new array arrives on every render, so any
  // unrelated re-render would silently clear whatever was highlighted.
  const [active, setActive] = useState({ term: '', index: -1 })

  const containerRef = useRef<HTMLDivElement>(null)
  const optionRefs = useRef<(HTMLElement | null)[]>([])

  // Clamped against what is actually there, so the index can never name an
  // option that is not on the page.
  const activeIndex = active.term === term && active.index < items.length ? active.index : -1

  const clearHighlight = () => setActive({ term: '', index: -1 })

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

  // The list scrolls, so the virtual cursor can walk off the bottom of what is
  // visible. This is the one place where touching the DOM directly is the right
  // answer: there is no declarative way to ask a scroll container to move.
  useEffect(() => {
    if (activeIndex < 0) {
      return
    }

    optionRefs.current[activeIndex]?.scrollIntoView({ block: 'nearest' })
  }, [activeIndex])

  function choose(item: T) {
    onChoose(item)
    setIsOpen(false)
    clearHighlight()
  }

  function onKeyDown(event: KeyboardEvent) {
    if (event.key === 'Escape') {
      setIsOpen(false)
      clearHighlight()
      return
    }

    if (event.key === 'Enter') {
      const highlighted = items[activeIndex]
      if (isOpen && highlighted) {
        event.preventDefault()
        choose(highlighted)
      }
      return
    }

    if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') {
      return
    }

    if (items.length === 0) {
      return
    }

    event.preventDefault()
    setIsOpen(true)

    // Wraps at both ends, so holding a key never dead-ends on a boundary.
    const step = event.key === 'ArrowDown' ? 1 : -1
    const next = activeIndex + step
    const wrapped = next < 0 ? items.length - 1 : next >= items.length ? 0 : next

    setActive({ term, index: wrapped })
  }

  return {
    isOpen,
    activeIndex,
    containerRef,
    /** For the caller's own reasons to open the list — typing into it, say. */
    open: () => setIsOpen(true),
    inputHandlers: {
      onFocus: () => setIsOpen(true),
      // Pointerdown outside only catches the mouse. Without this, tabbing to
      // the next control leaves the list floating over the page.
      onBlur: (event: FocusEvent) => {
        if (!containerRef.current?.contains(event.relatedTarget)) {
          setIsOpen(false)
        }
      },
      onKeyDown,
    },
    optionHandlers: (index: number, item: T) => ({
      ref: (node: HTMLElement | null) => {
        optionRefs.current[index] = node
      },
      // pointerdown, not click: the outside-click handler would close the list
      // on mousedown and the click would never land.
      onPointerDown: (event: { preventDefault: () => void }) => {
        event.preventDefault()
        choose(item)
      },
      onPointerEnter: () => setActive({ term, index }),
    }),
  }
}

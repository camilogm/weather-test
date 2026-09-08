/**
 * The API sends plain calendar dates ("2026-09-07"). Passing one straight to
 * `new Date()` parses it as UTC midnight, which in any negative-offset timezone
 * — San Salvador included — renders as the DAY BEFORE. So the parts are split
 * and handed to the local constructor instead.
 */
export function parseCalendarDate(value: string): Date {
  const [year, month, day] = value.split('-').map(Number)
  return new Date(year, month - 1, day)
}

export function weekdayOf(value: string): string {
  return parseCalendarDate(value).toLocaleDateString(undefined, { weekday: 'short' })
}

export function dayAndMonthOf(value: string): string {
  return parseCalendarDate(value).toLocaleDateString(undefined, {
    day: 'numeric',
    month: 'short',
  })
}

export function isToday(value: string): boolean {
  const date = parseCalendarDate(value)
  const today = new Date()

  return (
    date.getFullYear() === today.getFullYear() &&
    date.getMonth() === today.getMonth() &&
    date.getDate() === today.getDate()
  )
}

export function formatTemperature(celsius: number): string {
  return `${Math.round(celsius)}°`
}

/** Turns "PartlyCloudy" into "Partly cloudy" for humans. */
export function humanizeCondition(condition: string): string {
  const spaced = condition.replace(/([a-z])([A-Z])/g, '$1 $2')
  return spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase()
}

export function formatObservedAt(value: string): string {
  return new Date(value).toLocaleString(undefined, {
    hour: '2-digit',
    minute: '2-digit',
    day: 'numeric',
    month: 'short',
  })
}

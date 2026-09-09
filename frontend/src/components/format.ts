import { LOCALE, t } from '../i18n'

/**
 * The API sends plain calendar dates ("2026-09-07"). Passing one straight to
 * `new Date()` parses it as UTC midnight, which in any negative-offset timezone
 * — San Salvador included — renders as the DAY BEFORE. So the parts are split
 * and handed to the local constructor instead.
 */
export function parseCalendarDate(value: string): Date | null {
  const [year, month, day] = value.split('-').map(Number)

  // Returns null rather than an Invalid Date. A malformed value from upstream
  // should degrade to showing the raw string in one cell, not take the page
  // down with "NaN de NaN" everywhere.
  if (year === undefined || month === undefined || day === undefined) {
    return null
  }

  if (Number.isNaN(year) || Number.isNaN(month) || Number.isNaN(day)) {
    return null
  }

  return new Date(year, month - 1, day)
}

/**
 * Every formatter below pins the locale explicitly. Left to the browser's
 * preference, a Spanish product would render "Mon 7 Sep" for anyone whose
 * machine is set to English — half-translated, which reads worse than either
 * language on its own.
 */
export function weekdayOf(value: string): string {
  const date = parseCalendarDate(value)
  if (!date) {
    return value
  }

  // Spanish weekday abbreviations come back lowercase ("lun"); a card heading
  // reads better capitalised.
  const weekday = date.toLocaleDateString(LOCALE, { weekday: 'short' })
  return weekday.charAt(0).toUpperCase() + weekday.slice(1)
}

export function dayAndMonthOf(value: string): string {
  const date = parseCalendarDate(value)
  if (!date) {
    return value
  }

  return date.toLocaleDateString(LOCALE, { day: 'numeric', month: 'short' })
}

export function isToday(value: string): boolean {
  const date = parseCalendarDate(value)
  if (!date) {
    return false
  }

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

/**
 * "13.70° N · 89.19° O" — the masthead line under the city name.
 *
 * Two decimals is roughly a kilometre, which is as much precision as a city
 * forecast means. The hemisphere letters are Spanish (O for oeste, not W), so
 * they come out of the dictionary like every other visible string.
 */
export function formatCoordinates(latitude: number, longitude: number): string {
  const northSouth = t(latitude >= 0 ? 'compass.north' : 'compass.south')
  const eastWest = t(longitude >= 0 ? 'compass.east' : 'compass.west')

  return t('app.coordinates', {
    lat: `${Math.abs(latitude).toFixed(2)}° ${northSouth}`,
    lon: `${Math.abs(longitude).toFixed(2)}° ${eastWest}`,
  })
}

export function formatObservedAt(value: string): string {
  return new Date(value).toLocaleString(LOCALE, {
    hour: '2-digit',
    minute: '2-digit',
    day: 'numeric',
    month: 'short',
  })
}

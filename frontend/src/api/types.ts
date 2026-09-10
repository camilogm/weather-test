/**
 * Mirrors the contracts in WeatherService.Api/Contracts.
 * ASP.NET serialises camelCase by default, so these names line up as written.
 */

/** Where the backend got the data. Anything other than Provider/Cache is degraded. */
export type WeatherDataSource = 'Provider' | 'Cache' | 'StaleCache' | 'Historical'

export interface Provenance {
  source: WeatherDataSource
  degraded: boolean
  /** ISO-8601 instant. */
  retrievedAt: string
}

export interface WeatherLocation {
  name: string
  latitude: number
  longitude: number
}

export interface DailyForecast {
  /** Plain calendar date, "YYYY-MM-DD". Never parse this with `new Date()` alone. */
  date: string
  minTemperatureC: number
  maxTemperatureC: number
  condition: string
  icon: WeatherIconName
}

export interface Forecast {
  location: WeatherLocation
  provenance: Provenance
  days: DailyForecast[]
}

export interface CurrentWeather {
  location: WeatherLocation
  provenance: Provenance
  temperatureC: number
  apparentTemperatureC: number
  windSpeedKph: number
  relativeHumidityPercent: number
  condition: string
  icon: WeatherIconName
  observedAt: string
}

/** A candidate place from `/locations`, with the context needed to pick one. */
export interface LocationSuggestion {
  name: string
  region: string | null
  country: string | null
  countryCode: string | null
  latitude: number
  longitude: number
}

export interface LocationSearchResponse {
  results: LocationSuggestion[]
}

/**
 * A place the person has actually chosen. Coordinates travel with it, so the
 * forecast request never has to geocode the name a second time — and can never
 * land on a different city of the same name than the one they picked.
 */
export interface SelectedLocation {
  name: string
  latitude: number
  longitude: number
}

export type WeatherIconName =
  | 'clear'
  | 'mostly-clear'
  | 'partly-cloudy'
  | 'overcast'
  | 'fog'
  | 'drizzle'
  | 'rain'
  | 'freezing-rain'
  | 'snow'
  | 'thunderstorm'
  | 'unknown'

/** RFC 7807, which is what the API returns for every failure. */
export interface ProblemDetails {
  title?: string
  detail?: string
  status?: number
}

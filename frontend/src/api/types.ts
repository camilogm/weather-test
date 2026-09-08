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

export interface WeeklyForecast {
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

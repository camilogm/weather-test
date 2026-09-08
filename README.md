# WeatherService

A .NET 8 microservice that serves current conditions and a seven-day forecast,
plus a React front end that displays the week for **San Salvador**.

The interesting part is not fetching the weather. It is what happens when the
upstream stops answering.

---

## Quick start

```bash
git clone <this-repo>
cd weather-test
make env          # creates .env from env.example
docker compose up --build
```

| What            | Where                                                       |
| --------------- | ----------------------------------------------------------- |
| Front end       | <http://localhost:5173>                                      |
| API             | <http://localhost:8080>                                      |
| Swagger         | <http://localhost:8080/swagger> *(Development only)*         |
| Health          | <http://localhost:8080/health>                               |
| Postgres        | `localhost:5432`                                             |

No API key is needed. The service uses [Open-Meteo], which is free and requires
no registration — you can clone this and see it work without signing up for
anything. Migrations run automatically on startup.

[Open-Meteo]: https://open-meteo.com

### Without Docker

The API falls back to SQLite, so it runs with nothing installed but the .NET 8
SDK:

```bash
make api          # http://localhost:5080, SQLite, no database server needed
make web          # http://localhost:5173
```

`make help` lists every target.

---

## Endpoints

### `GET /weather/forecast`

Seven daily forecasts. Defaults to San Salvador.

```
GET /weather/forecast
GET /weather/forecast?city=Guatemala%20City
GET /weather/forecast?latitude=13.6929&longitude=-89.2182
```

```jsonc
{
  "location": { "name": "San Salvador", "latitude": 13.6929, "longitude": -89.2182 },
  "provenance": { "source": "Provider", "degraded": false, "retrievedAt": "2026-09-07T12:00:00+00:00" },
  "days": [
    {
      "date": "2026-09-07",
      "minTemperatureC": 21.4,
      "maxTemperatureC": 31.2,
      "condition": "PartlyCloudy",
      "icon": "partly-cloudy"
    }
  ]
}
```

### `GET /weather/current`

Same query parameters; returns temperature, apparent temperature, humidity,
wind and condition.

### Responses

| Status | When                                                             |
| ------ | ---------------------------------------------------------------- |
| `200`  | Data available — check `provenance` to see whether it is live     |
| `400`  | Malformed query (half a coordinate pair, latitude out of range)   |
| `404`  | No such city                                                     |
| `503`  | Every source exhausted; includes `Retry-After`                   |

Errors are [RFC 7807] problem documents. The distinction between `503` and `500`
is deliberate: a `500` says *this service is broken*, a `503` says *this service
is fine, its upstream is not, try again*. Reporting an upstream outage as a
`500` sends whoever is on call to read the wrong logs.

[RFC 7807]: https://datatracker.ietf.org/doc/html/rfc7807

---

## The part that matters: what happens when Open-Meteo is down

Every response declares where its data came from, in the body and in the
`X-Weather-Data-Source` header. A silent fallback is a lie of omission.

```
GET /weather/forecast
  │
  ├─ 1. cache, still fresh (10 min)   → 200  source: Cache
  │
  ├─ 2. Open-Meteo                    → 200  source: Provider
  │        │  timeout → retry → circuit breaker → timeout
  │        └─ fails, or the circuit is open ↓
  │
  ├─ 3. cache, expired but retained   → 200  source: StaleCache   degraded
  │                                          Cache-Control: no-store
  │        └─ nothing there ↓
  │
  ├─ 4. last snapshot in Postgres     → 200  source: Historical   degraded
  │        └─ nothing there ↓
  │
  └─ 5. nothing left                  → 503  + Retry-After
```

Two design consequences worth calling out:

**The cache deliberately outlives its own freshness.** `IMemoryCache` cannot
express *expired but still readable* — when an entry expires, it is gone. So
`IWeatherCache` is a port of our own, and `CachedValue` tracks freshness
(minutes) separately from retention (hours). Without that separation, step 3
above cannot exist.

**Persistence is not decorative.** The brief calls storing history optional; here
it is the last rung of the ladder, which is why it earns its place. Writing it is
also treated as a side effect: if Postgres is down, the error is logged and the
forecast is still returned. A failed `INSERT` the caller never asked for must not
turn a good answer into a `500`.

### The circuit breaker

Configured explicitly rather than through `AddStandardResilienceHandler()`. The
one-liner produces roughly the same strategies but hides every number, and the
numbers are the design.

```
total timeout   caps the whole operation, retries included
  retry         rides out a blip; exponential with jitter, so clients do not
                synchronise into a thundering herd when the upstream recovers
    breaker     stops calling a service that is clearly down
      attempt   caps one individual try
```

The breaker sits **inside** the retry on purpose: retries should count toward
tripping it. Put it outside and a single logical call could hammer a dying
service several times without the breaker ever noticing.

Its point is not "pick a different provider" — it is to **stop waiting**. A hung
dependency with no breaker parks a thread per in-flight request until it times
out, and a few hundred concurrent requests later the service is dead for reasons
that have nothing to do with its own code.

That claim is tested rather than asserted: `CircuitBreakerTests` drives a real
WireMock server into failure and then checks that once the circuit is open,
**no further request reaches the network at all**.

Forecast and geocoding use separate HTTP clients, and therefore separate breaker
state. A geocoder having a bad day must not cut off forecasts that are answering
perfectly well.

---

## Architecture

```
backend/
├── src/
│   ├── WeatherService.Application     ← domain, ports, use cases
│   ├── WeatherService.Infrastructure  ← adapters: HTTP, EF Core, cache
│   └── WeatherService.Api             ← controllers, error mapping, wiring
└── tests/
    ├── WeatherService.UnitTests
    └── WeatherService.IntegrationTests
frontend/                              ← React + Vite + Tailwind
```

`Application` has exactly one dependency: `Microsoft.Extensions.Logging.Abstractions`.
No HTTP, no EF, no `IMemoryCache`. That constraint is written into the `.csproj`
as a comment, and it is what lets the entire degradation chain be tested with no
network, no clock and no database — those tests run in milliseconds.

Everything crossing a boundary goes through a port:

| Port                | Adapter                      |
| ------------------- | ---------------------------- |
| `IWeatherProvider`  | `OpenMeteoWeatherProvider`   |
| `ILocationResolver` | `OpenMeteoLocationResolver`  |
| `IWeatherCache`     | `MemoryWeatherCache`         |
| `IForecastHistory`  | `EfForecastHistory`          |

The adapter also owns the boundary contract: every transport failure — dead
socket, `500`, truncated payload, open circuit — leaves as
`WeatherProviderException`. Nothing above that layer knows HTTP or Polly exist.

### Persistence

Snapshots are append-only: history is a log, not a cache. Overwriting would
destroy the very record that makes the last fallback useful.

**Postgres and SQLite have separate migration sets**, because they emit
incompatible DDL:

| Column         | Postgres                   | SQLite |
| -------------- | -------------------------- | ------ |
| `Id`           | `uuid`                     | `TEXT` |
| `Latitude`     | `double precision`         | `REAL` |
| `RetrievedAt`  | `timestamp with time zone` | `TEXT` |

One migration set generated for either provider fails on the other. Each
`DbContext` owns its own history instead.

---

## Testing

```bash
make test              # everything
make test-unit         # fast, no I/O
make test-integration  # real SQLite, real HTTP, real ASP.NET pipeline
```

70 tests, in two layers that do genuinely different jobs.

**Unit tests** cover the use cases through substituted ports. No network, no
database, no `Thread.Sleep` — `FakeTimeProvider` moves the clock, so testing a
24-hour cache retention window takes no wall-clock time at all.

**Integration tests** run against real infrastructure:

- `EfForecastHistoryTests` — a real SQLite engine, schema built by running the
  migrations, so a broken migration fails here rather than on your machine.
- `CircuitBreakerTests` — a real WireMock HTTP server told to misbehave.
- `WeatherEndpointsTests` — the real application through `WebApplicationFactory`,
  replacing only the upstream and the clock. Real routing, model binding,
  middleware, ProblemDetails and EF stay under test.

> **On `UseInMemoryDatabase`:** it is not used here, and that decision paid for
> itself during development. `EfForecastHistoryTests` immediately failed with
> `SQLite does not support expressions of type 'DateTimeOffset' in ORDER BY
> clauses` — SQLite stores one as text with the offset appended, which does not
> sort. The EF InMemory provider is a list of objects in C#; it would have sorted
> that happily and carried the defect to production. A real relational engine
> stops you. A fake applauds.

---

## Configuration

Everything is overridable through `appsettings.json`, environment variables
(`Section__Key`) or `.env` for compose.

| Setting                                    | Default                         |
| ------------------------------------------ | ------------------------------- |
| `Database__Provider`                       | `Postgres` (`Sqlite` in Development) |
| `Database__ConnectionString`               | local Postgres                  |
| `Database__MigrateOnStartup`               | `true`                          |
| `WeatherProvider__BaseAddress`             | `https://api.open-meteo.com/`   |
| `WeatherProvider__Resilience__MaxRetries`  | `2`                             |
| `WeatherProvider__Resilience__FailureRatio`| `0.5`                           |
| `WeatherProvider__Resilience__MinimumThroughput` | `5`                       |
| `WeatherProvider__Resilience__BreakDuration`| `00:00:15`                     |
| `Cors__AllowedOrigins__0`                  | `http://localhost:5173`         |

Migrations run on startup for convenience here. That is not a production habit —
there, migrations belong in a deployment step rather than in however many
instances happen to start at once.

---

## Front end

React 19 + Vite + Tailwind v4 + TypeScript. No framework beyond that: there is
no SSR requirement and no routing, so Next would have been weight without
purpose.

```bash
make web-install
make web            # http://localhost:5173
make web-build      # type-check and production build
```

**How it talks to the API.** `VITE_API_BASE_URL`, defaulting to
`http://localhost:8080`. Vite inlines env vars at *build* time, not runtime — an
image built for one host cannot be repointed by restarting it with a different
variable — which is why `docker-compose.yml` passes it as a build argument.

**What it does with provenance.** The degraded banner is not decoration. When the
backend reports `StaleCache` or `Historical`, the UI says so and shows when the
data was taken. Showing a three-hour-old forecast as if it were current would be
worse than showing nothing.

**Responsive.** Mobile first: one column, then two at `sm`, four at `lg`, seven
at `xl`. Icons are inline SVG drawn from theme tokens, so they follow light and
dark without a second asset set. `prefers-reduced-motion` is respected.

One subtlety worth knowing: the API sends plain calendar dates (`2026-09-07`).
Passing one straight to `new Date()` parses it as UTC midnight, which in any
negative-offset timezone — San Salvador included — renders as *the day before*.
`parseCalendarDate` splits the parts and uses the local constructor instead.

---

## Decisions and trade-offs

**Open-Meteo over OpenWeatherMap.** OpenWeatherMap's free tier returns five days,
not seven; the 16-day daily endpoint is paid, and One Call 3.0 requires a payment
method even for its free quota. It would have failed an explicit requirement, or
forced a reviewer to register and hand over a card to run this. Open-Meteo needs
no key and returns a native daily forecast well past seven days. `IWeatherProvider`
is designed so a second adapter is a new class, not a refactor.

**Three projects, not four.** Splitting `Domain` from `Application` would add a
layer of indirection that two read endpoints cannot justify. The boundary that
actually matters — domain isolated from infrastructure — is enforced by the
dependency graph and visible in the `.csproj` files.

**Controllers over minimal APIs.** Both are fine in .NET 8. Controllers give
`[ApiController]` model validation returning ProblemDetails for free, and read
more conventionally for "expose RESTful endpoints".

**Flat folders, no monorepo tool.** One backend and one front end do not need Nx
or workspaces; the tooling would be ceremony that hides the code being reviewed.
Compose and a Makefile at the root do the whole job.

**FluentAssertions pinned to v7.** v8 moved to a commercial licence. Shipping a
paid dependency into a technical exercise by accident is exactly the kind of
detail worth not shipping.

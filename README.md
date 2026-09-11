# WeatherService

A .NET 8 microservice that serves current conditions and up to sixteen days of
forecast, plus a React front end that displays them for **San Salvador**.

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
| Swagger         | <http://localhost:8080/swagger>                              |
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

Daily forecasts from today forward. Defaults to San Salvador and to seven days.

```
GET /weather/forecast
GET /weather/forecast?days=16
GET /weather/forecast?city=Guatemala%20City&days=14
GET /weather/forecast?latitude=13.6929&longitude=-89.2182
```

`days` runs from 1 to 16 and defaults to 7, which is what this endpoint answered
before the parameter existed — no client that never heard of it sees its
response change. Out of range is a `400` rather than a silent clamp: a caller
that asked for thirty days and quietly got sixteen would have no way to know its
request was not honoured.

**The range never reaches the provider.** Every call fetches, caches and stores
the whole sixteen-day horizon; `days` is a projection taken over that one
answer. Fetching per range would put the range in the cache key, and a service
already holding sixteen days would go back to the network to be asked for seven
of them. It also means a single upstream call answers every range — and that a
stored snapshot can back every range during an outage.

The projection counts from today, not from index zero. A snapshot recorded three
days ago still opens on the day it was taken, so slicing by index would replay
days that are over and label them a forecast. The cutoff is deliberately one day
looser than today: Open-Meteo's dates are local to the forecast location while
the service clock is UTC, and a location far enough west runs a whole calendar
day behind it. Keeping one finished day costs a card; dropping a live "today"
costs the day people opened the page to see.

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

### `GET /locations?query=san&limit=8`

Candidate places for a partial name, for the front end's city picker.

```jsonc
{
  "results": [
    {
      "name": "San Salvador",
      "region": "San Salvador",
      "country": "El Salvador",
      "countryCode": "SV",
      "latitude": 13.68935,
      "longitude": -89.18718
    }
  ]
}
```

Region and country are part of the contract rather than a nicety: "San
Salvador" alone cannot be chosen with confidence — El Salvador's capital and
San Salvador de Jujuy both answer to it — and the picker exists precisely so a
person can see which one they are selecting.

This endpoint is why the browser never calls the geocoding provider directly.
Going straight there would bypass the circuit breaker, the cache and the error
handling this service already owns, and put a third-party host in the page's
critical path. Nothing matching returns an empty list with `200`; a geocoder
that is down returns `503`, because "we could not search" and "there is nothing
named that" are different answers.

### Responses

| Status | When                                                             |
| ------ | ---------------------------------------------------------------- |
| `200`  | Data available — check `provenance` to see whether it is live     |
| `400`  | Malformed query (half a pair, latitude or `days` out of range)   |
| `404`  | No such city                                                     |
| `429`  | Rate limit spent; includes `Retry-After`                         |
| `503`  | Every source exhausted; includes `Retry-After`                   |

Errors are [RFC 7807] problem documents. The distinction between `503` and `500`
is deliberate: a `500` says *this service is broken*, a `503` says *this service
is fine, its upstream is not, try again*. Reporting an upstream outage as a
`500` sends whoever is on call to read the wrong logs.

[RFC 7807]: https://datatracker.ietf.org/doc/html/rfc7807

### Rate limiting

Putting this service between the browser and Open-Meteo buys a circuit breaker,
a cache and real error handling in that path. The other half of that bargain is
that the service now owns the quota — and these endpoints take no credentials,
so one caller in a loop would spend an allowance that belongs to everybody.

| Policy    | Endpoints            | Default        |
| --------- | -------------------- | -------------- |
| `weather` | `/weather/*`         | 60 per minute  |
| `search`  | `/locations`         | 120 per minute |

Two budgets rather than one, because the traffic shapes differ: a forecast is
one call per city a person picks, while the picker's search fires on every
debounced keystroke. A single number sized for search leaves the expensive
endpoint wide open; sized for forecasts, it throttles ordinary typing.

Sliding windows, not fixed. A fixed window lets a caller spend a full budget at
`11:59:59` and another at `12:00:00` — twice the intended rate across the
boundary, which is the exact burst this is here to stop. Nothing queues: making
a throttled caller wait holds a connection open to tell them something a `429`
says immediately.

A rejection is an RFC 7807 document with a `Retry-After`, like every other
failure here. The health check is deliberately outside all of it — throttling it
would make a healthy container flap.

> **Behind a proxy**, callers are partitioned by `RemoteIpAddress`, so a
> deployment that terminates TLS elsewhere needs `ForwardedHeaders` configured
> or every caller lands in one shared bucket. That bucket is shared rather than
> exempt on purpose: not knowing who someone is, is a reason to be more careful,
> not less.

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

### A partial answer is still an answer

The ladder above is about an upstream that stops answering. The harder case is an
upstream that answers with a hole in it, and it took a live payload to find.

Open-Meteo pads the tail of the horizon with `null` when the location's local
calendar runs past the model's data window. Asking for sixteen days at Atlantis,
ZA returned fifteen real days and a sixteenth of `weather_code: null`,
`temperature_2m_max: null`, `temperature_2m_min: null`. The wire type declared
those columns as `IReadOnlyList<int>` and `IReadOnlyList<double>`, so
System.Text.Json threw while reading the body — and the caller lost all fifteen
usable days to a `503`.

It depends on the location's UTC offset and the hour you ask, which is what makes
it nasty: at one point London and Nairobi were both answering `503` while San
Salvador, Madrid and Tokyo answered `200`. Nothing was wrong with the service, the
network or the cache. It was simply not San Salvador's turn yet.

The columns are nullable now, and the adapter distinguishes the two ways a
columnar payload can be wrong, because they deserve opposite treatment:

- A column of the **wrong length** is still fatal. Zipping mismatched columns
  pairs the wrong temperature with the wrong day and yields a forecast that looks
  entirely plausible and is wrong — worse than no forecast at all.
- A **null entry** inside correctly-sized columns is not. Nothing can be
  misaligned by it, and a day with no condition and no temperatures has nothing to
  render, so that day is dropped and the rest is served. Every `DailyForecast`
  carries its own date, so the series survives a hole in the middle as well as one
  at the end.

If no day survives, the answer leaves as a provider failure rather than as an
empty series — which matters more than it looks. An empty series would be cached
for ten minutes and written to history as a snapshot, poisoning the exact two
fallbacks that exist for this moment. A provider failure sends the use case down
the ladder instead.

The lesson is the one the brief is really asking about. Resilience is not only
retries and a breaker around a dead socket; it is also refusing to throw away
fifteen good days because the sixteenth is empty.

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

#### The numbers have to add up

Two arithmetic constraints tie these settings together, and both are easy to
break by editing one value in isolation.

**Every attempt has to fit inside the total timeout.** Three attempts of 3s plus
1.5s of backoff is 10.5s, comfortably inside 12s. Widen the attempt timeout to
5s and the sum becomes 16.5s against a 15s ceiling — the third attempt starts
knowing it will be guillotined, and the caller waits for it anyway.

**The breaker's throughput gate has to be reachable.** `MinimumThroughput`
gates the failure ratio: below it the ratio is never evaluated. A cut-short
attempt is a cancellation rather than a failure, and Polly does not record
those — so an over-long budget both wastes time and starves the breaker of the
evidence it needs. With the numbers above a hung upstream produces six recorded
failures inside the 30s window against a gate of five, and the circuit opens.
With the 5s/15s pair it produced four, and under sequential traffic the breaker
never opened at all.

`ResilienceSettingsTests` holds both sums, because observing them for real would
cost half a minute of wall clock per run — which is exactly why nobody checks
them by hand.

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

A log still needs a floor and a ceiling, and they are deliberately different
numbers:

| Window       | Value  | What it governs                                    |
| ------------ | ------ | -------------------------------------------------- |
| `UsableFor`  | 3 days | How old a snapshot may be and still be **served**  |
| `RetainFor`  | 7 days | How long a snapshot is **kept** before it is swept |

`UsableFor` is no longer what stops the past being served — the projection drops
days a snapshot has outlived before anything leaves the service. What it stops
is the *future* being served from an old reading: a snapshot from last week
still has days left in it, and they were forecast by a week-old model run.
Serving something old is honest only while it is labelled old *and* still worth
believing.

Three days sits well inside the sixteen a snapshot carries, so there is room to
widen it without the rows ever having been thrown away. That is a judgement
about forecast quality rather than arithmetic, so it was left where it was
rather than moved as a side effect of a longer horizon.

`RetainFor` is the wider of the two so the serving window can be widened later
without the rows having already been thrown away. Each write sweeps its own
location's expired rows on the way out: equality on the key plus a range on the
timestamp is exactly the shape of the existing index, so the sweep costs one
index scan. It runs outside a transaction and never fails the write — the
snapshot is already committed, and a missed sweep simply happens on the next
one.

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
make test-integration  # real SQLite, real Postgres, real HTTP, real ASP.NET pipeline
make test-postgres     # only the container-backed Postgres tests
```

128 tests on the backend — 64 unit, 64 integration — in two layers that do
genuinely different jobs.

**Unit tests** cover the use cases through substituted ports. No network, no
database, no `Thread.Sleep` — `FakeTimeProvider` moves the clock, so testing a
24-hour cache retention window takes no wall-clock time at all. One of them
earns its place without touching a port at all: `ResilienceSettingsTests` asserts
the two arithmetic constraints the resilience numbers have to satisfy, against
the production defaults, so a plausible-looking edit to a timeout fails a test
rather than quietly spending latency on an attempt that cannot finish.

**Integration tests** run against real infrastructure:

- `EfForecastHistoryTests` — a real SQLite engine, schema built by running the
  migrations, so a broken migration fails here rather than on your machine.
- `CircuitBreakerTests` — a real WireMock HTTP server told to misbehave, at
  production's retry count and throughput gate so retries are shown counting
  toward tripping it.
- `RateLimitingTests` — limits turned down to single digits, checking the edge:
  the `429`, its `Retry-After`, its problem document, and that search and
  forecast do not share a budget.
- `WeatherEndpointsTests` — the real application through `WebApplicationFactory`,
  replacing only the upstream and the clock. Real routing, model binding,
  middleware, ProblemDetails and EF stay under test.
- `PostgresForecastHistoryTests` — a real Postgres 16 in a container, via
  Testcontainers. See below: this suite exists because the other ten could not
  catch what it catches.

**Why a second database suite.** SQLite proves the *logic* — the windows, the
ordering, the pruning. It cannot prove anything about the engine production runs,
and that gap was real: the Postgres migrations were versioned in this repository
and **executed by no test at all**. A broken one would have passed the whole suite
and failed on `docker compose up`, in front of whoever ran it first.

So this suite is deliberately not a mirror of the SQLite one. Re-proving the age
cap on a second engine buys little; what earns its place is what is
engine-specific and therefore invisible to SQLite:

- the Postgres migrations apply from an empty database and leave none pending
- `timestamp with time zone` accepts what the repository writes — Npgsql *refuses*
  a `DateTime` whose kind is unspecified, so that pairing either works or throws
- `DateOnly` landed as a real `date` column, not as text that sorts correctly by
  accident
- `ON DELETE CASCADE` exists in the actual catalogue. The prune uses
  `ExecuteDeleteAsync`, which never loads the child rows, so the database is what
  clears them — and a count on an engine that never had the cascade would pass
  either way
- snake_case naming actually applied, because Postgres folds unquoted identifiers
  to lower case and a PascalCase column needs quoting forever

Each test gets its own freshly migrated database inside one shared container, so
the migrations are exercised every time rather than once, and nothing leaks
between tests.

> **Without Docker these skip rather than fail.** The README promises the suite
> runs with nothing but the .NET 8 SDK, so `RequiresDockerFactAttribute` reports
> them as skipped when no daemon answers. "This machine has no Docker" and "this
> suite is broken" deserve different words.

> **On `UseInMemoryDatabase`:** it is not used here, and that decision paid for
> itself during development. `EfForecastHistoryTests` immediately failed with
> `SQLite does not support expressions of type 'DateTimeOffset' in ORDER BY
> clauses` — SQLite stores one as text with the offset appended, which does not
> sort. The EF InMemory provider is a list of objects in C#; it would have sorted
> that happily and carried the defect to production. A real relational engine
> stops you. A fake applauds.

---

## Code quality harness

Tests answer "does it work". They do not answer "is it any good". The second
half of that is a **local SonarQube**, run from this repository so every quality
number quoted here can be reproduced rather than trusted.

```bash
make sonar-up      # start SonarQube, wait for it, provision an analysis token
make sonar-scan    # analyse backend + front end, then print the report
make sonar-report  # print the report again without re-analysing
make sonar-down    # stop it, keeping the analysis history
make sonar-clean   # stop it and delete the volumes and the token
```

`make sonar-up` is idempotent — re-running it against a server that is already
up, already has its password changed and already holds a valid token does
nothing. The token lands in `quality/.sonar-token`, which is gitignored.

Test files are declared to SonarQube as tests rather than as sources, so the
figures below describe production code. Counting a suite as source inflates the
line count and lets test helpers dilute the very metrics being read.

It runs as its own compose project on port **9001**, separate from `make up`:
quality tooling has no business sharing a lifecycle with the service under test,
and `make clean` must never be able to wipe an analysis history.

Coverage is measured too, and both scans carry it: the backend collects
OpenCover through `coverlet` while the tests run inside the `begin`/`end`
sandwich, and the front end writes an lcov report with vitest's v8 provider
before the scanner container starts. Either report can be produced on its own
with `make test-coverage` and `make web-test-coverage`.

Four things about this are worth knowing before you run it:

- **The C# analyser only runs as an MSBuild pass.** `sonar-scan-api` is a
  `begin` → `dotnet build` → `dotnet test` → `end` sandwich, and the build is not
  optional: without it the scanner indexes the files and applies no C# rule at
  all. That build turns `TreatWarningsAsErrors` off, because the injected Sonar
  analysers raise warnings of their own and a scan that cannot compile reports
  nothing.
- **The .NET scanner must be pinned to `net8.0`.** `dotnet tool install
  dotnet-sonarscanner` on its own resolves an `osx-x64` apphost that demands a
  .NET 10 runtime, which fails outright on an Apple Silicon machine carrying
  only the .NET 8 SDK this project targets. `make sonar-tools` passes
  `--framework net8.0` and runs automatically as part of the scan.
- **The lcov property is `reportPaths`, not `reportsPaths`.** The C# one is
  `sonar.cs.opencover.reportsPaths` and the JavaScript one is
  `sonar.javascript.lcov.reportPaths`, and an unknown property is dropped
  without an error. Get it wrong and the run reports a flat 0% that looks
  exactly like a project with no tests; only `-X` says otherwise, at `DEBUG`.
- **`TestResults/` is wiped before each backend scan.** Every `dotnet test` run
  lands in a fresh GUID directory, so without the wipe the reports of older
  commits accumulate and the scanner unions them into a coverage figure that
  describes no revision that ever existed.

### What it found, and what it did not

| | Backend | Front end |
| --- | --- | --- |
| Lines of code | 1646 | 1363 |
| Bugs | 0 | 0 |
| Vulnerabilities | 0 | 0 |
| Duplication | 0.0% | 0.0% |
| Coverage | 77.4% | 74.9% |
| Code smells | 2 | 2 |
| Technical debt | 0 min | 25 min |
| Reliability / Security / Maintainability | A / A / A | A / A / A |

The security rating was a `C` when this harness first ran, on three counts of
rule S2068 — the localhost development passwords then sitting in
`appsettings.json`, `DatabaseOptions.cs` and `DesignTimeContextFactories.cs`.
Not a leak, and the rating overstated them, but the useful half of the finding
was real and they are gone: see **Configuration** above.

The honest reading of that table, though, is the reason the harness is
documented rather than just used: **rating A almost everywhere, and static
analysis still missed the two most serious defects in the codebase.** Both were
unbounded-growth problems in `EfForecastHistory` — a table nothing ever pruned,
and a fallback with no age cap. Neither has a syntactic signature. An analyser
matches shapes; it cannot reason about what a row means or how many of them
there will be by Tuesday.

The coverage column arrived late, and how it arrived is the same lesson from a
different angle. Both projects reported **0.0%** for as long as no report was
handed to the scanner — not "unknown", not a warning, a confident zero sitting
next to three A ratings while 184 tests passed on every run. A number a tool
prints is only worth what you know about where it came from.

It does catch what it is good at, including on work done here: extracting
`useCombobox` went in with a four-level nested ternary, and the scan put
cognitive complexity up by 24 and technical debt up by 20 minutes until that was
rewritten with guard clauses. A quality gate that only ever agrees with you is
not a gate.

**The four findings still open are open on purpose**, which is the other half of
taking a gate seriously. Two are micro-optimisations (`CA1859`) that ask for a
concrete `Dictionary` and array in place of the read-only interfaces those two
members deliberately return. One is `S8786`, super-linear backtracking in
`/\/+$/` — a regular expression applied once, at module load, to a base URL Vite
baked in at build time, where no attacker-controlled input can reach it. The
last is `S6845`, `tabIndex` on the carousel's `<ol>`: a scrollable region that
cannot take focus is unreachable by keyboard, so obeying that one would break
WCAG 2.1.1 to satisfy a linter. A gate at zero is a gate someone either silenced
or damaged working code to please.

Sonar is the floor, not the verdict.

---

## Configuration

Everything is overridable through `appsettings.json`, environment variables
(`Section__Key`) or `.env` for compose.

**No credential is committed.** The connection strings in `appsettings.json`
and in the options defaults name a host, a database and a user, and stop there;
the password arrives through the environment. The compose stack passes a whole
`Database__ConnectionString` built from `.env`, Development runs on SQLite where
the question never comes up, and the design-time factory that `dotnet ef` uses
reads the same `POSTGRES_*` variables — so `make migration-up` works off the
`.env` that is already there rather than off a default somebody would eventually
copy somewhere real.

| Setting                                    | Default                         |
| ------------------------------------------ | ------------------------------- |
| `Database__Provider`                       | `Postgres` (`Sqlite` in Development) |
| `Database__ConnectionString`               | local Postgres, **no password** |
| `Database__MigrateOnStartup`               | `true`                          |
| `WeatherProvider__BaseAddress`             | `https://api.open-meteo.com/`   |
| `WeatherProvider__Resilience__AttemptTimeout` | `00:00:03`                   |
| `WeatherProvider__Resilience__TotalTimeout`| `00:00:12`                      |
| `WeatherProvider__Resilience__MaxRetries`  | `2`                             |
| `WeatherProvider__Resilience__FailureRatio`| `0.5`                           |
| `WeatherProvider__Resilience__MinimumThroughput` | `5`                       |
| `WeatherProvider__Resilience__BreakDuration`| `00:00:15`                     |
| `RateLimiting__Enabled`                    | `true`                          |
| `RateLimiting__Window`                     | `00:01:00`                      |
| `RateLimiting__WeatherPermits`             | `60`                            |
| `RateLimiting__SearchPermits`              | `120`                           |
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
make web-test       # vitest
```

**One request primitive.** `useWeather` and `useLocationSearch` were the same
state machine written twice, so the shared part is now `useAsyncResource`: one
request in flight per key, aborted on the way out, and an answer proven to
belong to the question currently being asked. Status is derived during render
by comparing the settled result's key against the live one, which makes a stale
response structurally impossible to display — it simply does not match.

Two details in it are load-bearing and easy to lose in a rewrite:

- **The debounce lives inside**, delaying the request while the key stays live.
  Debouncing the value and passing the delayed one as the key would make a
  settled result match for the length of the delay, so the previous term's
  suggestions would read as current instead of as stale.
- **The revalidation counter is not part of the key.** Fold it in and asking for
  fresh data invalidates what is on screen, so a refresh blanks the page to load
  the very thing it is replacing.

`vitest` covers both, including the two races — a slow answer for a place
already left behind, and results that stop counting the moment the term moves
on. Verified to bite: replacing either guard with a null check fails exactly the
tests that describe it.

**How it talks to the API.** `VITE_API_BASE_URL`, defaulting to
`http://localhost:8080`. Vite inlines env vars at *build* time, not runtime — an
image built for one host cannot be repointed by restarting it with a different
variable — which is why `docker-compose.yml` passes it as a build argument.

**Copy.** The interface is in Spanish, and every user-facing string lives in
`src/i18n/es.json` — nowhere else. There is no language switcher, because the
product does not need one; the copy is centralised for two reasons that outlast
that question:

- Someone who is not a developer can read and correct the whole product's wording
  in one file, without opening a single `.tsx`.
- `CopyKey` is derived from the JSON, so a missing or misspelled key fails the
  **build** rather than rendering blank space in front of a user.

Wording avoids `tú`/`vos` imperatives entirely — El Salvador uses *voseo*,
Mexico and Colombia do not, and picking one would sound foreign to most of the
region. The action lives in the button label (`Reintentar`) instead of in the
sentence. Dates and numbers are formatted with an explicit `es-419` locale:
left to the browser's preference, a Spanish page renders `Mon 7 Sep` for anyone
whose machine is set to English, which reads worse than either language alone.

Errors travel from the API layer as a **kind** (`notFound`, `unavailable`,
`unreachable`, `unexpected`), never as a sentence. The backend's RFC 7807
`detail` is written in English for whoever is debugging, and rendering it
straight into the UI would leak the server's language into the product.

**What it does with provenance.** The degraded banner is not decoration. When the
backend reports `StaleCache` or `Historical`, the UI says so and shows when the
data was taken. Showing a three-hour-old forecast as if it were current would be
worse than showing nothing.

**The carousel.** Sixteen days do not fit a grid laid out for seven, and sixteen
stacked rows on a phone is a lot of scrolling to reach next Tuesday. The cards
sit on a scroll-snap track with a fixed width, so the last one on screen is half
cut off and the row visibly keeps going.

The scrolling is the browser's. A carousel built from transforms and an index
has to re-implement touch momentum, trackpad gestures, keyboard paging and the
focus ring following a card into view, and gets at least one of them wrong.
`useCarousel` adds only what native scrolling has no opinion about: whether
either direction is worth offering, and what one step means. It names no scroll
behaviour, so a `motion-safe:` rule in the stylesheet is what turns the glide
into a jump for anyone who asked their system to stop moving things.

The track is a focusable, labelled region — a scrollable area that cannot be
focused is unreachable by keyboard. The arrows are a pointer convenience: they
appear only when something is off screen and are gone below `sm`, where the
gesture is a swipe.

**The range control** is native radios under the styling, not buttons carrying
`aria-pressed`. The choice is exclusive and radios say so for free: arrow keys
move between them, the group is one tab stop, and a screen reader announces "2
of 3" rather than three unrelated toggles. It offers only ranges the data can
fill, so a degraded answer carrying ten days ends the list at ten.

The page asks the API for all sixteen days and slices them in the view. The
endpoint trims server-side, which is the right contract for a caller that wants
seven days and nothing more — but this page puts a range control in front of a
person, and a control that waits on the network to redraw days the browser
already holds is a spinner where there should be none. Sixteen days is about two
kilobytes of JSON; the whole horizon costs less than the round trip it saves.

**Responsive.** Mobile first. Icons are inline SVG drawn from theme tokens, so
they follow light and dark without a second asset set. `prefers-reduced-motion`
is respected.

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
no key and covers the whole sixteen-day horizon this service promises — verified
against the live API, which answers `Allowed range 0 to 16` past that ceiling.
`IWeatherProvider` is designed so a second adapter is a new class, not a
refactor; one that reached further would let `ForecastHorizon.MaximumDays` rise
without a single caller changing.

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

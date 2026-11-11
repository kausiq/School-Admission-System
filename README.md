# School Admission System

Seat-hold based online admission ("first come, first served"). Backend: .NET 8 + PostgreSQL (EF Core). Frontend: React 18 + Vite + Tailwind CSS.

## Run (only Docker needed)

```bash
docker compose up --build
```

- Frontend: http://localhost:3000
- Backend API: http://localhost:8080
- Hold duration: `HOLD_DURATION_SECONDS` (default 600), e.g. `HOLD_DURATION_SECONDS=60 docker compose up --build`
- Data persists in the `pgdata` volume (survives `docker compose down` / `up`; use `down -v` to wipe).

### Configuration (env vars)
| Where | Variable | Meaning |
|---|---|---|
| backend | `ConnectionStrings__Default` | PostgreSQL connection string |
| backend | `HOLD_DURATION_SECONDS` | hold lifetime, default 600 |
| frontend | `API_URL` | backend URL (injected at container start into `/config.js`); for `npm run dev` use `VITE_API_URL` |

## How to use the UI
1. "Class তৈরি (Admin)" → create a class (e.g. `Class 6 - Morning Shift`, 120 seats).
2. Enter a 17-digit birth-registration number in the box at the top (this is the student identity; no login).
3. "Class তালিকা" → **আবেদন করুন** (seat is held) → "আমার আবেদন" → **ভর্তি ফি দিন** (simulated payment) or **বাতিল**.

## Tests
Backend tests (5 tests incl. a concurrency test with 50 parallel requests; needs PostgreSQL, started by compose):
```bash
docker compose --profile test run --rm backend-tests
```
Locally: start Postgres, set `ConnectionStrings__Default`, then `cd backend/tests && dotnet test`.

## Design notes
- **No overselling:** applying runs in a transaction that locks the class row (`SELECT … FOR UPDATE`), so requests for a class are serialized — also across multiple backend processes (see ASSUMPTIONS.md).
- **Expiry without background jobs:** a `HELD` row with `expiresAt <= now` is treated as `EXPIRED` in every query/count, so seats return automatically and nobody has to call an API.
- A partial unique index (`class, student` where status in HELD/CONFIRMED) backs up the "one active application per class" rule.

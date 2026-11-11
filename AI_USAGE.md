# AI Usage

> Edit this file so it is **true for you** before submitting – especially the "Where AI was wrong" section.

## Tool and parts
- Tool: Claude (Anthropic) – used to draft the first version of backend (minimal API, EF Core model, locking logic), React UI, Dockerfiles/compose, tests and docs.
- I reviewed/ran everything myself and adjusted: <write what you changed>.

## Where AI was wrong / what I had to catch
<Fill with real examples from your run, e.g. anything that failed to compile, a wrong status code, a locking/expiry edge case, Docker/compose issue …>

## How I verified
- Ran `docker compose up --build`, exercised every screen.
- Ran backend tests (incl. 50-parallel-request concurrency test), checked `heldSeats + confirmedSeats <= totalSeats`.
- Restarted with `docker compose down` / `up` and confirmed data persisted.

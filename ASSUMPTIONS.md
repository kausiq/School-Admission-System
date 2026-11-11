# Assumptions

1. **Seat accounting:** `heldSeats` counts only unexpired HELD rows; `confirmedSeats` counts CONFIRMED; `availableSeats = total - held - confirmed`. Expiry is evaluated lazily by comparing `expiresAt` with the current time, so no cron/background worker is needed and seats free up exactly at expiry. *Why:* simplest design that is correct even with several backend processes.
2. **Concurrency (and bonus – two processes, one DB):** every apply runs in a transaction with `SELECT … FOR UPDATE` on the class row, then counts active holds and inserts. Locking is done by PostgreSQL, not in app memory, so it holds across processes. Confirm/cancel lock the hold row. Schema creation is guarded by a PostgreSQL advisory lock so two processes starting together don't race.
3. **"One active application per student per class":** active = unexpired HELD or CONFIRMED. After EXPIRED/CANCELLED the student may apply again. Enforced in code and by a partial unique index.
4. **Error codes:** invalid/missing `X-Student-Id` (not 17 digits) or bad body → 400; unknown class/hold → 404; confirming/cancelling someone else's hold → 403; no seats, duplicate application, confirming an expired/cancelled hold, cancelling a confirmed hold → 409. Errors are JSON `{ "error": "<Bangla message>" }` (extra field, allowed by the contract).
5. **Confirm is idempotent:** repeated confirm on a CONFIRMED hold returns 200 with the same body; the count is increased once. Cancel on an already CANCELLED hold returns 204.
6. **A confirmed admission cannot be cancelled** (409) — fee is paid; refunds are out of scope.
7. **GET /holds/{id}** is public (no header) as in the spec, so it returns only holdId/classId/status/expiresAt (no student id).
8. **Identity:** the 17-digit number is trusted as-is (no login, per brief). Saved in `localStorage` so a refresh/browser restart keeps the student's applications. Admin screen is open to everyone (no auth in brief).
9. **Class creation:** name 1–100 chars, seats 1–100000. Duplicate class names are allowed.
10. **Payment** is simulated: clicking "ভর্তি ফি দিন" calls confirm.
11. **Live updates:** polling every 3 s (simple, robust) instead of WebSockets; the countdown uses the server `Date` header to correct local clock skew, and a hold flips to EXPIRED on screen exactly at `expiresAt` without a reload.
12. **CORS** is open (`*`) because the API is called from another origin (localhost:3000 → 8080).
13. **ID format:** `c_<12 hex>` / `h_<12 hex>`.

# AI Usage

## Tool Used
Claude (Anthropic), via the Claude.ai chat interface.

## How I Used AI
I used Claude as a development assistant for initial implementation and documentation drafts.

- **Backend (.NET 8 + PostgreSQL):** Drafted eight Minimal API endpoints, EF Core models, transaction handling, row-level locking, lazy expiry, and error handling.
- **Frontend (React + Vite):** Generated initial UI components, class listings, application management, countdown timers, polling, and error states. Used Tailwind CSS for styling.
- **Docker:** Drafted Dockerfiles and Docker Compose configuration, including PostgreSQL health checks and test profiles.
- **Testing:** Generated five backend tests, including a concurrency test with 50 parallel requests against a 10-seat class.
- **Documentation:** Drafted README.md and ASSUMPTIONS.md, which I reviewed and edited for technical accuracy and consistency with the implementation.
- **Requirements:** Provided the extracted PDF specification to Claude as the implementation reference.

## Where AI Was Wrong and How I Caught It

1. **Incorrect error handling:** Malformed JSON returned HTTP 500 instead of 400. My automated test caught this, and I updated the exception handler to handle `BadHttpRequestException` and `JsonException` correctly.
2. **Untested generated code:** AI could not restore NuGet packages or compile the backend. I built and tested everything independently using Docker.
3. **Stale Docker images:** Tests continued using an outdated image after code changes. I resolved this by using `--build` when running the test container.
4. **UI and edge cases:** I manually checked countdown accuracy, expired application states, page refresh behavior, and backend-unavailable error handling.

## How I Verified the Result

- Ran all five backend tests successfully, covering concurrency, duplicate holds, idempotent confirmation, expiry, and cancellation.
- Manually tested class creation, applications, countdowns, payment confirmation, cancellation, and expiration.
- Verified data persistence after restarting Docker containers.
- Reviewed the PostgreSQL locking and lazy-expiry implementation to understand and validate its concurrency behavior.

## Final Takeaway

I used AI to accelerate development, not replace engineering judgment. I independently reviewed the implementation, identified and fixed issues, ran automated and manual tests, and verified the final system against the requirements.
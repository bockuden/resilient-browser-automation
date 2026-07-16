# ADR 0001: Use FastAPI for the deterministic browser target

- Status: accepted
- Date: 2026-07-16

## Context

The automation worker needs repeatable integration and end-to-end tests without
depending on third-party websites. The target must simulate browser-specific
behavior and failures while keeping the portfolio's main focus on C#.

## Decision

Use a small Python FastAPI service as the test stand and run it through Docker
Compose. Scenario behavior is selected by query parameters, and request counters
are isolated by `run_id`.

## Consequences

- Tests are deterministic and can run locally or in CI without external sites.
- The different language boundary demonstrates a real service integration.
- Python remains supporting infrastructure; production orchestration, browser
  control, persistence, resilience, and observability remain in C#.
- In-memory counters are sufficient for a single-container test stand. The
  service is not intended for horizontal scaling or production use.


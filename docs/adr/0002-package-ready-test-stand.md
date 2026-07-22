# ADR 0002: Keep the FastAPI stand independently distributable

- Status: superseded by [ADR 0003](0003-extract-fastapi-test-stand.md)
- Date: 2026-07-16

## Context

The deterministic target is useful beyond this repository. Other automation
developers should be able to exercise retry, pagination, authentication, DOM
drift, delays, and duplicate handling without cloning or building the C# worker.

## Decision

Give `test-stand` its own `pyproject.toml`, semantic version, dependency list,
CLI entry point, tests, README, Dockerfile, and a non-generic import package
named `resilient_automation_test_stand`. Keep its HTTP surface independent of
C# implementation details. During early development it remains in this monorepo
so worker and target contracts can evolve together.

Extract it to a separate repository when either of these becomes true:

- another project consumes a released stand version;
- the stand needs an independent release cadence or maintainer;
- compatibility with Selenium, Java, Node.js, or other workers becomes a goal.

## Consequences

- Developers can use a Python virtual environment or a container.
- A future repository split will be packaging work, not an application rewrite.
- Scenario names and response shapes become a versioned public contract.
- Changes to endpoints require compatibility tests and release notes.

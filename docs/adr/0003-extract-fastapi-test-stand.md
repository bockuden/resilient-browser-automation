# ADR 0003: Extract the FastAPI test stand

- Status: accepted
- Date: 2026-07-22

## Context

The deterministic FastAPI target reached an independently distributable
package and needed its own API contract, CI, image publication, and release
cadence. Keeping its source in the worker repository coupled Python validation
to C# changes and made consumers build infrastructure they did not own.

## Decision

Maintain the stand at
`https://github.com/bockuden/resilient-automation-test-stand` with an independent
release cycle. Contract version `0.1.0` is published as:

`ghcr.io/bockuden/resilient-automation-test-stand:0.1.0`

This repository keeps the Compose service name `demo-site` but consumes that
versioned image for both the browser target and the reporting helper. The stand
repository owns Python tests, package builds, its OpenAPI snapshot, Docker
validation, and releases. This repository owns worker integration tests.

Update the Compose image version only in an intentional worker change after the
new stand release succeeds independently. Run the complete browser E2E suite
before accepting that change. Do not consume `latest` in worker validation.

## Compatibility update: stand 0.4.0

On 2026-07-24, C# compatibility gate D1 validated the independently published
stand version `0.4.0`. Compose now consumes:

`ghcr.io/bockuden/resilient-automation-test-stand:0.4.0`

The validation covered the full Compose demo plus explicit DOM-change and
protected-login Chromium scenarios. The recorded result is
[`stand-0.4.0-compatibility.md`](../stand-0.4.0-compatibility.md). This is a
consumer-evidence commit, not a C# `v1.0.0` release.

## Consequences

- The C# repository no longer contains or tests Python source.
- C# CI proves compatibility with one explicit stand contract version.
- Stand and worker releases can proceed independently.
- Rollback is a Compose version change rather than restoration of copied source.
- Scenario or response changes require coordination through released versions.

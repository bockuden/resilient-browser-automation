# Stand 1.0.0rc1 C# compatibility evidence

- Date: 2026-07-25
- Consumer repository: `bockuden/resilient-browser-automation`
- Stand source and prerelease: `bockuden/resilient-automation-test-stand` /
  `v1.0.0rc1`
- PyPI package: `resilient-automation-test-stand==1.0.0rc1`
- Compose image: `ghcr.io/bockuden/resilient-automation-test-stand:1.0.0rc1`
- Pulled image digest:
  `sha256:9bc6189c19a521021179432c450fc8846c8b6dfc7bb14bbd78e1e022e76e4682`

## Result

The C# worker is compatible with the exactly published `1.0.0rc1` stand. This
is prerelease consumer evidence only; it does not create a C# `v1.0.0` release.

## Executed browser checks

1. Separate temporary Python 3.13 environments installed both the PyPI wheel
   and sdist for `resilient-automation-test-stand==1.0.0rc1`; both installed
   CLI commands completed successfully.
2. `./eng/demo-compose.ps1` pulled the exact GHCR tag, built the worker, and
   completed the full Compose browser E2E in 269 seconds. It verified successful
   extraction, idempotent duplicate delivery, transient `503` recovery, natural
   catalog end, duplicate items, bounded concurrency, checkpoint failure/resume,
   graceful cancellation, and permanent-failure artifacts. The permanent and
   resume scenarios intentionally returned exit code `3`; cancellation returned
   exit code `4`; the script completed successfully after checking those expected
   outcomes and stopping all Compose services.
3. Explicit Chromium jobs against the same exact Compose service verified
   `scenario=dom-change` and the protected-login flow. Both completed with exit
   code `0` and checkpoint page `4`.

The worker consumes only the documented HTTP/browser contract: URLs, status
codes, retry behavior, login flow, pagination, and rendered data attributes. No
validation step reads or depends on Python implementation details.

## Pinning rule

Compose is intentionally pinned to exact `1.0.0rc1` for D2. `latest` is never
used for compatibility validation. D3 will replace this pin with exact `1.0.0`
only after GA.

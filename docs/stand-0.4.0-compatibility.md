# Stand 0.4.0 C# compatibility evidence

- Date: 2026-07-24
- Consumer repository: `bockuden/resilient-browser-automation`
- Stand source and release: `bockuden/resilient-automation-test-stand` / `v0.4.0`
- PyPI package: `resilient-automation-test-stand==0.4.0` (wheel and sdist
  available)
- Compose image: `ghcr.io/bockuden/resilient-automation-test-stand:0.4.0`
- Pulled image digest:
  `sha256:7addbaca4ab2054efa08723006c3f55969afb7f37917094e8c88695fa855c33f`

## Result

The C# worker is compatible with the published stand `0.4.0`. This record is
consumer evidence only; it does not create a C# `v1.0.0` release.

## Executed browser checks

1. A fresh Python 3.13 virtual environment installed
   `resilient-automation-test-stand==0.4.0` from PyPI; its
   `automation-test-stand --help` CLI command completed successfully.
2. `./eng/demo-compose.ps1` pulled the exact GHCR tag, built the worker, and
   completed the Compose demo.
3. The demo verified successful extraction, idempotent duplicate delivery,
   transient `503` retry recovery, natural catalog end, duplicate items,
   bounded concurrency, checkpoint failure/resume, graceful cancellation, and
   permanent failure evidence.
4. The final persisted state contained 9 completed jobs, 1 cancelled job, 1
   failed job, and 117 catalog items. Cancellation returned exit code `4`; the
   intentional permanent failure returned exit code `3` and wrote screenshot,
   HTML, trace, and error metadata.
5. Explicit Chromium jobs against the same Compose service verified
   `scenario=dom-change` and the protected-login flow. Both completed with exit
   code `0` and checkpoint page `4`.

## Pinning rule

The consumer uses the exact `0.4.0` image tag. Compatibility validation never
uses `latest`; any later stand version requires a separate reviewed Compose
change and this E2E gate again.

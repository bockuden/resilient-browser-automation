# Test Stand compatibility matrix

The worker is a reference consumer, not part of the Test Stand distribution.
It consumes only the released HTTP/browser contract and pins an exact OCI image
tag in [`docker-compose.yml`](../docker-compose.yml). `latest` is never used by
the local demo or the `browser-e2e` GitHub Actions job.

| Worker revision | Exact Test Stand release | Validation date | Evidence / CI | Passed challenge coverage |
| --- | --- | --- | --- | --- |
| [`v1.0.0`](https://github.com/bockuden/resilient-browser-automation/releases/tag/v1.0.0) at `01854c8` | `1.1.3` / `ghcr.io/bockuden/resilient-automation-test-stand:1.1.3` | 2026-07-28 | Full local Compose Chromium E2E, 172 s; [release CI run 30397596555](https://github.com/bockuden/resilient-browser-automation/actions/runs/30397596555) | L1 happy-path pagination; L2 transient `503` + `Retry-After`; L3 protected login, DOM change and duplicates; bonus checkpoint resume, cancellation and terminal evidence |
| `main` at `85b8435` | `1.0.0` / `ghcr.io/bockuden/resilient-automation-test-stand:1.0.0` | 2026-07-26 | [Compatibility evidence](stand-1.0.0-compatibility.md); [GitHub Actions run 30196994593](https://github.com/bockuden/resilient-browser-automation/actions/runs/30196994593) | Happy path, transient and permanent failures, duplicates, concurrency, resume, cancellation and natural end |

The current evidence for `1.1.3` is recorded in
[stand-1.1.3-compatibility.md](stand-1.1.3-compatibility.md). A Test Stand
release is not automatically adopted merely because it is newer: changing the
pin requires a reviewed Compose diff and another complete consumer validation.

## Contract-change rule

For a Test Stand public-contract change, follow its
[compatibility policy](https://github.com/bockuden/resilient-automation-test-stand/blob/main/docs/compatibility.md):

1. add or update a behavior test;
2. review the OpenAPI snapshot when the HTTP/OpenAPI surface changes;
3. add a changelog and compatibility note;
4. make an explicit SemVer decision; and
5. complete a C# compatibility review before a stable release.

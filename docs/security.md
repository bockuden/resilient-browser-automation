# Security

This project intentionally avoids anti-bot, credential harvesting, proxy
rotation, CAPTCHA bypass, or automation against third-party websites. The demo
target is local and deterministic.

## Secrets

- Demo credentials are local test data: `demo` / `automation`.
- Credentials are configured through typed settings and are not written to logs.
- Failure metadata redacts common secret names such as password, token,
  authorization, cookie, and secret.
- Generated databases, browser downloads, traces, screenshots, and artifacts
  are ignored by Git.

## Browser Artifacts

HTML and JSON metadata can be redacted before writing. Screenshots and traces
are evidence, so they may contain visible page data from the target. The demo
target is the pinned FastAPI stand image running locally; for real systems,
route artifacts to restricted storage and apply retention controls.

## CI Permissions

GitHub Actions use read-only repository permissions:

```yaml
permissions:
  contents: read
```

The workflow caches NuGet packages only. Browser binaries, profiles, traces,
databases, and generated artifacts are not cached.

## Containers

The worker Docker image runs as the non-root `app` user. SQLite and artifacts
are written through a mounted `/data` volume. Compose pins the external test
stand to version `1.0.0rc1`; version updates require a reviewed Compose change and
a complete browser E2E run.

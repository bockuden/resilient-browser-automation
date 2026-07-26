# Stand 1.0.0 C# compatibility evidence

## Scope

- Date: 2026-07-26
- Stand release: [`v1.0.0`](https://github.com/bockuden/resilient-automation-test-stand/releases/tag/v1.0.0)
- Compose image: `ghcr.io/bockuden/resilient-automation-test-stand:1.0.0`
- Resolved image digest:
  `sha256:316f6dd7dc84bb5a66e5dae7492eb7284731e575ced81a7281ad2857ef780eca`

The worker consumes the stand only through its released HTTP/browser contract.
No Python source, module names, or implementation details are used. `latest` is
not used for validation.

## Browser E2E

`eng/demo-compose.ps1` completed successfully against the exact GA image in
376 seconds. It rebuilt the C# worker and exercised Chromium extraction for:

- successful extraction of 20 items over four pages and a duplicate delivery
  that remained idempotent;
- transient HTTP 503 recovery, natural catalog end, duplicate-item handling,
  and bounded concurrency;
- durable checkpoint failure after page 2 followed by resume from page 3;
- graceful cancellation with worker exit code `4`;
- permanent HTTP 500 failure with redacted error JSON, HTML, screenshot, and
  trace artifacts.

The final report contained 9 completed jobs, 1 cancelled job, 1 failed job,
117 catalog items, and a checkpoint of page 4 for the resumed job. Expected
failure exit code `3` was observed for the resume-failure and permanent-failure
scenarios.

## Local CI-equivalent checks

The following completed successfully in Release configuration:

```text
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -m:1
dotnet test --configuration Release --no-restore --no-build --verbosity minimal -m:1
```

The test run passed 16 unit tests and 7 integration tests. The Compose stack was
stopped after the demo.

## Clean-clone validation

A fresh clone of commit `c363b42` used the externally supplied SDK
`10.0.302` through `RESILIENT_BROWSER_AUTOMATION_DOTNET_ROOT`; no SDK files were
copied into the repository. Restore, Release build, the same 16 unit and 7
integration tests, and `docker compose config --quiet` all completed
successfully with a clean Git status.

## Release gate

This evidence completes the validation portion of D3 and validates the exact GA
stand for C# release preparation. Creating and pushing the C# `v1.0.0` tag and
publishing cross-linked GitHub release notes remain explicit publication actions.

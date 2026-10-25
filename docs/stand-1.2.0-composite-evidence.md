# Test Stand 1.2.0 composite compatibility evidence

## Scope

- Validation date: 2026-10-25
- Exact Test Stand image: `ghcr.io/bockuden/resilient-automation-test-stand:1.2.0`
- Compatibility run: [GitHub Actions run 40197301570](https://github.com/bockuden/resilient-browser-automation/actions/runs/40197301570)
- Reproducible input: [`jobs.playwright.composite-events.jsonl`](../samples/jobs.playwright.composite-events.jsonl)

## Ordered event sequence

Both jobs use `scenario=success`, four pages, protected access, and the same
ordered `events_json` configuration. They use independent fresh run IDs:
`composite-playwright-a` and `composite-playwright-b`.

1. Page 2 returns one HTTP 503 with `Retry-After: 1`; the worker retries and
   continues the job.
2. The session expires before page 3; the browser signs in again and resumes
   the requested page.
3. The page 3 API response is delayed by 350 ms.
4. Page 3 repeats the final item from page 2 (`item-010`).
5. The page 3 item selector changes from `catalog-item` to
   `catalog-item-changed`; the worker resolves the item cards through its
   semantic fallback.

## Verified outcome

The Compose workflow completed both jobs against the exact image. Their final
catalog rows and checkpoints matched across the two run IDs. Each job completed
through page 4, retained one job attempt, and produced the same 19 unique item
rows: items 1–5 on page 1, items 6–9 on page 2, item 10 and items 12–15 on page
3, and items 16–20 on page 4. The duplicate `item-010` was stored once and its
source page was updated to page 3.

The two HTTP 503 responses scheduled exactly two retries in total. A SQLite
audit trigger recorded each page checkpoint once per job: pages 1, 2, 3, and 4.
The workflow also verified that both independent run IDs produced identical
results.

# Failure Matrix

| Failure | Classification | Worker action | Evidence |
| --- | --- | --- | --- |
| Invalid JSON or missing `jobId` | Permanent input error | Reject before claim | Structured warning |
| Invalid URL or `maxPages` outside 1-100 | Permanent input error | Reject before browser startup | Structured warning |
| Completed `jobId` delivered again | Expected duplicate delivery | Return completed result, do not open browser | Final JSON summary |
| HTTP 503, 502, 504, 429, 408 | Transient | Retry current page with bounded backoff | Retry log event `2001` |
| `Retry-After` fits remaining job budget | Transient with server pacing | Use server delay | Retry log event `2001` |
| Permanent HTTP 500 | Terminal browser failure | Mark failed | `error.json`, `page.html`, `screenshot.png`, `trace.zip` |
| Missing stable locator | Terminal contract drift | Mark failed | DOM snapshot and screenshot |
| Duplicate catalog item IDs | Expected data condition | Upsert by `(jobId, externalId)` | SQLite row count remains stable |
| Process interruption after checkpoint | Recoverable | Resume after last durable page | Checkpoint row |
| Cancellation | Terminal non-success | Mark cancelled | Structured event `1002` |
| Artifact capture failure | Diagnostics failure | Preserve original exception | Error log for original failure |

The deterministic FastAPI stand exercises these cases without depending on a
public website or live credentials.

# Python async ownership and blocking-call review: 2026-10-15

This first standalone pass implements `python/async-owned-calls-review-v1`. **Async/Blocking Usage and Error Handling remain unscored.** Method Complexity remains the sole scored Python dimension. No package version, evidence-v3 dimension contract, skill instruction, or published artifact changes in this milestone.

## Pinned observations

| Selected production source | Selected functions | Async functions | Synchronous owners excluded | Direct async-body calls | Thread-helper calls | Wrapper constructions / immediate invocations | Unresolved direct calls | Deferred generator calls | Blocking catalogue candidates |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Starlette | 508 | 135 | 373 | 717 | 11 | 0 / 0 | 585 | 2 | 0 |
| Django | 9,748 | 266 | 9,482 | 818 | 0 | 42 / 40 | 644 | 6 | 0 |

Other import-matched direct calls: Starlette 121; Django 92. Wrapper construction and invocation are separate expressions, not two independent offloaded operations. The deferred column is separate from direct-call totals. Counts are source syntax, not execution frequency, duration, or handling-success rates.

The narrow catalogue has no direct blocking candidates in these selections. That is not a safe-async verdict: receiver methods, local helpers, indirect wrappers, and most other calls are unresolved. In particular, the reviewer cannot classify arbitrary file/database methods or CPU work by name. These corpus runs validate ownership and reporting behavior, not blocking-detection recall. Positive catalogue detection is exercised by synthetic fixtures.

## Source review

The [selection manifest](../../calibration/corpus/python-selection.json) pins Starlette `96d5023c55b29224edb8d684868c49f681d079a9` and Django `dab0a5c47f159eb58472110dcbc10bd4a9b01adb`. No target install, test execution, or runtime profiling was performed.

- [Starlette `BackgroundTask.__call__`](https://github.com/encode/starlette/blob/96d5023c55b29224edb8d684868c49f681d079a9/starlette/background.py#L19): async callbacks are awaited; the synchronous branch uses `run_in_threadpool`. The helper is recognized, while `self.func` remains unresolved. A callback reference is not counted as an invoked callback body.
- [Starlette `UploadFile.read`](https://github.com/encode/starlette/blob/96d5023c55b29224edb8d684868c49f681d079a9/starlette/datastructures.py#L469): the in-memory path calls `self.file.read`; the other path passes that method to `run_in_threadpool`. The direct receiver call stays unresolved, avoiding a blanket penalty for `.read` that would ignore memory-backed behavior.
- [Starlette `run_in_threadpool`](https://github.com/encode/starlette/blob/96d5023c55b29224edb8d684868c49f681d079a9/starlette/concurrency.py#L32): constructs a `functools.partial` and passes it to `anyio.to_thread.run_sync`. The imported helper is recognized; the rebound `func` parameter remains unresolved. No callable dataflow is invented.
- [Django `acheck_password`](https://github.com/django/django/blob/dab0a5c47f159eb58472110dcbc10bd4a9b01adb/django/contrib/auth/hashers.py#L89): `sync_to_async(verify_password, thread_sensitive=False)(...)` yields distinct wrapper-construction and invocation records. The `setter` callback stays unresolved. These records describe syntax, not measured hashing performance.
- [Django timing-attack mitigation](https://github.com/django/django/blob/dab0a5c47f159eb58472110dcbc10bd4a9b01adb/django/contrib/auth/__init__.py#L402): dynamically obtains `set_password`, then passes it to `sync_to_async`. The wrapper import is recognized, but the callback's implementation and business rationale are not graded.

Unresolved reasons across direct and deferred calls are recorded in the machine-readable report. The conservative reflective/cross-scope veto accounts for 1 Starlette call and 106 Django calls. Those totals describe withheld matches, not additional problems.

## Verification and provenance

- 163 Python tests pass without skips, including 23 async-review tests covering import aliases and shadowing, conditional/relative/star imports, cross-scope writes, reflective code, nested owners, callbacks versus eager arguments, wrapper identities, deferred generators, documented intentional blocking, selection separation, stale/tampered facts, budgets, and fresh-output CLI behavior.
- Seven shared source-facts conformance tests pass.
- Both corpus runs verify clean pinned checkouts, exact source/function/declaration baselines, unchanged Method Complexity (Starlette 7.1; Django 5.6), and exact existing handler/context/transfer/operation rows plus summaries. No target source is changed.
- The prior packaging milestone fixed start-only call-identity collisions. Its historical standalone operation hashes are therefore not suitable as current baselines. The new [prior-evidence baseline](../../calibration/baselines/python-async-prior-evidence.json) records corrected full-extent operation-row hashes extracted from archived adapter 0.11.0 evidence only after verifying the archived files against the committed package record's SHA-256 values. Earlier handler/context/transfer hashes remain valid.

Reproduce with Python 3.12:

```powershell
py -3.12 analyzers/python/async_usage_corpus.py --repos E:/repos --output TestResults/python-async-review-new
```

Full source facts and reviewer reports are under `TestResults/python-async-usage-2026-10-15-final/`. The committed [JSON record](python-async-usage-2026-10-15.json) preserves invocation IDs, policy/configuration/implementation hashes, source/function/async-row hashes, summaries, unresolved reasons, and examples.

Next delivery boundary: checkpoint this standalone evidence, then package and teach the skill to present it with the same unresolved and unscored limits. Broader receiver or wrapper resolution needs additional reviewed fixtures and corpus evidence before any scoring discussion.

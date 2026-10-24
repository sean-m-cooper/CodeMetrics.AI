# Python callback adapter study: 2026-10-19

The standalone policy `python/async-callback-adapters-review-v1` adds partial construction/reference context and conservative `sync_to_async` decorator links. **Async/Blocking Usage and Error Handling remain unscored.** Method Complexity remains Python's only scored dimension. Packaged adapter 0.13.0, its validation contract, and the companion skill are unchanged in this milestone.

## Pinned observations

| Production selection | Async functions | Direct calls | Partial constructions | Stored/inline partial use links | Decorator use links | Method Complexity |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Starlette | 135 | 717 | 9 | 0 | 0 | 7.1 |
| Django | 266 | 818 | 2 | 0 | 0 | 5.6 |

The original unresolved direct import-call counts remain 585 and 644, respectively. Deferred generator bodies retain two calls in Starlette and six in Django, with no adapter matches. Original async calls, context rows and summaries, source facts, Method Complexity, and error-handling evidence match the frozen baselines. Both pinned checkouts remain clean. No target code was installed, imported, built, or executed.

These counts describe source expressions, not execution frequency, successful partial construction, offloaded operations, or runtime performance. The linked-use columns cover recognized callees/helper callbacks, not arbitrary argument passing. None of the pinned selections exercises a positive stored/inline partial use link or a supported decorator link; synthetic fixtures cover those paths. This study does not establish their real-world recall.

## Reviewed examples

- **Starlette `concurrency.py:33`, `run_in_threadpool`:** `func = functools.partial(func, *args, **kwargs)` exposes construction, unpacked arguments, and an unresolved callback. The parameter plus reassignment vetoes a later stored-partial link. `anyio.to_thread.run_sync(func)` remains original helper evidence with an unresolved callback.
- **Starlette `middleware/cors.py:148`, `CORSMiddleware.simple_response`:** partial construction records the `self.send` expression and bound keyword shapes. Receiver identity and behavior remain unresolved. Rebinding the `send` parameter is not followed.
- **Django `test/client.py:1446`, `AsyncClient.request`:** `partial(store_rendered_templates, data)` links the callback expression to the selected same-file definition. It is one source-definition reference at construction. Passing the resulting value to `signals.template_rendered.connect` is outside the recognized helper catalogue, so no later partial-use link is claimed. The corpus deliberately includes shipped `django/test/**` helpers as production through its explicit population override.
- **Django `test/client.py:1465`:** `partial(self._parse_json, response)` records construction while leaving receiver behavior unresolved. Assignment to `response.json` is not a supported single-name stored binding.

The real-source result is useful even with zero later-use links: it separates known partial-construction syntax from unknown callable behavior without lowering unresolved totals or inventing offload success. One of Django's two construction callback expressions has a local-definition link; all nine Starlette callback expressions and Django's receiver callback remain unresolved.

## Validation

- 205 Python tests passed without skips, including 22 new adapter tests. The first run with the base interpreter skipped four existing coverage integration tests; the full rerun used the existing coverage-enabled interpreter and passed all tests.
- Seven shared source-facts conformance tests passed.
- Positive fixtures cover aliased partial constructors, bound expressions and unpacking, inline invocation/helper callbacks, same-owner stored bindings, existing stored wrappers, bare/factory decorators, literal `thread_sensitive` options, lexical decorator scope, and nested definitions.
- Negative fixtures cover ambiguous, reassigned, deleted, conditional, cross-owner, reflected, shadowed, deferred, receiver, late, decorated-async, unknown/stacked decorator, unsupported-argument, and unselected-definition cases. Eager factory calls and lambda ownership stay separate.
- Failure checks cover changed source/selection, tampered original reports, parse failures, collection failures, owner/call budgets, invalid run IDs, fresh deterministic observations, and no overwrite. Existing reports are not mutated.
- The corpus runner first reruns the prior context study and checks its frozen results, then checks the new report against the same source snapshot. Both clean pinned revisions are checked after collection.

Reproduce from the repository root:

```powershell
py -3.12 analyzers/python/async_adapters_corpus.py --repos E:/repos --output TestResults/python-adapters-new
& E:/temp/codemetrics-python-coverage/Scripts/python.exe -m unittest discover -s analyzers/python/tests
node --test shared/source-facts/conformance.test.mjs
```

The [machine-readable record](python-async-adapters-2026-10-19.json) contains pinned revisions, invocation identities, source and evidence hashes, implementation hash, summaries, and bounded examples. Full reports are under `TestResults/python-async-adapters-2026-10-19/`. The [policy guide](../../../analyzers/python/ASYNC_ADAPTERS.md) describes supported syntax and limits. Packaging and companion-skill integration remain the next delivery step; no release or score change is included here.

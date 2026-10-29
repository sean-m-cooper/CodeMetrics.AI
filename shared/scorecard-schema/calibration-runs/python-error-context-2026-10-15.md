# Python exception-context study: 2026-10-15

Policy: `python/exception-context-patterns-v1`. This standalone pass adds bounded try/else/finally structure, handler and following statements, surrounding prose, and conservative patterns to the existing handler observations. **Error Handling remains unscored.** Neither pattern recognition nor unresolved status is a judgment of correctness.

The [machine-readable record](python-error-context-2026-10-15.json) binds the results to pinned revisions, source/function/handler digests, implementation digest, run/audit IDs, and configuration fingerprints. The [policy guide](../../../analyzers/python/ERROR_HANDLING_CONTEXT.md) defines pattern precedence, exclusions, provenance, and limitations.

| Project | Handlers | Recognized shapes | Unresolved |
| --- | ---: | ---: | ---: |
| Requests | 66 | 17 | 49 |
| Starlette | 62 | 5 | 57 |
| HTTPie | 69 | 9 | 60 |
| Django | 1,267 | 162 | 1,105 |
| Total | 1,464 | 193 | 1,271 |

These are analyzer pattern counts, not handled/unhandled populations. All files use the recorded production-candidate selection; this is not proof of runtime reachability. Unresolved reasons can overlap and must not be summed as distinct failures.

| Primary pattern | Requests | Starlette | HTTPie | Django |
| --- | ---: | ---: | ---: | ---: |
| Local documentation on a no-op | 2 | 0 | 5 | 41 |
| Direct literal fallback return | 10 | 3 | 3 | 107 |
| Guarded import with literal default | 5 | 1 | 1 | 5 |
| Direct bare rethrow | 0 | 0 | 0 | 6 |
| No-op followed by literal return | 0 | 1 | 0 | 3 |
| Guarded import with inert handler | 0 | 0 | 0 | 0 |

The inert import-guard pattern has positive and negative synthetic tests but no matches in these four selections under this policy. Do not broaden it merely to increase recognition. The literal-default variant was added after inspecting Requests' direct `ImportError` branches that assign `None` to the imported version variable; it requires that exact import-binding relationship and excludes unrelated or computed assignments.

## Source-reviewed cases

- **Requests, `src/requests/__init__.py:51` and `:56`:** direct imports of character-detection version values fall back to `None` on import failure. Both match the imported-binding/literal-default shape. This describes the branch without proving dependency optionality or compatibility.
- **Starlette, `starlette/schemas.py:14`:** `import yaml` falls back to `yaml = None` on `ModuleNotFoundError`. The `pragma` and `type: ignore` comments remain tool directives; recognition comes from source structure, not prose credit.
- **HTTPie, `httpie/context.py:13`:** importing `curses` falls back to `None`, with an inline note about builds without curses. The recognized assignment is to the imported binding.
- **Django, `django/contrib/gis/shortcuts.py:11`:** the guarded NumPy import uses `False` as its explicit default. The preceding comment is captured as context; the literal need not be `None` to describe the same bounded shape.
- **Starlette, `starlette/staticfiles.py:228`:** missing conditional-request headers fall through to a direct `return False` at line 231. The protected body and same-suite continuation make this visible; no claim is made that every runtime path reaches that return.
- **Django, `django/contrib/sessions/backends/base.py:378` and `:392`:** removing custom expiry tolerates an absent key, then returns within the same branch. Both synchronous and asynchronous variants preserve their function boundary and the preceding comment about clearing custom expiry.
- **Django, `django/contrib/admin/views/main.py:490`:** the suite directly rethrows selected errors; a source comment explains that callers treat them specially. The adjacent error-translation branch remains unresolved because its raised expression is outside the bare-rethrow rule. Recognition does not prove outward propagation.
- **HTTPie, `httpie/output/ui/man_pages.py:30`:** an exception from launching the manual-page process returns `False`. This is a literal fallback shape even though the exception spelling is broad; the analyzer does not judge the API contract or resolve the type.

## Deliberately unresolved examples

- **Requests, `src/requests/compat.py:43`:** the function docstring describes searching supported character-detection libraries, but the protected operation is `importlib.import_module`. Dynamic import calls and returned variables are outside this first pattern. The docstring is preserved; the local no-op category is unchanged.
- **HTTPie, `httpie/compat.py:22`:** the import guard contains a replacement `cached_property` class. That compatibility implementation is substantive code, not a literal default, so it remains unresolved.
- **Starlette, `starlette/middleware/body_limit.py:95`:** the no-op is inside a try with cleanup in `finally`. The context records cleanup locations and the `enclosing-finally` blocker rather than treating the control signal as proven safe.
- **Starlette, `starlette/_exception_handler.py:43`:** conditional rethrows and handler dispatch remain unresolved. That does not mean the framework lacks error handling; it means this reviewer does not establish the behavior of its conditional calls.

These cases support retaining explicit uncertainty. No handler percentage, error-handling score, business-intent grade, or automatic refactoring recommendation is justified by the pattern counts.

## Verification and delivery boundary

- 109 Python tests passed, including 15 focused context tests. Nine shared conformance/calibration tests passed.
- Tests cover pattern boundaries, import-name shadowing, dynamic namespace vetoes, computed/variable fallbacks, exception groups, nested handlers, context managers, deferred functions versus immediate class bodies, lexical continuation, documentation/truncation, source/owner/base-report drift, empty/test populations, invocation IDs, output preservation, and non-execution.
- The corpus runner requires clean pinned checkouts before and after analysis, verifies frozen source and function facts, and checks that every original handler row matches the prior study. Context analysis regenerates the base observations exactly and does not mutate its inputs.
- All 1,464 base handler observations and existing complexity facts are unchanged. Scoring code and the packaged 0.8.0 contract were not changed. Existing Method Complexity scores therefore remain Requests 7.0, Starlette 7.1, HTTPie 6.8, and Django 5.6 under the existing policy.
- Full reports, including unchanged base reviews, are in `TestResults/python-error-context-2026-10-15-verified/`. The earlier directory without `-verified` records the initial, narrower experiment and is not the final policy result.

This context extension is standalone and uncommitted at this milestone. It is not yet bundled into the npm adapter or consumed by the skill. Packaging should preserve these semantics and historical evidence contracts; scoring requires a separate justified proposal.

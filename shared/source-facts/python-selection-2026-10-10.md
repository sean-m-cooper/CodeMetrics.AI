# Python population and corpus review: 2026-10-10

The Python adapter now has an explicit, overridable implementation population and a first pinned external corpus. These are **experimental method-complexity previews**, not overall grades or calibrated Python scorecards. All clones are under `E:/repos`; target applications were not installed, built, imported, or executed.

## Measured results

The selections are package source, with test/support/generated/declaration scope recorded in source-facts v2. The [manifest](../calibration/corpus/python-selection.json) pins exact revisions; the [report](python-selection-2026-10-10.json) records source fingerprints, top functions, exclusions, and source-review observations.

| Selection | Files analyzed | Implementation bodies | Declaration bodies excluded | CC ≥ 10 | Maximum CC | Experimental preview |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Requests `src/requests/**` | 19 | 246 | 22 | 14 | 21 | 7.0 |
| Starlette `starlette/**` | 36 | 508 | 11 | 22 | 22 | 7.1 |
| HTTPie `httpie/**` | 78 | 464 | 1 | 17 | 26 | 6.8 |
| Django `django/**` | 907 | 9,748 | 1 | 480 | 99 | 5.6 |

All **10,966 retained implementation bodies** have exactly the same decision counts and CC as the original adapter on the same source. Each original body is accounted for exactly once as retained or excluded. The 35 exclusions are 29 overload declarations, four protocol declarations, and two abstract declarations. A normal `pass`, ellipsis, or docstring-only body receives no automatic exclusion.

Requests' same-file preview moved from 7.1 to 7.0 after removing declaration-only CC1 credit. The other rounded previews stayed unchanged. This population correction did not change the scoring ladder or improve source code.

## Findings that shaped the policy

These observations come from reading the pinned source, not executing it or judging its business requirements.

- **Public testing utilities can be production code.** Django's `django.test` package needs an explicit production override. The manifest includes `--production-path 'django/test/**'`; an unqualified test-directory rule would omit shipped framework behavior.
- **Typing declarations are not implementations.** Requests' protocol and overload bodies and Starlette's signature declarations are recorded as exclusions. Concrete implementations and callback bodies retain their own identities. Only recognized imports with no detected rebinding justify these exclusions.
- **Documented abstract contracts are different from no-op implementations.** HTTPie's `BaseStream.iter_body` and Django's `BaseTaskBackend.enqueue` are abstract, docstring-only declarations. Methods that return values, raise exceptions, or contain other behavior remain measured.
- **Large CC can describe required control flow.** Requests' multipart encoder handles several supported input forms. Starlette's telemetry middleware handles request/route metadata and owns its nested callback separately. HTTPie's entry-point method distinguishes nine exception handlers. Django's schema alteration method handles database capability and migration sequencing branches; its CC99 includes 36 `if` increments, 13 loops, 45 boolean increments, and four comprehension loops. These facts support review leads, not automatic claims of defects or safe refactors.

Pinned source references: [Requests multipart encoder](https://github.com/psf/requests/blob/611c6162cbc4ac2020a2f91c7cfa4f3abf9bbb60/src/requests/models.py#L183), [Starlette telemetry middleware](https://github.com/Kludex/starlette/blob/96d5023c55b29224edb8d684868c49f681d079a9/starlette/middleware/opentelemetry.py#L47), [HTTPie entry point](https://github.com/httpie/cli/blob/5b604c37c6c67e18e7c3e9aee6c88a8c22b98345/httpie/core.py#L32), [Django schema editor](https://github.com/django/django/blob/dab0a5c47f159eb58472110dcbc10bd4a9b01adb/django/db/backends/base/schema.py#L984).

## Regression checks and remaining scope

Python selection tests cover explicit overrides, overlapping patterns, test/support/generated populations, `.pyi` files, alias shadowing, concrete abstract/protocol bodies, nested ownership, selected parse failures, output protection, and generated-marker text inside strings. Shared conformance tests validate v1/v2 compatibility, population provenance, declaration double-credit prevention, and unchanged weighting for identical measurements.

The corpus runner checks clean pinned revisions, validates facts, compares retained measurements to the original trusted analyzer, and verifies [frozen baselines](../calibration/baselines/python-selection.json) containing complete function/declaration digests. The recorded baseline was rerun successfully in verification mode. This external corpus remains a manual check; ordinary CI runs the deterministic fixture tests without cloning or executing external projects.

Completed verification: 23 Python tests, seven shared conformance tests, 256 JS/TS tests, the installed JS/TS package smoke test, six frozen JS/TS calibration fixtures, and all four pinned Python corpus selections passed. The final v2 validator also accepted each recorded Python fact document and reproduced its frozen preview. `git diff --check` passed.

These four selections are a first calibration sample. Additional Python-native projects and review of version-specific syntax will be needed before production scoring. Python remains limited to the 3.12 parser; the next delivery work is packaging and standard evidence/skill integration for supported measurements. Coverage/CRAP, duplication, other dimensions, and cross-language calibration remain outside this pass.

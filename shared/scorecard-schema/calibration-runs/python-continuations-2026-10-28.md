# Python handler continuation review: 2026-10-28

Implemented bounded continuation analysis as a separate, standalone evidence extension. **Error Handling and Async/Blocking Usage remain unscored.** Previous disposition classifications, packaged evidence, and skill behavior are unchanged. See the [policy](../../../analyzers/python/ERROR_HANDLING_CONTINUATIONS.md) and [machine-readable validation record](python-continuations-2026-10-28.json).

## Results

All four pinned, clean corpus checkouts completed source-only analysis. Source facts and prior exception evidence matched the installed-package baseline; regenerated original disposition row digests matched the October 24 study exactly. New run IDs and a new shared audit ID identify this validation. No target project was executed.

| Project | New explicit continuations | Original swallowing candidates | Remaining candidates | Original unresolved dispositions | Remaining unresolved |
| --- | ---: | ---: | ---: | ---: | ---: |
| Requests | 0 | 0 | 0 | 9 | 9 |
| Starlette | 1 | 0 | 0 | 19 | 18 |
| HTTPie | 1 | 0 | 0 | 14 | 13 |
| Django | 57 | 25 | 16 | 276 | 228 |

These are source-evidence classifications, not handling-success counts or code-quality improvements. The target code did not change. Django's 57 continuations comprise nine former candidates and 48 formerly unresolved cases. Two retained-default links were recorded, both widget getter fallbacks.

## Django examples

The following nine prior candidates now have explicit post-handler continuations:

| Handler | New observation |
| --- | --- |
| `contrib/redirects/middleware.py:44`, `RedirectFallbackMiddleware.process_response` | Subsequent branches return a redirect/gone response or the original response |
| `contrib/sitemaps/__init__.py:94`, `Sitemap.get_domain` | Subsequent paths explicitly raise a configuration error or return a domain |
| `core/files/base.py:38`, `File.size` | Subsequent paths return an alternate measurement or raise |
| `core/validators.py:321`, `EmailValidator.validate_domain` | An explicit validation error is raised after leaving the inner branch |
| `core/validators.py:347`, `EmailValidator.validate_domain_part` | Returns `False` after leaving the inner branch |
| `forms/widgets.py:848`, `ChoiceWidget.value_from_datadict` | Returns through the retained getter; the default assignment and guarded replacement are linked |
| `test/testcases.py:1690`, `FSFilesHandler.get_response` | Returns the parent handler's result |
| `views/debug.py:107`, `get_caller` | Returns an explicit conditional fallback |
| `views/debug.py:453`, `ExceptionReporter._get_source` | Returns the source variable after the failed file-read attempt |

`FileInput.value_from_datadict` at `forms/widgets.py:558` supplies the second retained-default link (`files.get` versus optional `files.getlist`). Its previous disposition was unresolved rather than a swallowing candidate.

`MigrationWriter.basedir` at `db/migrations/writer.py:239` remains a candidate with an unresolved continuation because it is inside another try's `else` suite. Outer tries are deliberately a boundary in this first continuation policy. No special exemption based on the method or exception name was added.

The file-session save/delete catches at `contrib/sessions/backends/file.py:183,202` remain candidates. Missing-object removal, optional capabilities, mail delivery, and shutdown cases also need API/operational context. The remaining 16 candidates must not be treated as confirmed defects or automatic deductions.

## Verification

- **230 Python tests passed**, with no skips. Seven continuation test methods cover a 24-case labeled matrix, retained-default positives and seven veto cases, source/invocation integrity, input immutability, limits, no target execution, output preservation, and unreachable returns inside subsequent branches.
- **All 24 curated continuation scenarios matched their labels**, with zero false positives or misses against those labels. These are regression examples, not an independent sample or runtime accuracy estimate.
- The final corpus run used the final implementation hashes. Its rows exactly matched the initial corpus run after the unreachable-return fix; the fix is exercised by its dedicated regression test.
- Prior source facts, exception evidence, and disposition rows were preserved; inputs and corpus repositories stayed unchanged.
- `git diff --check` passed. No package build, skill integration, scoring change, commit, push, or release was performed in this milestone.

Final full reports: `TestResults/python-continuations-final-2026-10-28/`. Test log: `TestResults/python-continuations-tests-final-2026-10-28.log`. The earlier `python-continuations-2026-10-28/` run predates the unreachable-return fix and is retained only for comparison.

## Next boundary

Commit the standalone behavior and continuation work, then package the new evidence with strict validation and skill guidance. Continue to distinguish explicit source dispositions from successful recovery. The unresolved/candidate availability policy still needs a product decision before Error Handling can be scored.

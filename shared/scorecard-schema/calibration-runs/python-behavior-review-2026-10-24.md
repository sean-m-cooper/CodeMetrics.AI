# Python behavior review: 2026-10-24

Implemented standalone handler-disposition and bounded async-helper reviewers. **Error Handling and Async/Blocking Usage remain unscored.** The packaged adapter, scorecard skill, and Method Complexity policy are unchanged. See the [policy and commands](../../../analyzers/python/BEHAVIOR_REVIEW.md) and [machine-readable validation record](python-behavior-review-2026-10-24.json).

## Pinned corpus results

The final run used the four clean revisions in `shared/calibration/corpus/python-selection.json`, with new run IDs and one new audit ID. Target projects were parsed only. Source facts/selection, prior exception observations, and prior async observations matched the saved installed-package evidence exactly, excluding invocation IDs and timestamps. Reviewers did not mutate their inputs.

| Project | Handlers | Explicit/documented dispositions | Swallowing candidates | Unresolved dispositions | Async owners | Local helper links | Blocking candidate paths |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Requests | 66 | 57 | 0 | 9 | 0 | 0 | unavailable |
| Starlette | 62 | 43 | 0 | 19 | 135 | 6 | 0 |
| HTTPie | 69 | 55 | 0 | 14 | 0 | 0 | unavailable |
| Django | 1,267 | 966 | 25 | 276 | 266 | 22 | 0 |

The disposition columns partition handlers. Explicit/documented dispositions include propagation attempts, explicit fallbacks, calls/state updates, loop transfers, mixed explicit paths, and documented suppression. They are not successful-handling counts. Helper links count reference paths, not unique functions, executed calls, or successful offloads. No blocking catalogue matches is a bounded detection result; receiver calls and other unsupported relationships remain unresolved.

## Source review and adjustments

The initial implementation's candidate list exposed structural context that should withhold a swallowing classification. These cases were inspected against local pinned source and reproduced in regression fixtures:

- Starlette `routing.py`, `Mount.url_path_for:452`, `Host.url_path_for:518`, and `Router.url_path_for:641`: an empty handler allows route search to continue; the router eventually raises if no route matches. An enclosing-loop guard now retains these as unresolved context rather than swallowing candidates.
- Starlette `websockets.py`, `iter_text:154`, `iter_bytes:161`, and `iter_json:168`: empty handlers surround generator iteration and disconnect handling. Protected yields now trigger a generator-protocol review reason.
- HTTPie `output/formatters/colors.py:171,177`: empty handlers occur while probing lexer candidates in loops. They receive the loop-context guard.
- HTTPie `config.py:74`: the missing-file no-op is distinct from the neighboring translation of other OS errors. Multiple sibling handlers now require contextual review for an otherwise unexplained no-op.
- Starlette `datastructures.py:132`: the IPv6 probe already remained unresolved because its `else` branch changes formatting only on successful parsing.

All six initial Starlette candidates and all three initial HTTPie candidates moved to unresolved context without exception-name whitelists. Ten initial Django candidates also moved to unresolved context. Existing reports and classifications were not rewritten; this is a separate extension.

Remaining Django candidates are **not established defects**. For example, `django/utils/datastructures.py:22`, `OrderedSet.discard`, catches `KeyError` from `self.remove(item)` and does nothing. This is consistent with deliberate discard semantics, which the current analyzer cannot establish from a call through `self`. `django/utils/log.py:177`, `AdminEmailHandler.send_mail`, suppresses `MailerDoesNotExist`; the code alone does not establish the application's reporting policy. These examples demonstrate why the 25 candidates cannot yet become automatic deductions.

The review is purposive, not a random sample or an independently labeled accuracy study. No corpus-wide false-positive or recall percentage is claimed.

## Verification

- **223 Python tests passed**, no skips, using the coverage-enabled Python 3.12 environment. The full suite includes 17 new behavior-review tests covering ownership, conditional paths, documented choices, contextual guards, eager versus callback execution, partials/wrappers/decorators, deferred generators, shadowing, depth/global budgets, source drift, tampering, input immutability, no target execution, and CLI output preservation.
- **27 curated source-contract cases matched their expected results.** Handler cases: 2 candidate positives and 13 negatives; async cases: 5 candidate positives and 7 negatives. No false positives or false negatives against those labels. Several negatives were added after inspecting the corpus, so this is regression evidence, not a held-out validation result.
- All four corpus reviews completed. Implementation hashes in the durable JSON were checked against the final reviewer source files. Earlier source facts and exception/async evidence were preserved.
- No package build, skill integration, score calibration, release, or publication was performed in this milestone.

Full diagnostic artifacts are under `TestResults/python-behavior-review-final-2026-10-24/`. The earlier `python-behavior-review-2026-10-24/` run is exploratory and predates the structural guards; it is not the final validation. The final test log is `TestResults/python-behavior-tests-verified-2026-10-24.log`.

## Next boundary

Commit the standalone work, then package and validate both extensions with explicit skill guidance. Before assigning scores, expand independently reviewed cases and decide how unresolved dispositions, candidate-only evidence, and limited blocking-catalogue coverage affect availability. Do not convert this table into quality scores.

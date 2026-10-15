# Experimental Python exception context

This standalone reviewer adds bounded lexical context and conservative syntactic patterns to the existing [handler observations](ERROR_HANDLING.md). **Error Handling stays unscored.** Recognized patterns are not proof of correct handling, and unresolved handlers are not defects. Method Complexity and the packaged 0.8.0 evidence contract are unchanged.

```powershell
py -3.12 analyzers/python/error_handling_context.py E:/repos/my-project --include 'src/**' --output TestResults/handler-context.json
py -3.12 analyzers/python/error_handling_context_corpus.py --repos E:/repos --output TestResults/handler-context-corpus-new
```

Python 3.12 and its standard library are sufficient; target code is never imported or executed. The CLI accepts a source file or directory and the existing selection flags. Pass a manifest's directory rather than the manifest itself. Output must be a new `.json` file with an existing parent; omission writes to stdout. Supplied UUID run/audit IDs are preserved, otherwise generated. Complete, including empty, returns 0; incomplete analysis or invalid invocation returns 2. This context extension is not yet packaged or consumed by the scorecard skill.

## Policy: `python/exception-context-patterns-v1`

Each context row joins one existing handler by ID, source hash, location, population, and owner. It records:

- The containing try statement, its protected body, `else`, `finally`, and sibling handler lines.
- The handler's direct statement kinds/locations, including literal return kinds and simple literal-assignment targets.
- Up to three following statements **in the same lexical suite**, without skipping active statements or crossing out of a branch. These are nearby syntax, not a promise they execute next.
- Lexically enclosing constructs, stopping at the deferred function boundary. Class bodies retain their surrounding execution context.
- Contiguous same-indentation comments immediately before the try, plus the nearest function/class docstring. These are surrounding prose, not an automatic explanation of each handler. Existing local documentation classification is preserved.

Direct suites show at most 32 statements, with full direct-statement counts and truncation flags. Surrounding documentation is capped at 2,000 characters per context and marked when truncated. Literal observations record value kinds, not string contents. Complex expressions and nested suites remain inspectable through source locations and the original owned-body observations, not full source dumps.

## Pattern precedence and boundaries

Exactly one primary pattern is recorded per handler; unmatched cases have `status: unresolved`, `pattern: null`, and explicit reasons. Pattern priority is a reporting convention, not a scoring ladder.

| Pattern | Required local syntax |
| --- | --- |
| `locally-documented-no-op` | Inert suite with local documentation under the existing policy |
| `direct-literal-fallback-return` | Handler contains exactly one return of a scalar literal, signed numeric literal, or implicit `None` |
| `direct-bare-rethrow` | Handler contains exactly one bare `raise` |
| `guarded-import-shape` | Protected body is exactly one import statement; narrow import-exception name syntax; handler is inert |
| `guarded-import-literal-default` | Same protected import and exception constraints; handler is exactly one scalar-literal assignment to names bound by that import |
| `no-op-then-literal-return` | Inert handler followed immediately by a scalar-literal return in the containing try's own suite |

Local documentation presence is recorded even with unresolved execution context. Its precedence means a documented import guard remains primarily documented. It does not establish rationale adequacy or successful handling. TODO prose and terse comments are preserved without judging business decisions. Nearby prose does not upgrade an undocumented local suite.

For the other patterns, exception groups, lexically enclosing finalizers, context managers, and nested handler execution are conservative blockers. An enclosing class executes immediately; entering a method/function resets deferred execution context. The finalizer flags are from the original handler evidence. An enclosing try without a finalizer can still intercept a rethrow; a recognized rethrow is only a local shape, never a propagation guarantee.

The import patterns accept `ImportError`, `ModuleNotFoundError`, or a tuple containing only those names. Any syntactic binding/deletion of a candidate exception name anywhere in the file, a star import, or a direct `exec`/`eval`/`globals`/`locals` call vetoes recognition. This deliberately over-conservative check is not complete symbol resolution. Indirect namespace mutation, externally supplied bindings, and cross-module semantics remain unresolved. Qualified exception names and dynamic `import_module` calls are outside this first pattern. Even a recognized guard does not prove that a dependency is optional: imports can execute module code and fail for other reasons.

Returns of calls, variables, attributes, containers, and computed expressions remain unresolved. Logging followed by a rethrow, conditional recovery, compatibility-class implementations, imported fallback implementations, and additional protected work remain unresolved too. Explicit scalar fallbacks describe syntax, not API-contract correctness. Neither an exception name nor a function name establishes business intent.

## Provenance and failure behavior

The context report embeds the unchanged original handler review, shares its run/audit IDs, and records its handler-row digest, context policy/configuration fingerprint, and producer. Source files are hash-verified and reparsed. Original handler rows and summary must regenerate exactly, selected function identities must still match, and the shared handler budget applies. Context analysis does not mutate source facts or handler observations.

Source drift, malformed source, missing/changed owners, changed handler observations, or a budget failure withholds all context rows and the summary. The embedded base review is diagnostic context on a failed run, not permission to claim fresh usable results. These checks are source-consistency checks rather than authentication of externally edited artifacts or immutable-filesystem guarantees.

Summary counts separate recognized shapes, unresolved handlers, patterns, and populations. Reason counts can overlap; they are not a partition. No handled/unhandled rate, deduction, defect grade, or automatic refactoring recommendation is derived. The [pinned corpus study](../../shared/scorecard-schema/calibration-runs/python-error-context-2026-10-15.md) records observed patterns, conservative exclusions, and source-reviewed examples.

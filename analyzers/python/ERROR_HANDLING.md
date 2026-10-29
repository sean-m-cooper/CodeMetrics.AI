# Experimental Python exception-handler evidence

This source-only reviewer records exception-handler syntax and local documentation without importing or executing target code. It assigns **no score, deduction, gate, defect classification, or handling-success rate**. Python's packaged Error Handling dimension remains unavailable; Method Complexity remains its only scored dimension.

```powershell
py -3.12 analyzers/python/error_handling.py E:/repos/my-project --include 'src/**' --output TestResults/handlers.json
py -3.12 analyzers/python/error_handling_corpus.py --repos E:/repos --output TestResults/python-handlers-new
```

Python 3.12 is required. The CLI accepts a directory or Python source file, with the same selection/population flags as `source_facts.py`. Pass a manifest's containing directory rather than the manifest itself. Output must be a new `.json` file with an existing parent; omission writes to stdout. Caller-supplied UUID `--run-id` and `--audit-id` are preserved, otherwise fresh IDs are generated. Exit 0 means complete, including an empty selection; exit 2 means incomplete analysis or invalid invocation. The development `codetestament-python` adapter **0.11.0** embeds this report at `dimensions.errorHandling.review` under ruleset `python-2026-10-15-exception-operations-review-v1`. The npm archive includes the producer and this policy document. The companion skill reports the evidence without assigning an Error Handling score. Use an explicit local archive until publication; published npm 0.5.0 lacks this workflow.

The packaged path analyzes handlers automatically from the same source facts and run/audit IDs as the other measurements. The validator checks matching provenance, source digests, owner identities, unique handler IDs, annotation/classification consistency, owned-transfer fields, and recomputed category/population summaries. It does not independently reparse source or prove that an externally edited report retained every handler. Malformed, missing, or incomplete handler evidence fails the combined run and withholds its Method Complexity score and all findings. Empty complete reports contain zero handlers without implying excellent handling. Historical 0.7.0 evidence remains valid with its original score and no invented handler observations; 0.5/0.6 previews retain their original contracts. Cross-version comparison is not enabled.

## Policy: `python/exception-handler-observations-v1`

Each selected `except` or `except*` suite receives one observation. Module and class-body handlers are included as well as handlers in selected functions. File populations remain explicit; tests, support, generated files, and declaration files follow the shared source-selection policy. The handler population is not the function population and must not be used as its denominator.

| Classification | Syntactic condition |
| --- | --- |
| `documented-no-op` | Only `pass`, bare ellipsis, or string-literal statements, with local documentation as defined below |
| `undocumented-no-op` | The same inert statements, without qualifying local documentation |
| `terminal-raise` | Final direct statement is `raise` |
| `terminal-return` | Final direct statement is `return` |
| `terminal-loop-control` | Final direct statement is `break` or `continue` |
| `other-body` | Any other body, including logging, assignments, conditional transfers, or nested recovery logic |

These categories describe syntax. A final direct `raise` is not a guarantee that all paths propagate: earlier branches may return, enclosing handlers may intercept it, or `finally` may override it. Explicit fallbacks and loop transfers are not defects by default. `other-body` establishes neither adequate nor inadequate handling.

Owned body observations record raises (bare or with an explicit cause), returns, loop transfers, and call counts. Nested function/class bodies and nested handlers cannot credit the enclosing handler; nested handlers receive their own rows. Class bodies retain lexically enclosing `finally` context because they execute at definition time; function bodies start a separate context. `enclosingFinallyLines` records containing try-statement locations with a final suite, not proof of runtime behavior or a call graph.

## Documentation presence

For inert suites, preserve comments after the handler-header colon, including inline comments and trailing indented comment-only lines. Preserve nonempty string-literal statements too. Require at least one alphanumeric character. Punctuation-only comments and recognized tool directives (`noqa`, `type: ignore`, `pragma: no cover`, `fmt:`, `pylint:`, `ruff:`) do not qualify; directive comments are retained separately. Comments within the exception-type expression, before the handler, or after a dedent are outside this association rule.

`documented-no-op` means **local prose is present**, not that the analyzer has judged its adequacy. Short notes such as `# Nope` and TODO prose are preserved without guessing the author's intent. Conversely, `undocumented-no-op` does not mean the surrounding function lacks an explanation: a preceding comment, docstring, or later fallback may explain it. No minimum word count or comment-to-code ratio is used. Neither category warrants an automatic penalty. Comments on non-inert bodies are not collected by this first policy.

## Provenance and limits

The report records run/audit IDs, source-facts producer and policy, parser version, selection inventory/fingerprint, and its own configuration fingerprint. Each row records a stable source-position ID, source hash, location, population, owner, exception-type syntax, exception-group flag, and `review-only` disposition. Summary counts are separated by classification, owner kind, and file population. Zero handlers is an observation, not a perfect score.

Source reads verify the selected hashes and existing path/size boundaries. Repeated or changed function identities and populations exceeding 100,000 handlers fail closed. Any source-facts or review failure leaves all handler rows withheld and the summary null. Complete facts are not mutated. Integrity checks bind the review to its supplied facts; they do not authenticate externally edited artifacts.

Exception types and calls are not resolved. A spelling such as `Exception` does not prove the built-in type is referenced. Context-manager suppression, `__exit__`/`__aexit__`, API contracts, logging effectiveness, retries, cancellation policy, and business correctness are outside scope. No .NET `throw ex` rule is transplanted to Python's `raise exc`.

The [first pinned corpus review](../../shared/scorecard-schema/calibration-runs/python-error-handling-2026-10-13.md) demonstrates why no-op counts require context before any future calibration. The [packaged validation record](../../shared/scorecard-schema/calibration-runs/python-error-handling-package-2026-10-15.md) records installed-package and skill verification.

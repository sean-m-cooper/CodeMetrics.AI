# Experimental Python raise and chaining evidence

This standalone reviewer extends the existing [exception context](ERROR_HANDLING_CONTEXT.md) with owned `raise` statements and four conservative local patterns. **Error Handling remains unscored.** Method Complexity, the packaged 0.9.0 contract, and existing context classifications are unchanged.

```powershell
py -3.12 analyzers/python/error_handling_transfers.py E:/repos/my-project --include 'src/**' --output TestResults/exception-transfers.json
py -3.12 analyzers/python/error_handling_transfers_corpus.py --repos E:/repos --output TestResults/exception-transfers-corpus-new
```

Python 3.12 and its standard library are sufficient. Target code is parsed, never imported or executed. The entry point is a Python source file or directory; pass a manifest's directory rather than the manifest. Existing selection flags and optional UUID run/audit IDs are supported. Output must be a new `.json` file with an existing parent; omission writes to stdout. Complete results, including empty selections, return 0. Incomplete analysis or invalid invocation returns 2. This extension is not yet bundled into the npm adapter or consumed by the skill.

## Policy: `python/exception-raise-chaining-v1`

Each row joins one original handler by ID, source hash, location, population, and owner. It preserves the original context classification and records the handler's `as` name as syntax, its owned raises, and an extended classification. Ownership follows the original handler policy: nested handlers, functions, lambdas, and class bodies cannot credit their enclosing handler. Independently selected nested handlers retain their own rows. The pass covers handlers, not all raises throughout the project.

Each raise records:

- A stable file/line/column identity, ending line, and whether it is a direct handler statement.
- Raised-expression and cause AST kinds, plus a bounded simple name or attribute path when available. Calls record the callee path, not their argument values. An absent expression is distinct from the literal `None`.
- Whether the raised expression or explicit cause is a name with exactly the same spelling as the handler's `as` name. This is **syntactic correspondence**, not binding analysis or proof of object identity.
- Named-expression presence and enclosing statement locations within the handler. Up to 32 containers are retained, with an explicit truncation flag. Calls/subscripts within a receiver are not simple symbol paths; paths have at most eight attributes and 200 characters.

The global budget is 100,000 owned raise statements. Exceeding it fails the pass rather than silently truncating the population. Summary counts keep populations separate and distinguish exception syntax, cause syntax, matching handler-name spellings, and handlers with raises. Conditional raises are observations, not proof that their enclosing handler always raises. Several raises may belong to one handler.

## Conservative classification

All previously recognized context patterns are retained exactly. Existing unresolved blockers remain: exception groups, enclosing finalizers, context managers, nested handler execution, and unresolved import-exception bindings. New patterns apply only to an otherwise unresolved handler whose entire body is one `raise` statement. No named expression may occur in that raise.

| New pattern | Required syntax |
| --- | --- |
| `direct-handler-name-raise` | Raised expression is a name matching the handler's `as` name; no explicit cause |
| `direct-symbol-or-call-raise` | A simple name/attribute or call to one of those symbols; no explicit cause |
| `direct-raise-from-handler-name` | The same supported raised expression, with a cause name matching the handler's `as` name |
| `direct-raise-from-none` | The same supported raised expression, with literal `from None` |

`from None` takes precedence over the handler-name cause check, followed by the no-cause patterns. Bare `raise` retains the existing `direct-bare-rethrow` classification. Unsupported one-raise expressions or causes receive `raise-expression-or-cause-unresolved`; other suites preserve their prior unresolved reason.

These labels do not resolve the exception type or prove an exception is newly constructed. A callable can return an existing exception, return something invalid, mutate bindings, or fail during evaluation. A name can be a class, instance, alias, or unrelated value. No scope-wide or runtime binding analysis is claimed. Even an unchanged matching name is not a guarantee of outward propagation; outer code can intercept the exception.

Rebinding followed by a raise, logging followed by a raise, conditional dispatch, returned expressions, computed raised objects, and causes with other names or expressions remain unresolved as handler patterns. Their owned raises are still recorded. A `with_traceback(...)` call through a simple path remains a call observation, not proof of traceback preservation. Context blockers never remove the raw syntax observations.

## Interpretation boundaries

The [Python 3.12 language reference](https://docs.python.org/3.12/reference/simple_stmts.html#the-raise-statement) distinguishes bare re-raising, raising an expression, explicit causes, and `from None`. An omitted `from` does not establish that causal context was lost: Python also supports implicit exception context. `from None` explicitly suppresses the automatic display of that context; it does not justify a defect finding or a claim that the exception history has been erased.

Do not transplant a .NET `throw ex` penalty to `raise error`. Do not grade explicit versus implicit chaining, infer business intent from names, treat the absence of `from` as an error, or reward a recognized shape as correct handling. A translation candidate is a source-review description, not a resolved behavioral classification. Documentation is preserved through the embedded context report without judging its adequacy.

## Provenance and failure behavior

The report embeds the unchanged context report and shares its run/audit IDs. It records a context-row digest, producer, policy, and configuration fingerprint. The pass first regenerates and compares the input context report, then hash-verifies and reparses selected sources to collect transfers. Both original handler rows and context rows must match again. The corpus runner additionally checks that projecting the new raises to the original line/bare/explicit-cause fields exactly reproduces every original owned-raise observation.

Source drift, changed context/provenance, parse errors, or budget failures withhold all new rows and the summary. Embedded prior reports are diagnostic only on an incomplete run. These checks detect internal/source inconsistencies; they do not authenticate edited artifacts or promise an immutable filesystem.

The [pinned corpus study](../../shared/scorecard-schema/calibration-runs/python-error-transfers-2026-10-15.md) records reviewed positive cases and deliberate exclusions. Neither pattern counts nor unresolved counts become a score, deduction, gate, handled/unhandled rate, defect claim, or automatic refactoring requirement.

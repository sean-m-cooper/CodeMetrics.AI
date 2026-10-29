# Python handler continuations

Policy: `python/exception-continuations-review-v1`. This standalone extension follows bounded source continuations after an inert exception handler. **Error Handling and Async/Blocking Usage remain unscored.** It preserves the earlier [handler dispositions](BEHAVIOR_REVIEW.md) and records its own classifications separately. It is not yet packaged or integrated into the scorecard skill.

## Run and provenance

```powershell
py -3.12 analyzers/python/error_handling_continuations.py E:/repos/project --output TestResults/continuations.json
py -3.12 analyzers/python/continuation_corpus.py --repos E:/repos --baseline TestResults/python-async-adapters-package-2026-10-24/final --dispositions TestResults/python-behavior-review-final-2026-10-24 --output TestResults/continuation-corpus-new
```

Python 3.12 and the standard library are sufficient. The source CLI accepts the existing selection flags and optional UUID run/audit IDs. It parses target sources without importing or executing them. Output requires a new `.json` file with an existing parent. Complete reviews exit 0, including empty populations; incomplete analysis or invalid invocation exits 2.

The reviewer regenerates the supplied operations and disposition evidence, verifies source facts and source hashes, and compares the original disposition rows again during collection. Each new row retains its handler identity, population, source digest, original classification/reasons, continuation evidence, and resulting classification. The report binds the original row digest and policy configuration. Drift, tampering, incomplete inputs, or exhausted report budgets discard all extension rows and its summary. Source checks do not authenticate external artifacts or lock a changing filesystem.

## What can change a classification

Only undocumented inert handlers previously classified as unresolved or as swallowing candidates are eligible. Documented suppression, existing explicit dispositions, and non-inert handlers retain their classifications.

After a handled exception, the continuation begins after its containing `try`. The try's `else` suite and other handlers are skipped. At the end of an enclosing `if`/`elif`/`else` branch, analysis continues after the whole conditional, without entering the opposite branch or re-evaluating its condition. Nested branches can be exited this way until reaching the selected function/module boundary or a limit.

Statements and subsequent conditionals use the existing bounded path engine. Only when **all supported paths reach an explicit return or raise** can the extension report an explicit fallback, propagation, or mixed explicit disposition. A returned call is visible delegation syntax, not a resolved recovery contract. A call or assignment alone is insufficient. Implicit function return is not credited as explicit fallback. Bare `raise` outside the handler is unresolved rather than mistaken for rethrowing its exception.

For example:

```python
if has_literal_domain:
    try:
        validate_ip(domain)
        return
    except ValidationError:
        pass
raise ValidationError(message)
```

The handler's continuation reaches the explicit raise after leaving the branch. This is a propagation observation conditional on normal expression evaluation, not an assertion about runtime type identity or overall validation correctness.

## Retained defaults

The extension also links a narrow guarded assignment to an earlier default and a later return:

```python
getter = data.get
if allow_multiple:
    try:
        getter = data.getlist
    except AttributeError:
        pass
return getter(name)
```

The protected body must contain exactly one simple assignment to a local name. Its RHS and the initializer must be constants or bounded name/attribute paths; calls, subscripts, destructuring, and multiple protected operations are excluded. The initializer must occur on the lexical route to the try. Intervening conditional writes, deletions, imports, pattern bindings, exception targets, and subsequent rebinding withhold the link. Using the same name as the handler's exception target also withholds it because that target is cleared afterward.

Global/nonlocal bindings and recognizable reflective operations withhold default links. Owner scans are bounded; oversized owners have no default link. A later return must use the name directly or as the callee of a returned call. Evidence records locations and expression kinds, not literal contents or a judgment of the default's adequacy. The observation describes retained local binding syntax; it does not authenticate object state, external callbacks, or reflective runtime behavior.

## Boundaries and budgets

- Enclosing loops, context managers, exception handlers, outer tries, class bodies, exception groups, finalizers, and generator owners block continuation classification. An outer try's `else` is also a boundary. No claim is made that these are defective patterns.
- Unsupported statements encountered in the continuation preserve an unresolved path. Later returns cannot erase that uncertainty. Once all paths terminate, later unreachable statements do not supply or invalidate evidence.
- Subsequent conditionals retain both branches. The analyzer does not solve predicates, prune infeasible paths, infer successful logging, or evaluate business decisions.
- At most eight enclosing branch transitions are followed. A continuation examines at most 256 AST nodes; oversized continuations remain unresolved. Default-owner inspection also withholds links above 256 nodes. A shared 100,000-node report budget bounds continuation/default inspection and fails the report on exhaustion. Existing path-depth and statement budgets also apply.
- A continuation that reaches scope end or a boundary preserves the previous classification and reports an explicit reason. Missing default links mean unsupported/unavailable evidence, not missing defaults.

Summary counts partition handler classifications and separately report explicit continuations, candidate reclassifications, default links, and unresolved reasons. They are not handling-success rates, grades, deductions, or evidence that runtime failures cannot occur.

## Validation

The tests include a 24-case labeled continuation matrix, retained-default positives and seven veto cases, nested branch traversal, skipped try/if branches, budgets, source drift, altered evidence, input immutability, no target execution, and output preservation. Corpus validation checks exact prior source facts, exception evidence, and disposition row digests against the frozen four-project study. These are curated regression checks, not an independent accuracy estimate.

The [October 28 study](../../shared/scorecard-schema/calibration-runs/python-continuations-2026-10-28.md) records the actual validation results and remaining Django review leads.

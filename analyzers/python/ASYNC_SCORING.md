# Python Async/Blocking Usage scoring study

Policy `python/async-blocking-owner-rate-v1` implements a standalone numeric score
over the existing [async helper evidence](BEHAVIOR_REVIEW.md). It measures detected
blocking usage in selected production async implementations. It does not measure
latency, throughput, appropriate concurrency, or the correctness of a business
decision.

This is an experimental scoring study, separate from packaged scorecard evidence
v3. The development npm adapter still scores only Method Complexity. Historical
reports, overall scores, baseline gates, and the companion skill are unchanged.
Error Handling remains unscored. Enabling this dimension in the package requires
a versioned contract, shared-validator support, and skill integration.

## Population and ladder

Let N be the number of distinct selected production `async def` implementations,
including async generators. Let B be the number of those owners with at least one
non-excluded blocking-catalogue observation in its eager execution context.
Each owner contributes once to B and once to N, regardless of call or path count.
Ordinary synchronous functions cannot dilute the denominator. Selected test,
support, and generated owners retain observations but do not enter either count.
Declaration-only exclusions remain those of source selection. Nested async owners
count separately; their bodies never count against their enclosing function.

| Affected owner rate | Score |
| --- | ---: |
| B = 0 or below 1% | 10 |
| Below 2% | 9 |
| Below 5% | 8 |
| At most 10% | 6 |
| At most 15% | 4 |
| At most 20% | 2 |
| Above 20% | 0 |

The accepted JS/TS owner-rate bands supply the product-policy baseline. Exact
integer cross-products choose the band; rounded percentages never choose it.
Zero eligible owners means **unavailable**, not 10. A small population can score
0 for one affected owner. Below 1%, a 10 can still contain scored observations;
always show B and N. These thresholds are coaching policy, not empirical defect
prediction or evidence of score equivalence between languages.

All selected production async implementations enter N, even if they currently do
no awaiting or delegate to another async function. This is a declared-async
population, consistent with the existing Python ownership inventory. It is not a
measurement of actual suspension, nontrivial workloads, or reachable functions.
Changing eligibility later requires another policy rather than silent retuning.

## Scored observations and exclusions

The seven existing import spellings are the complete initial catalogue:
`time.sleep`, `subprocess.run`, `subprocess.call`, `subprocess.check_call`,
`subprocess.check_output`, `os.system`, and `urllib.request.urlopen`.

Direct calls and supported eager same-file synchronous helper paths qualify.
Repeated helper calls affect their async root once. Two async roots using the same
helper affect two owners; `distinctScoredSourceCalls` separately deduplicates the
shared blocking expression. Synchronous helpers never become extra denominator
credit. No severity weighting or additional per-call deduction is applied.

Recognized thread-helper callbacks, wrapper callback references, and deferred
generator work remain observations without deductions. `to_thread(work())`
evaluates `work()` eagerly and does not receive a callback exemption. A generator
expression's first iterable retains eager context. Creating a synchronous
generator or coroutine does not prove execution of its body. Sequential awaits,
throttling, and missing concurrency are not scored hazards.

Import spelling is not authenticated runtime identity. The existing binding and
helper vetoes apply, including shadowing, mutation, ambiguous decorators, source
order, same-file scope, and the three-edge helper limit. Arguments and conditions
are not evaluated; unreachable syntax or zero-duration sleep can remain a signal.
Receiver calls, cross-file helpers, arbitrary callbacks, CPU-heavy work, and
non-catalogue APIs remain outside detection. No automatic rewrite is prescribed.

## Documented choice

`adjacent-simple-statement-declared-blocking-prose-v1` retains qualifying comments as
`documented-choice` observations without deductions. It checks each call along
the eager path, so a rationale at one async call site applies only to that path;
a rationale on the blocking statement in the shared helper applies to every
observed path through it.

A comment must be an actual Python comment token immediately above a simple
statement at the same indentation, or trailing its final line. The statement can
be a call expression, assignment, return, raise, or assertion. Multiple statements
on a line, one-line compound suites, declaration boundaries, intervening statements,
blank lines, and comments attached to an enclosing branch do not inherit intent.
Multiline statements and Python's UTF-8 AST columns are handled explicitly.

The prose must contain `blocking`, `synchronous`, `sync-over-async`, or `sleep`,
and an intent word: `intentional`, `intentionally`, `deliberate`, `deliberately`,
`required`, `necessary`, or `unavoidable`. It must have at least two alphabetic
words. Tool directives, TODO/FIXME/HACK/XXX markers, and text that parses as Python
code do not qualify. Caution words (`not`, `never`, `avoid`, `remove`, `replace`,
`shouldn't`, `mustn't`, `unnecessary`) withhold automatic exclusion. For example,
"Blocking should be avoided" is a warning, not an exemption. The conservative
caution rule can also withhold valid explanations such as "Blocking is required
because the device does not support async"; rewriting the local declaration to
"Synchronous operation required by the device protocol" makes intent explicit.
This is a deterministic documentation convention, not natural-language verification
that an explanation is adequate. General docstrings and strings that
look like comments do not qualify. No Python CMAI annotation syntax is introduced
by this study.

```python
async def run():
    # Synchronous operation required by the device protocol.
    time.sleep(1)
```

The observation and rationale remain visible. The score trusts the declared
choice without judging its business justification or claiming the call is safe.

## Unknowns and availability

Unresolved paths remain attached to every owner and summarized by reason, along
with `ownersWithUnresolvedPaths`. They receive no deduction and are not classified
as nonblocking or correct. Owners with unresolved paths remain in the declared
async population; removing them would select a different denominator and could
inflate or distort the affected rate. These counts are not semantic-analysis
coverage percentages. Even an imported non-catalogue call can have unknown
behavior without producing an unresolved helper path.

A 10 means the observed affected-owner rate falls in the top band of this narrow
catalogue. It does **not** certify excellent or safe async code. Report unresolved
counts and the limited scope beside any score, especially for a zero-match
codebase. Neither the absence of catalogue matches nor passing curated tests
establishes recall across real Python applications.

An incomplete parse, stale/tampered helper evidence, source mismatch, or exhausted
global traversal/index budget withholds the score and all contribution rows.
The existing helper-depth limit retains an explicit unresolved path. The new
comment/source index is capped at 1,000,000 AST nodes across files needed for
candidate classification; helper budgets remain unchanged. All prior evidence is
regenerated against source and compared before scoring. Selection, policies,
configuration fingerprints, source digests, and run/audit IDs bind the result.
These integrity checks do not authenticate externally edited files or lock a
concurrently changing filesystem.

## Run and validate

Python 3.12, standard library only; target code is never imported or executed:

```powershell
py -3.12 analyzers/python/async_scoring.py E:/repos/project --output TestResults/async-score.json
py -3.12 -m unittest discover -s analyzers/python/tests -p test_async_scoring.py -v
py -3.12 analyzers/python/async_scoring_corpus.py --repos E:/repos --baseline TestResults/python-async-adapters-package-2026-10-24/final --helpers TestResults/python-behavior-review-final-2026-10-24 --output TestResults/async-scoring-new
```

The scoring CLI accepts the existing selection flags and optional UUID run/audit
IDs. It requires a new output JSON file with an existing parent, or writes to
stdout. Complete reports, including empty populations, exit 0; incomplete reports
and invalid invocations exit 2. This output is not a v3 scorecard or a gate input.

The corpus runner verifies clean pinned revisions before and after each run,
exact original source facts, and exact prior async/helper evidence apart from
fresh invocation IDs/timestamps. It records implementation hashes, contribution
hashes, and uncertainty alongside scores. It does not rerun target tests or alter
corpus source. Unit tests cover exact thresholds, positive/negative execution
contexts, intent boundaries, populations, shared-helper deduplication, budgets,
source drift, tampering, and CLI output preservation.

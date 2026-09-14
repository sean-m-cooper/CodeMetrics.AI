# Error handling: source population and declared intent

Implemented in .NET 2.3.0, ruleset `dotnet-2026-09-14-dependency-availability`, policy `dotnet/errorHandling/catch-population-v2`. This supersedes the absolute-count ladder. Old runs retain their original interpretation and are incompatible baseline gates.

## Population and score

The population is all catch clauses in analyzable source trees. Normalize physical paths and exact catch spans; each source catch counts once across project/framework observations. Conditional source sites remain distinct. Unknown file identities remain separate. Take the greatest issue weight observed at a site, including across frameworks:

| Recognized issue | Weight |
|---|---:|
| Unexplained empty catch or `throw ex` | 1 |
| Broad catch returning a default without recognized handling | 0.75 |
| Broad catch without recognized handling | 0.5 |
| No scored catch issue | 0 |

`populationScore = 10 - 10 * sum(site weights) / source catch count`.

An empty population has no observed catch penalty (10), not evidence of a complete exception strategy. One catch's overlapping rules never add their weights. Informational console output and type-level missing-logger advice remain visible but have no independent penalty. The score is the minimum of the population score, a cap of 9 when an unexplained empty catch or stack-damaging rethrow exists, and the existing synchronous-blocking cap of 4. Round once to one decimal, half up, using decimal arithmetic.

These are explicit product choices, not an empirical defect model. Two unexplained empties among two catches score 0; two among 100 score 9 after the critical-site cap; 20 among 100 score 8; 50 among 100 score 5. A single broad-default site scores 2.5 when it is the entire population. These examples exclude the independent synchronous-blocking cap. Do not change weights to fit repository reputations or preferred scores.

Evidence records the source population, affected catches, weighted sum/rate, documented catches, raw project/framework observation counts, formula, weights and limiting caps. Findings retain their project/framework observations and catch span, even when several findings contribute to one site's maximum weight. A catch without a scored issue is not necessarily verified as correctly handled.

## Deterministic recognition of intent

Existing logging, rethrow, exception-bearing returns, invoked error callbacks and standard-error reporting remain recognized. New recognition accepts a prose comment inside the catch, or trailing its closing brace, as declared intent. It requires at least two words containing letters, excludes standalone TODO/FIXME/HACK markers, empty comments, parseable commented-out statements, and suppression directives. Comments in deferred functions or nested catch blocks do not explain the enclosing catch. The analyzer does not judge whether the business reason is good. An ordinary local explanation does not require a CMAI code. Explicit `CMAI5001` directives retain their existing scoped, rationale-required behavior.

This is a lexical declaration contract, not natural-language validation. It can accept an unhelpful prose comment and miss a one-word or non-prose explanation. Keep review comments separate from claims of proven correctness. A rationale never exempts `throw ex` from the stack-trace rule.

A direct write of a nonempty diagnostic into an `out`/`ref` parameter named for an error, message or diagnostic, followed by a direct return, also counts as handling. Supported values include diagnostic strings, interpolated/concatenated messages, collection expressions and localized diagnostic templates. Null/default outputs, unrelated local variables, deferred writes and later overwrites do not qualify. This bounded recognition establishes a reporting path, not correctness on every possible path. Naming a method `Try...` or `IsValid...` alone does not waive findings.

## Security classification in the same ruleset

Policy `dotnet/security/identifier-cors-flow-v2` preserves the security score ladder. Secret candidates now exclude descriptive literals that spell the declaration's name, optionally with naming suffixes (`TokenName`, `Name`, `Path`, `Url`, `Protector`) or a dotted identifier prefix. The literal must consist of letters and identifier separators. Suffixes alone do not exclude credential payloads. This remains a heuristic; neither names nor literals prove security.

CORS analysis associates the two calls with one fluent chain or the same resolved local/parameter receiver in one executable scope. It separates lambda policies and opposite branches. It recognizes a preceding guard whose conjunction rejects the exact configuration flags required by both calls, with an unconditional `continue`, `return` or `throw`. Intervening writes invalidate that guard recognition. This is bounded static analysis, not general interprocedural control/data flow; arbitrary aliases, mutations through other methods and complex Boolean equivalence are outside its proof scope.

Imported vulnerabilities and authorization observations still participate in security scoring. Fixing false positives does not certify the target application as secure.

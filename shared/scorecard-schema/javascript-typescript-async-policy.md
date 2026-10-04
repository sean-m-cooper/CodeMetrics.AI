# JavaScript/TypeScript Async/Blocking Usage

Development ruleset `javascript-typescript-2026-10-03-owner-population` uses
`javascript-typescript/performanceAsync/owner-population-v3`. The JSON dimension remains
`performanceAsync`; its display name is **Async/Blocking Usage**. It describes
avoidable usage hazards, not latency, throughput or a workload's appropriate
concurrency. Cross-ecosystem calibration remains unestablished.

## Classification

| Pattern | Treatment | Boundary |
|---|---|---|
| Inline async standard Promise executor | Warning; scored | Constructor resolves to the TypeScript standard-library Promise declaration |
| Inline async Array/ReadonlyArray forEach callback | Info; excluded review lead | Resolved signature belongs to the standard Array contract |
| Conditional/repeated imported React hook | Warning; scored | Syntax ancestry within its owning function, excluding always-evaluated operands |
| Inline async React effect callback | Warning; scored | Existing imported useEffect/useLayoutEffect recognition |
| Effect without dependency array | Info; excluded review lead | Existing missing-argument observation; intent is not judged |

The Promise constructor does not adopt the async executor's returned Promise.
Rejection of that returned Promise therefore does not automatically reject the
constructed Promise. [ESLint's rule documentation](https://eslint.org/docs/latest/rules/no-async-promise-executor)
describes this failure mode. Recognition here is narrower than a name match: a
shadowed, imported or unresolved constructor named Promise is not assumed to be
the standard constructor. Inline parentheses and type assertions are transparent.

Array.forEach does not wait for callback Promises; the
[typescript-eslint documentation](https://typescript-eslint.io/rules/no-misused-promises/)
illustrates the corresponding void-return callback hazard. We retain this as a
review lead because intent, rejection handling and required ordering need context.
Sequential awaits and throttling receive no penalty from these rules.

React checks distinguish execution position: an if/ternary condition, a logical
left operand and a for-loop initializer/iterable execute before their associated
branch/iteration. Outer conditional contexts still apply. Loop bodies, repeated
tests/increments and per-iteration binding initializers remain restricted. React's
special [use API](https://react.dev/reference/react/use) is permitted in conditions
and loops and is not classified as an ordinary hook by this probe.

## Scoring and applicability

The `owner-rate-v3` policy replaces the provisional 6/10 cliff. Let N be the
number of distinct eligible owners, B the number of those owners with at least
one scored finding, and rate = 100 * B / N. Each owner contributes once to each
population, regardless of finding count. Comparisons use integer cross-products,
not a rounded display percentage.

| Affected owner rate | Score |
|---|---:|
| B = 0 or below 1% | 10 |
| Below 2% | 9 |
| Below 5% | 8 |
| At most 10% | 6 |
| At most 15% | 4 |
| At most 20% | 2 |
| Above 20% | 0 |

These bands implement product policy, not empirical cross-ecosystem calibration.
A 10 can contain findings below 1%; small populations can score 0 for one affected
owner. Report B and N with the score. Never interpret the score as runtime speed.

An eligible owner is an implemented function containing its own async modifier,
await/for-await, resolved standard Promise construction/method call, imported React
hook call, or recognized usage finding. Synchronous functions without these signals,
bodyless signatures, and a React import alone are excluded. Nested functions own
their own operations; a synchronous wrapper is not eligible merely because it
contains an async callback. Standard calls require TypeScript standard-library
signatures. Custom APIs and unresolved Promise names do not establish eligibility.

One synthetic module owner per file covers qualifying operations outside functions,
including module/class initialization. An async callback is an eligible function;
a misuse at its containing API call belongs to the enclosing call-site owner.
Thus a module-level async Promise executor can produce two eligible owners with
one affected. Mixed React and Promise populations share this product policy; they
are not calibrated against equivalent runtime workloads. The observations include
every owner, its eligibility reasons, and whether it is affected.

No eligible owners means skipped, not 10. A scored finding without an eligible
owner fails analysis instead of disappearing from the numerator. Parse failures
withhold all implemented source dimension scores while retaining partial findings.
Complexity, MI, and raw CSV formulas are unchanged.

Informational findings carry `classification: reviewLead` and
`scoreDisposition: excludedReviewLead`, have an `excluded` scoring effect, and do
not fail warning/error gates. Confidence describes the observation, not the wisdom
of the developer's business choice. General promise flow, safe concurrency,
referenced callbacks, constructor aliases, runtime replacement of standard APIs
and exhaustive Rules of Hooks analysis remain outside this scope.

## Compatibility

The new ruleset and `javascript-typescript/performanceAsync/owner-population-v3`
scope distinguish this policy from the earlier React-only and 6/10 usage policies. Consumers must honor recorded
scope instead of assuming every JS/TS score is React-only. Older evidence and its
scores remain valid historical records, but are incompatible baseline gates.
Existing finding identities remain stable; changing a review lead from warning
to info is an intentional correction. No .NET policy is changed.

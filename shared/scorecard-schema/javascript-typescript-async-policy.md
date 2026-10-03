# JavaScript/TypeScript Async/Blocking Usage

Development ruleset `javascript-typescript-2026-10-02-async-usage` uses
`javascript-typescript/performanceAsync/usage-v2`. The JSON dimension remains
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

The existing provisional 6/10 band structure is retained: one or more scored
signals select 6; otherwise an applicable scope scores 10. This extension does not
claim the numerical bands have been calibrated on representative JS/TS projects.
No percentage-of-functions scoring is introduced. Complexity, MI and raw CSV
formulas are unchanged.

React imports with executable functions, async functions, awaits or resolved
standard Promise constructions establish an applicable scope. No applicable
surface means skipped/unmeasured. Top-level operations are inspected independently
of function metric populations. Parse failures withhold all implemented source
dimension scores while retaining partial findings for diagnosis.

Informational findings carry `classification: reviewLead` and
`scoreDisposition: excludedReviewLead`, have an `excluded` scoring effect, and do
not fail warning/error gates. Confidence describes the observation, not the wisdom
of the developer's business choice. General promise flow, safe concurrency,
referenced callbacks, constructor aliases, runtime replacement of standard APIs
and exhaustive Rules of Hooks analysis remain outside this scope.

## Compatibility

The new ruleset and `javascript-typescript/performanceAsync/v2` scope distinguish
this coverage from the earlier React-only policy. Consumers must honor recorded
scope instead of assuming every JS/TS score is React-only. Older evidence and its
scores remain valid historical records, but are incompatible baseline gates.
Existing finding identities remain stable; changing a review lead from warning
to info is an intentional correction. No .NET policy is changed.

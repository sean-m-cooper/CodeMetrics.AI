# CodeMetrics.AI rule catalog

Generated from the catalog shipped in the analyzer package. Codes are permanent aliases; existing rule IDs remain stable. Number ranges identify dimensions (1000 architecture through 9000 maintainability).

Use `code-metrics rules --format json` for the installed package version and machine-readable definitions, or `code-metrics rules --code CMAI5001` to look up a code. Metric entries describe aggregate components; they are not emitted per-site diagnostics.

Only entries marked as supporting annotations accept CMAI comments. Place a directive immediately before the indicated syntax or its containing member. CMAI directives require a reason after ` -- ` or `—`; its content is accepted without evaluating the developer's business decision. Existing category directives retain their legacy behavior. Each code addresses only its own rule; one source site can participate in multiple rules. A declaration in evidence is not proof it matched an occurrence.

<a id="cmai1001"></a>

## CMAI1001: High structural coupling

- Identity: `dotnet/architecture/highCoupling`
- Dimension: `architecture`
- Kind: `finding`

Structural dependencies exceed the configured threshold. A coordination or factory role can explain the measurement; it does not establish a responsibility defect.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai1002"></a>

## CMAI1002: High class complexity

- Identity: `dotnet/architecture/highCyclomaticComplexity`
- Dimension: `architecture`
- Kind: `finding`

Aggregate type complexity exceeds the configured architecture threshold. Review the responsibilities and necessary branching.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai1003"></a>

## CMAI1003: Large class

- Identity: `dotnet/architecture/largeClass`
- Dimension: `architecture`
- Kind: `finding`

Included type source lines exceed the configured size threshold. Review the source context before recommending decomposition.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai1004"></a>

## CMAI1004: Project dependency cycle

- Identity: `dotnet/architecture/projectCycle`
- Dimension: `architecture`
- Kind: `finding`

Production project references form a cycle. The graph observation does not establish whether the dependency is accidental.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai1005"></a>

## CMAI1005: Controller data dependency

- Identity: `dotnet/architecture/controllerDataDependency`
- Dimension: `architecture`
- Kind: `finding`

A controller directly depends on a recognized data-access type. Review the intended orchestration and boundary responsibilities.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai1006"></a>

## CMAI1006: Concrete infrastructure dependency

- Identity: `dotnet/architecture/concreteInfrastructureDependency`
- Dimension: `architecture`
- Kind: `finding`

A source type depends on recognized concrete infrastructure. Review whether the chosen boundary is deliberate.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai2001"></a>

## CMAI2001: Decomposition distribution

- Identity: `dotnet/codeQuality/decomposition`
- Dimension: `codeQuality`
- Kind: `metric`

Scores executable complexity divided by decomposition-function count. Fields and bodyless members are excluded; named local helpers count like methods, and branch-free callbacks do not inflate the denominator. This ratio does not establish cohesion or readability.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai2002"></a>

## CMAI2002: Method complexity distribution

- Identity: `dotnet/codeQuality/methodComplexity`
- Dimension: `codeQuality`
- Kind: `metric`

Scores distinct authored executable functions: 40% worst individual score plus 60% mean of the remaining functions. Own CC anchors 3/5/10/20/40 map to 10/8/6/4/0 with linear interpolation. A single-function scope uses its individual score. Repeated source observations count once at maximum variant CC. Decomposition retains its separate type-instance population.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai3001"></a>

## CMAI3001: Placeholder test

- Identity: `dotnet/testing/placeholderTest`
- Dimension: `testing`
- Kind: `finding`

A recognized test has placeholder structure. Review its assertions and intended behavior.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai3002"></a>

## CMAI3002: Uncovered production project

- Identity: `dotnet/testing/uncoveredProject`
- Dimension: `testing`
- Kind: `finding`

The probe did not associate a production project with recognized test coverage signals. This is a heuristic, not proof that tests are absent.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai3003"></a>

## CMAI3003: Test and coverage signals

- Identity: `dotnet/testing/testSignals`
- Dimension: `testing`
- Kind: `metric`

Aggregates recognized test methods, assertions, project associations and optional supplied coverage. Static test signals do not establish test effectiveness.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai4001"></a>

## CMAI4001: Potential hardcoded secret

- Identity: `dotnet/security/hardcodedSecret`
- Dimension: `security`
- Kind: `finding`

Source matches a potential credential literal. Descriptive literals spelling the declaration name (including route, key and purpose identifiers) are excluded by a bounded lexical rule. Naming suffixes alone do not exempt credentials. Review actual use; never reproduce secret values in reports.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai4002"></a>

## CMAI4002: Interpolated raw SQL

- Identity: `dotnet/security/rawSqlInterpolation`
- Dimension: `security`
- Kind: `finding`

A recognized raw SQL API receives interpolated or concatenated input. Review parameterization and the origin of values.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai4003"></a>

## CMAI4003: Deserialization configuration

- Identity: `dotnet/security/unsafeDeserialization`
- Dimension: `security`
- Kind: `finding`

A recognized deserialization pattern can accept unsafe type information. Review trust boundaries and serializer settings.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai4004"></a>

## CMAI4004: Permissive credentialed CORS

- Identity: `dotnet/security/allowAnyOriginWithCredentials`
- Dimension: `security`
- Kind: `finding`

The same CORS builder enables permissive origins and credentials without a recognized rejecting guard. Separate policies and opposite branches are distinguished. Recognition is bounded static analysis; review the actual request boundary and configuration.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai4005"></a>

## CMAI4005: Anonymous access declaration

- Identity: `dotnet/security/allowAnonymous`
- Dimension: `security`
- Kind: `finding`

An AllowAnonymous declaration permits anonymous access. Review the endpoint's intended exposure; the declaration alone is not a vulnerability.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai4006"></a>

## CMAI4006: Unrecognized authorization boundary

- Identity: `dotnet/security/missingAuthorization`
- Dimension: `security`
- Kind: `finding`

The probe did not recognize an authorization boundary for a source endpoint. Global or external controls may provide that boundary.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai4007"></a>

## CMAI4007: Dependency vulnerability observations

- Identity: `dotnet/security/vulnerabilityExposure`
- Dimension: `security`
- Kind: `metric`

Security scoring includes vulnerable-package observations supplied by the dependency probe. Findings and advisory details reside in the dependency dimension.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai5001"></a>

## CMAI5001: Empty catch

- Identity: `dotnet/errorHandling/emptyCatch`
- Dimension: `errorHandling`
- Kind: `finding`

An empty catch has no recognized handling, local explanatory comment or accepted scoped suppression. Prose rationale is accepted as declared intent without judging the business decision. Empty/task-marker comments and commented-out statements do not qualify. Scoring uses the maximum issue weight per source catch within the catch population.

Excludes this rule occurrence before scoring; raw metrics are unchanged.

Supported scopes: catch, member. Rationale required.

```csharp
// codemetrics-ignore: CMAI5001 -- Cleanup failure must not replace the original exception.
```

<a id="cmai5002"></a>

## CMAI5002: Caught exception rethrow

- Identity: `dotnet/errorHandling/throwEx`
- Dimension: `errorHandling`
- Kind: `finding`

Rethrowing the caught exception variable resets its stack origin. A bare throw preserves the existing stack trace.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai5003"></a>

## CMAI5003: Unrecognized broad-catch handling

- Identity: `dotnet/errorHandling/broadCatchWithoutLoggingOrRethrow`
- Dimension: `errorHandling`
- Kind: `finding`

A broad catch has no recognized logging, propagation, exception-bearing return, invoked error callback, diagnostic output parameter or local explanatory comment. Review the surrounding handling path. Overlapping catch findings contribute only their maximum weight per source catch.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai5004"></a>

## CMAI5004: Default return after broad catch

- Identity: `dotnet/errorHandling/broadCatchReturnsDefault`
- Dimension: `errorHandling`
- Kind: `finding`

A broad catch returns a default value without a recognized handling path or local explanatory comment. Explicit diagnostic outputs can establish reporting. Method names alone do not establish a valid fallback contract. Scoring counts the source catch once at its highest issue weight.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai5005"></a>

## CMAI5005: Synchronous task boundary

- Identity: `dotnet/errorHandling/syncBlockingCall`
- Dimension: `errorHandling`
- Kind: `finding`

A recognized task wait may block the calling thread. Proven completion guards, including ternary branches, and narrowly resolved completed-return helpers are excluded. Synchronous method/property contracts, bounded all-uses private helper chains, recognized synchronous callback APIs and local explanations remain unscored review leads, consistently with CMAI8001. Other waits retain the existing error-handling cap. A successful void Task.Wait establishes completion for subsequent access; timed waits and swallowed wait failures do not. These classifications do not prove runtime safety or absence of preceding synchronous I/O.

Excludes this rule occurrence before scoring; raw metrics are unchanged.

Supported scopes: statement, member. Rationale required.

```csharp
// codemetrics-ignore: CMAI5005 -- The host requires a synchronous boundary.
```

<a id="cmai5006"></a>

## CMAI5006: Console output

- Identity: `dotnet/errorHandling/consoleWriteLine`
- Dimension: `errorHandling`
- Kind: `finding`

Source uses Console.WriteLine. This informational observation may be appropriate for command-line or diagnostic output.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai5007"></a>

## CMAI5007: Unrecognized reporting for multiple catches

- Identity: `dotnet/errorHandling/missingLoggerForMultipleCatches`
- Dimension: `errorHandling`
- Kind: `finding`

A type contains multiple catches without recognized handling paths or an ILogger member/parameter. Custom reporting and recovery may be intentional. This advisory adds no independent score reduction to the catch-population policy.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai6001"></a>

## CMAI6001: Unresolved documentation reference

- Identity: `dotnet/documentation/unresolvedCref`
- Dimension: `documentation`
- Kind: `finding`

An XML documentation cref could not be resolved. Review the referenced symbol and compilation context.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai6002"></a>

## CMAI6002: Documentation presence and content signals

- Identity: `dotnet/documentation/documentationSignals`
- Dimension: `documentation`
- Kind: `metric`

Aggregates README, documentation directory, architecture documents, AI instructions, XML documentation, public API comments and stale-marker signals. This is a partial documentation assessment.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai7001"></a>

## CMAI7001: Vulnerable direct dependency

- Identity: `dotnet/dependencyManagement/vulnerableDirectDependency`
- Dimension: `dependencyManagement`
- Kind: `finding`

The configured package feed reports a vulnerability for a direct dependency. Review advisory applicability and available remediation.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai7002"></a>

## CMAI7002: Vulnerable transitive dependency

- Identity: `dotnet/dependencyManagement/vulnerableTransitiveDependency`
- Dimension: `dependencyManagement`
- Kind: `finding`

The configured package feed reports a vulnerability for a transitive dependency. Review the dependency path and advisory applicability.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai7003"></a>

## CMAI7003: Deprecated dependency

- Identity: `dotnet/dependencyManagement/deprecatedDependency`
- Dimension: `dependencyManagement`
- Kind: `finding`

The package feed reports deprecation. Review its stated reason and any replacement; deprecation alone does not establish upgrade compatibility.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai7004"></a>

## CMAI7004: Outdated dependency

- Identity: `dotnet/dependencyManagement/outdatedDependency`
- Dimension: `dependencyManagement`
- Kind: `finding`

The package feed reports a newer version. Evidence records target-framework compatibility and scoring exclusions; compatible assets do not establish a safe upgrade.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai7005"></a>

## CMAI7005: Unsupported target framework signal

- Identity: `dotnet/dependencyManagement/unsupportedTargetFramework`
- Dimension: `dependencyManagement`
- Kind: `finding`

The probe's framework policy flags a target framework. Review the recorded policy version and the project's compatibility constraints.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai7006"></a>

## CMAI7006: Package version drift

- Identity: `dotnet/dependencyManagement/versionDrift`
- Dimension: `dependencyManagement`
- Kind: `finding`

Enabled projects specify differing versions of a package. Review whether the divergence reflects intentional compatibility boundaries.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai7007"></a>

## CMAI7007: Central package management signal

- Identity: `dotnet/dependencyManagement/noCentralPackageManagement`
- Dimension: `dependencyManagement`
- Kind: `finding`

The probe did not find the expected central package management configuration. Review whether centralization fits the repository.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai7008"></a>

## CMAI7008: Dependency observation failure

- Identity: `dotnet/dependencyManagement/dependencyProbeFailure`
- Dimension: `dependencyManagement`
- Kind: `finding`

A package query failed or produced unusable evidence. Dependency results are unavailable; this is an execution diagnostic, not a source-code defect.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai8001"></a>

## CMAI8001: Synchronous wait on asynchronous work

- Identity: `dotnet/performanceAsync/syncOverAsync`
- Dimension: `performanceAsync`
- Kind: `finding`

A recognized task wait may block the calling thread. Proven Task/ValueTask guards, completed ternary branches and narrowly resolved completed-return helpers are excluded. Synchronous interface/override methods and properties, bounded all-uses private helper chains, recognized synchronous callback APIs and local explanations remain unscored review leads. Arbitrary callbacks, mixed callers, async contracts and unknown virtual implementations are not exempted. A contract or explanation is not proof of runtime safety, and completed-return helpers may still perform synchronous I/O. Counts once per physical source span and rule at maximum severity across frameworks. A successful void Task.Wait establishes completion for subsequent access; timed waits and swallowed wait failures do not.

Excludes this rule occurrence before scoring; raw metrics are unchanged.

Supported scopes: statement, member. Rationale required.

```csharp
// codemetrics-ignore: CMAI8001 -- The host requires a synchronous boundary.
```

<a id="cmai8002"></a>

## CMAI8002: Thread sleep

- Identity: `dotnet/performanceAsync/threadSleep`
- Dimension: `performanceAsync`
- Kind: `finding`

Thread.Sleep blocks the calling thread. Synchronous contracts or bounded local blocking rationale make this an unscored review lead; otherwise it retains warning severity. Review runtime cost before changing the execution contract. Counts once per physical source span and rule at maximum severity across frameworks; observations and classification reasons remain in evidence.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai8003"></a>

## CMAI8003: Persistence inside a loop

- Identity: `dotnet/performanceAsync/saveChangesInsideLoop`
- Dimension: `performanceAsync`
- Kind: `finding`

SaveChanges occurs inside a loop. This is an unscored review lead: existing batches, session lifetime, transactions and notifications may require persistence there. Evidence identifies recognized iteration-scoped using declarations. Do not prescribe moving persistence outside a loop without reviewing those boundaries. Counts once per physical source span and rule at maximum severity across frameworks; observations and classification reasons remain in evidence.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai8004"></a>

## CMAI8004: Cancellation propagation signal

- Identity: `dotnet/performanceAsync/missingCancellationToken`
- Dimension: `performanceAsync`
- Kind: `finding`

An asynchronous API has I/O-like calls without a recognized cancellation input. Direct tokens and context parameters with an accessible token member forwarded to task-returning calls are recognized. Other signatures remain unscored review leads; a missing parameter alone does not prove dropped cancellation. Counts once per physical source span and rule at maximum severity across frameworks; observations and classification reasons remain in evidence.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai8005"></a>

## CMAI8005: Query materialization order

- Identity: `dotnet/performanceAsync/materializationBeforeQueryShape`
- Dimension: `performanceAsync`
- Kind: `finding`

A query is materialized before recognized shaping operations. This is an unscored review lead because provider semantics, snapshots and input size can justify the boundary. Counts once per physical source span and rule at maximum severity across frameworks; observations and classification reasons remain in evidence.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai8006"></a>

## CMAI8006: Sequential awaited I/O

- Identity: `dotnet/performanceAsync/awaitedIoInsideLoop`
- Dimension: `performanceAsync`
- Kind: `finding`

Recognized I/O is awaited within a loop. This is an unscored review lead: ordering, shared state, early exit and back-pressure can require sequential execution. Review those constraints before proposing concurrency. Counts once per physical source span and rule at maximum severity across frameworks; observations and classification reasons remain in evidence.

Excludes this rule occurrence before scoring; raw metrics are unchanged.

Supported scopes: loop, member. Rationale required.

```csharp
// codemetrics-ignore: CMAI8006 -- Shared DbContext and transaction ordering require sequential execution.
```

<a id="cmai8007"></a>

## CMAI8007: Unbounded asynchronous fan-out

- Identity: `dotnet/performanceAsync/unboundedWhenAll`
- Dimension: `performanceAsync`
- Kind: `finding`

A recognized Task.WhenAll construction has no detected concurrency bound. This remains an unscored review lead; review actual input size and resource limits. Counts once per physical source span and rule at maximum severity across frameworks; observations and classification reasons remain in evidence.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai8008"></a>

## CMAI8008: Shared-state mutation during fan-out

- Identity: `dotnet/performanceAsync/sharedStateMutationInFanOut`
- Dimension: `performanceAsync`
- Kind: `finding`

Concurrent work appears to mutate shared state. Review synchronization, ownership and task execution behavior. Counts once per physical source span and rule, using the highest observed severity across frameworks; framework observations remain in evidence.

Comment exclusion is not implemented for this rule in this analyzer version.

<a id="cmai9001"></a>

## CMAI9001: Maintainability index distribution

- Identity: `dotnet/maintainability/maintainabilityIndex`
- Dimension: `maintainability`
- Kind: `metric`

Scores distinct authored executable functions: 40% of the weakest fifth mean plus 60% of the remaining mean. Each function contributes once; enum and non-executable declarations give no credit. Own MI uses exclusively owned body CC, tokens and source lines. Repeated source observations count once at minimum MI. MI 40/52/58/65/70/75 maps linearly to 0/2/4/6/8/10. Empty populations are unmeasured; distribution statistics are diagnostic only. Explicit legacy inputs retain the labeled type policy.

Comment exclusion is not implemented for this rule in this analyzer version.

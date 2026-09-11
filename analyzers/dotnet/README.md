# CodeMetrics.AI

A .NET 10 global tool that performs deterministic static analysis on .NET solutions using the Roslyn compiler API. Produces VS-compatible code metrics and scored scorecard evidence across 9 quality dimensions.

## Install

```bash
dotnet tool install -g CodeMetrics.AI
```

## Usage

```bash
# Auto-discover solution in current directory
code-metrics

# Specify solution explicitly
code-metrics --solution MyApp.slnx

# Skip dependency probe (avoids dotnet list package calls)
code-metrics --skip-dependency-probe

# Custom output paths
code-metrics --output ./results/metrics.csv --scorecard-output ./results/evidence.json
```

## Output

| File | Description |
|------|-------------|
| `.scorecard/dotnet/metrics.csv` | VS-compatible raw metrics (same format as Visual Studio's Code Metrics Results) |
| `.scorecard/dotnet/evidence.json` | Scored evidence across 9 dimensions (schema v3) |

## Dimensions

The tool scores your codebase across 9 quality dimensions (0-10 scale):

| Dimension | Method |
|-----------|--------|
| Code Quality | Statistical — decomposition ratio and max member cyclomatic complexity |
| Maintainability | Statistical — maintainability index population/tail/extreme analysis |
| Error Handling | Rule-based — empty catches, throw ex, broad catches, sync blocking |
| Performance & Async | Rule-based — sync-over-async, sequential I/O, unbounded fan-out, shared-state concurrency |
| Security | Rule-based — hardcoded secrets, SQL interpolation, unsafe deserialization |
| Testing | Rule-based — test coverage, assertion density, placeholder detection |
| Documentation | Deduction-based — README, docs/, XML docs, public API coverage, unresolved `cref` references |
| Dependency Management | Rule-based — vulnerabilities, outdated, deprecated, version drift; failed commands are unscored |
| Architecture & SOLID | Rule-based — project cycles, layering violations, metric hotspots |

## Raw Metrics

Per-type and per-member metrics collected via Roslyn:

- **Cyclomatic Complexity** — decision point counting
- **Lines of Code** — source lines (excluding comments/blanks/braces) and executable statements
- **Maintainability Index** — composite of CC, LOC, and Halstead Volume
- **Class Coupling** — distinct external type dependencies
- **Depth of Inheritance** — base type chain length

## MSBuild Integration

Copy `Directory.Build.targets` from the [scorecard-tooling](https://github.com/sean-m-cooper/ai_tools/tree/main/skills/code-scorecard/scorecard-tooling) directory to your solution root:

```bash
dotnet build /t:Scorecard
dotnet build /t:Scorecard /p:ScorecardConfiguration=Release
```

## Options

| Option | Default | Description |
|--------|---------|-------------|
| `--solution` | Auto-discover | Path to .sln or .slnx file |
| `--output` | `.scorecard/dotnet/metrics.csv` | CSV output path |
| `--scorecard-output` | `.scorecard/dotnet/evidence.json` | JSON evidence output path |
| `--configuration` | `Debug` | Build configuration |
| `--skip-dependency-probe` | `false` | Skip dependency management checks |

## Project Filtering

The tool automatically skips non-production projects:

- Test projects (name contains "Tests")
- Aspire hosts (AppHost, ServiceDefaults, Hosting)
- Benchmarks, Samples, Demo, Playground projects

## Code annotations and recognized attributes

CodeMetrics.AI recognizes category-scoped suppression comments and selected framework attributes whose meaning affects a scorecard dimension. It does not require a CodeMetrics.AI package reference in the analyzed solution.

### Category-scoped suppression

Place a directive immediately before the affected statement, loop, catch, or method:

```csharp
// codemetrics-ignore: awaitedIoInsideLoop — shared DbContext; sequential by design
foreach (var item in items)
{
    await repository.GetAsync(item.Id, ct);
}
```

The supported categories are `awaitedIoInsideLoop`, `syncOverAsync`,
`syncBlockingCall`, and `emptyCatch`. Use `all` or `*` only when every supported
finding in that scope has been reviewed. The optional text after `—` or `--` records
the reason without affecting matching.

### Intentional synchronous code

The legacy `// amp-metrics: sync-required` method annotation remains recognized for
backward compatibility when a synchronous boundary cannot safely be converted to async:

```csharp
// amp-metrics: sync-required
public void RunSynchronously()
{
    var result = operation.GetAwaiter().GetResult();
}
```

For that method, the annotation:

- Changes sync-over-async findings from `error` to `info`.
- Suppresses `awaitedIoInsideLoop` findings.

Use it narrowly: the annotation records an architectural constraint; it does not make blocking or sequential I/O faster.

### Dependency injection

Parameters decorated with `[FromServices]` are excluded from the broad raw class-coupling
census, but included in structural coupling as real method-injected collaborators:

```csharp
public IActionResult Get([FromServices] IReportBuilder reports)
{
    return Ok(reports.Build());
}
```

`[FromServicesAttribute]` and namespace-qualified forms are also recognized.

The Architecture evidence retains that information separately under
`extra.controllerActionCoupling`. Each controller profile reports constructor dependency
count, maximum per-action raw and structural type coupling, maximum `[FromServices]`
parameter count, and an action-by-action breakdown. Per-action values are supplemental;
the containing type's structural coupling drives `highCoupling` scoring.

Service dependencies are classified as interfaces or concrete classes through Roslyn
symbols. Type-name conventions such as an `I` prefix and namespace fragments such as
`.Interfaces` are not used to decide whether `concreteInfrastructureDependency` applies.

DI registration extension types require no annotation. Static types whose exposed extension methods target `IServiceCollection` or recognized host/application builders are excluded from Architecture hotspot penalties because their coupling is intentional composition-root wiring.

Architecture preserves raw `classCoupling` and `coupledTypes` for compatibility and audit
evidence, but `highCoupling` findings use a separate structural count. Structural coupling
includes constructor and primary-constructor dependencies, fields and injected properties,
`[FromServices]` parameters, constructed behavioral types, invoked collaborators, static
helpers, and reflection targets. It excludes:

- Types used only as method payloads, return values, or local data flow
- Passive data carriers
- Attributes, anonymous types, and tuples
- Common value and container types such as `Guid`, `DateTime`, `CancellationToken`,
  collections, tasks, and delegates
- ASP.NET Core presentation contracts such as `ControllerBase` and `IActionResult`

Filtering is role- and symbol-based rather than a blanket `System.*`,
`Microsoft.AspNetCore.*`, or `Microsoft.Extensions.*` namespace exclusion. Behavioral
dependencies such as `HttpClient`, `ILogger<T>`, caches, options, and `DbContext` remain
eligible when structurally referenced. Structural `highCoupling` thresholds are 10 for
general types and 8 for controllers.

Raw coupling is also not used as an Architecture hotspot signal for subclasses of framework contracts whose required surface dominates the metric, currently `AuthenticationHandler<TOptions>` and `DbContext`. Complexity and class-size findings still apply to those types, and their raw coupling remains in `metrics.csv`.

Executable `Program` and `Startup` composition roots are likewise excluded only from
the Architecture `highCoupling` hotspot. Raw coupling, complexity, and size remain
available. `extra.couplingProvenance` reports every structural hotspot and every type that
would have crossed the former raw threshold, including raw and structural symbols, exclusions
grouped by reason, and whether the raw count alone would have triggered a finding.
`extra.excludedCouplingReferencesByReason` aggregates those exclusions across the solution.

### Async concurrency semantics

`unboundedWhenAll` is reported only when the analyzer can see a deferred task-producing projection over a source whose cardinality is not bounded at the call site. Passing an already-created task collection to `Task.WhenAll` is not itself treated as creating concurrency. Constructor-materialized strategy sets and fixed inline collections are treated as startup- or author-bounded.

Accessing `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` is not reported when the
same task symbol is provably complete after a dominating `await Task.WhenAll(...)` in
the same block. A task returned by `await Task.WhenAny(...)` is also known complete;
merely being passed into `WhenAny` is not sufficient.

ASP.NET Core middleware entry points are exempt from `missingCancellationToken` when
their symbols match conventional middleware (`Invoke`/`InvokeAsync` on a `*Middleware`
type with `HttpContext` first) or an `IMiddleware` implementation. Request cancellation
for these entry points is provided through `HttpContext.RequestAborted`.

Back-pressure is recognized through `ChannelWriter`, `ChannelReader`, and `SemaphoreSlim`, including authored wrapper methods and interface contracts when every analyzed implementation delegates to a recognized back-pressure primitive.

The Performance & Async dimension also reports `sharedStateMutationInFanOut` when a concurrent projection passes captured state to an authored implementation that demonstrably mutates that parameter-reachable object graph.

### Evidence population and samples

Starting with 2.2.0, every scored dimension also includes `scoringDecision`, recording policy inputs, selected rules, nested components, binding/nonbinding caps and finding effects. The existing scores and `dotnet-2026-09-08` ruleset are unchanged. See [the decision contract](../../shared/scorecard-schema/scoring-decisions.md); these effects are not independent finding deductions.

Starting with 2.1.0 (`dotnet-2026-09-08`), Architecture metric hotspots use a population/severity policy. Coupling, complexity, and size each have a component score:

```text
population penalty = min(6, 12 × hotspot count / eligible type count)
severity penalty   = min(4, 2 × max(0, worst threshold ratio − 1))
component score    = 10 − population penalty − severity penalty
architecture score = min(component scores, graph/layering cap), rounded to 1 decimal
```

The population penalty reaches six points when at least half the eligible types are hotspots. The severity penalty reaches four points when the worst type reaches three times its threshold. Using the worst component avoids adding three penalties for one type that is large, complex, and coupled. One mildly coupled type among 100 eligible types (11 dependencies, threshold 10) scores 9.7; 50 types at the threshold score 4.0. These are explicit heuristic policy choices, covered by regression tests, not empirically calibrated universal quality thresholds.

Each component counts its entire eligible population, including types below the hotspot threshold. Passive data carriers and DI extension types are excluded from all three components; framework archetypes and application composition roots are excluded from coupling only. Coupling uses each type's structural threshold (10, or 8 for controllers), with the existing raw fallback when structural metrics are unavailable. Complexity uses the smaller of `CC / 80` and `density / 8`, so both thresholds must be reached. Size uses source lines / 500. Empty eligible populations contribute no metric penalty and are explicitly recorded with count zero.

Project cycles still cap the score at 0; layering errors cap it at 2; one, two, or more advisory layering warnings cap it at 8, 6, or 4. Metric warnings do not also count toward these caps. `architectureMetrics` records the formula, denominators, rates, worst ratios, penalties, and cap. High confidence in a metric means confidence in the measurement; a hotspot remains a lead for design review.

Architecture `findings` and `hotspotCount` describe the complete hotspot population. The `hotspots` property remains a top-ten presentation sample; `hotspotsTruncated` states whether additional findings exist. When truncated, `basis` reports both the population and displayed count.

If any `dotnet list package` invocation fails, Dependency Management returns `status: "failed"` without a score. Its basis and `dependencyCommands` diagnostics identify the failing arguments, exit code or exception, and a bounded stderr summary.

The CLI requests NuGet JSON output version 1. Missing, malformed, or unsupported reports also fail the dependency probe. Each vulnerable, deprecated, or outdated package occurrence has a finding identifying its project and target framework. `observations` preserves requested/resolved/latest versions when provided, dependency kind, deprecation reasons, suggested alternative package/version range, and vulnerability severities/advisory URLs. Missing metadata remains null. Counts mean package occurrences per project and target framework, not distinct package IDs or advisory counts. Dependency score thresholds are unchanged.

Outdated findings distinguish scored candidates, framework-incompatible exclusions, and Aspire exclusions. Framework compatibility can be `compatible`, `incompatible`, or `unknown`; compatible assets do not guarantee an upgrade has no breaking changes. Excluded candidates are informational and do not restore an outdated penalty.

Outdated-package scoring inspects the reported latest package version's `ref`, `lib`,
runtime-library, dependency-group, build, and tool target frameworks. An upgrade is excluded
when none of those assets are compatible with the consuming project TFM. Each target-framework
section of a multi-target project is evaluated independently, so a `net10.0`-only package does
not penalize a `net9.0` target but remains a valid upgrade for a `net10.0` target.

Package metadata is read from the local NuGet cache or the package sources reported by
`dotnet list package`. Compatibility checks have bounded download size, concurrency, and time.
If package metadata or a TFM cannot be resolved, the upgrade remains scored rather than being
silently suppressed. Evidence reports `outdatedFrameworkIncompatibleExcluded`,
`outdatedFrameworkCompatibilityUnknown`, and the corresponding package/project/TFM details.
Vulnerability and deprecation checks are unaffected.

Outdated packages belonging to projects identified by `Aspire.AppHost.Sdk` or
`Aspire.Hosting.AppHost` do not contribute to the package-upgrade penalty because the
AppHost is local orchestration infrastructure. The excluded count is reported as
`outdatedAspireExcluded`. Vulnerable and deprecated Aspire dependencies remain visible
and scored.

### Passive request and response types

Passive data carriers are identified structurally rather than by names such as `Request`, `Response`, or `Dto`. Records, classes, and structs that only declare state through primary-constructor parameters, auto-properties, fields, or assignment-only constructors are treated as data carriers.

Their raw metrics remain in `metrics.csv`, but they are excluded from the scored Code Quality,
Maintainability, and Architecture-hotspot populations. References to them remain visible in raw
coupling evidence but do not contribute to structural `highCoupling` scoring.

Types are scored normally as soon as they define behavior, including methods, computed properties, custom accessors, operators, validation logic, or nontrivial constructor logic.

`Program` and `Startup` composition-root types remain in `metrics.csv` and in the scored
Maintainability population, but receive a 10-point MI adjustment when thresholds are
evaluated and stay out of the general offender sample. This gives their expected
registration density more room without hiding a severely degraded composition root.

Documented empty catches are exempt only for a narrow exception type in a conservative
try/fallback shape: the catch contains an explanatory comment, the try has a success
return, and the immediately following statement returns the fallback value. Broad or
undocumented empty catches remain errors.

### Authorization

The Security dimension recognizes `[Authorize]` and `[AllowAnonymous]`, including `Attribute`-suffixed and namespace-qualified forms.

- The presence of a symbol-resolved `[Authorize]` signals that the project uses authorization.
- A controller- or base-type `[Authorize]` prevents a `missingAuthorization` finding for that controller.
- A controller- or base-type `[AllowAnonymous]` also prevents `missingAuthorization`, but produces an `allowAnonymous` warning for manual review when present in source.
- A controller without a class-level attribute is not flagged when every public action explicitly declares `[Authorize]` or `[AllowAnonymous]` intent.
- `[AllowAnonymous]` on an individual member likewise produces an `allowAnonymous` warning.

Attributes across partial declarations of the same controller are evaluated through the
controller's Roslyn symbol, so same-named controllers in different namespaces are not
merged. Because global MVC filters and fallback policies may still protect a controller,
`missingAuthorization` findings carry medium confidence and explicitly request effective-policy verification.

### Test frameworks

Test projects are detected by project name or by methods decorated with one of these attributes:

- `Fact`
- `Theory`
- `Test`
- `TestCase`
- `TestMethod`
- `DataTestMethod`

Attributes may use the `Attribute` suffix or a namespace qualifier, such as `[FactAttribute]` or `[Xunit.Fact]`.

Skipped or ignored tests are counted when a supported test attribute has a named `Skip` or `Ignore` argument:

```csharp
[Fact(Skip = "temporarily disabled")]
public void Uses_xunit_skip() { }

[Test(Ignore = "temporarily disabled")]
public void Uses_named_ignore() { }
```

Standalone framework-specific ignore attributes are not currently interpreted; only named arguments on the supported test attributes above are counted.

### No general exclusion attribute

CodeMetrics.AI does not provide a custom attribute for excluding arbitrary production types or members from metrics. Production code in analyzed projects remains included unless a documented semantic exemption applies or the project/file is excluded by the filtering rules above.

## Requirements

- .NET 10 SDK
- Solution must be buildable (`dotnet build` succeeds)

## License

[MIT](../../LICENSE)

## Evidence v3 and execution status

Version 2.0 emits schema v3. See the [migration guide](../../shared/scorecard-schema/migration-v3.md) and [comparison/CI guide](../../docs/evidence-workflows.md). Raw CSV columns remain unchanged. `--configuration` controls the actual Roslyn/MSBuild load. Missing or ambiguous solutions, workspace/compilation failures, and explicit unusable coverage return exit code 2. Incomplete source analysis produces partial findings with failed, unscored source dimensions. Cancellation returns 130.

Starting with 2.0.1, project-loading warnings (including NuGet vulnerability advisories) are retained in `analysis.diagnostics` with kind `workspaceWarning` and do not invalidate otherwise complete source analysis. The CLI recovers their original severity from temporary logs of the same MSBuild design-time load, then removes those logs. Actual errors, warnings promoted to errors, and unclassified workspace failures remain blocking. The dependency/security probes still report dependency risks; no warning settings or audit checks are disabled.

## Coverage inputs

Use `--coverage path/to/coverage.cobertura.xml` for an explicit report. Otherwise the analyzer checks `.scorecard/coverage.cobertura.xml` beneath the solution directory. Evidence records the report path, content SHA-256, matching status, matched/unmatched files, and nullable branch rate. When file-level observations exist, only matching production files contribute line coverage. Root-only reports retain aggregate compatibility and are labeled `aggregateUnverified`; they cannot establish project coverage. An explicitly requested missing, invalid, or unmatched report fails the testing dimension. The analyzer reads coverage; it does not execute tests or generate coverage.

`--solution` also accepts an explicit `.csproj`; this scores only that project, with references available for semantic resolution. Source filtering remains bounded by the entry point directory. See [consumer integration](../../docs/evidence-workflows.md#skill-and-other-evidence-consumers) for structured scope and validated v2/v3 reads.

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
| `.scorecard/dotnet/evidence.json` | Scored evidence across 9 dimensions (schema v2) |

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

CodeMetrics.AI provides one analyzer-specific comment annotation and recognizes selected framework attributes whose meaning affects a scorecard dimension. It does not require a CodeMetrics.AI package reference in the analyzed solution.

### Intentional synchronous code

Place `// amp-metrics: sync-required` immediately before a method when a synchronous boundary is intentional and cannot safely be converted to async:

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

Parameters decorated with `[FromServices]` are excluded from class-coupling calculations. This prevents action-level dependency injection from making the containing controller appear more coupled than it is:

```csharp
public IActionResult Get([FromServices] IReportBuilder reports)
{
    return Ok(reports.Build());
}
```

`[FromServicesAttribute]` and namespace-qualified forms are also recognized.

DI registration extension types require no annotation. Static types whose exposed extension methods target `IServiceCollection` or recognized host/application builders are excluded from Architecture hotspot penalties because their coupling is intentional composition-root wiring.

Raw coupling is also not used as an Architecture hotspot signal for subclasses of framework contracts whose required surface dominates the metric, currently `AuthenticationHandler<TOptions>` and `DbContext`. Complexity and class-size findings still apply to those types, and their raw coupling remains in `metrics.csv`.

### Async concurrency semantics

`unboundedWhenAll` is reported only when the analyzer can see a deferred task-producing projection over a source whose cardinality is not bounded at the call site. Passing an already-created task collection to `Task.WhenAll` is not itself treated as creating concurrency. Constructor-materialized strategy sets and fixed inline collections are treated as startup- or author-bounded.

Back-pressure is recognized through `ChannelWriter`, `ChannelReader`, and `SemaphoreSlim`, including authored wrapper methods and interface contracts when every analyzed implementation delegates to a recognized back-pressure primitive.

The Performance & Async dimension also reports `sharedStateMutationInFanOut` when a concurrent projection passes captured state to an authored implementation that demonstrably mutates that parameter-reachable object graph.

### Evidence population and samples

Architecture `findings` and `hotspotCount` describe the complete hotspot population. The `hotspots` property remains a top-ten presentation sample; `hotspotsTruncated` states whether additional findings exist. When truncated, `basis` reports both the population and displayed count.

If any `dotnet list package` invocation fails, Dependency Management returns `status: "failed"` without a score. Its basis and `dependencyCommands` diagnostics identify the failing arguments, exit code or exception, and a bounded stderr summary.

### Passive request and response types

Passive data carriers are identified structurally rather than by names such as `Request`, `Response`, or `Dto`. Records, classes, and structs that only declare state through primary-constructor parameters, auto-properties, fields, or assignment-only constructors are treated as data carriers.

Their raw metrics remain in `metrics.csv`, but they are excluded from the scored Code Quality, Maintainability, and Architecture-hotspot populations. A data carrier still counts as a dependency of code that consumes it.

Types are scored normally as soon as they define behavior, including methods, computed properties, custom accessors, operators, validation logic, or nontrivial constructor logic.

### Authorization

The Security dimension recognizes `[Authorize]` and `[AllowAnonymous]`, including `Attribute`-suffixed and namespace-qualified forms.

- The presence of `[Authorize]` signals that the project uses authorization.
- A controller-level `[Authorize]` prevents a `missingAuthorization` finding for that controller.
- A controller-level `[AllowAnonymous]` also prevents `missingAuthorization`, but produces an `allowAnonymous` warning for manual review.
- `[AllowAnonymous]` on an individual member likewise produces an `allowAnonymous` warning.

Attributes across partial declarations of the same controller are evaluated together.

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

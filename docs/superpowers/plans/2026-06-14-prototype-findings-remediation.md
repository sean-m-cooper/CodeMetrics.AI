# Prototype Findings Remediation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce false positives reported from the earlier CodeMetrics.AI prototype while preserving the current scorecard contract and green test suite.

**Architecture:** Keep remediation inside the existing probe and metric classes; this repo does not currently have `ProjectLoader`, `ArchitectureProbe.AnalyzeAsync`, or `ScorecardEvidenceBuilder` types. Prefer small syntax/semantic helpers inside the owning probe first, and only introduce shared helpers if two probes need the same suppression semantics.

**Tech Stack:** .NET 10, C#, Roslyn syntax/semantic APIs, xUnit, FluentAssertions.

---

## Current-State Evaluation

| Prototype finding | Current project status | Evidence |
| --- | --- | --- |
| #1 `[FromServices]` coupling and controller threshold | Still applicable. Coupling walks all descendants and method symbols without recognizing `[FromServices]`; controller coupling threshold is fixed at 30. | `src/CodeMetrics.AI/Metrics/ClassCouplingCalculator.cs:23`, `src/CodeMetrics.AI/Probes/ArchitectureProbe.cs:362` |
| #2 concrete infrastructure flags interfaces/framework types | Partially fixed for interface-looking constructor parameter text, but not framework namespaces. Current analyzer does not use symbols in architecture layering. | `src/CodeMetrics.AI/Probes/ArchitectureProbe.cs:254`, `src/CodeMetrics.AI/Probes/ArchitectureProbe.cs:290` |
| #3 `Task.WhenAll(t1, t2)` params overload | Already fixed. Multiple arguments are not flagged and a test exists. | `src/CodeMetrics.AI/Probes/PerformanceAsyncProbe.cs:321`, `tests/CodeMetrics.AI.Tests/Probes/PerformanceAsyncProbeTests.cs:536` |
| #4 intentional sequential loops via `sync-required` | Still applicable. There is no suppression comment helper. | `src/CodeMetrics.AI/Probes/PerformanceAsyncProbe.cs:269` |
| #5 middleware `InvokeAsync(HttpContext)` cancellation token | Still applicable. Missing-cancellation analyzer has no middleware convention exclusion. | `src/CodeMetrics.AI/Probes/PerformanceAsyncProbe.cs:185` |
| #6 missing authorization on partial controllers | Still applicable. Missing authorization is project-wide, but each class declaration is evaluated alone. | `src/CodeMetrics.AI/Probes/SecurityProbe.cs:333` |
| #7 intentionally public endpoints | No code change by default. Keep documented/manual-review behavior unless a suppression comment feature is explicitly desired. | `src/CodeMetrics.AI/Probes/SecurityProbe.cs:238` |
| #8 Program/Startup in maintainability top offenders | Still applicable for `Program`; current top offender list includes all types. `Startup` is also not excluded in current maintainability output. | `src/CodeMetrics.AI/Probes/MaintainabilityProbe.cs:33` |
| #9 decomposition ratio for single-method classes | Still applicable. Current eligibility is `MemberCount > 0`, so single-member types affect scoring/offenders. | `src/CodeMetrics.AI/Probes/CodeQualityProbe.cs:10` |
| #10 multi-TFM project name suffixes | Still applicable if Roslyn/MSBuild names projects with target framework suffixes. Current comparison uses raw names. | `src/CodeMetrics.AI/Probes/TestingProbe.cs:298`, `src/CodeMetrics.AI/Probes/TestingProbe.cs:316` |
| #11 cursor pagination loop suppression | Still applicable. Awaited I/O loop analyzer only checks loop and method name. | `src/CodeMetrics.AI/Probes/PerformanceAsyncProbe.cs:269` |
| #12 sync-over-async interface/override downgrades and suppression | Still applicable. Current severity is always `error` for `.Result`, `.Wait()`, and `.GetAwaiter().GetResult()`. | `src/CodeMetrics.AI/Probes/PerformanceAsyncProbe.cs:66` |
| #13 NuGet/out-of-solution source documents | Still applicable. Current collection/probes iterate every compilation syntax tree; there is no solution-root file filter. | `src/CodeMetrics.AI/Metrics/MetricsCollector.cs:14`, `src/CodeMetrics.AI/Probes/PerformanceAsyncProbe.cs:15`, `src/CodeMetrics.AI/SolutionAnalyzer.cs:66` |

Baseline verification before remediation: `dotnet test` passes with 266 tests.

## File Structure

- Modify `src/CodeMetrics.AI/Metrics/ClassCouplingCalculator.cs`: skip `[FromServices]` parameters when collecting method parameter coupling.
- Modify `src/CodeMetrics.AI/Probes/ArchitectureProbe.cs`: use symbol-aware constructor parameter metadata, framework namespace guard, and controller-specific high-coupling threshold.
- Modify `src/CodeMetrics.AI/Probes/PerformanceAsyncProbe.cs`: add suppression-comment helper, middleware exclusion, sync-over-async downgrade logic, and cursor-pagination detection.
- Modify `src/CodeMetrics.AI/Probes/SecurityProbe.cs`: aggregate partial controller attributes before emitting `missingAuthorization`.
- Modify `src/CodeMetrics.AI/Probes/MaintainabilityProbe.cs`: exclude `Program` and `Startup` top offenders.
- Modify `src/CodeMetrics.AI/Probes/CodeQualityProbe.cs`: remove single-member types from decomposition scoring/offenders.
- Modify `src/CodeMetrics.AI/Probes/TestingProbe.cs`: normalize target-framework suffixes before production/test project comparison.
- Modify `src/CodeMetrics.AI/SolutionAnalyzer.cs` and/or probe entrypoints: filter syntax trees to solution-root documents and exclude `.nuget/packages`.
- Test files: add focused tests in existing `ClassCouplingTests`, `ArchitectureProbeTests`, `PerformanceAsyncProbeTests`, `SecurityProbeTests`, `MaintainabilityProbeTests`, `CodeQualityProbeTests`, and `TestingProbeTests`.

### Task 1: Coupling False Positives

**Files:**
- Modify: `src/CodeMetrics.AI/Metrics/ClassCouplingCalculator.cs`
- Modify: `src/CodeMetrics.AI/Probes/ArchitectureProbe.cs`
- Test: `tests/CodeMetrics.AI.Tests/Metrics/ClassCouplingTests.cs`
- Test: `tests/CodeMetrics.AI.Tests/Probes/ArchitectureProbeTests.cs`

- [ ] **Step 1: Add failing `[FromServices]` coupling test**

Add a test proving action-injected dependencies do not inflate class coupling:

```csharp
[Fact]
public void FromServicesMethodParameter_IsExcludedFromCoupling()
{
    const string code = """
        namespace Microsoft.AspNetCore.Mvc { public sealed class FromServicesAttribute : System.Attribute { } }
        public class BigInjectedService { }
        public class MyController {
            public void Get([Microsoft.AspNetCore.Mvc.FromServices] BigInjectedService svc) { }
        }
        """;

    var (tree, model, _) = RoslynTestHelper.CompileCode(code);
    var controller = RoslynTestHelper.FindAllNodes<ClassDeclarationSyntax>(tree)
        .Single(c => c.Identifier.Text == "MyController");

    ClassCouplingCalculator.Calculate(controller, model).Should().Be(0);
}
```

- [ ] **Step 2: Implement parameter-attribute skip**

In `ClassCouplingCalculator.Calculate`, before collecting `method.Parameters`, find matching `ParameterSyntax` nodes and skip parameters whose syntax has an attribute named `FromServices` or `FromServicesAttribute`.

- [ ] **Step 3: Add failing controller threshold test**

Add an architecture hotspot test where a controller with `ClassCoupling = 35` does not emit `highCoupling`, while a non-controller still does:

```csharp
[Fact]
public void MetricHotspots_ControllerUsesHigherCouplingThreshold()
{
    var metrics = new List<TypeMetrics>
    {
        new()
        {
            Project = "TestProject",
            Namespace = "MyNs",
            Type = "OrdersController",
            FilePath = "OrdersController.cs",
            CyclomaticComplexity = 5,
            ClassCoupling = 35,
            LinesOfSource = 50
        }
    };

    var result = Analyze("class Placeholder { }", metrics);

    result.Findings.Should().NotContain(f => f.Category == "highCoupling");
}
```

- [ ] **Step 4: Implement controller threshold**

In `FindMetricHotspots`, compute `var couplingThreshold = tm.Type.EndsWith("Controller", StringComparison.Ordinal) ? 50 : 30;` and use it in both comparison and message.

- [ ] **Step 5: Run targeted tests**

Run: `dotnet test --filter "ClassCouplingTests|ArchitectureProbeTests"`

Expected: all selected tests pass.

### Task 2: Architecture Dependency Guards

**Files:**
- Modify: `src/CodeMetrics.AI/Probes/ArchitectureProbe.cs`
- Test: `tests/CodeMetrics.AI.Tests/Probes/ArchitectureProbeTests.cs`

- [ ] **Step 1: Add framework namespace guard tests**

Add tests showing `Microsoft.Extensions.*` and `Microsoft.AspNetCore.*` constructor parameters on services are not reported as concrete infrastructure dependencies even if their short names contain `Context`, `Client`, or `Gateway`.

- [ ] **Step 2: Introduce symbol-aware constructor parameter metadata**

Change `GetAllConstructorParameterTypeNames` to accept `SemanticModel`, return `(string TypeName, string? Namespace, int Line)`, and use `model.GetTypeInfo(param.Type).Type` to capture fully qualified namespace when available. Continue preserving syntax text as the fallback type name.

- [ ] **Step 3: Add framework skip**

Before keyword matching for `concreteInfrastructureDependency`, skip parameters whose namespace starts with `Microsoft.Extensions.` or `Microsoft.AspNetCore.`.

- [ ] **Step 4: Run targeted tests**

Run: `dotnet test --filter ArchitectureProbeTests`

Expected: all architecture tests pass.

### Task 3: Performance Async Suppressions

**Files:**
- Modify: `src/CodeMetrics.AI/Probes/PerformanceAsyncProbe.cs`
- Test: `tests/CodeMetrics.AI.Tests/Probes/PerformanceAsyncProbeTests.cs`

- [ ] **Step 1: Add middleware cancellation-token test**

Add a test proving `public async Task InvokeAsync(HttpContext context)` with async I/O does not emit `missingCancellationToken`.

- [ ] **Step 2: Implement middleware exclusion**

In `AnalyzeMissingCancellationToken`, before async I/O checks, skip methods named `InvokeAsync` with exactly one parameter whose type string is `HttpContext` or ends with `.HttpContext`.

- [ ] **Step 3: Add suppression comment tests**

Add tests proving `// amp-metrics: sync-required` on or immediately above a method suppresses `awaitedIoInsideLoop` and downgrades or suppresses sync-over-async according to the desired policy. Use the prototype recommendation: sync-over-async becomes an `info` note, awaited I/O loop is suppressed.

- [ ] **Step 4: Add override and explicit interface tests**

Add tests for `public override string M()` and `string IFoo.M()` containing `.GetAwaiter().GetResult()`; assert the emitted `syncOverAsync` finding severity is `info`.

- [ ] **Step 5: Implement sync-required and contract-bound sync helper**

Add private helpers:

```csharp
private static bool HasSyncRequiredSuppression(MethodDeclarationSyntax method) { ... }
private static bool IsOverrideOrExplicitInterfaceImplementation(MethodDeclarationSyntax method) { ... }
private static string SyncOverAsyncSeverity(SyntaxNode node) { ... }
```

Use `info` severity when the containing method is override/explicit-interface or has the suppression comment.

- [ ] **Step 6: Add cursor-pagination loop test**

Add a test with:

```csharp
do
{
    var response = await client.GetAsync(request);
    request.PageToken = response.NextPageToken;
} while (request.PageToken != null);
```

Assert no `awaitedIoInsideLoop` finding is emitted.

- [ ] **Step 7: Implement cursor-pagination detection**

When an awaited I/O call is inside a loop, inspect the loop body for an assignment from awaited response variable property named `NextToken`, `ContinuationToken`, `NextPageToken`, `PageToken`, or `Cursor` into a request/token variable used by the loop. Suppress only that pagination-shaped loop.

- [ ] **Step 8: Run targeted tests**

Run: `dotnet test --filter PerformanceAsyncProbeTests`

Expected: all performance async tests pass, including existing `WhenAllWithMultipleInlineArgs_DoesNotFindUnboundedWhenAll`.

### Task 4: Security Partial Authorization

**Files:**
- Modify: `src/CodeMetrics.AI/Probes/SecurityProbe.cs`
- Test: `tests/CodeMetrics.AI.Tests/Probes/SecurityProbeTests.cs`

- [ ] **Step 1: Add partial controller tests**

Add one test where `partial class PublicController` has `[Authorize]` in one declaration and actions in another, and another where `[AllowAnonymous]` appears on one partial declaration. Assert no `missingAuthorization` finding.

- [ ] **Step 2: Implement merged partial attributes**

In `AnalyzeMissingAuthorization`, collect controller class declarations by name across all syntax trees:

```csharp
var controllersByName = allRoots
    .SelectMany(r => r.DescendantNodes().OfType<ClassDeclarationSyntax>())
    .Where(c => c.Identifier.Text.EndsWith("Controller", StringComparison.Ordinal))
    .GroupBy(c => c.Identifier.Text, StringComparer.Ordinal);
```

For each group, check `HasAttribute` across the union before emitting one finding for the first declaration only.

- [ ] **Step 3: Run targeted tests**

Run: `dotnet test --filter SecurityProbeTests`

Expected: all security tests pass.

### Task 5: Scorecard Output Hygiene

**Files:**
- Modify: `src/CodeMetrics.AI/Probes/MaintainabilityProbe.cs`
- Modify: `src/CodeMetrics.AI/Probes/CodeQualityProbe.cs`
- Test: `tests/CodeMetrics.AI.Tests/Probes/MaintainabilityProbeTests.cs`
- Test: `tests/CodeMetrics.AI.Tests/Probes/CodeQualityProbeTests.cs`

- [ ] **Step 1: Add maintainability offender exclusion test**

Add a test with low-MI `Program` and `Startup` plus another low-MI type. Assert top offenders exclude `Program` and `Startup`.

- [ ] **Step 2: Implement offender exclusion**

Before `OrderBy` in `MaintainabilityProbe`, filter offenders with:

```csharp
.Where(t => !IsEntryPointType(t))
```

where `IsEntryPointType` checks `t.Type is "Program" or "Startup"` or `FilePath.EndsWith("Program.cs")` / `FilePath.EndsWith("Startup.cs")`.

- [ ] **Step 3: Add single-member decomposition test**

Add a `CodeQualityProbeTests` case with a single-member type with high decomposition ratio and multi-member clean types; assert the single-member type does not affect decomposition metrics or `topOffenders`.

- [ ] **Step 4: Implement decomposition eligibility split**

Use `decompositionEligible = types.Where(t => t.MemberCount >= 2).ToList()` for decomposition scoring/offender ranking, and keep max-member-CC eligibility as `MemberCount > 0` so single large methods can still be represented by the cyclomatic complexity signal.

- [ ] **Step 5: Run targeted tests**

Run: `dotnet test --filter "MaintainabilityProbeTests|CodeQualityProbeTests"`

Expected: all selected tests pass.

### Task 6: Multi-TFM Test Project Matching

**Files:**
- Modify: `src/CodeMetrics.AI/Probes/TestingProbe.cs`
- Test: `tests/CodeMetrics.AI.Tests/Probes/TestingProbeTests.cs`

- [ ] **Step 1: Add multi-TFM matching test**

Add a test where analyzed project names include `MyApp.Core (net9.0)` and `MyApp.Core (net10.0)`, and test projects include `MyApp.Core.Tests`. Assert no `uncoveredProject` findings for either TFM variant.

- [ ] **Step 2: Implement project-name normalization**

Add:

```csharp
private static string StripTargetFrameworkSuffix(string name)
{
    var index = name.LastIndexOf(" (", StringComparison.Ordinal);
    return index > 0 && name.EndsWith(")", StringComparison.Ordinal)
        ? name[..index]
        : name;
}
```

Call it for production project names before `DoesTestProjectCover`.

- [ ] **Step 3: Run targeted tests**

Run: `dotnet test --filter TestingProbeTests`

Expected: all testing probe tests pass.

### Task 7: Source Document Filtering

**Files:**
- Modify: `src/CodeMetrics.AI/SolutionAnalyzer.cs`
- Modify: `src/CodeMetrics.AI/Metrics/MetricsCollector.cs`
- Modify probe entrypoints that enumerate syntax trees, or centralize filtering before creating probe inputs.
- Test: add focused tests where practical; otherwise verify by integration run against a fixture solution containing an out-of-root source document.

- [ ] **Step 1: Decide filter boundary**

Prefer centralizing at input construction: create a filtered `Compilation` or filtered syntax-tree enumeration wrapper is not trivial with Roslyn compilations, so the practical plan is to pass `solutionDir` into analyzers that enumerate syntax trees and call a shared helper `SourceFileFilter.ShouldAnalyze(filePath, solutionDir)`.

- [ ] **Step 2: Create source file filter**

Add `src/CodeMetrics.AI/SourceFileFilter.cs` with:

```csharp
namespace CodeMetrics.AI;

public static class SourceFileFilter
{
    public static bool ShouldAnalyze(string? filePath, string solutionRoot)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return true;

        var fullPath = Path.GetFullPath(filePath);
        var fullRoot = Path.GetFullPath(solutionRoot);

        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            return false;

        return fullPath.IndexOf($"{Path.DirectorySeparatorChar}.nuget{Path.DirectorySeparatorChar}packages{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase) < 0;
    }
}
```

- [ ] **Step 3: Add filter tests**

Create or extend `ProjectFilterTests` with tests for in-root source, out-of-root source, and `.nuget/packages` source paths.

- [ ] **Step 4: Thread `solutionDir` into metric and probe enumeration**

Update `MetricsCollector.Collect(project.Name, compilation, solutionDir)` and probe `Analyze` methods that enumerate `compilation.SyntaxTrees` to skip trees when `SourceFileFilter.ShouldAnalyze(tree.FilePath, solutionDir)` is false.

- [ ] **Step 5: Run full tests**

Run: `dotnet test`

Expected: all tests pass.

## Recommended Execution Order

1. Task 1: coupling, because it affects architecture hotspots and type metrics.
2. Task 2: architecture namespace guards, because it is localized and shares architecture tests.
3. Task 3: performance async suppressions, because it carries the most behavioral nuance.
4. Task 4: partial authorization, localized to security.
5. Task 5: evidence/output hygiene, localized to scorecard extras.
6. Task 6: multi-TFM test matching, localized to testing.
7. Task 7: source document filtering, last because it changes analyzer plumbing across dimensions.

## Self-Review

Spec coverage: all prototype findings #1 through #13 are classified and either planned, already satisfied, or intentionally deferred. Placeholder scan: no task relies on "TBD" or unspecified test coverage. Type consistency: all referenced files and method names exist in the current repository except the explicitly planned `SourceFileFilter`.

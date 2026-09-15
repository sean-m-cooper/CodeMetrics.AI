# Production scope and explicit intent

The unpublished `dotnet-2026-09-14-scope-explicit-intent` ruleset retains existing numerical ladders and corrects evidence classification. Earlier rulesets are not compatible baseline gates.

## Benchmark scope

Production metrics exclude projects named Benchmark/Benchmarks or ending in .Benchmark/.Benchmarks, and projects beneath bench, benchmark or benchmarks directories. An unconditional literal IsBenchmarkProject property in the physical project file can explicitly enable exclusion or opt out of benchmark inference. Imported and conditional properties are not evaluated by this bounded recognizer. Conflicting declarations do not establish an override.

Executables with semantically bound BenchmarkDotNet Benchmark methods are also excluded unless explicitly opted out. A BenchmarkDotNet reference alone or an unrelated same-name attribute does not exclude code. Existing test/host/sample exclusions remain independent. Dependency Management still includes enabled benchmark project packages; development-only advisories do not become production Security findings.

## Completed task switch branches

A Result access in a branch proven to select terminal Task.Status values is not sync-over-async. Statement cases and switch-expression constant/or patterns must all prove RanToCompletion, Faulted or Canceled for the same local or parameter. Terminal does not mean successful: Result can still throw for a failed or cancelled task. Mixed/default cases, goto transfers, receiver writes and deferred functions do not establish this proof. This is a bounded source proof, not general interprocedural mutation analysis.

## Explicit anonymous access

CMAI4005 records recognized ASP.NET Core, MVC or Web API AllowAnonymous annotations as informational, high-confidence intent, without a Security penalty. Semantic aliases such as [Anonymous] are supported, as are attributes implementing ASP.NET Core IAllowAnonymous. Unrelated attributes with matching short names do not establish intent. Recognized class/method intent also satisfies the controller authorization-intent check.

The record retains source location and variant evidence, with classification explicitAnonymousIntent and scoreDisposition excludedExplicitIntent. Explicit intent does not exempt secrets, SQL findings or package advisories. This policy trusts the access decision; it does not certify endpoint safety or add comment-based CMAI suppression support.

# OrchardCore compatibility uncertainty investigation

Investigated September 13, 2026 (Pacific time). Diagnostic replay timestamps are UTC.

## Problem statement

Upgrade framework compatibility became unknown for 536 project/TFM observations,
up from 6. This is a loss of assessment coverage, not evidence of 530 new package
incompatibilities. Dependency Management remained 2; all eight other dimensions
matched exactly.

Baseline run: `045c01bf-8ba2-47b8-be26-419777575693`, audit
`f9d26fd2-d63f-40d5-ae00-d10bf68c30c6`.
Latest run: `a818413b-6095-472b-8b97-ce8e53aa52af`, audit
`b6baed34-5995-40a3-b263-d0b0cd45f1da`.
Evidence is retained under `E:/repos/OrchardCore/.scorecard/dotnet/runs/<runId>/`.

## Assumptions and limits

The original successful NuGet command output and compatibility HTTP exceptions
were not retained. Replays reconstruct candidate rows from findings and supply the
currently configured public NuGet source. They exercise the compiled compatibility
resolver, not the complete scorecard or the exact original request ordering.
Current cache inspection does not establish every historical cache entry.

## Current approach and dependency map

NuGet outdated report -> source discovery -> service-index request -> package base
addresses -> candidate package resolution -> framework comparison -> per-project
findings and scoring.

- `PackageFrameworkCompatibility.AssessAsync` groups package/version pairs, then
  shares one 20-second budget across source discovery, four concurrent resolution
  slots, and queued work. Budget cancellation makes unfinished groups unknown.
- `NuGetServiceIndexClient` uses a 10-second HTTP timeout. Request, IO, JSON, and
  timeout failures are swallowed while trying other sources. If no source yields
  a package base address, remote resolution has nothing to query.
- `NuGetPackageFrameworkResolver` checks the global/configured local package cache
  before remote downloads. Remote packages are inspected in memory, with a 50 MiB
  limit, and are not persisted by this resolver for future runs.
- Missing frameworks and several distinct failure modes collapse into absent
  dictionary entries. DependencyProbe reports these as compatibility unknown.
- `anyCommandFailed` describes the three NuGet CLI commands; it does not describe
  the health of the subsequent HTTP compatibility assessment.

Source pointers: `PackageFrameworkCompatibility.cs:88`,
`NuGetServiceIndexClient.cs:10`, `NuGetPackageFrameworkResolver.cs:20`,
`NuGetPackageClient.cs:42`, and `DependencyProbe.cs:295`, under
`analyzers/dotnet/src/CodeMetrics.AI/Probes/`.

## Data state and supporting evidence

| Observation | Baseline | Latest |
| --- | ---: | ---: |
| Compatibility unknown, excluding Aspire | 6 | 536 |
| Scored outdated observations | 541 | 542 |
| Framework-incompatible exclusions | 1 | 0 |
| Dependency Management score | 2 | 2 |

The latest 536 unknown observations cover **52 package IDs**. SourceLink.GitHub
and CSharp.CodeStyle contribute **235 each**, or 470 total. An earlier summary
counted three excluded Aspire findings too, producing 53 IDs and 236 observations
for each of those packages; that summary was not the scored population.

Latest versions did not change in matched findings. The only six compatible
observations left in the problematic run belong to Microsoft.CodeAnalysis.CSharp
5.9.0 (two) and xunit.v3.mtp-v2 4.0.1 (four), both present in the local cache.
No unknown candidate version was present in that cache during this investigation.

The previously incompatible SourceLink candidate for the netstandard2.0 source
generator became unknown. Existing policy includes unknown upgrades in outdated
scoring, explaining 541 -> 542 without a newly discovered package upgrade.

The resolver, service-index client, and package client have no source differences
between baseline revision `2fb1306` and implementation commit `c32b677`.

### Bounded diagnostic replays

Reflection invoked the existing compiled internal `AssessAsync` method in
PowerShell on .NET 10.0.11, using 542 non-Aspire candidate findings. No product
source or package cache was changed. The DLL hash matches the evaluated nupkg:
`fbad07f113d0adf217932fba3a7d2d228c7e2f0b705170df604f431cb3da7a6b`.

| Replay | Known assessments | Unknown | Elapsed |
| --- | ---: | ---: | ---: |
| NuGet.org service index supplied | 536 | 6 | 13.63 seconds |
| Controlled empty remote-source list, same local cache | 6 | 536 | 0.05 seconds |

The controlled case reproduces both the count and the two packages retaining known
results. It demonstrates the behavior of unavailable remote discovery; it does
not prove why discovery was unavailable in the historical run.

Records: `TestResults/dependency-compatibility-investigation/replay.json` and
`TestResults/dependency-compatibility-investigation/no-remote-sources.json`.
An initial diagnostic invocation omitted the report's required version/projects
fields and failed validation; it is not counted as a resolver result.

## Root cause analysis and missing information

**Strongest explanation:** loss of remote package-base-address discovery, such as
a failed or timed-out service-index request. That naturally preserves cached
results while losing every remote-dependent result, and the controlled replay
matches that pattern. Missing source data in the original command report could
produce the same outcome; the original report was not saved to distinguish it.

**Other plausible mechanisms:** package-endpoint failures or exhaustion of the
shared 20-second budget. These are supported failure paths, but a general budget
problem alone is a weaker explanation for the complete loss of remote results.
The healthy replay completed inside that budget. It does not prove the budget is
adequate under all conditions.

**Confirmed analyzer weakness:** infrastructure failures lose their identity and
become ordinary compatibility unknowns, with no assessment-health diagnostic.
Project/TFM repetition amplifies a few package lookup failures into hundreds of
observations. The saved evidence cannot identify the original HTTP status,
exception, elapsed time, discovered-source count, or unstarted package groups.

The six baseline unknowns are a separate matter: five Lucene observations contain
`Not found at the sources` in latestVersion, and one concerns Microsoft.Playwright
1.62.0. The baseline does not identify the Playwright failure cause. Neither should
be described as a confirmed incompatible upgrade.

## Simplest solutions to consider next

1. Retain structured compatibility-stage health: source discovery outcomes,
   package failure reasons, elapsed time, and unique-package versus project/TFM
   coverage. Preserve unknowns while distinguishing unavailable evidence from
   unsupported metadata and real incompatibility.
2. Make lookup recovery resilient with bounded retry and reusable package metadata
   caching. Separate source-discovery failure from queued-work budget exhaustion
   before changing timeout values. Test an unavailable index, delayed downloads,
   and mixed local/remote candidates.
3. Separately review unavailable latest-version values and whether unknown
   compatibility should affect outdated scoring. That is a policy decision, not
   part of this investigation or a reason to retune scores.

Original scorecards remain intact. This investigation changes documentation only.

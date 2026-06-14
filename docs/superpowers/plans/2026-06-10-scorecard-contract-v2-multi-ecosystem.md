# Scorecard Contract v2 And Multi-Ecosystem Skill Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the .NET-centric evidence contract with a universal schema v2, give every analyzer a collision-free per-ecosystem output path, make the code-scorecard skill ecosystem-aware, enforce the schema with conformance tests, fix the JS scaffold's broken `bin` path, and establish a cross-language calibration methodology.

**Architecture:** Evidence schema v2 replaces `solution` with a universal `subject` block and adds `tool.ecosystem`; all analyzers write to `.scorecard/<ecosystem>/` so polyglot repos never collide. The shared contract files are created now at `shared/scorecard-schema/` (layout-independent — they don't move during the later reorg). The .NET tool is updated to emit v2 natively (nothing is published, so no v1 compatibility is kept). The code-scorecard skill (separate repo: `E:\repos\ai_tools`) gains ecosystem detection, per-ecosystem bootstrap, and multi-evidence rendering. The existing 2026-06-09 reorg plan is revised in place to drop its v1 schema task and fix its JS scaffold defects.

**Tech Stack:** .NET 10 / C# / xUnit / FluentAssertions / JsonSchema.Net, JSON Schema draft 2020-12, Markdown skill files, MSBuild targets, (revised plan only) TypeScript / Vitest / ajv.

**Ordering constraint:** Execute this plan **before** `docs/superpowers/plans/2026-06-09-analyzer-suite-reorg-js-scaffold.md`. This plan edits files in their current (pre-reorg) locations and revises that plan's content.

**Two repositories:** Tasks 1–6 commit to `E:\repos\CodeMetrics.AI`. Tasks 7–9 commit to `E:\repos\ai_tools`. Run git commands from the repo each task names.

---

## Design Decisions (locked)

- **Ecosystem ids** are the directory names under `analyzers/`: `dotnet`, `javascript-typescript`, `python`, `rust`. The same id names the output directory: `.scorecard/<ecosystem>/metrics.csv` and `.scorecard/<ecosystem>/evidence.json`. Evidence declares its id in `tool.ecosystem`.
- **Schema v2 `subject` block** replaces v1's `solution`: `root` (absolute path to the analyzed root), `entryPoint` (solution file / package.json / pyproject.toml / Cargo.toml), optional `name`, optional `variant` (build configuration for .NET; tsconfig path for JS). `filters` renames `totalProjects`/`analyzedProjects` to `totalUnits`/`analyzedUnits`.
- **`score` is omitted (not 0) for skipped/failed dimensions.** Schema enforces: `status == "scored"` requires `score`.
- **No cross-ecosystem averaging.** The skill renders one scorecard per ecosystem plus a side-by-side suite summary. Cross-ecosystem comparability requires the calibration procedure in `calibration.md`; until an ecosystem is calibrated, its scores carry an explicit caveat.
- **CSV fallback is `dotnet`-only.** Its archetypes and thresholds are Roslyn-calibrated; other ecosystems fall back to qualitative scoring.

## File Structure

**Repo `E:\repos\CodeMetrics.AI`:**
- Modify: `src/CodeMetrics.AI/Output/EvidenceModel.cs` — schema v2 model (`SubjectInfo`, `ToolInfo.Ecosystem`, unit-based filters)
- Modify: `src/CodeMetrics.AI/Probes/DimensionResult.cs` — nullable `Score`
- Modify: `src/CodeMetrics.AI/SolutionAnalyzer.cs` — build v2 evidence
- Modify: `src/CodeMetrics.AI/CliOptions.cs`, `src/CodeMetrics.AI/Program.cs` — `.scorecard/dotnet/` defaults
- Modify: `src/CodeMetrics.AI/CodeMetrics.AI.csproj` — version 1.1.0
- Modify: `README.md` — v2 + new paths
- Modify: `tests/CodeMetrics.AI.Tests/Output/EvidenceWriterTests.cs` — v2 expectations
- Create: `tests/CodeMetrics.AI.Tests/Output/SchemaConformanceTests.cs` — generated output validates against the shared schema
- Create: `shared/scorecard-schema/evidence.schema.v2.json`, `dimensions.md`, `metrics-csv.md`, `ecosystems.md`, `calibration.md`, `examples/dotnet-evidence.json`, `examples/javascript-typescript-evidence.json`
- Modify: `docs/superpowers/plans/2026-06-09-analyzer-suite-reorg-js-scaffold.md` — drop v1 schema task, fix JS scaffold
- Delete: empty residue dirs `analyzers/python`, `analyzers/rust`

**Repo `E:\repos\ai_tools` (skill):**
- Modify: `skills/code-scorecard/SKILL.md` — ecosystem detection, v2 validation, multi-ecosystem output
- Rewrite: `skills/code-scorecard/bootstrap.md` — per-ecosystem bootstrap
- Modify: `skills/code-scorecard/csv-fallback.md` — dotnet-only banner + paths
- Modify: `skills/code-scorecard/troubleshooting.md` — new paths, schema 2, JS entries
- Modify: `skills/code-scorecard/scorecard-tooling/Directory.Build.targets` + `README.md` — `.scorecard\dotnet\` output

---

### Task 1: Remove Stale Scaffold Residue

**Files:**
- Delete: `analyzers/python/`, `analyzers/rust/` (empty, untracked)

- [ ] **Step 1: Delete the empty directories**

Run from `E:\repos\CodeMetrics.AI`:

```powershell
Remove-Item -Recurse -Force analyzers\python, analyzers\rust -Confirm:$false
if ((Get-ChildItem analyzers -Force | Measure-Object).Count -eq 0) { Remove-Item analyzers -Force -Confirm:$false }
```

Expected: exits `0`. The `analyzers/` tree is gone (the reorg plan recreates it later). Leave `shared/scorecard-schema/examples/` in place — Task 4 populates it.

- [ ] **Step 2: Confirm git sees no change**

Run: `git status --short`
Expected: no output (the dirs were empty and untracked). No commit needed.

---

### Task 2: Shared Scorecard Contract v2

**Files:**
- Create: `shared/scorecard-schema/evidence.schema.v2.json`
- Create: `shared/scorecard-schema/ecosystems.md`
- Create: `shared/scorecard-schema/dimensions.md`
- Create: `shared/scorecard-schema/metrics-csv.md`
- Create: `shared/scorecard-schema/calibration.md`
- Create: `shared/scorecard-schema/examples/dotnet-evidence.json`
- Create: `shared/scorecard-schema/examples/javascript-typescript-evidence.json`

- [ ] **Step 1: Write the v2 schema**

Write `shared/scorecard-schema/evidence.schema.v2.json`:

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://github.com/sean-m-cooper/CodeMetrics.AI/shared/scorecard-schema/evidence.schema.v2.json",
  "title": "CodeMetrics.AI Evidence v2",
  "type": "object",
  "required": ["schemaVersion", "generatedAtUtc", "tool", "subject", "filters", "population", "dimensions"],
  "additionalProperties": false,
  "properties": {
    "schemaVersion": { "const": 2 },
    "generatedAtUtc": { "type": "string", "format": "date-time" },
    "tool": {
      "type": "object",
      "required": ["name", "version", "ecosystem"],
      "additionalProperties": false,
      "properties": {
        "name": { "type": "string", "minLength": 1 },
        "version": { "type": "string", "minLength": 1 },
        "ecosystem": { "enum": ["dotnet", "javascript-typescript", "python", "rust"] }
      }
    },
    "subject": {
      "type": "object",
      "required": ["root", "entryPoint"],
      "additionalProperties": false,
      "properties": {
        "root": { "type": "string", "minLength": 1 },
        "entryPoint": { "type": "string", "minLength": 1 },
        "name": { "type": "string" },
        "variant": { "type": "string" }
      }
    },
    "filters": {
      "type": "object",
      "required": ["totalUnits", "analyzedUnits", "skipped"],
      "additionalProperties": false,
      "properties": {
        "totalUnits": { "type": "integer", "minimum": 0 },
        "analyzedUnits": { "type": "integer", "minimum": 0 },
        "skipped": {
          "type": "array",
          "items": {
            "type": "object",
            "required": ["name", "reason"],
            "additionalProperties": false,
            "properties": {
              "name": { "type": "string", "minLength": 1 },
              "reason": { "type": "string", "minLength": 1 }
            }
          }
        }
      }
    },
    "population": {
      "type": "object",
      "required": ["types", "members"],
      "additionalProperties": false,
      "properties": {
        "types": { "type": "integer", "minimum": 0 },
        "members": { "type": "integer", "minimum": 0 }
      }
    },
    "dimensions": {
      "type": "object",
      "required": [
        "codeQuality",
        "maintainability",
        "errorHandling",
        "performanceAsync",
        "security",
        "testing",
        "documentation",
        "dependencyManagement",
        "architecture"
      ],
      "additionalProperties": false,
      "properties": {
        "codeQuality": { "$ref": "#/$defs/dimensionResult" },
        "maintainability": { "$ref": "#/$defs/dimensionResult" },
        "errorHandling": { "$ref": "#/$defs/dimensionResult" },
        "performanceAsync": { "$ref": "#/$defs/dimensionResult" },
        "security": { "$ref": "#/$defs/dimensionResult" },
        "testing": { "$ref": "#/$defs/dimensionResult" },
        "documentation": { "$ref": "#/$defs/dimensionResult" },
        "dependencyManagement": { "$ref": "#/$defs/dimensionResult" },
        "architecture": { "$ref": "#/$defs/dimensionResult" }
      }
    }
  },
  "$defs": {
    "dimensionResult": {
      "type": "object",
      "required": ["status", "basis", "findings"],
      "properties": {
        "status": { "enum": ["scored", "skipped", "failed"] },
        "score": { "type": "number", "minimum": 0, "maximum": 10 },
        "basis": { "type": "string", "minLength": 1 },
        "findings": {
          "type": "array",
          "items": { "$ref": "#/$defs/finding" }
        }
      },
      "additionalProperties": true,
      "if": {
        "properties": { "status": { "const": "scored" } },
        "required": ["status"]
      },
      "then": { "required": ["score"] }
    },
    "finding": {
      "type": "object",
      "required": ["category", "severity", "message"],
      "additionalProperties": false,
      "properties": {
        "category": { "type": "string", "minLength": 1 },
        "severity": { "enum": ["info", "warning", "error"] },
        "file": { "type": "string" },
        "line": { "type": "integer", "minimum": 0 },
        "project": { "type": "string" },
        "type": { "type": "string" },
        "member": { "type": "string" },
        "package": { "type": "string" },
        "confidence": { "enum": ["high", "medium", "low"] },
        "message": { "type": "string", "minLength": 1 }
      }
    }
  }
}
```

Design notes baked into this schema (the tightening that fixes audit issue #8): strict `additionalProperties: false` everywhere the contract is fixed; `dimensionResult` alone allows extras because analyzers attach probe-specific data (the .NET tool's `[JsonExtensionData]`); `score` is conditionally required only for `status == "scored"`; `findings` is always required (empty array is fine); `generatedAtUtc` carries `format: date-time`; severity/status/confidence are closed enums matching what the .NET probes actually emit.

- [ ] **Step 2: Write the ecosystem registry**

Write `shared/scorecard-schema/ecosystems.md`:

```markdown
# Ecosystem Registry

An **ecosystem id** is the stable identifier shared by an analyzer's source directory, its output directory, and its evidence (`tool.ecosystem`). Ids are lowercase, hyphenated, and never renamed.

| Ecosystem id | Analyzer location | Entry point (`subject.entryPoint`) | Output directory |
|---|---|---|---|
| `dotnet` | `analyzers/dotnet` | `.sln` / `.slnx` file | `.scorecard/dotnet/` |
| `javascript-typescript` | `analyzers/javascript-typescript` | `package.json` | `.scorecard/javascript-typescript/` |
| `python` | `analyzers/python` (planned) | `pyproject.toml` | `.scorecard/python/` |
| `rust` | `analyzers/rust` (planned) | `Cargo.toml` | `.scorecard/rust/` |

## Output path rule

Every analyzer writes its default outputs under `.scorecard/<ecosystem-id>/`:

```text
.scorecard/<ecosystem-id>/metrics.csv
.scorecard/<ecosystem-id>/evidence.json
```

This keeps polyglot repositories (e.g. a .NET API with a React frontend) collision-free: each analyzer owns its subdirectory, and consumers discover evidence by globbing `.scorecard/*/evidence.json`.

## `subject.variant`

`variant` captures the analyzer-specific build/config flavor: the build configuration (`Debug`/`Release`) for `dotnet`, the tsconfig path for `javascript-typescript`. It is optional; omit it when the ecosystem has no meaningful variant.
```

- [ ] **Step 3: Write the dimension contract**

Write `shared/scorecard-schema/dimensions.md`:

```markdown
# Scorecard Dimensions

All analyzers use a 0-10 score scale and these stable dimension keys.

| Key | Purpose |
|-----|---------|
| `codeQuality` | Complexity, decomposition, and local code-shape risks |
| `maintainability` | Maintainability index distribution and difficult-to-change areas |
| `errorHandling` | Exception, rejection, logging, and failure-path quality |
| `performanceAsync` | Async, concurrency, blocking, and avoidable performance risks |
| `security` | Static security findings and imported vulnerability signals |
| `testing` | Test presence, assertion quality, skipped tests, and coverage signals |
| `documentation` | README, docs, API docs, and onboarding material |
| `dependencyManagement` | Vulnerable, deprecated, outdated, or inconsistent dependencies |
| `architecture` | Cycles, layering, coupling hotspots, and framework-specific structure risks |

Analyzers may use language-specific rules inside each dimension. The key names, the 0-10 scale, and the `scored` / `skipped` / `failed` status values are stable.

## Score Comparability

Scores are always comparable **within** an ecosystem: the same analyzer version applies the same thresholds to every repository it scores.

Scores are comparable **across** ecosystems only after the analyzer has completed the corpus calibration procedure in `calibration.md`. Until then, consumers (including the code-scorecard skill) must present per-ecosystem scores side by side without averaging or ranking them against each other, and must caveat uncalibrated ecosystems.

| Ecosystem | Calibration status |
|---|---|
| `dotnet` | Baseline (thresholds define the reference distribution) |
| `javascript-typescript` | Uncalibrated |
| `python` | Uncalibrated |
| `rust` | Uncalibrated |
```

- [ ] **Step 4: Write the CSV contract**

Write `shared/scorecard-schema/metrics-csv.md`:

````markdown
# Metrics CSV Contract

Default path (see `ecosystems.md` for the ecosystem id rule):

```text
.scorecard/<ecosystem-id>/metrics.csv
```

Header:

```text
Scope,Project,Namespace,Type,Member,Maintainability Index,Cyclomatic Complexity,Depth of Inheritance,Class Coupling,Lines of Source code,Lines of Executable code
```

## Column Meanings

| Column | Meaning |
|--------|---------|
| Scope | `Type` or `Member` |
| Project | Analyzer-specific project or package name |
| Namespace | Namespace, module path, or package path |
| Type | Type, component, hook, module bucket, or equivalent analyzer unit |
| Member | Method, function, component body, hook callback, or equivalent member unit |
| Maintainability Index | 0-100 maintainability index |
| Cyclomatic Complexity | Runtime decision complexity |
| Depth of Inheritance | Inheritance depth where meaningful, otherwise 0 |
| Class Coupling | Analyzer-specific coupling count |
| Lines of Source code | Non-blank source lines excluding comments and braces-only lines where supported |
| Lines of Executable code | Executable statement count or closest language-specific equivalent |
````

- [ ] **Step 5: Write the calibration methodology**

Write `shared/scorecard-schema/calibration.md`:

```markdown
# Cross-Ecosystem Calibration

The shared contract promises a stable 0-10 scale per dimension. That promise is only meaningful across ecosystems if each analyzer's thresholds are tuned so that comparable codebases earn comparable scores. This document defines how an ecosystem earns "calibrated" status. The `dotnet` analyzer is the baseline: its thresholds define the reference distribution.

## Prerequisites: metric parity

Before distribution tuning, the new analyzer must demonstrate formula parity with the baseline on the shared raw metrics:

1. **Maintainability index** — identical formula: `MAX(0, (171 − 5.2·ln(HalsteadVolume) − 0.23·CyclomaticComplexity − 16.2·ln(LinesOfCode)) · 100 / 171)`. The language-specific inputs (Halstead token classification, line counting policy) must be documented in the analyzer's README, with fixture files whose expected MI values are hand-computed and asserted in tests.
2. **Cyclomatic complexity** — a construct parity table mapping baseline constructs to the ecosystem's equivalents (e.g. C# `??` ↔ JS `??`, `switch` sections ↔ `case` clauses), asserted by fixture tests. The counting policy is: start at 1 per member; +1 per runtime decision point including logical `&&`/`||` and null-coalescing operators; type-only declarations add nothing.
3. **Population definitions** — what counts as a "type" and "member" for `population` and CSV rows must be documented (e.g. React function components and custom hooks are member-level units).

## Calibration procedure

1. **Assemble a reference corpus** of 6-10 public repositories for the ecosystem, selected for spread, not fame: at least two widely accepted as high quality, at least two with known quality problems (archived/legacy), sizes spanning roughly 10k-500k LOC, and at least one of each dominant framework flavor (e.g. for `javascript-typescript`: one React app, one Node service, one library).
2. **Establish the baseline distribution** (once): run the `dotnet` analyzer over its own corpus and record per-dimension score median, p25, and p10.
3. **Run and compare**: run the candidate analyzer over its corpus. For each dimension, compare its score distribution to the baseline distribution.
4. **Tune thresholds** (MI cutoffs, CC bands, decomposition bands, finding deduction weights) until per-dimension medians are within ±0.5 and p10 within ±1.0 of the baseline. Tuning changes thresholds, never the formulas.
5. **Record the run**: commit a summary under `shared/scorecard-schema/calibration-runs/<ecosystem>-<tool-version>.md` listing the corpus (repo + commit SHA), per-dimension distributions, and the threshold values frozen for that tool version. Update the status table in `dimensions.md`.
6. **Recalibrate** whenever a dimension's rules or thresholds change. `tool.version` in evidence ties every scorecard to a frozen threshold set.

## Interim rule for uncalibrated ecosystems

Until an ecosystem completes this procedure:

- Its analyzer README must state that scores are uncalibrated across ecosystems.
- The code-scorecard skill renders its scores with an explicit caveat and never averages them with other ecosystems' scores.
- The dotnet CSV fallback procedure (archetypes, Roslyn-calibrated thresholds) must not be applied to its CSV output.
```

- [ ] **Step 6: Write the evidence examples**

Write `shared/scorecard-schema/examples/dotnet-evidence.json`:

```json
{
  "schemaVersion": 2,
  "generatedAtUtc": "2026-06-10T00:00:00.0000000Z",
  "tool": {
    "name": "CodeMetrics.AI",
    "version": "1.1.0",
    "ecosystem": "dotnet"
  },
  "subject": {
    "root": "/repo",
    "entryPoint": "/repo/MyApp.slnx",
    "name": "MyApp",
    "variant": "Debug"
  },
  "filters": {
    "totalUnits": 2,
    "analyzedUnits": 1,
    "skipped": [
      { "name": "MyApp.Tests", "reason": "Test project" }
    ]
  },
  "population": {
    "types": 1,
    "members": 1
  },
  "dimensions": {
    "codeQuality": { "status": "scored", "score": 10, "basis": "Example.", "findings": [] },
    "maintainability": { "status": "scored", "score": 10, "basis": "Example.", "findings": [] },
    "errorHandling": {
      "status": "scored",
      "score": 8,
      "basis": "Example.",
      "findings": [
        {
          "category": "emptyCatch",
          "severity": "warning",
          "file": "/repo/src/MyApp/Worker.cs",
          "line": 42,
          "project": "MyApp",
          "type": "Worker",
          "confidence": "high",
          "message": "Empty catch block swallows exceptions."
        }
      ]
    },
    "performanceAsync": { "status": "scored", "score": 10, "basis": "Example.", "findings": [] },
    "security": { "status": "scored", "score": 10, "basis": "Example.", "findings": [] },
    "testing": { "status": "scored", "score": 10, "basis": "Example.", "findings": [] },
    "documentation": { "status": "scored", "score": 10, "basis": "Example.", "findings": [] },
    "dependencyManagement": { "status": "skipped", "basis": "Dependency probe skipped via --skip-dependency-probe.", "findings": [] },
    "architecture": { "status": "scored", "score": 10, "basis": "Example.", "findings": [] }
  }
}
```

Write `shared/scorecard-schema/examples/javascript-typescript-evidence.json`:

```json
{
  "schemaVersion": 2,
  "generatedAtUtc": "2026-06-10T00:00:00.0000000Z",
  "tool": {
    "name": "codemetrics-ai",
    "version": "0.1.0",
    "ecosystem": "javascript-typescript"
  },
  "subject": {
    "root": "/repo",
    "entryPoint": "/repo/package.json",
    "name": "my-app",
    "variant": "tsconfig.json"
  },
  "filters": {
    "totalUnits": 1,
    "analyzedUnits": 1,
    "skipped": []
  },
  "population": {
    "types": 1,
    "members": 1
  },
  "dimensions": {
    "codeQuality": { "status": "scored", "score": 10, "basis": "Example.", "findings": [] },
    "maintainability": { "status": "scored", "score": 10, "basis": "Example.", "findings": [] },
    "errorHandling": { "status": "scored", "score": 10, "basis": "Example.", "findings": [] },
    "performanceAsync": { "status": "scored", "score": 10, "basis": "Example.", "findings": [] },
    "security": { "status": "scored", "score": 10, "basis": "Example.", "findings": [] },
    "testing": { "status": "scored", "score": 10, "basis": "Example.", "findings": [] },
    "documentation": { "status": "scored", "score": 10, "basis": "Example.", "findings": [] },
    "dependencyManagement": { "status": "scored", "score": 10, "basis": "Example.", "findings": [] },
    "architecture": { "status": "scored", "score": 10, "basis": "Example.", "findings": [] }
  }
}
```

Note the dotnet example deliberately exercises a `skipped` dimension **without** a `score` key and a finding with every optional field — Task 5's conformance tests validate both examples against the schema.

- [ ] **Step 7: Commit**

```powershell
git add shared\scorecard-schema
git commit -m "feat: add universal scorecard contract schema v2"
```

Expected: commit succeeds.

---

### Task 3: Evidence Schema v2 In The .NET Tool

**Files:**
- Modify: `tests/CodeMetrics.AI.Tests/Output/EvidenceWriterTests.cs`
- Modify: `src/CodeMetrics.AI/Output/EvidenceModel.cs`
- Modify: `src/CodeMetrics.AI/Probes/DimensionResult.cs`
- Modify: `src/CodeMetrics.AI/SolutionAnalyzer.cs:33,89-103,119-138`

- [ ] **Step 1: Rewrite the evidence writer tests for v2**

Replace the full contents of `tests/CodeMetrics.AI.Tests/Output/EvidenceWriterTests.cs`:

```csharp
using System.Text.Json;
using CodeMetrics.AI.Output;
using FluentAssertions;

namespace CodeMetrics.AI.Tests.Output;

public class EvidenceWriterTests
{
    [Fact]
    public async Task WriteAsync_ProducesValidJson_WithSchemaVersion2Subject()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var model = new EvidenceModel
            {
                Subject = new SubjectInfo
                {
                    Root = "C:/test",
                    EntryPoint = "C:/test/my.sln",
                    Name = "my",
                    Variant = "Release"
                },
                Filters = new FilterInfo
                {
                    TotalUnits = 3,
                    AnalyzedUnits = 2,
                    Skipped =
                    [
                        new SkippedUnitInfo { Name = "MyProject.Tests", Reason = "Test project" }
                    ]
                },
                Population = new PopulationInfo { Types = 42, Members = 120 },
                Dimensions = new Dictionary<string, object>
                {
                    ["codeQuality"] = "placeholder"
                }
            };

            await EvidenceWriter.WriteAsync(tempFile, model);

            var json = await File.ReadAllTextAsync(tempFile);
            json.Should().NotBeNullOrWhiteSpace();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            root.GetProperty("schemaVersion").GetInt32().Should().Be(2);
            root.GetProperty("subject").GetProperty("root").GetString().Should().Be("C:/test");
            root.GetProperty("subject").GetProperty("entryPoint").GetString().Should().Be("C:/test/my.sln");
            root.GetProperty("subject").GetProperty("name").GetString().Should().Be("my");
            root.GetProperty("subject").GetProperty("variant").GetString().Should().Be("Release");
            root.GetProperty("filters").GetProperty("totalUnits").GetInt32().Should().Be(3);
            root.GetProperty("filters").GetProperty("analyzedUnits").GetInt32().Should().Be(2);
            root.GetProperty("population").GetProperty("types").GetInt32().Should().Be(42);
            root.GetProperty("population").GetProperty("members").GetInt32().Should().Be(120);
            root.TryGetProperty("solution", out _).Should().BeFalse("v2 replaced 'solution' with 'subject'");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task WriteAsync_CreatesDirectory_WhenItDoesNotExist()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"evidence-test-{Guid.NewGuid():N}");
        var outputPath = Path.Combine(tempDir, "subdir", "evidence.json");
        try
        {
            var model = new EvidenceModel();

            await EvidenceWriter.WriteAsync(outputPath, model);

            File.Exists(outputPath).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAsync_UsedCamelCasePropertyNames()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var model = new EvidenceModel
            {
                Population = new PopulationInfo { Types = 5, Members = 10 }
            };

            await EvidenceWriter.WriteAsync(tempFile, model);

            var json = await File.ReadAllTextAsync(tempFile);

            json.Should().Contain("\"schemaVersion\"");
            json.Should().Contain("\"generatedAtUtc\"");
            json.Should().Contain("\"population\"");

            json.Should().NotContain("\"SchemaVersion\"");
            json.Should().NotContain("\"GeneratedAtUtc\"");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task WriteAsync_ToolInfo_ContainsNameVersionAndEcosystem()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var model = new EvidenceModel();

            await EvidenceWriter.WriteAsync(tempFile, model);

            var json = await File.ReadAllTextAsync(tempFile);
            using var doc = JsonDocument.Parse(json);
            var tool = doc.RootElement.GetProperty("tool");

            tool.GetProperty("name").GetString().Should().Be("CodeMetrics.AI");
            tool.GetProperty("version").GetString().Should().NotBeNullOrEmpty();
            tool.GetProperty("ecosystem").GetString().Should().Be("dotnet");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --verbosity minimal`
Expected: build FAILS with compile errors (`SubjectInfo` not defined, `TotalUnits` not found, `SkippedUnitInfo` not defined). A compile failure is the failing state for this step.

- [ ] **Step 3: Rewrite the evidence model**

Replace the full contents of `src/CodeMetrics.AI/Output/EvidenceModel.cs`:

```csharp
namespace CodeMetrics.AI.Output;

public sealed class EvidenceModel
{
    public int SchemaVersion { get; init; } = 2;
    public string GeneratedAtUtc { get; init; } = DateTime.UtcNow.ToString("O");
    public ToolInfo Tool { get; init; } = new();
    public SubjectInfo Subject { get; init; } = new();
    public FilterInfo Filters { get; init; } = new();
    public PopulationInfo Population { get; init; } = new();
    public Dictionary<string, object> Dimensions { get; init; } = [];
}

public sealed class ToolInfo
{
    public string Name { get; init; } = "CodeMetrics.AI";
    public string Version { get; init; } = typeof(ToolInfo).Assembly.GetName().Version?.ToString() ?? "1.0.0";
    public string Ecosystem { get; init; } = "dotnet";
}

public sealed class SubjectInfo
{
    public string Root { get; init; } = "";
    public string EntryPoint { get; init; } = "";
    public string? Name { get; init; }
    public string? Variant { get; init; }
}

public sealed class FilterInfo
{
    public int TotalUnits { get; init; }
    public int AnalyzedUnits { get; init; }
    public List<SkippedUnitInfo> Skipped { get; init; } = [];
}

public sealed class SkippedUnitInfo
{
    public required string Name { get; init; }
    public required string Reason { get; init; }
}

public sealed class PopulationInfo
{
    public int Types { get; init; }
    public int Members { get; init; }
}
```

- [ ] **Step 4: Make `DimensionResult.Score` nullable**

In `src/CodeMetrics.AI/Probes/DimensionResult.cs`, change:

```csharp
    public double Score { get; init; }
```

to:

```csharp
    public double? Score { get; init; }
```

`EvidenceWriter`'s `WhenWritingNull` option then omits `score` for skipped/failed dimensions, matching the schema's conditional requirement. All probes assign a concrete `double` (implicitly converted), so they compile unchanged. FluentAssertions has nullable-numeric overloads for `Be`/`BeGreaterThan`/`BeApproximately`/`BeInRange`, so existing probe tests compile unchanged.

- [ ] **Step 5: Update `SolutionAnalyzer` to build v2 evidence**

In `src/CodeMetrics.AI/SolutionAnalyzer.cs`, make three edits.

Edit A — the skipped-project list type (line 33):

```csharp
        var skipped = new List<SkippedUnitInfo>();
```

and its population inside the loop (line 38):

```csharp
                skipped.Add(new SkippedUnitInfo { Name = project.Name, Reason = reason });
```

Edit B — the skipped dependency probe no longer fakes a score (lines 92–98). Replace:

```csharp
            depResult = new DimensionResult
            {
                Status = "skipped",
                Score = 0,
                Basis = "Dependency probe skipped via --skip-dependency-probe."
            };
```

with:

```csharp
            depResult = new DimensionResult
            {
                Status = "skipped",
                Basis = "Dependency probe skipped via --skip-dependency-probe."
            };
```

Edit C — the evidence construction (lines 119–138). Replace:

```csharp
        var evidence = new EvidenceModel
        {
            Solution = new Output.SolutionInfo
            {
                Path = Path.GetFullPath(solutionPath),
                Configuration = options.Configuration
            },
            Filters = new FilterInfo
            {
                TotalProjects = allProjects.Count,
                AnalyzedProjects = analyzed.Count,
                Skipped = skipped,
            },
```

with:

```csharp
        var evidence = new EvidenceModel
        {
            Subject = new SubjectInfo
            {
                Root = solutionDir,
                EntryPoint = Path.GetFullPath(solutionPath),
                Name = Path.GetFileNameWithoutExtension(solutionPath),
                Variant = options.Configuration
            },
            Filters = new FilterInfo
            {
                TotalUnits = allProjects.Count,
                AnalyzedUnits = analyzed.Count,
                Skipped = skipped,
            },
```

(`Population` and `Dimensions` lines stay as they are.)

- [ ] **Step 6: Verify no stale v1 names remain**

Run: `rg -n "SolutionInfo|TotalProjects|AnalyzedProjects|SkippedProjectInfo" src tests`
Expected: no output.

- [ ] **Step 7: Run the full test suite**

Run: `dotnet test --verbosity minimal`
Expected: exits `0`, all tests pass.

- [ ] **Step 8: Commit**

```powershell
git add src tests
git commit -m "feat: emit universal evidence schema v2"
```

Expected: commit succeeds.

---

### Task 4: Per-Ecosystem Default Output Paths (.NET)

**Files:**
- Modify: `src/CodeMetrics.AI/CliOptions.cs:6-7`
- Modify: `src/CodeMetrics.AI/Program.cs:12,18`
- Modify: `src/CodeMetrics.AI/CodeMetrics.AI.csproj:10`
- Modify: `README.md`

- [ ] **Step 1: Change the CLI defaults**

In `src/CodeMetrics.AI/CliOptions.cs`, change:

```csharp
    public string Output { get; set; } = ".scorecard/metrics.csv";
    public string ScorecardOutput { get; set; } = ".scorecard/evidence.json";
```

to:

```csharp
    public string Output { get; set; } = ".scorecard/dotnet/metrics.csv";
    public string ScorecardOutput { get; set; } = ".scorecard/dotnet/evidence.json";
```

In `src/CodeMetrics.AI/Program.cs`, change the two matching `DefaultValueFactory` lines:

```csharp
    DefaultValueFactory = _ => ".scorecard/dotnet/metrics.csv"
```

```csharp
    DefaultValueFactory = _ => ".scorecard/dotnet/evidence.json"
```

- [ ] **Step 2: Bump the tool version**

In `src/CodeMetrics.AI/CodeMetrics.AI.csproj`, change `<Version>1.0.1</Version>` to `<Version>1.1.0</Version>`.

- [ ] **Step 3: Update the README**

In `README.md`, make these edits:

The Usage custom-paths example comment stays valid (explicit paths override defaults). Replace the Output table:

```markdown
| File | Description |
|------|-------------|
| `.scorecard/metrics.csv` | VS-compatible raw metrics (same format as Visual Studio's Code Metrics Results) |
| `.scorecard/evidence.json` | Scored evidence across 9 dimensions (schema v1) |
```

with:

```markdown
| File | Description |
|------|-------------|
| `.scorecard/dotnet/metrics.csv` | VS-compatible raw metrics (same format as Visual Studio's Code Metrics Results) |
| `.scorecard/dotnet/evidence.json` | Scored evidence across 9 dimensions (schema v2) |

Outputs live under `.scorecard/dotnet/` so analyzers for other ecosystems (see `shared/scorecard-schema/ecosystems.md`) can coexist in the same repository without collisions.
```

Replace the Options table defaults:

```markdown
| `--output` | `.scorecard/metrics.csv` | CSV output path |
| `--scorecard-output` | `.scorecard/evidence.json` | JSON evidence output path |
```

with:

```markdown
| `--output` | `.scorecard/dotnet/metrics.csv` | CSV output path |
| `--scorecard-output` | `.scorecard/dotnet/evidence.json` | JSON evidence output path |
```

- [ ] **Step 4: Run tests and a smoke run**

Run:

```powershell
dotnet test --verbosity minimal
dotnet run --project src\CodeMetrics.AI -- --solution CodeMetrics.AI.slnx
Get-Content .scorecard\dotnet\evidence.json -TotalCount 5
```

Expected: tests pass; the tool analyzes this repo's own solution; the evidence file exists under `.scorecard\dotnet\` and starts with `"schemaVersion": 2`. Then delete the smoke output: `Remove-Item -Recurse -Force .scorecard -Confirm:$false`.

- [ ] **Step 5: Commit**

```powershell
git add src README.md
git commit -m "feat: write outputs under .scorecard/dotnet and bump to 1.1.0"
```

Expected: commit succeeds.

---

### Task 5: Schema Conformance Tests (.NET)

This is the enforcement half of the schema-tightening fix: the generated evidence and both shared examples are validated against `evidence.schema.v2.json` in CI, so contract drift fails the build instead of surfacing in a consumer.

**Files:**
- Modify: `tests/CodeMetrics.AI.Tests/CodeMetrics.AI.Tests.csproj`
- Create: `tests/CodeMetrics.AI.Tests/Output/SchemaConformanceTests.cs`

- [ ] **Step 1: Add the JSON Schema validator package**

Run:

```powershell
dotnet add tests\CodeMetrics.AI.Tests\CodeMetrics.AI.Tests.csproj package JsonSchema.Net
```

Expected: exits `0`; a `JsonSchema.Net` PackageReference appears in the test csproj.

- [ ] **Step 2: Write the conformance tests (failing first run is fine — they must pass unedited)**

Write `tests/CodeMetrics.AI.Tests/Output/SchemaConformanceTests.cs`:

```csharp
using System.Text.Json.Nodes;
using CodeMetrics.AI.Output;
using CodeMetrics.AI.Probes;
using FluentAssertions;
using Json.Schema;

namespace CodeMetrics.AI.Tests.Output;

public class SchemaConformanceTests
{
    private static readonly string SchemaDir = FindSchemaDir();
    private static readonly JsonSchema Schema =
        JsonSchema.FromFile(Path.Combine(SchemaDir, "evidence.schema.v2.json"));

    // Walk up from the test output directory to the repo root. Robust to the
    // analyzers/dotnet move planned in the 2026-06-09 reorg.
    private static string FindSchemaDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "shared", "scorecard-schema");
            if (File.Exists(Path.Combine(candidate, "evidence.schema.v2.json")))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "shared/scorecard-schema/evidence.schema.v2.json not found walking up from test output.");
    }

    private static readonly string[] AllDimensionKeys =
    [
        "codeQuality", "maintainability", "errorHandling", "performanceAsync",
        "security", "testing", "documentation", "dependencyManagement", "architecture"
    ];

    private static EvidenceModel CreateRepresentativeModel()
    {
        var dimensions = new Dictionary<string, object>();
        foreach (var key in AllDimensionKeys)
        {
            dimensions[key] = new DimensionResult
            {
                Status = "scored",
                Score = 7.5,
                Basis = "Representative basis.",
                Findings =
                [
                    new Finding
                    {
                        Category = "representative",
                        Severity = "warning",
                        File = "C:/repo/src/A.cs",
                        Line = 10,
                        Project = "My",
                        Type = "A",
                        Message = "Representative finding."
                    }
                ]
            };
        }

        return new EvidenceModel
        {
            Subject = new SubjectInfo
            {
                Root = "C:/repo",
                EntryPoint = "C:/repo/My.slnx",
                Name = "My",
                Variant = "Debug"
            },
            Filters = new FilterInfo
            {
                TotalUnits = 2,
                AnalyzedUnits = 1,
                Skipped = [new SkippedUnitInfo { Name = "My.Tests", Reason = "Test project" }]
            },
            Population = new PopulationInfo { Types = 1, Members = 1 },
            Dimensions = dimensions,
        };
    }

    private static async Task<JsonNode> SerializeAsync(EvidenceModel model)
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await EvidenceWriter.WriteAsync(tempFile, model);
            return JsonNode.Parse(await File.ReadAllTextAsync(tempFile))!;
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private static EvaluationResults Evaluate(JsonNode node) =>
        Schema.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List });

    private static string Errors(EvaluationResults results) =>
        string.Join("; ", results.Details
            .Where(d => d.HasErrors)
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}")));

    [Fact]
    public async Task GeneratedEvidence_ValidatesAgainstSchemaV2()
    {
        var node = await SerializeAsync(CreateRepresentativeModel());

        var results = Evaluate(node);

        results.IsValid.Should().BeTrue(Errors(results));
    }

    [Fact]
    public async Task SkippedDimension_WithoutScore_IsValid()
    {
        var model = CreateRepresentativeModel();
        model.Dimensions["dependencyManagement"] = new DimensionResult
        {
            Status = "skipped",
            Basis = "Dependency probe skipped via --skip-dependency-probe."
        };
        var node = await SerializeAsync(model);

        var results = Evaluate(node);

        results.IsValid.Should().BeTrue(Errors(results));
        node["dimensions"]!["dependencyManagement"]!.AsObject()
            .ContainsKey("score").Should().BeFalse("skipped dimensions must omit score");
    }

    [Fact]
    public async Task Evidence_MissingDimensionKey_IsInvalid()
    {
        var node = await SerializeAsync(CreateRepresentativeModel());
        node["dimensions"]!.AsObject().Remove("architecture");

        var results = Evaluate(node);

        results.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ScoredDimension_WithoutScore_IsInvalid()
    {
        var node = await SerializeAsync(CreateRepresentativeModel());
        node["dimensions"]!["codeQuality"]!.AsObject().Remove("score");

        var results = Evaluate(node);

        results.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("dotnet-evidence.json")]
    [InlineData("javascript-typescript-evidence.json")]
    public void ExampleEvidence_ValidatesAgainstSchemaV2(string exampleFile)
    {
        var path = Path.Combine(SchemaDir, "examples", exampleFile);
        var node = JsonNode.Parse(File.ReadAllText(path))!;

        var results = Evaluate(node);

        results.IsValid.Should().BeTrue(Errors(results));
    }
}
```

- [ ] **Step 3: Run the conformance tests**

Run: `dotnet test --verbosity minimal --filter SchemaConformanceTests`
Expected: exits `0`, 6 tests pass (4 facts + 2 theory cases). If `GeneratedEvidence_ValidatesAgainstSchemaV2` fails, the error message names the violating instance location — fix the schema or the model, whichever is wrong, before proceeding.

- [ ] **Step 4: Run the full suite and commit**

Run: `dotnet test --verbosity minimal`
Expected: exits `0`.

```powershell
git add tests
git commit -m "test: validate generated evidence against shared schema v2"
```

Expected: commit succeeds.

---

### Task 6: Revise The 2026-06-09 Reorg Plan

The reorg plan has not been executed. These edits remove its now-obsolete v1 schema task, fix the npm `bin`/tsconfig mismatch, point the JS scaffold at the v2 contract and per-ecosystem paths, and add schema validation to the JS tests. Apply each edit with the Edit tool against `docs/superpowers/plans/2026-06-09-analyzer-suite-reorg-js-scaffold.md`.

**Files:**
- Modify: `docs/superpowers/plans/2026-06-09-analyzer-suite-reorg-js-scaffold.md`

- [ ] **Step 1: Update the File Structure list**

Replace:

```markdown
- `shared/scorecard-schema/evidence.schema.v1.json`: shared evidence schema for analyzer outputs.
- `shared/scorecard-schema/metrics-csv.md`: shared CSV column contract.
- `shared/scorecard-schema/dimensions.md`: stable dimension key documentation.
- `shared/scorecard-schema/examples/dotnet-evidence.json`: minimal .NET evidence example.
- `shared/scorecard-schema/examples/javascript-typescript-evidence.json`: minimal JS/TS evidence example.
```

with:

```markdown
- `shared/scorecard-schema/`: pre-existing shared contract (schema v2, dimension/CSV/calibration docs, examples) created by the 2026-06-10 contract plan. This plan only verifies it; it does not create or move it.
```

- [ ] **Step 2: Rescope Task 5 to verification only**

Replace the entire `### Task 5: Add Shared Scorecard Contract Docs` section (from its heading down to, but not including, `### Task 6:`) with:

````markdown
### Task 5: Verify Shared Scorecard Contract Docs

The shared contract was created by `docs/superpowers/plans/2026-06-10-scorecard-contract-v2-multi-ecosystem.md`. Nothing moves in this reorg — `shared/` is already at its final location.

- [ ] **Step 1: Verify the contract files exist and parse**

Run:

```powershell
Get-ChildItem shared\scorecard-schema -Recurse -File | Select-Object Name
Get-Content shared\scorecard-schema\evidence.schema.v2.json -Raw | ConvertFrom-Json | Out-Null
```

Expected: `evidence.schema.v2.json`, `dimensions.md`, `metrics-csv.md`, `ecosystems.md`, `calibration.md`, and both example JSON files are listed; the schema parses without error. No commit needed.

---
````

- [ ] **Step 3: Fix the JS package devDependencies (ajv for schema validation)**

In the Task 6 `package.json` block, replace:

```json
  "devDependencies": {
    "@types/node": "^22.15.29",
    "vitest": "^3.2.2"
  }
```

with:

```json
  "devDependencies": {
    "@types/node": "^22.15.29",
    "ajv": "^8.17.1",
    "ajv-formats": "^3.0.1",
    "vitest": "^3.2.2"
  }
```

- [ ] **Step 4: Fix the tsconfig so `dist/cli.js` matches the `bin` entry**

In the Task 6 tsconfig step, replace:

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "NodeNext",
    "moduleResolution": "NodeNext",
    "rootDir": ".",
    "outDir": "dist",
    "declaration": true,
    "strict": true,
    "esModuleInterop": true,
    "forceConsistentCasingInFileNames": true,
    "skipLibCheck": true
  },
  "include": ["src/**/*.ts", "tests/**/*.ts"]
}
```

with:

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "NodeNext",
    "moduleResolution": "NodeNext",
    "rootDir": "src",
    "outDir": "dist",
    "declaration": true,
    "strict": true,
    "esModuleInterop": true,
    "forceConsistentCasingInFileNames": true,
    "skipLibCheck": true
  },
  "include": ["src/**/*.ts"]
}
```

and replace the step's expectation line `Expected: `npm run build` compiles source and tests to `dist`.` with `Expected: `npm run build` compiles `src` only, emitting `dist/cli.js` at the exact path the package `bin` entry declares. Tests are executed by Vitest directly and are never compiled or published.`

- [ ] **Step 5: Point the contract constants at schema v2 and per-ecosystem paths**

In the Task 6 `scorecard-contract.ts` step, replace the file content with:

```typescript
export const ecosystem = "javascript-typescript";
export const schemaVersion = 2;

export const defaultMetricsPath = ".scorecard/javascript-typescript/metrics.csv";
export const defaultEvidencePath = ".scorecard/javascript-typescript/evidence.json";

export const dimensionKeys = [
  "codeQuality",
  "maintainability",
  "errorHandling",
  "performanceAsync",
  "security",
  "testing",
  "documentation",
  "dependencyManagement",
  "architecture",
] as const;

export type DimensionKey = (typeof dimensionKeys)[number];

export const metricsCsvHeader =
  "Scope,Project,Namespace,Type,Member,Maintainability Index,Cyclomatic Complexity,Depth of Inheritance,Class Coupling,Lines of Source code,Lines of Executable code";
```

In the Task 6 `index.ts` step, replace the file content with:

```typescript
export {
  defaultEvidencePath,
  defaultMetricsPath,
  dimensionKeys,
  ecosystem,
  metricsCsvHeader,
  schemaVersion,
  type DimensionKey,
} from "./scorecard-contract.js";
```

- [ ] **Step 6: Replace the smoke tests and add the ajv schema test**

Replace the Task 6 Step 8 test-file content (`tests/scorecard-contract.test.ts`) with:

```typescript
import { describe, expect, it } from "vitest";
import {
  defaultEvidencePath,
  defaultMetricsPath,
  dimensionKeys,
  ecosystem,
  metricsCsvHeader,
  schemaVersion,
} from "../src/index.js";

describe("scorecard contract", () => {
  it("uses the per-ecosystem default output paths", () => {
    expect(defaultMetricsPath).toBe(".scorecard/javascript-typescript/metrics.csv");
    expect(defaultEvidencePath).toBe(".scorecard/javascript-typescript/evidence.json");
  });

  it("declares its ecosystem id and schema version", () => {
    expect(ecosystem).toBe("javascript-typescript");
    expect(schemaVersion).toBe(2);
  });

  it("exports the stable dimension keys", () => {
    expect(dimensionKeys).toEqual([
      "codeQuality",
      "maintainability",
      "errorHandling",
      "performanceAsync",
      "security",
      "testing",
      "documentation",
      "dependencyManagement",
      "architecture",
    ]);
  });

  it("matches the shared metrics CSV header", () => {
    expect(metricsCsvHeader).toBe(
      "Scope,Project,Namespace,Type,Member,Maintainability Index,Cyclomatic Complexity,Depth of Inheritance,Class Coupling,Lines of Source code,Lines of Executable code",
    );
  });
});
```

and add to the same step a second test file, `tests/evidence-schema.test.ts`:

```typescript
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import Ajv2020 from "ajv/dist/2020.js";
import addFormats from "ajv-formats";
import { describe, expect, it } from "vitest";

const schemaPath = fileURLToPath(
  new URL("../../../shared/scorecard-schema/evidence.schema.v2.json", import.meta.url),
);
const examplePath = fileURLToPath(
  new URL(
    "../../../shared/scorecard-schema/examples/javascript-typescript-evidence.json",
    import.meta.url,
  ),
);

describe("evidence schema v2", () => {
  it("accepts the javascript-typescript example evidence", () => {
    const ajv = new Ajv2020({ strict: false });
    addFormats(ajv);
    const validate = ajv.compile(JSON.parse(readFileSync(schemaPath, "utf8")));

    const valid = validate(JSON.parse(readFileSync(examplePath, "utf8")));

    expect(validate.errors ?? []).toEqual([]);
    expect(valid).toBe(true);
  });
});
```

- [ ] **Step 7: Fix the verify commands and test counts**

In the reorg plan, replace **both** occurrences (Task 6 Step 10 and Task 8 Step 2) of:

```powershell
node dist\src\cli.js --help
```

with:

```powershell
node dist\cli.js --help
```

and replace **both** occurrences of `Vitest reports `3` passing tests` with `Vitest reports `5` passing tests`. Also replace Task 6 Step 6's expectation line `Expected: `node dist/src/cli.js --help` prints help after build.` with `Expected: `node dist/cli.js --help` prints help after build.`

- [ ] **Step 8: Fix output paths in the reorg plan's README/docs content**

In the Task 3 root-README content block, replace:

```markdown
| `.scorecard/metrics.csv` | Raw code metrics in the shared CSV shape |
| `.scorecard/evidence.json` | Scored evidence across stable quality dimensions |
```

with:

```markdown
| `.scorecard/<ecosystem>/metrics.csv` | Raw code metrics in the shared CSV shape |
| `.scorecard/<ecosystem>/evidence.json` | Scored evidence across stable quality dimensions (schema v2) |

Ecosystem ids (`dotnet`, `javascript-typescript`, ...) are defined in `shared/scorecard-schema/ecosystems.md`. Per-ecosystem output directories let analyzers coexist in polyglot repositories.
```

In the Task 3 `docs/analyzers/dotnet.md` content block, replace:

```markdown
| `.scorecard/metrics.csv` | VS-compatible raw metrics |
| `.scorecard/evidence.json` | Scorecard evidence using schema version 1 |
```

with:

```markdown
| `.scorecard/dotnet/metrics.csv` | VS-compatible raw metrics |
| `.scorecard/dotnet/evidence.json` | Scorecard evidence using schema version 2 |
```

In the Task 6 JS README content block, replace:

```markdown
npx codemetrics-ai --output .scorecard/metrics.csv --scorecard-output .scorecard/evidence.json
```

with:

```markdown
npx codemetrics-ai --output .scorecard/javascript-typescript/metrics.csv --scorecard-output .scorecard/javascript-typescript/evidence.json
```

and replace:

```markdown
| `.scorecard/metrics.csv` | Shared metrics CSV |
| `.scorecard/evidence.json` | Scorecard evidence using schema version 1 |
```

with:

```markdown
| `.scorecard/javascript-typescript/metrics.csv` | Shared metrics CSV |
| `.scorecard/javascript-typescript/evidence.json` | Scorecard evidence using schema version 2 |
```

In the Task 7 Python and Rust docs content blocks, replace **both** occurrences of:

```markdown
The analyzer will produce `.scorecard/metrics.csv` and `.scorecard/evidence.json` using the shared scorecard contract.
```

with (using the matching ecosystem id, `python` or `rust`):

```markdown
The analyzer will produce `.scorecard/<ecosystem>/metrics.csv` and `.scorecard/<ecosystem>/evidence.json` using the shared scorecard contract (schema v2).
```

- [ ] **Step 9: Strengthen the final-verification grep**

Replace the reorg plan's Task 8 Step 3 `rg` command block with:

```powershell
rg -n "dotnet pack src/CodeMetrics|dotnet test --verbosity|dotnet build --verbosity|Project Path=\"src/CodeMetrics" .github analyzers README.md docs shared --glob '!docs/superpowers/**'
rg -n "\.scorecard/(metrics\.csv|evidence\.json)|schema version 1|schemaVersion.. 1|evidence\.schema\.v1" .github analyzers README.md docs shared --glob '!docs/superpowers/**'
```

(`docs/superpowers/**` is excluded from both commands because the spec and plan documents quote the old strings they superseded.)

with expectation: `Expected: no output from either command — every output-path reference carries an ecosystem segment and no v1 schema references remain.`

- [ ] **Step 10: Commit the plan revision**

```powershell
git add docs\superpowers\plans\2026-06-09-analyzer-suite-reorg-js-scaffold.md
git commit -m "docs: revise reorg plan for schema v2, ecosystem paths, and npm bin fix"
```

Expected: commit succeeds.

---

### Task 7: Skill — Ecosystem-Aware SKILL.md

All Task 7–9 work happens in `E:\repos\ai_tools`. Run git commands from that repo.

**Files:**
- Modify: `E:\repos\ai_tools\skills\code-scorecard\SKILL.md`

- [ ] **Step 1: Update the Overview evidence paragraph**

Replace:

```markdown
The deterministic pass starts from scorecard-native JSON evidence at `<solution-root>\.scorecard\evidence.json`, produced by the `CodeMetrics.AI` global tool via the bundled `Scorecard` MSBuild target. Before generating or trusting evidence, update the tool so the latest deterministic rules are used. Do not silently substitute qualitative scoring when JSON evidence includes a deterministic dimension score.
```

with:

```markdown
The deterministic pass starts from scorecard-native JSON evidence at `<repo-root>\.scorecard\<ecosystem>\evidence.json` (schema v2), produced by the CodeMetrics.AI analyzer for each detected ecosystem — the `code-metrics` dotnet global tool for `dotnet`, the `codemetrics-ai` NPM CLI for `javascript-typescript`. Before generating or trusting evidence, update the analyzer so the latest deterministic rules are used. Do not silently substitute qualitative scoring when JSON evidence includes a deterministic dimension score.
```

- [ ] **Step 2: Replace Required Inputs and the scope paragraph**

Replace:

```markdown
- **Preferred deterministic evidence:** `<solution-root>\.scorecard\evidence.json`
- **Compatibility fallback for dimensions 2 and 9 only:** `<solution-root>\.scorecard\metrics.csv`

**First-time setup, missing evidence, or regenerating after a tool update:** see `bootstrap.md` for the full Step 0–5 procedure (resolve solution → check prerequisites → install/update tool → drop `Directory.Build.targets` → generate → validate).

**Solution scope:** if the repo contains multiple `.sln` or `.slnx` files, specify which one to use at invocation time — e.g. "run the scorecard against eContract.API.slnx". If none is specified, the skill will detect and prompt.
```

with:

```markdown
- **Preferred deterministic evidence, one file per detected ecosystem:** `<repo-root>\.scorecard\<ecosystem>\evidence.json` (ecosystem ids: `dotnet`, `javascript-typescript`)
- **Compatibility fallback for dimensions 2 and 9, `dotnet` ecosystem only:** `<repo-root>\.scorecard\dotnet\metrics.csv`

**First-time setup, missing evidence, or regenerating after a tool update:** see `bootstrap.md` for ecosystem detection and the per-ecosystem generation procedure.

**Scope:** if the repo contains multiple `.sln`/`.slnx` files or multiple package workspaces, specify which entry point to use at invocation time — e.g. "run the scorecard against eContract.API.slnx". To restrict a polyglot repo to one ecosystem, say so — e.g. "scorecard the dotnet side only". If nothing is specified, the skill detects and prompts.
```

- [ ] **Step 3: Insert the Ecosystem Detection section**

Insert immediately before the `## Invocation Args` heading:

```markdown
## Ecosystem Detection

Detect which analyzers apply before touching evidence. Check the repo root (non-recursive):

| Marker | Ecosystem id | Analyzer |
|---|---|---|
| `*.sln` / `*.slnx` | `dotnet` | `code-metrics` dotnet global tool |
| `package.json` | `javascript-typescript` | `codemetrics-ai` NPM CLI |

- Both markers present → polyglot repo: score **each** detected ecosystem (unless the invocation scoped to one).
- Evidence for each detected ecosystem lives at `.scorecard\<ecosystem>\evidence.json`. Missing, invalid, or mismatched evidence → run the bootstrap for that ecosystem (`bootstrap.md`).
- An evidence directory with no matching marker (e.g. `.scorecard\dotnet\` in a repo with no solution) → stale evidence; report it and do not score from it.
- No marker at all → no deterministic analyzer applies. Produce a fully qualitative scorecard and say so explicitly in the output.

---
```

- [ ] **Step 4: Update the Invocation Args solution bullet**

Replace:

```markdown
- **Solution path** (`.sln` or `.slnx`): scope the scorecard to that solution (see Solution scope above).
```

with:

```markdown
- **Entry-point path** (`.sln`, `.slnx`, or a `package.json`): scope the scorecard to that entry point's ecosystem (see Scope above).
- **Ecosystem name** (`dotnet`, `javascript-typescript`): scope a polyglot repo to one ecosystem.
```

- [ ] **Step 5: Rewrite the Deterministic Evidence Pass**

Replace the entire section body (from `Start with `.scorecard\evidence.json` whenever present and valid for the requested run:` through item `7.` and the closing `If evidence is missing entirely...` paragraph) with:

```markdown
Run this pass once per detected ecosystem, starting from `.scorecard\<ecosystem>\evidence.json`:

1. Parse JSON and require `schemaVersion == 2`. If the schema is missing or unsupported, report that explicitly and regenerate with the latest analyzer for that ecosystem (see `bootstrap.md`).
2. Validate provenance: `tool.ecosystem` must equal the directory name the evidence was found under; `subject.entryPoint` must match the resolved entry point; for `dotnet`, `subject.variant` must match the requested configuration; `tool.version` must match the analyzer version just installed/updated. If any value is missing or mismatched, regenerate evidence.
3. Never decide freshness by comparing `generatedAtUtc` or filesystem LastWriteTime values to source-file mtimes. Those values vary across clones and CI checkouts.
4. For each dimension under `dimensions`, use the JSON `score` when `status` is `scored`.
5. Include the dimension `status`, `basis`, and top finding counts in the evidence summary.
6. If a dimension is `skipped` or `failed`, report the status and reason. Fall back only for that dimension:
   - **Code Quality (dim 2) or Maintainability (dim 9), `dotnet` ecosystem only:** use the CSV deterministic procedure in `csv-fallback.md` against `.scorecard\dotnet\metrics.csv`
   - **Any other dimension, or any non-dotnet ecosystem:** use qualitative scoring against the Scoring Anchors above, citing concrete artifacts
7. Do not hide probe limitations. State that deterministic probes are conservative static evidence, not a substitute for human review. For ecosystems marked uncalibrated in the shared contract (`shared/scorecard-schema/dimensions.md` in the CodeMetrics.AI repo), add one line noting that scores are not calibrated against other ecosystems.

If evidence is missing entirely for a detected ecosystem, jump to `bootstrap.md` before scoring that ecosystem.
```

- [ ] **Step 6: Restrict the CSV-fallback statements to dotnet**

Replace (Scoring Anchors closing paragraph):

```markdown
When JSON evidence contains a dimension score, use it as authoritative and cite its basis/status. Qualitative anchors apply only to dimensions without usable JSON evidence. CSV deterministic fallback applies only to Code Quality and Maintainability.
```

with:

```markdown
When JSON evidence contains a dimension score, use it as authoritative and cite its basis/status. Qualitative anchors apply only to dimensions without usable JSON evidence. CSV deterministic fallback applies only to Code Quality and Maintainability, and only for the `dotnet` ecosystem — its thresholds and archetypes are Roslyn-calibrated.
```

Also update the two dimension headings: replace `### 2. Code Quality *(JSON deterministic when available; CSV deterministic fallback)*` with `### 2. Code Quality *(JSON deterministic when available; CSV deterministic fallback, dotnet only)*`, and `### 9. Maintainability *(JSON deterministic when available; CSV deterministic fallback)*` with `### 9. Maintainability *(JSON deterministic when available; CSV deterministic fallback, dotnet only)*`.

- [ ] **Step 7: Add multi-ecosystem rendering to Output Format**

Insert immediately after the mode/sections table (the table ending with `| **`--verbose --explain`** | 1, 2, 3, 4, 5, 6, 7 |` and its explanatory paragraph `Sections 1–3 are always shown...`):

```markdown
**Polyglot repos:** when more than one ecosystem was scored, render the selected sections once **per ecosystem**, each under an `## <ecosystem>` heading, then close with a single **Suite Summary** table:

| Dimension | dotnet | javascript-typescript |
|-----------|--------|------------------------|
| ... one row per dimension, then a per-ecosystem **Overall** row ... | | |

Never average, combine, or rank scores **across** ecosystems — cross-ecosystem comparability requires the calibration procedure in the shared contract, and uncalibrated ecosystems must carry a one-line caveat under the table.
```

- [ ] **Step 8: Fix the status wording and Rules**

In the Section 2 example table, replace `completed/skipped/failed/fallback` with `scored/skipped/failed/fallback`.

In `## Rules`, replace:

```markdown
- **One decimal on the overall.** Unweighted mean of applicable dimensions only.
```

with:

```markdown
- **One decimal on the overall.** Unweighted mean of applicable dimensions only, computed per ecosystem.
- **Never average across ecosystems.** Polyglot repos get one overall per ecosystem and a side-by-side Suite Summary, nothing blended.
```

- [ ] **Step 9: Update Supporting References**

Replace:

```markdown
- **`csv-fallback.md`** — CSV deterministic procedure for Code Quality (dim 2) and Maintainability (dim 9), including the 9-step pass, archetype tagging, per-archetype scoring reference, and calibration notes
```

with:

```markdown
- **`csv-fallback.md`** — CSV deterministic procedure for Code Quality (dim 2) and Maintainability (dim 9), **dotnet ecosystem only**, including the 9-step pass, archetype tagging, per-archetype scoring reference, and calibration notes
```

and add at the end of the list:

```markdown
- **Shared contract** — schema v2, ecosystem registry, dimension keys, and the cross-ecosystem calibration procedure live in the CodeMetrics.AI repo under `shared/scorecard-schema/`
```

- [ ] **Step 10: Commit (ai_tools repo)**

```powershell
git add skills\code-scorecard\SKILL.md
git commit -m "feat(code-scorecard): ecosystem detection and schema v2 evidence pass"
```

Expected: commit succeeds.

---

### Task 8: Skill — Per-Ecosystem bootstrap.md

**Files:**
- Rewrite: `E:\repos\ai_tools\skills\code-scorecard\bootstrap.md`

- [ ] **Step 1: Replace the full contents of `bootstrap.md`**

````markdown
# Bootstrap — Setup & Evidence Generation

Use this file when any detected ecosystem lacks `.scorecard\<ecosystem>\evidence.json`, the evidence doesn't match the requested run, or this is the first scorecard on a repo. Once evidence exists and matches the requested entry point / variant / tool version for every detected ecosystem, skip ahead and score directly from `SKILL.md`.

## Step 0 — Detect ecosystems and resolve entry points

Check the repo root (non-recursive) and apply any invocation-arg scoping:

| Marker | Ecosystem id | Entry point to resolve |
|---|---|---|
| `*.sln` / `*.slnx` | `dotnet` | The solution file. Exactly one found: use it silently. Multiple: ask which. |
| `package.json` | `javascript-typescript` | The root `package.json` (workspace root in a monorepo). |

- Both markers → bootstrap **both** ecosystems unless the invocation scoped to one.
- Neither marker → no deterministic analyzer applies. Return to `SKILL.md` and produce a fully qualitative scorecard, saying so explicitly.

Record each resolved entry point — evidence validation in `SKILL.md` compares `subject.entryPoint` against it.

## Step 1 — Check prerequisites (all detected ecosystems, one pass)

| # | Check | What to look for |
|---|-------|-----------------|
| 1 | Latest analyzer for each ecosystem | Run the matching install/update block below before generating or accepting evidence. Do not skip the update just because a tool is already installed. |
| 2 | (`dotnet` only) `Directory.Build.targets` at solution root defines a `Scorecard` target | If absent, the MSBuild target won't be injected. |
| 3 | `.scorecard\<ecosystem>\evidence.json` exists and matches the requested run | Require `schemaVersion == 2`, `tool.ecosystem` equal to the folder name, matching `subject.entryPoint`, matching `subject.variant` (dotnet), and current tool version. Do **not** use filesystem mtimes to decide freshness. |
| 4 | (`dotnet` only) `.scorecard\dotnet\metrics.csv` for compatibility fallback | Required only if JSON is missing/unsupported and dimensions 2/9 must be scored from CSV. |

Present any gaps to the user as one consolidated list, then ask once whether to set everything up automatically:

> **Scorecard prerequisites missing:**
> - [ ] latest `code-metrics` global tool (dotnet)
> - [ ] `Directory.Build.targets` (MSBuild Scorecard target)
> - [ ] `.scorecard\dotnet\evidence.json` (missing, unsupported, or mismatched)
> - [ ] latest `codemetrics-ai` NPM CLI (javascript-typescript)
> - [ ] `.scorecard\javascript-typescript\evidence.json` (missing, unsupported, or mismatched)
>
> **Set up everything automatically?** (yes / no / I'll export from VS instead)

List only the rows for detected ecosystems. "I'll export from VS instead" applies to `dotnet` only — see Path B at the bottom.

---

## dotnet ecosystem

### D1 — Install or update the tool

```pwsh
if (dotnet tool list -g | Select-String "codemetrics.ai") {
    dotnet tool update -g CodeMetrics.AI
} else {
    dotnet tool install -g CodeMetrics.AI
}
```

Safe to run every time — fast, and guarantees the latest deterministic rules.

### D2 — Drop the Directory.Build.targets

Copy `scorecard-tooling/Directory.Build.targets` (in this skill's directory) to the solution root. It defines the `Scorecard` MSBuild target that shells out to `code-metrics` and writes under `.scorecard\dotnet\`. One-time setup per solution.

### D3 — Generate evidence

Default configuration is `Debug`; pass `/p:ScorecardConfiguration=Release` for a release-build audit.

```pwsh
# With explicit solution (resolved in Step 0):
dotnet build "<path-to-sln-or-slnx>" /t:Scorecard /p:ScorecardSolutionPath="<path-to-sln-or-slnx>"

# Without explicit solution (auto-discovery):
dotnet build /t:Scorecard
```

### D4 — Validate

```pwsh
$rows = Import-Csv .scorecard\dotnet\metrics.csv
$typeCount = ($rows | Where-Object Scope -eq 'Type').Count
$memberCount = ($rows | Where-Object Scope -eq 'Member').Count
$evidence = Get-Content .scorecard\dotnet\evidence.json -Raw | ConvertFrom-Json
Write-Host "Schema: $($evidence.schemaVersion)   Ecosystem: $($evidence.tool.ecosystem)   Types: $typeCount   Members: $memberCount"
```

Expected: `Schema: 2`, `Ecosystem: dotnet`, non-zero counts. **If both counts are 0:** re-run with explicit args:

```pwsh
code-metrics --solution <path-to-sln-or-slnx> --output .scorecard\dotnet\metrics.csv --scorecard-output .scorecard\dotnet\evidence.json
```

If still zero rows, fall back to the Visual Studio export (Path B) for dimensions 2 and 9 only.

---

## javascript-typescript ecosystem

### J1 — Check Node

```pwsh
node --version
```

Expected: v20 or later. If Node is missing, report it and score this ecosystem qualitatively.

### J2 — Run the analyzer

```pwsh
npx --yes codemetrics-ai@latest
```

Defaults write `.scorecard/javascript-typescript/metrics.csv` and `.scorecard/javascript-typescript/evidence.json`. Pass `--project <path-to-package.json>` or `--tsconfig <path>` when Step 0 resolved a non-root entry point.

**If the package is not yet published or the command produces no evidence file:** say so explicitly, score all nine dimensions for this ecosystem qualitatively against the Scoring Anchors, and never substitute the dotnet CSV fallback — its archetypes and thresholds are C#-calibrated.

### J3 — Validate

```pwsh
$evidence = Get-Content .scorecard\javascript-typescript\evidence.json -Raw | ConvertFrom-Json
Write-Host "Schema: $($evidence.schemaVersion)   Ecosystem: $($evidence.tool.ecosystem)   Types: $($evidence.population.types)   Members: $($evidence.population.members)"
```

Expected: `Schema: 2`, `Ecosystem: javascript-typescript`, non-zero counts.

---

## Path B — Visual Studio GUI export (dotnet only, reliable fallback)

If `dotnet build /t:Scorecard` produces empty metrics (see `troubleshooting.md`):

1. Open the solution in Visual Studio 2022
2. **Analyze → Calculate Code Metrics → For Solution**
3. Wait for the Code Metrics Results window to populate
4. Click the **Export list** icon (floppy disk) → save as `.scorecard\dotnet\metrics.csv` at the solution root

The VS export produces only the CSV columns. It can replace JSON evidence for dimensions 2 and 9 only; dimensions 1, 3, 4, 5, 6, 7, and 8 require qualitative scoring with source access.
````

- [ ] **Step 2: Verify and commit (ai_tools repo)**

Run: `rg -n "scorecard\\\\evidence|scorecard\\\\metrics" skills\code-scorecard\bootstrap.md`
Expected: no output (no un-prefixed `.scorecard\` paths remain — prefixed paths like `.scorecard\dotnet\evidence.json` don't match).

```powershell
git add skills\code-scorecard\bootstrap.md
git commit -m "feat(code-scorecard): per-ecosystem bootstrap procedure"
```

Expected: commit succeeds.

---

### Task 9: Skill — Fallback Scope, Troubleshooting, MSBuild Target

**Files:**
- Modify: `E:\repos\ai_tools\skills\code-scorecard\csv-fallback.md`
- Modify: `E:\repos\ai_tools\skills\code-scorecard\troubleshooting.md`
- Modify: `E:\repos\ai_tools\skills\code-scorecard\scorecard-tooling\Directory.Build.targets`
- Modify: `E:\repos\ai_tools\skills\code-scorecard\scorecard-tooling\README.md`

- [ ] **Step 1: Scope csv-fallback.md to dotnet**

Replace the opening paragraph:

```markdown
Use this file **only** when JSON evidence is missing or unsupported for Code Quality (Dimension 2) or Maintainability (Dimension 9) and a `.scorecard\metrics.csv` export is available. For all other cases, score from `.scorecard\evidence.json` per `SKILL.md`.
```

with:

```markdown
> **Ecosystem scope: `dotnet` only.** The filters, archetypes, and thresholds in this file are calibrated for Roslyn metrics on C# code. Never apply them to another ecosystem's CSV; for non-dotnet ecosystems, score dimensions 2 and 9 qualitatively until that ecosystem completes the calibration procedure in the CodeMetrics.AI shared contract (`shared/scorecard-schema/calibration.md`).

Use this file **only** when JSON evidence is missing or unsupported for Code Quality (Dimension 2) or Maintainability (Dimension 9) and a `.scorecard\dotnet\metrics.csv` export is available. For all other cases, score from `.scorecard\dotnet\evidence.json` per `SKILL.md`.
```

- [ ] **Step 2: Update troubleshooting.md paths and schema version**

In `troubleshooting.md`:

Replace:

```pwsh
code-metrics --solution <path-to-.sln-or-.slnx> --output .scorecard\metrics.csv --scorecard-output .scorecard\evidence.json
```

with:

```pwsh
code-metrics --solution <path-to-.sln-or-.slnx> --output .scorecard\dotnet\metrics.csv --scorecard-output .scorecard\dotnet\evidence.json
```

Replace:

```markdown
**Symptom:** `.scorecard\evidence.json` is absent, invalid JSON, or has an unsupported `schemaVersion`.
```

with:

```markdown
**Symptom:** `.scorecard\<ecosystem>\evidence.json` is absent, invalid JSON, or has an unsupported `schemaVersion`.
```

Replace:

```markdown
**Fix:** Update `CodeMetrics.AI`, regenerate with the `Scorecard` target, and confirm the JSON validation command prints `Schema: 1`. If JSON remains unavailable but `.scorecard\metrics.csv` matches the requested solution/configuration, use CSV fallback (see `csv-fallback.md`) only for Code Quality and Maintainability.
```

with:

```markdown
**Fix:** Update the analyzer for that ecosystem, regenerate, and confirm the validation command prints `Schema: 2`. If JSON remains unavailable for `dotnet` but `.scorecard\dotnet\metrics.csv` matches the requested solution/configuration, use CSV fallback (see `csv-fallback.md`) only for Code Quality and Maintainability. CSV fallback never applies to other ecosystems.
```

Append a new section at the end of the file:

```markdown
## `codemetrics-ai` (NPM) not found or produces no output

**Symptom:** `npx codemetrics-ai` fails to resolve, or runs without writing `.scorecard\javascript-typescript\evidence.json`.

**Cause:** The NPM analyzer is not yet published, Node is older than v20, or the repo has no analyzable sources under its configured roots.

**Fix:** Check `node --version` (need ≥ 20) and retry with `npx --yes codemetrics-ai@latest`. If the package is unavailable or still produces nothing, score the `javascript-typescript` ecosystem qualitatively against the Scoring Anchors and state that deterministic JS evidence was unavailable. Do not apply the dotnet CSV fallback to JS code.
```

- [ ] **Step 3: Update the MSBuild target output directory**

In `scorecard-tooling/Directory.Build.targets`, replace:

```xml
      <ScorecardOutputDir>$(SolutionDir).scorecard\</ScorecardOutputDir>
```

with:

```xml
      <ScorecardOutputDir>$(SolutionDir).scorecard\dotnet\</ScorecardOutputDir>
```

- [ ] **Step 4: Update the tooling README output section**

In `scorecard-tooling/README.md`, replace:

```markdown
The target produces two files under `.scorecard/`:

- `metrics.csv` — VS-compatible raw code metrics
- `evidence.json` — scored scorecard evidence (schema v1)
```

with:

```markdown
The target produces two files under `.scorecard/dotnet/`:

- `metrics.csv` — VS-compatible raw code metrics
- `evidence.json` — scored scorecard evidence (schema v2)
```

- [ ] **Step 5: Verify no stale references remain in the skill**

Run from `E:\repos\ai_tools\skills\code-scorecard`:

```powershell
rg -n "schemaVersion == 1|schema v1|Schema: 1|scorecard\\\\evidence\.json|scorecard\\\\metrics\.csv|scorecard/evidence\.json|scorecard/metrics\.csv" .
```

Expected: no output. (Paths with an ecosystem segment like `.scorecard\dotnet\evidence.json` don't match these patterns.)

- [ ] **Step 6: Commit (ai_tools repo)**

```powershell
git add skills\code-scorecard
git commit -m "feat(code-scorecard): dotnet-scoped fallback, v2 paths in tooling and troubleshooting"
```

Expected: commit succeeds.

---

### Task 10: Final Verification

- [ ] **Step 1: Full .NET suite**

Run from `E:\repos\CodeMetrics.AI`: `dotnet test --verbosity minimal`
Expected: exits `0`, all tests passing (including the 6 schema-conformance tests).

- [ ] **Step 2: No stale v1/path references in CodeMetrics.AI**

```powershell
rg -n "schemaVersion.{0,4}1|schema v1|evidence\.schema\.v1|\.scorecard/(metrics\.csv|evidence\.json)" src tests shared README.md docs --glob '!docs/superpowers/**'
```

Expected: no output. (`docs/superpowers/` is excluded wholesale: the spec is historical, and both plan documents necessarily quote the old v1 strings they replace. The revised reorg plan's own Task 8 Step 3 verifies the post-reorg tree.)

- [ ] **Step 3: Clean status in both repos**

```powershell
git -C E:\repos\CodeMetrics.AI status --short
git -C E:\repos\ai_tools status --short
```

Expected: no output from either.

- [ ] **Step 4: Confirm execution order is documented**

Verify the revised reorg plan still begins with its Task 1 baseline check, and that this plan's commits all precede any reorg execution. The reorg plan (`2026-06-09-analyzer-suite-reorg-js-scaffold.md`) is now safe to execute: it moves the v2-emitting .NET tool and scaffolds the JS package against the v2 contract with a working `bin` path.

---

## Out of Scope (deliberate)

- **Implementing the JS analyzer's metrics** — the reorg plan scaffolds it; metric implementation is its own future plan, which must satisfy the parity prerequisites in `calibration.md` (fixture-asserted MI formula, CC parity table).
- **Running the calibration corpus** — `calibration.md` defines the procedure; executing it requires a functional JS analyzer.
- **npm/NuGet publish workflows and per-analyzer tag schemes** (`dotnet-v*` / `js-v*`) — settle when the first JS publish is actually planned; nothing publishes until then.

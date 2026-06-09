# Analyzer Suite Reorg And JS/TS Scaffold Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reorganize CodeMetrics.AI into a multi-analyzer suite and add the first buildable `codemetrics-ai` JavaScript/TypeScript analyzer scaffold.

**Architecture:** The existing .NET analyzer moves under `analyzers/dotnet/` without changing its package identity or runtime behavior. Shared scorecard docs live under `shared/scorecard-schema/`, and the new JS/TS analyzer starts as an independent NPM package under `analyzers/javascript-typescript/` with contract constants and smoke tests.

**Tech Stack:** .NET 10, C# project/solution files, GitHub Actions, Node.js 20+, TypeScript, Vitest, NPM package bin entry points, JSON Schema documentation.

---

## File Structure

Files and responsibilities after this plan:

- `analyzers/dotnet/CodeMetrics.AI.slnx`: solution file for the existing .NET analyzer.
- `analyzers/dotnet/src/CodeMetrics.AI/CodeMetrics.AI.csproj`: existing NuGet tool project, with README package path updated.
- `analyzers/dotnet/tests/CodeMetrics.AI.Tests/CodeMetrics.AI.Tests.csproj`: existing tests, with project reference updated.
- `analyzers/dotnet/README.md`: .NET-specific analyzer usage copied from the current root README.
- `README.md`: suite overview and analyzer index.
- `.github/workflows/ci.yml`: suite CI that validates .NET and JS/TS analyzer workspaces.
- `.github/workflows/publish.yml`: NuGet publish workflow updated for the moved .NET project.
- `shared/scorecard-schema/evidence.schema.v1.json`: shared evidence schema for analyzer outputs.
- `shared/scorecard-schema/metrics-csv.md`: shared CSV column contract.
- `shared/scorecard-schema/dimensions.md`: stable dimension key documentation.
- `shared/scorecard-schema/examples/dotnet-evidence.json`: minimal .NET evidence example.
- `shared/scorecard-schema/examples/javascript-typescript-evidence.json`: minimal JS/TS evidence example.
- `docs/analyzers/dotnet.md`: .NET analyzer guide.
- `docs/analyzers/javascript-typescript.md`: JS/TS analyzer design guide.
- `docs/analyzers/python.md`: Python next-wave analyzer guide.
- `docs/analyzers/rust.md`: Rust next-wave analyzer guide.
- `analyzers/javascript-typescript/package.json`: NPM package metadata for `codemetrics-ai`.
- `analyzers/javascript-typescript/tsconfig.json`: TypeScript compiler config.
- `analyzers/javascript-typescript/src/index.ts`: package exports.
- `analyzers/javascript-typescript/src/cli.ts`: CLI entry point.
- `analyzers/javascript-typescript/src/scorecard-contract.ts`: shared output paths and dimension constants.
- `analyzers/javascript-typescript/tests/scorecard-contract.test.ts`: Vitest smoke tests for package contract.
- `analyzers/python/README.md`: next-wave analyzer landing page.
- `analyzers/rust/README.md`: next-wave analyzer landing page.

---

### Task 1: Baseline Verification

**Files:**
- Read: `CodeMetrics.AI.slnx`
- Read: `.github/workflows/ci.yml`
- Read: `.github/workflows/publish.yml`

- [ ] **Step 1: Confirm current .NET tests pass**

Run:

```powershell
dotnet test --verbosity minimal
```

Expected: command exits `0`, with all tests passing.

- [ ] **Step 2: Confirm the worktree is clean before moving files**

Run:

```powershell
git status --short
```

Expected: no output.

- [ ] **Step 3: Commit checkpoint is not needed**

No files changed in this task.

---

### Task 2: Move .NET Analyzer Into `analyzers/dotnet`

**Files:**
- Move: `CodeMetrics.AI.slnx` to `analyzers/dotnet/CodeMetrics.AI.slnx`
- Move: `src/` to `analyzers/dotnet/src/`
- Move: `tests/` to `analyzers/dotnet/tests/`
- Create: `analyzers/dotnet/README.md`
- Modify: `analyzers/dotnet/CodeMetrics.AI.slnx`
- Modify: `analyzers/dotnet/src/CodeMetrics.AI/CodeMetrics.AI.csproj`
- Modify: `analyzers/dotnet/tests/CodeMetrics.AI.Tests/CodeMetrics.AI.Tests.csproj`

- [ ] **Step 1: Create the analyzer directory**

Run:

```powershell
New-Item -ItemType Directory -Force -Path analyzers\dotnet | Out-Null
```

Expected: command exits `0`.

- [ ] **Step 2: Move existing .NET files**

Run:

```powershell
Move-Item -LiteralPath CodeMetrics.AI.slnx -Destination analyzers\dotnet\CodeMetrics.AI.slnx
Move-Item -LiteralPath src -Destination analyzers\dotnet\src
Move-Item -LiteralPath tests -Destination analyzers\dotnet\tests
Copy-Item -LiteralPath README.md -Destination analyzers\dotnet\README.md
```

Expected: `CodeMetrics.AI.slnx`, `src`, and `tests` no longer exist at the repository root. `analyzers/dotnet/README.md` exists.

- [ ] **Step 3: Update the .NET project README package include**

Edit `analyzers/dotnet/src/CodeMetrics.AI/CodeMetrics.AI.csproj` so the README item is:

```xml
<ItemGroup>
  <None Include="..\..\README.md" Pack="true" PackagePath="\" />
</ItemGroup>
```

Expected: the path points from `analyzers/dotnet/src/CodeMetrics.AI/` to `analyzers/dotnet/README.md`.

- [ ] **Step 4: Verify the test project reference still points to the moved source project**

Confirm `analyzers/dotnet/tests/CodeMetrics.AI.Tests/CodeMetrics.AI.Tests.csproj` contains:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\CodeMetrics.AI\CodeMetrics.AI.csproj" />
</ItemGroup>
```

Expected: no change is needed after the move because the relative layout inside `analyzers/dotnet/` remains the same.

- [ ] **Step 5: Verify the solution file still points to the moved source and test projects**

Confirm `analyzers/dotnet/CodeMetrics.AI.slnx` contains:

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/CodeMetrics.AI/CodeMetrics.AI.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/CodeMetrics.AI.Tests/CodeMetrics.AI.Tests.csproj" />
  </Folder>
</Solution>
```

Expected: no change is needed after the move because the solution file moved with its relative source and test folders.

- [ ] **Step 6: Run .NET tests from the analyzer directory**

Run:

```powershell
dotnet test analyzers\dotnet\CodeMetrics.AI.slnx --verbosity minimal
```

Expected: command exits `0`, with all tests passing.

- [ ] **Step 7: Commit the .NET move**

Run:

```powershell
git add analyzers\dotnet CodeMetrics.AI.slnx src tests
git commit -m "chore: move dotnet analyzer into suite layout"
```

Expected: commit succeeds.

---

### Task 3: Update Suite README And .NET Analyzer Docs

**Files:**
- Modify: `README.md`
- Create: `docs/analyzers/dotnet.md`

- [ ] **Step 1: Replace the root README with the suite overview**

Write `README.md`:

```markdown
# CodeMetrics.AI

CodeMetrics.AI is a suite of deterministic code analyzers that produce shared scorecard evidence for AI-assisted codebase review.

Each analyzer runs in the package ecosystem natural to its target language, then writes the same default outputs:

| File | Description |
|------|-------------|
| `.scorecard/metrics.csv` | Raw code metrics in the shared CSV shape |
| `.scorecard/evidence.json` | Scored evidence across stable quality dimensions |

## Analyzers

| Ecosystem | Location | Package | Status |
|-----------|----------|---------|--------|
| .NET / C# | `analyzers/dotnet` | `CodeMetrics.AI` NuGet global tool | Available |
| JavaScript / TypeScript / React | `analyzers/javascript-typescript` | `codemetrics-ai` NPM package | Scaffolded |
| Python | `analyzers/python` | Python package | Planned next wave |
| Rust | `analyzers/rust` | Rust crate | Planned next wave |

## Shared Contract

The shared scorecard contract is documented in `shared/scorecard-schema`.

Stable dimensions:

- `codeQuality`
- `maintainability`
- `errorHandling`
- `performanceAsync`
- `security`
- `testing`
- `documentation`
- `dependencyManagement`
- `architecture`

## .NET Usage

```bash
dotnet tool install -g CodeMetrics.AI
code-metrics
```

See `analyzers/dotnet/README.md` for .NET-specific usage.

## JavaScript / TypeScript Usage

```bash
npx codemetrics-ai
```

The JS/TS analyzer scaffold lives in `analyzers/javascript-typescript`.

## License

[MIT](LICENSE)
```

Expected: root README describes the suite rather than only the .NET tool.

- [ ] **Step 2: Add a .NET analyzer docs page**

Write `docs/analyzers/dotnet.md`:

```markdown
# .NET Analyzer

The .NET analyzer is packaged as the `CodeMetrics.AI` NuGet global tool and exposed through the `code-metrics` command.

## Location

```text
analyzers/dotnet/
```

## Usage

```bash
dotnet tool install -g CodeMetrics.AI
code-metrics
code-metrics --solution MyApp.slnx
code-metrics --skip-dependency-probe
code-metrics --output ./results/metrics.csv --scorecard-output ./results/evidence.json
```

## Outputs

| File | Description |
|------|-------------|
| `.scorecard/metrics.csv` | VS-compatible raw metrics |
| `.scorecard/evidence.json` | Scorecard evidence using schema version 1 |

## Local Development

```bash
dotnet test analyzers/dotnet/CodeMetrics.AI.slnx --verbosity minimal
dotnet pack analyzers/dotnet/src/CodeMetrics.AI/CodeMetrics.AI.csproj -c Release -o ./nupkg
```
```

Expected: docs page has analyzer-specific commands with moved paths.

- [ ] **Step 3: Commit README and docs**

Run:

```powershell
git add README.md docs\analyzers\dotnet.md
git commit -m "docs: add analyzer suite overview"
```

Expected: commit succeeds.

---

### Task 4: Update GitHub Actions For Suite Layout

**Files:**
- Modify: `.github/workflows/ci.yml`
- Modify: `.github/workflows/publish.yml`

- [ ] **Step 1: Update CI workflow**

Write `.github/workflows/ci.yml`:

```yaml
name: CI

on:
  push:
    branches: [master]
  pull_request:
    branches: [master]

jobs:
  dotnet:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Build .NET analyzer
        run: dotnet build analyzers/dotnet/CodeMetrics.AI.slnx --verbosity minimal

      - name: Test .NET analyzer
        run: dotnet test analyzers/dotnet/CodeMetrics.AI.slnx --verbosity minimal

  javascript-typescript:
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: analyzers/javascript-typescript
    steps:
      - uses: actions/checkout@v4

      - name: Setup Node
        uses: actions/setup-node@v4
        with:
          node-version: '20'
          cache: npm
          cache-dependency-path: analyzers/javascript-typescript/package-lock.json

      - name: Install dependencies
        run: npm ci

      - name: Build JS/TS analyzer
        run: npm run build

      - name: Test JS/TS analyzer
        run: npm test
```

Expected: .NET commands reference `analyzers/dotnet`, and JS/TS CI is ready once package files and lockfile exist.

- [ ] **Step 2: Update NuGet publish workflow**

Write `.github/workflows/publish.yml`:

```yaml
name: Publish to NuGet

on:
  push:
    tags:
      - 'v*'

jobs:
  publish:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Test
        run: dotnet test analyzers/dotnet/CodeMetrics.AI.slnx --verbosity minimal

      - name: Pack
        run: dotnet pack analyzers/dotnet/src/CodeMetrics.AI/CodeMetrics.AI.csproj -c Release -o ./nupkg /p:Version=${GITHUB_REF_NAME#v}

      - name: Push to NuGet
        run: dotnet nuget push ./nupkg/*.nupkg --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json --skip-duplicate
```

Expected: publish workflow packs the moved .NET project.

- [ ] **Step 3: Commit workflow updates**

Run:

```powershell
git add .github\workflows\ci.yml .github\workflows\publish.yml
git commit -m "ci: update workflows for analyzer suite layout"
```

Expected: commit succeeds.

---

### Task 5: Add Shared Scorecard Contract Docs

**Files:**
- Create: `shared/scorecard-schema/evidence.schema.v1.json`
- Create: `shared/scorecard-schema/metrics-csv.md`
- Create: `shared/scorecard-schema/dimensions.md`
- Create: `shared/scorecard-schema/examples/dotnet-evidence.json`
- Create: `shared/scorecard-schema/examples/javascript-typescript-evidence.json`

- [ ] **Step 1: Create schema directories**

Run:

```powershell
New-Item -ItemType Directory -Force -Path shared\scorecard-schema\examples | Out-Null
```

Expected: command exits `0`.

- [ ] **Step 2: Add evidence schema**

Write `shared/scorecard-schema/evidence.schema.v1.json`:

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://github.com/sean-m-cooper/CodeMetrics.AI/shared/scorecard-schema/evidence.schema.v1.json",
  "title": "CodeMetrics.AI Evidence",
  "type": "object",
  "required": ["schemaVersion", "generatedAtUtc", "tool", "solution", "filters", "population", "dimensions"],
  "properties": {
    "schemaVersion": { "const": 1 },
    "generatedAtUtc": { "type": "string" },
    "tool": {
      "type": "object",
      "required": ["name", "version"],
      "properties": {
        "name": { "type": "string", "minLength": 1 },
        "version": { "type": "string", "minLength": 1 }
      },
      "additionalProperties": true
    },
    "solution": {
      "type": "object",
      "additionalProperties": true
    },
    "filters": {
      "type": "object",
      "additionalProperties": true
    },
    "population": {
      "type": "object",
      "additionalProperties": true
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
      },
      "additionalProperties": true
    }
  },
  "$defs": {
    "dimensionResult": {
      "type": "object",
      "required": ["status", "score", "basis"],
      "properties": {
        "status": { "enum": ["scored", "skipped", "failed"] },
        "score": { "type": "number", "minimum": 0, "maximum": 10 },
        "basis": { "type": "string" },
        "findings": {
          "type": "array",
          "items": { "$ref": "#/$defs/finding" }
        }
      },
      "additionalProperties": true
    },
    "finding": {
      "type": "object",
      "required": ["category", "severity", "message"],
      "properties": {
        "category": { "type": "string", "minLength": 1 },
        "severity": { "enum": ["info", "warning", "error"] },
        "file": { "type": "string" },
        "line": { "type": "integer", "minimum": 1 },
        "project": { "type": "string" },
        "type": { "type": "string" },
        "member": { "type": "string" },
        "package": { "type": "string" },
        "message": { "type": "string", "minLength": 1 }
      },
      "additionalProperties": true
    }
  },
  "additionalProperties": true
}
```

Expected: schema accepts existing .NET-style evidence while enforcing stable dimension keys and result shapes.

- [ ] **Step 3: Add metrics CSV documentation**

Write `shared/scorecard-schema/metrics-csv.md`:

```markdown
# Metrics CSV Contract

Default path:

```text
.scorecard/metrics.csv
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
```

Expected: CSV contract matches the current .NET writer header.

- [ ] **Step 4: Add dimension documentation**

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

Analyzers may use language-specific rules inside each dimension. The key names and score scale are stable.
```

Expected: all nine dimension keys are documented.

- [ ] **Step 5: Add evidence examples**

Write `shared/scorecard-schema/examples/dotnet-evidence.json`:

```json
{
  "schemaVersion": 1,
  "generatedAtUtc": "2026-06-09T00:00:00.0000000Z",
  "tool": {
    "name": "CodeMetrics.AI",
    "version": "1.0.1"
  },
  "solution": {
    "path": "/repo/MyApp.slnx",
    "configuration": "Debug"
  },
  "filters": {
    "totalProjects": 1,
    "analyzedProjects": 1,
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

Write `shared/scorecard-schema/examples/javascript-typescript-evidence.json`:

```json
{
  "schemaVersion": 1,
  "generatedAtUtc": "2026-06-09T00:00:00.0000000Z",
  "tool": {
    "name": "codemetrics-ai",
    "version": "0.1.0"
  },
  "solution": {
    "path": "/repo/package.json",
    "configuration": "tsconfig.json"
  },
  "filters": {
    "totalProjects": 1,
    "analyzedProjects": 1,
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

Expected: both examples include every stable dimension key.

- [ ] **Step 6: Commit shared contract docs**

Run:

```powershell
git add shared\scorecard-schema
git commit -m "docs: add shared scorecard contract"
```

Expected: commit succeeds.

---

### Task 6: Add JS/TS Analyzer NPM Scaffold

**Files:**
- Create: `analyzers/javascript-typescript/package.json`
- Create: `analyzers/javascript-typescript/tsconfig.json`
- Create: `analyzers/javascript-typescript/README.md`
- Create: `analyzers/javascript-typescript/src/index.ts`
- Create: `analyzers/javascript-typescript/src/cli.ts`
- Create: `analyzers/javascript-typescript/src/scorecard-contract.ts`
- Create: `analyzers/javascript-typescript/tests/scorecard-contract.test.ts`
- Generate: `analyzers/javascript-typescript/package-lock.json`

- [ ] **Step 1: Create package directories**

Run:

```powershell
New-Item -ItemType Directory -Force -Path analyzers\javascript-typescript\src | Out-Null
New-Item -ItemType Directory -Force -Path analyzers\javascript-typescript\tests | Out-Null
```

Expected: command exits `0`.

- [ ] **Step 2: Add NPM package metadata**

Write `analyzers/javascript-typescript/package.json`:

```json
{
  "name": "codemetrics-ai",
  "version": "0.1.0",
  "description": "Deterministic code metrics and scorecard evidence for JavaScript, TypeScript, and React projects",
  "license": "MIT",
  "type": "module",
  "bin": {
    "codemetrics-ai": "./dist/cli.js"
  },
  "files": [
    "dist",
    "README.md"
  ],
  "scripts": {
    "build": "tsc -p tsconfig.json",
    "test": "vitest run"
  },
  "engines": {
    "node": ">=20"
  },
  "dependencies": {
    "typescript": "^5.8.3"
  },
  "devDependencies": {
    "@types/node": "^22.15.29",
    "vitest": "^3.2.2"
  }
}
```

Expected: package name and CLI are both `codemetrics-ai`.

- [ ] **Step 3: Add TypeScript config**

Write `analyzers/javascript-typescript/tsconfig.json`:

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

Expected: `npm run build` compiles source and tests to `dist`.

- [ ] **Step 4: Add scorecard contract constants**

Write `analyzers/javascript-typescript/src/scorecard-contract.ts`:

```typescript
export const defaultMetricsPath = ".scorecard/metrics.csv";
export const defaultEvidencePath = ".scorecard/evidence.json";

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

Expected: constants match shared schema docs and current .NET CSV header.

- [ ] **Step 5: Add package exports**

Write `analyzers/javascript-typescript/src/index.ts`:

```typescript
export {
  defaultEvidencePath,
  defaultMetricsPath,
  dimensionKeys,
  metricsCsvHeader,
  type DimensionKey,
} from "./scorecard-contract.js";
```

Expected: consumers can import contract constants.

- [ ] **Step 6: Add CLI entry point**

Write `analyzers/javascript-typescript/src/cli.ts`:

```typescript
#!/usr/bin/env node

import { defaultEvidencePath, defaultMetricsPath } from "./scorecard-contract.js";

const args = new Set(process.argv.slice(2));

if (args.has("--help") || args.has("-h")) {
  console.log(`codemetrics-ai

Deterministic code metrics and scorecard evidence for JavaScript, TypeScript, and React projects.

Usage:
  codemetrics-ai [options]

Options:
  --project <path>            Path to package.json
  --tsconfig <path>           Path to tsconfig.json
  --output <path>             Metrics CSV output path (default: ${defaultMetricsPath})
  --scorecard-output <path>   Evidence JSON output path (default: ${defaultEvidencePath})
  -h, --help                  Show help
`);
  process.exit(0);
}

console.log("codemetrics-ai JS/TS analyzer scaffold");
console.log(`Metrics output: ${defaultMetricsPath}`);
console.log(`Evidence output: ${defaultEvidencePath}`);
```

Expected: `node dist/src/cli.js --help` prints help after build.

- [ ] **Step 7: Add JS/TS analyzer README**

Write `analyzers/javascript-typescript/README.md`:

```markdown
# codemetrics-ai

NPM analyzer for JavaScript, TypeScript, and React projects.

## Usage

```bash
npx codemetrics-ai
npx codemetrics-ai --project ./package.json
npx codemetrics-ai --tsconfig ./tsconfig.json
npx codemetrics-ai --output .scorecard/metrics.csv --scorecard-output .scorecard/evidence.json
```

## Outputs

| File | Description |
|------|-------------|
| `.scorecard/metrics.csv` | Shared metrics CSV |
| `.scorecard/evidence.json` | Scorecard evidence using schema version 1 |

## Development

```bash
npm install
npm run build
npm test
```
```

Expected: package README matches the approved package and CLI name.

- [ ] **Step 8: Add smoke tests**

Write `analyzers/javascript-typescript/tests/scorecard-contract.test.ts`:

```typescript
import { describe, expect, it } from "vitest";
import {
  defaultEvidencePath,
  defaultMetricsPath,
  dimensionKeys,
  metricsCsvHeader,
} from "../src/index.js";

describe("scorecard contract", () => {
  it("uses the shared default output paths", () => {
    expect(defaultMetricsPath).toBe(".scorecard/metrics.csv");
    expect(defaultEvidencePath).toBe(".scorecard/evidence.json");
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

Expected: tests exercise the exported contract constants.

- [ ] **Step 9: Install dependencies and generate lockfile**

Run:

```powershell
Push-Location analyzers\javascript-typescript
npm install
Pop-Location
```

Expected: `analyzers/javascript-typescript/package-lock.json` is created.

- [ ] **Step 10: Build and test the package**

Run:

```powershell
Push-Location analyzers\javascript-typescript
npm run build
npm test
node dist\src\cli.js --help
Pop-Location
```

Expected: build exits `0`, Vitest reports `3` passing tests, and CLI help prints usage.

- [ ] **Step 11: Commit JS/TS scaffold**

Run:

```powershell
git add analyzers\javascript-typescript
git commit -m "feat: scaffold javascript typescript analyzer"
```

Expected: commit succeeds.

---

### Task 7: Add JS/TS, Python, And Rust Analyzer Docs

**Files:**
- Create: `docs/analyzers/javascript-typescript.md`
- Create: `docs/analyzers/python.md`
- Create: `docs/analyzers/rust.md`
- Create: `analyzers/python/README.md`
- Create: `analyzers/rust/README.md`

- [ ] **Step 1: Add JS/TS analyzer docs**

Write `docs/analyzers/javascript-typescript.md`:

```markdown
# JavaScript / TypeScript / React Analyzer

The JavaScript / TypeScript analyzer is packaged as the `codemetrics-ai` NPM package and exposed through the `codemetrics-ai` command.

## Location

```text
analyzers/javascript-typescript/
```

## Supported Inputs

- `.js`
- `.jsx`
- `.ts`
- `.tsx`
- `package.json`
- `tsconfig.json`

## Metrics Policy

Cyclomatic complexity starts at `1` per function-like unit and counts runtime decision points such as `if`, loops, `catch`, non-default `switch` cases, ternaries, logical operators, and nullish coalescing.

TypeScript type-only declarations do not add runtime complexity. JSX markup does not add complexity by itself, but expressions inside JSX are analyzed.

## React

React components and custom hooks are first-class analysis targets. React-specific findings are mapped into the stable scorecard dimensions.
```

Expected: docs page captures JS/TS scope.

- [ ] **Step 2: Add Python docs**

Write `docs/analyzers/python.md`:

```markdown
# Python Analyzer

The Python analyzer is planned for the next implementation wave.

## Location

```text
analyzers/python/
```

## Expected Scope

- Python packages and applications.
- Backend frameworks such as FastAPI and Django.
- Script-heavy automation repositories.
- AI, data, and tooling codebases.

## Shared Outputs

The analyzer will produce `.scorecard/metrics.csv` and `.scorecard/evidence.json` using the shared scorecard contract.
```

Write `analyzers/python/README.md`:

```markdown
# CodeMetrics.AI Python Analyzer

Planned next-wave analyzer for Python codebases.

The analyzer will use the shared scorecard contract documented in `../../shared/scorecard-schema`.
```

Expected: Python has a clear next-wave landing page.

- [ ] **Step 3: Add Rust docs**

Write `docs/analyzers/rust.md`:

```markdown
# Rust Analyzer

The Rust analyzer is planned for the next implementation wave.

## Location

```text
analyzers/rust/
```

## Expected Scope

- Rust crates and workspaces.
- CLI, backend, systems, and WebAssembly-oriented repositories.
- Cargo metadata and dependency analysis.

## Shared Outputs

The analyzer will produce `.scorecard/metrics.csv` and `.scorecard/evidence.json` using the shared scorecard contract.
```

Write `analyzers/rust/README.md`:

```markdown
# CodeMetrics.AI Rust Analyzer

Planned next-wave analyzer for Rust crates and workspaces.

The analyzer will use the shared scorecard contract documented in `../../shared/scorecard-schema`.
```

Expected: Rust has a clear next-wave landing page.

- [ ] **Step 4: Commit analyzer docs**

Run:

```powershell
git add docs\analyzers analyzers\python analyzers\rust
git commit -m "docs: add next wave analyzer guides"
```

Expected: commit succeeds.

---

### Task 8: Final Verification

**Files:**
- Verify: repository-wide moved layout and generated lockfile.

- [ ] **Step 1: Run .NET tests**

Run:

```powershell
dotnet test analyzers\dotnet\CodeMetrics.AI.slnx --verbosity minimal
```

Expected: command exits `0`, with all tests passing.

- [ ] **Step 2: Run JS/TS build and tests**

Run:

```powershell
Push-Location analyzers\javascript-typescript
npm run build
npm test
node dist\src\cli.js --help
Pop-Location
```

Expected: build exits `0`, Vitest reports `3` passing tests, and CLI help prints usage.

- [ ] **Step 3: Verify no stale root paths remain in workflows or solution references**

Run:

```powershell
rg -n "dotnet pack src/CodeMetrics|dotnet test --verbosity|dotnet build --verbosity|Project Path=\"src/CodeMetrics|ProjectReference Include=\"\\.\\.\\\\\\.\\.\\\\src" .github analyzers README.md docs shared
```

Expected: no output.

- [ ] **Step 4: Check git status**

Run:

```powershell
git status --short
```

Expected: no output.

- [ ] **Step 5: Commit checkpoint is not needed**

All implementation tasks committed their changes.

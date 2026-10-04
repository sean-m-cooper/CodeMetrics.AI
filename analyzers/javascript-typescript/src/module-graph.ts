import path from "node:path";
import { isBuiltin } from "node:module";
import ts from "typescript";
import type { PackageInput } from "./discovery.js";
import { hash, type Dimension, type Finding } from "./evidence.js";
import { moduleReferences, type ModuleReference } from "./module-references.js";
import { canonicalFile, moduleResolver } from "./module-resolution.js";
import { moduleTopology } from "./module-topology.js";
import { dependencyRoles, type DependencyRole } from "./module-forwarding.js";

type Resolution = "internal" | "external" | "builtin" | "outOfScope" | "unavailableSource" | "unresolved" | "dynamic";
interface ModuleNode { id: string; project: string; analyzed: boolean; reExportOnly: boolean; }
export interface DependencyObservation {
  from: string; project: string; line: number; sourceSpanStart: number;
  form: ModuleReference["form"]; usage: ModuleReference["usage"]; specifier: string | null;
  roles: DependencyRole[];
  resolution: Resolution; via: string; target?: string; targetFile?: string; declarationTarget?: boolean;
}
function reExportOnly(source: ts.SourceFile): boolean {
  return source.statements.some(statement => ts.isExportDeclaration(statement) && !!statement.moduleSpecifier) &&
    source.statements.every(statement => ts.isImportDeclaration(statement) || (ts.isExportDeclaration(statement) && !!statement.moduleSpecifier) ||
      ts.isInterfaceDeclaration(statement) || ts.isTypeAliasDeclaration(statement) || ts.isEmptyStatement(statement) ||
      (ts.isExpressionStatement(statement) && ts.isStringLiteral(statement.expression)));
}

export class ModuleGraphCollector {
  private readonly nodes = new Map<string, ModuleNode>();
  private readonly dependencies: DependencyObservation[] = [];
  private readonly scanned = new Set<string>();
  constructor(private readonly root: string, private readonly packages: PackageInput[]) {
    for (const pkg of packages) for (const file of pkg.files) {
      const key = canonicalFile(file);
      if (!this.nodes.has(key)) this.nodes.set(key, { id: this.relative(file), project: pkg.name, analyzed: false, reExportOnly: false });
    }
  }
  private relative(file: string) { return path.relative(this.root, file).replaceAll("\\", "/"); }
  forPackage(pkg: PackageInput, program: ts.Program) {
    const resolve = moduleResolver(pkg, this.packages, program);
    return (source: ts.SourceFile) => {
      const key = canonicalFile(source.fileName), node = this.nodes.get(key)!;
      if (this.scanned.has(key)) return;
      this.scanned.add(key); node.analyzed = true; node.reExportOnly = reExportOnly(source);
      const references = moduleReferences(source, program.getTypeChecker());
      const roles = dependencyRoles(source, program.getTypeChecker(), references);
      for (const reference of references) {
        const start = reference.node.getStart(source);
        const observation: DependencyObservation = { from: node.id, project: node.project,
          line: source.getLineAndCharacterOfPosition(start).line + 1, sourceSpanStart: start,
          form: reference.form, usage: reference.usage, roles: roles.get(reference.node)!, specifier: reference.literal?.text ?? null,
          resolution: "dynamic", via: "nonLiteralSpecifier" };
        if (reference.literal) this.resolveObservation(observation, source, reference.literal, resolve);
        this.dependencies.push(observation);
      }
    };
  }
  private resolveObservation(observation: DependencyObservation, source: ts.SourceFile, literal: ts.StringLiteralLike, resolve: ReturnType<typeof moduleResolver>) {
    if (isBuiltin(literal.text)) {
      observation.resolution = "builtin"; observation.via = "nodeBuiltin";
      observation.target = literal.text.startsWith("node:") ? literal.text : `node:${literal.text}`;
      return;
    }
    const { resolved, via } = resolve(source, literal);
    observation.via = via;
    if (!resolved) { observation.resolution = "unresolved"; return; }
    const file = canonicalFile(resolved.resolvedFileName);
    const target = this.nodes.get(file);
    observation.declarationTarget = /\.d\.[cm]?ts$/i.test(file);
    observation.targetFile = target?.id ?? this.relative(ts.sys.realpath?.(resolved.resolvedFileName) ?? resolved.resolvedFileName);
    if (target) { observation.resolution = "internal"; observation.target = target.id; }
    else observation.resolution = file.includes("/node_modules/") ? "external" : "outOfScope";
  }
  finish(incompleteSource: boolean): Dimension {
    const nodes = [...this.nodes.values()].sort((a, b) => a.id < b.id ? -1 : a.id > b.id ? 1 : 0);
    const byId = new Map(nodes.map(node => [node.id, node]));
    const dependencies = this.dependencies.map(item => item.resolution === "internal" && !byId.get(item.target!)!.analyzed ?
      { ...item, resolution: "unavailableSource" as const } : item)
      .sort((a, b) => a.from < b.from ? -1 : a.from > b.from ? 1 : a.sourceSpanStart - b.sourceSpanStart);
    const topology = moduleTopology(nodes.map(node => node.id), dependencies);
    const degrees = new Map(topology.modules.map(node => [node.id, node]));
    const counts = Object.fromEntries((['internal', 'external', 'builtin', 'outOfScope', 'unavailableSource', 'unresolved', 'dynamic'] as const)
      .map(resolution => [resolution, dependencies.filter(item => item.resolution === resolution).length])) as Record<Resolution, number>;
    const literalOccurrences = dependencies.length - counts.dynamic;
    const gaps = incompleteSource || this.scanned.size < nodes.length || counts.unresolved + counts.dynamic + counts.outOfScope + counts.unavailableSource > 0;
    const cycles = topology.cycles.map(modules => ({ id: hash(modules.join("\n")), modules }));
    const findings: Finding[] = cycles.map(cycle => ({
      ruleId: "javascript-typescript/architecture/moduleDependencyCycle", category: "moduleDependencyCycle",
      fingerprint: hash(`javascript-typescript/architecture/moduleDependencyCycle|${cycle.modules.join("|")}`),
      severity: "info", confidence: "high", file: cycle.modules[0], project: byId.get(cycle.modules[0])!.project,
      message: `${cycle.modules.length} module(s) form a cyclic value-dependency group; review loading behavior and intent.`,
      observations: { modules: cycle.modules, classification: "reviewLead", scoreDisposition: "excludedUncalibrated",
        countingUnit: "stronglyConnectedComponent", executionOrderEstablished: false }
    }));
    return { status: incompleteSource ? "failed" : "skipped", basis: incompleteSource ?
      "Incomplete source analysis; module graph is diagnostic only." : "Module dependency evidence collected; architecture scoring is not calibrated.",
      findings, scope: { id: "javascript-typescript/architecture/module-graph-v1", coverage: "partial",
        includes: ["source-module-references", "explicit-type-only-dependencies", "internal-value-cycles", "internal-value-fan-in-out"],
        excludes: ["architecture-score", "runtime-loading-order", "comprehensive-dynamic-resolution", "cohesion", "solid-compliance"] },
      dependencyGraph: { version: 1, graphKind: "source-value-dependencies", coverage: {
        status: gaps ? "gaps" : "observedDependenciesResolved", selectedModules: nodes.length, analyzedModules: this.scanned.size,
        dependencyOccurrences: dependencies.length, literalOccurrences, resolvedLiteralOccurrences: literalOccurrences - counts.unresolved,
        literalResolutionPercent: literalOccurrences ? 100 * (literalOccurrences - counts.unresolved) / literalOccurrences : null,
        occurrencesByResolution: counts, explicitTypeOnlyOccurrences: dependencies.filter(item => item.usage === "typeOnly").length },
        resolutionEnvironment: { typescriptVersion: ts.version, nodeVersion: process.versions.node },
        limitations: ["Value/mixed references are source syntax, not proof of emitted runtime imports.",
          "Graph scope contains selected source modules only; external and excluded targets are not traversed.",
          "Literal calls contribute dependency edges without establishing when or whether they execute.",
          "Require aliases, custom loaders, evaluated strings and bundler-specific resolution are not inferred.",
          "TypeScript resolution may select declarations; declarationTarget identifies these observations.",
          "Ranked fan-in/out lists are observations, not calibrated defects or score deductions."],
        nodes: nodes.map(node => ({ ...node, ...degrees.get(node.id) })), dependencies, cycles,
        dependencyViews: topology.dependencyViews,
        highestFanOut: topology.highestFanOut, highestFanIn: topology.highestFanIn } };
  }
}

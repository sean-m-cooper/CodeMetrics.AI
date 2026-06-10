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

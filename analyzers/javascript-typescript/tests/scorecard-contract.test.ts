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

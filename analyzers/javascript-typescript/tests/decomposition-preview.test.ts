import { describe, expect, it } from "vitest";
import { aggregate, candidates, spearman } from "../scripts/decomposition-preview.mjs";

describe("offline decomposition experiment", () => {
  it("keeps zero populations null and honors the proposed anchors", () => {
    expect(aggregate([], candidates.ownedStatements)).toBeNull();
    for (const [statements, score] of candidates.ownedStatements) expect(aggregate([statements], candidates.ownedStatements)).toBe(score);
    expect(aggregate([5, 15, 40], candidates.ownedStatements)).toBe(8.1); // .4*6 + .6*(10+9)/2
    expect(aggregate([160, 160, 10], candidates.ownedStatements)).toBe(3); // Only one worst gets the 40% group.
  });
  it("reports tied-rank correlation and leaves constant/absent populations unavailable", () => {
    expect(spearman([1,2,2,3],[4,5,5,6])).toBe(1);
    expect(spearman([1,2,3],[3,2,1])).toBe(-1);
    expect(spearman([1,1],[1,2])).toBeNull();
    expect(spearman([],[])).toBeNull();
  });
});

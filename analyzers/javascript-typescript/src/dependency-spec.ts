import semver from "semver";

export type DependencyKind = "production" | "development" | "optional" | "peer";
type Json = Record<string, unknown>;
const object = (value: unknown): value is Json => !!value && typeof value === "object" && !Array.isArray(value);
const kinds = { dependencies: "production", devDependencies: "development", optionalDependencies: "optional", peerDependencies: "peer" } as const;
export function declarations(manifest: Json, includeDevelopment = true): { requested: string; range: string; kind: DependencyKind; optional:boolean }[] {
  const rows: { requested: string; range: string; kind: DependencyKind; optional:boolean }[]=[];
  for (const [field,kind] of Object.entries(kinds)) {
    if (!includeDevelopment && kind==="development") continue;
    if (manifest[field]===undefined) continue;
    if (!object(manifest[field])) throw new Error(`Invalid ${field}`);
    for (const [requested,range] of Object.entries(manifest[field])) {
      if (typeof range!=="string") throw new Error(`Invalid dependency range for ${requested}`);
      if (kind==="production" && object(manifest.optionalDependencies) && requested in manifest.optionalDependencies) continue;
      const peer=object(manifest.peerDependenciesMeta)?manifest.peerDependenciesMeta[requested]:undefined;
      rows.push({requested,range,kind,optional:kind==="optional" || kind==="peer" && object(peer) && peer.optional===true});
    }
  }
  return rows.sort((a,b)=>a.requested.localeCompare(b.requested,"en") || a.kind.localeCompare(b.kind,"en"));
}
export function registrySpec(name: string, range: string): { name: string; range: string } | undefined {
  if (range.startsWith("npm:")) {
    const match=/^npm:((?:@[^/]+\/)?[^@/]+)(?:@(.+))?$/.exec(range);
    return match ? {name:match[1],range:match[2]??"*"} : undefined;
  }
  return semver.validRange(range) ? {name,range} : undefined;
}

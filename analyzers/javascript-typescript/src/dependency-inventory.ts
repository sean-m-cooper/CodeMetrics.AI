import fs from "node:fs";
import path from "node:path";
import semver from "semver";
import type { PackageInput } from "./discovery.js";
import { hash } from "./evidence.js";
import { pnpmInventory } from "./dependency-pnpm.js";

import { declarations, registrySpec, type DependencyKind } from "./dependency-spec.js";
export { declarations, registrySpec, type DependencyKind } from "./dependency-spec.js";
export interface DependencyUse { project: string; manifest: string; from: string; requested: string; range: string; resolvedRange?: string; kind: DependencyKind; direct: boolean; optional: boolean; }
export interface DependencyPackage {
  id: string; name: string; version: string; source: "publicRegistry" | "otherSource" | "workspace";
  locations: string[]; uses: DependencyUse[];
}
export interface DependencyGap { manifest: string; reason: string; dependency?: string; from?:string; range?:string; }
export interface DependencyInventory {
  version: 1; status: "complete" | "gaps"; countingUnit: "packageNameVersionSource";
  packages: DependencyPackage[]; gaps: DependencyGap[];
  notices: {manifest:string;from:string;dependency:string;reason:string;range:string;version?:string}[];
  manifests: { file: string; lockfile?: string; manager?: string; declarations: ReturnType<typeof declarations>; overrides?:unknown }[];
  lockfiles: { file: string; version: number; sha256: string; projectDocument?: number; environmentDocument?: "excluded" }[];
  configurationInputs?: {file:string;sha256:string}[];
}
type Json = Record<string, unknown>;
const object = (value: unknown): value is Json => !!value && typeof value === "object" && !Array.isArray(value);
const slash = (value: string) => value.replaceAll("\\", "/");
const within = (root: string, file: string) => { const r=path.relative(root,file);return !path.isAbsolute(r) && r!==".." && !r.startsWith(".."+path.sep); };
function read(file: string): { data: Json; sha256: string } {
  if (fs.statSync(file).size>20*1024*1024) throw new Error("Dependency input exceeds 20 MiB");
  const text=fs.readFileSync(file,"utf8"), data:unknown=JSON.parse(text);
  if (!object(data)) throw new Error("Dependency input must be an object");
  return {data,sha256:hash(text)};
}
function locate(start: string, root: string, inputs:Set<string>): {file?:string; manager?:string} {
  const directories:string[]=[];
  for(let dir=start;within(root,dir);dir=path.dirname(dir)) {
    directories.push(dir);
    if(dir===root) break;
  }
  let manager:string|undefined;
  for(const dir of directories) {
    const manifest=path.join(dir,"package.json");
    if(fs.existsSync(manifest)) inputs.add(manifest);
    const declared=fs.existsSync(manifest) ? read(manifest).data.packageManager : undefined;
    if(!manager && typeof declared==="string") manager=declared;
  }
  if(manager && !/^(npm|pnpm)@/.test(manager)) return {manager};
  for(const dir of directories) {
    const candidates=manager?.startsWith("pnpm@")?["pnpm-lock.yaml"]:manager?.startsWith("npm@")?["npm-shrinkwrap.json","package-lock.json"]:["pnpm-lock.yaml","npm-shrinkwrap.json","package-lock.json"];
    if(!manager && fs.existsSync(path.join(dir,"pnpm-lock.yaml")) && ["npm-shrinkwrap.json","package-lock.json","yarn.lock","bun.lock"].some(name=>fs.existsSync(path.join(dir,name)))) return {manager:"ambiguous lockfiles without packageManager"};
    for(const name of candidates) {
      const file=path.join(dir,name); if(fs.existsSync(file)) return {file,manager:name==="pnpm-lock.yaml"?"pnpm":"npm"};
    }
    const unsupported=["yarn.lock","bun.lock","bun.lockb"].find(name=>fs.existsSync(path.join(dir,name)));
    if(unsupported) {inputs.add(path.join(dir,unsupported));return {manager:unsupported};}
  }
  return {manager};
}
function validLocation(location: string): boolean {
  return !location.includes("\\") && !path.posix.isAbsolute(location) && !location.split("/").includes("..") && !location.includes(":");
}
function resolve(packages: Json, from: string, name: string): string | undefined {
  if(!/^(?:@[^/]+\/)?[^/]+$/.test(name) || name.includes("\\") || name.includes("..")) return;
  for(let dir=from;;dir=path.posix.dirname(dir)) {
    const at=path.posix.join(dir,"node_modules",name);
    if(object(packages[at])) return at;
    if(!dir || dir===".") return;
  }
}
function packageSource(entry: Json): DependencyPackage["source"] {
  if(typeof entry.resolved!=="string") return "otherSource";
  try { const url=new URL(entry.resolved);return url.protocol==="https:" && url.hostname==="registry.npmjs.org" && !url.username && !url.password ? "publicRegistry" : "otherSource"; }
  catch { return "otherSource"; }
}

/** Reads only selected manifests and reachable lockfile entries; never installs or executes packages. */
export function dependencyInventory(selected: Pick<PackageInput,"entryPoint"|"root"|"name">[], root: string): {inventory: DependencyInventory; inputs:string[]} {
  const inventory:DependencyInventory={version:1,status:"complete",countingUnit:"packageNameVersionSource",packages:[],gaps:[],notices:[],manifests:[],lockfiles:[]};
  const inputs=new Set(selected.map(pkg=>pkg.entryPoint)), rows=new Map<string,DependencyPackage>();
  const locks=new Map<string,{data:Json;sha256:string}>();
  const relative=(file:string)=>slash(path.relative(root,file));
  for(const pkg of selected) {
    const manifest=relative(pkg.entryPoint);
    const gap=(reason:string,dependency?:string,from?:string,range?:string)=>inventory.gaps.push({manifest,reason,...(dependency?{dependency}:{}),...(from!==undefined?{from}:{}),...(range?{range}:{})});
    try {
      const data=read(pkg.entryPoint).data, declared=declarations(data), located=locate(pkg.root,root,inputs);
      inventory.manifests.push({file:manifest,declarations:declared,...(data.overrides?{overrides:data.overrides}:{}),...(located.file?{lockfile:relative(located.file)}:{}),...(located.manager?{manager:located.manager}:{})});
      if(!located.file) { gap(located.manager && !/^(npm|pnpm)@/.test(located.manager) ? `Unsupported package manager/lockfile: ${located.manager}` : `No ${located.manager?.split("@")[0]??"npm or pnpm"} lockfile found`);continue; }
      inputs.add(located.file);
      if(!within(root,fs.realpathSync(located.file))) {gap("Lockfile is outside repository");continue;}
      if(located.manager==="pnpm") { pnpmInventory(pkg,root,located.file,inventory,inputs,rows);continue; }
      if(!locks.has(located.file)) locks.set(located.file,read(located.file));
      const lock=locks.get(located.file)!, packages=lock.data.packages;
      if(![2,3].includes(Number(lock.data.lockfileVersion)) || !object(packages)) {gap("Only npm lockfile versions 2 and 3 with a packages map are supported");continue;}
      if(!inventory.lockfiles.some(row=>row.file===relative(located.file!))) inventory.lockfiles.push({file:relative(located.file),version:Number(lock.data.lockfileVersion),sha256:lock.sha256});
      if(Object.keys(packages).some(location=>!validLocation(location))) {gap("Unsafe or malformed lockfile location");continue;}
      const start=slash(path.relative(path.dirname(located.file),pkg.root)), locked=packages[start];
      if(!object(locked)) {gap("Selected manifest has no lockfile package entry");continue;}
      if(JSON.stringify(declared)!==JSON.stringify(declarations(locked))) {gap("Manifest and lockfile declarations differ; resolved inventory is withheld");continue;}
      const visited=new Set<string>();
      const queue=declared.map(edge=>({from:start,...edge,mayBeAbsent:edge.optional,role:edge.kind,direct:true}));
      for(let index=0;index<queue.length;index++) {
        if(index>=100000) {gap("Dependency traversal limit exceeded");break;}
        const edge=queue[index], position=resolve(packages,edge.from,edge.requested);
        if(!position) {
          if(edge.mayBeAbsent) inventory.notices.push({manifest,from:edge.from,dependency:edge.requested,range:edge.range,reason:"Optional dependency/peer is absent from the lockfile; no missing-package deduction"});
          else gap(`Unresolved ${edge.kind} dependency in lockfile`,edge.requested,edge.from,edge.range);
          continue;
        }
        let location=position, entry=packages[location] as Json;
        const linked=entry.link===true;
        if(linked) {
          if(typeof entry.resolved!=="string" || !validLocation(entry.resolved) || !object(packages[entry.resolved])) {gap("Unresolved workspace/link target",edge.requested);continue;}
          location=entry.resolved;entry=packages[location] as Json;
          const target=path.resolve(path.dirname(located.file),location,"package.json");
          if(!within(root,target) || !fs.existsSync(target) || !within(root,fs.realpathSync(target))) {gap("Workspace manifest unavailable or outside repository",edge.requested);continue;}
          inputs.add(target);
          if(JSON.stringify(declarations(read(target).data))!==JSON.stringify(declarations(entry))) {gap("Workspace manifest and lockfile differ",edge.requested);continue;}
        }
        const spec=registrySpec(edge.requested,edge.range);
        const name=typeof entry.name==="string" ? entry.name : spec?.name??edge.requested;
        if(typeof entry.version!=="string" || !semver.valid(entry.version)) {gap("Missing/non-semver resolved version",edge.requested);continue;}
        if(spec && semver.validRange(spec.range) && !semver.satisfies(entry.version,spec.range))
          inventory.notices.push({manifest,from:edge.from,dependency:edge.requested,range:edge.range,version:entry.version,
            reason:"Locked version differs from requested range; overrides or peer-resolution policy may explain this. Installation validity is not assessed"});
        if(spec && spec.name!==name) inventory.notices.push({manifest,from:edge.from,dependency:edge.requested,range:edge.range,version:entry.version,
          reason:`Locked identity is ${name}; an alias/override may replace the requested package. Installation policy is not assessed`});
        const source=linked?"workspace":packageSource(entry), id=hash(`${name}|${entry.version}|${source}`);
        const row=rows.get(id)??{id,name,version:entry.version,source,locations:[],uses:[]};rows.set(id,row);
        const install=`${relative(located.file)}#${position}`;
        if(!row.locations.includes(install)) row.locations.push(install);
        const use:DependencyUse={project:pkg.name,manifest,from:edge.from,requested:edge.requested,range:edge.range,kind:edge.role,direct:edge.direct,optional:edge.optional};
        if(!row.uses.some(existing=>JSON.stringify(existing)===JSON.stringify(use))) row.uses.push(use);
        const key=`${location}|${edge.role}|${edge.optional}`;if(visited.has(key)) continue;visited.add(key);
        for(const child of declarations(entry,false)) queue.push({from:location,...child,mayBeAbsent:child.optional,optional:edge.optional||child.optional,role:edge.role,direct:false});
      }
    } catch(error) { gap(error instanceof Error?error.message:"Dependency inventory unavailable"); }
  }
  inventory.packages=[...rows.values()].sort((a,b)=>a.name.localeCompare(b.name,"en") || a.version.localeCompare(b.version,"en") || a.source.localeCompare(b.source,"en"));
  for(const row of inventory.packages) { row.locations.sort(); row.uses.sort((a,b)=>JSON.stringify(a).localeCompare(JSON.stringify(b),"en")); }
  inventory.gaps=[...new Map(inventory.gaps.map(gap=>[JSON.stringify(gap),gap])).values()];
  inventory.notices=[...new Map(inventory.notices.map(notice=>[JSON.stringify(notice),notice])).values()];
  inventory.status=inventory.gaps.length?"gaps":"complete";
  return {inventory,inputs:[...inputs]};
}

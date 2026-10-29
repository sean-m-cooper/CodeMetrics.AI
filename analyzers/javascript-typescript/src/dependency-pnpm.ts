import fs from "node:fs";
import path from "node:path";
import semver from "semver";
import { parseAllDocuments } from "yaml";
import { hash } from "./evidence.js";
import type { PackageInput } from "./discovery.js";
import type { DependencyInventory, DependencyPackage, DependencyUse } from "./dependency-inventory.js";
import { declarations, registrySpec, type DependencyKind } from "./dependency-spec.js";

type ObjectValue = Record<string,unknown>;
const object = (value:unknown):value is ObjectValue => !!value && typeof value==="object" && !Array.isArray(value);
const own = (value:unknown,key:string):unknown => object(value) && Object.hasOwn(value,key) ? value[key] : undefined;
const slash = (value:string) => value.replaceAll("\\","/");
const within = (root:string,file:string) => {const r=path.relative(root,file);return !path.isAbsolute(r) && r!==".." && !r.startsWith(".."+path.sep);};
const fields = {production:"dependencies",development:"devDependencies",optional:"optionalDependencies"} as const;
type Declaration = ReturnType<typeof declarations>[number];
interface Edge extends Declaration {from:string;ref?:string;resolvedRange?:string;role:DependencyKind;direct:boolean;mayBeAbsent:boolean;importer:boolean;}

function input(file:string,root:string,inputs:Set<string>):string {
  inputs.add(file);
  if(!within(root,fs.realpathSync(file))) throw new Error("pnpm input is outside repository");
  if(fs.statSync(file).size>20*1024*1024) throw new Error("pnpm input exceeds 20 MiB");
  return fs.readFileSync(file,"utf8");
}
function yamlDocuments(text:string):ObjectValue[] {
  try {
    const docs=parseAllDocuments(text,{uniqueKeys:true,strict:true});
    if(!docs.length || docs.length>2 || docs.some(doc=>doc.errors.length || doc.warnings.length)) throw new Error();
    const values:unknown[]=docs.map(doc=>doc.toJS({maxAliasCount:0}));
    if(!values.every(object)) throw new Error();
    return values as ObjectValue[];
  } catch { throw new Error("Malformed or unsupported pnpm YAML (duplicate keys, tags, aliases, or document layout)"); }
}
function projectLock(text:string):{lock:ObjectValue;document:number} {
  const docs=yamlDocuments(text);
  if(docs.some(doc=>doc.lockfileVersion!=="9.0" && doc.lockfileVersion!==9)) throw new Error("Only pnpm lockfile version 9 is supported");
  if(docs.length===2) {
    const env=docs[0],importers=env.importers;
    if(!object(importers) || Object.keys(importers).length!==1 || !object(importers["."])) throw new Error("Unsupported pnpm environment lock document");
    const importer=importers["."];
    if(!("configDependencies" in importer || "packageManagerDependencies" in importer) || Object.values(fields).some(field=>importer[field]!==undefined)) throw new Error("Ambiguous pnpm project lock documents");
  }
  const lock=docs.at(-1)!;
  if(!object(lock.importers)) throw new Error("pnpm project lockfile has no importers map");
  if(Object.values(lock.importers).some(importer=>object(importer) && ("configDependencies" in importer || "packageManagerDependencies" in importer))) throw new Error("pnpm environment document has no following project document");
  if(lock.packages!==undefined && !object(lock.packages) || lock.snapshots!==undefined && !object(lock.snapshots)) throw new Error("Malformed pnpm package or snapshot map");
  if(Object.keys(lock.importers).some(key=>key!=="." && (!key || key.includes("\\") || key.includes(":") || path.posix.isAbsolute(key) || key.split("/").includes("..")))) throw new Error("Unsafe pnpm importer location");
  return {lock,document:docs.length};
}
function catalogRange(declaration:Declaration,config:ObjectValue,lock:ObjectValue):string|undefined {
  if(!declaration.range.startsWith("catalog:")) return;
  const catalog=declaration.range.slice(8)||"default";
  const configured=catalog==="default" ? config.catalog : own(config.catalogs,catalog);
  const range=own(configured,declaration.requested),record=own(own(lock.catalogs,catalog),declaration.requested);
  const overridden=record===undefined && typeof range==="string" && own(lock.overrides,declaration.requested)===range && [range,declaration.range].includes(own(config.overrides,declaration.requested) as string);
  if(typeof range!=="string" || !overridden && (!object(record) || record.specifier!==range)) throw new Error(`Catalog unavailable or stale for ${declaration.requested}`);
  return range;
}
function importerRecord(importer:ObjectValue,edge:Declaration):ObjectValue|undefined {
  const candidates=edge.kind==="peer" ? Object.values(fields).map(field=>own(importer[field],edge.requested)) : [own(importer[fields[edge.kind]],edge.requested)];
  return candidates.find(object);
}
function overrideRange(name:string,config:ObjectValue,rootManifest:ObjectValue,lock:ObjectValue):string|undefined {
  const current=own(config.overrides,name)??own(own(rootManifest.pnpm,"overrides"),name);
  if(typeof current!=="string") return;
  const resolved=catalogRange({requested:name,range:current,kind:"production",optional:false},config,lock)??current;
  return own(lock.overrides,name)===resolved ? resolved : undefined;
}
function importerEdges(location:string,manifest:ObjectValue,lock:ObjectValue,config:ObjectValue,rootManifest:ObjectValue,includeDevelopment:boolean):Edge[] {
  const importer=own(lock.importers,location);
  if(!object(importer)) throw new Error(`Selected/workspace manifest has no pnpm importer: ${location}`);
  const all=declarations(manifest);
  for(const [kind,field] of Object.entries(fields)) {
    if(importer[field]!==undefined && !object(importer[field])) throw new Error("Malformed pnpm importer dependencies");
    for(const [name,record] of Object.entries(object(importer[field])?importer[field]:{})) {
      if(!object(record) || typeof record.specifier!=="string" || typeof record.version!=="string") throw new Error("Malformed pnpm importer resolution");
      if(!all.some(edge=>edge.requested===name && (edge.kind===kind || edge.kind==="peer"))) throw new Error(`Manifest and pnpm importer declarations differ: ${location}`);
    }
  }
  const edges:Edge[]=[];
  for(const edge of all) {
    const record=importerRecord(importer,edge),resolvedRange=catalogRange(edge,config,lock);
    if(edge.kind!=="peer" && (!record || record.specifier!==edge.range && record.specifier!==overrideRange(edge.requested,config,rootManifest,lock))) throw new Error(`Manifest and pnpm importer declarations differ: ${location}`);
    if(!includeDevelopment && edge.kind==="development") continue;
    edges.push({...edge,from:location,ref:typeof record?.version==="string"?record.version:undefined,...(resolvedRange?{resolvedRange}:{}),role:edge.kind,direct:true,mayBeAbsent:edge.optional,importer:true});
  }
  return edges;
}
function identity(requested:string,ref:string):{key:string;base:string;name:string;version:string}|undefined {
  // Snapshot suffixes encode peer contexts (and patches); preserve the complete key as a location.
  const key=semver.valid(ref.split("(")[0])?`${requested}@${ref}`:ref;
  const base=key.split("(")[0],match=/^((?:@[^/\s]+\/)?[^@/:\s]+)@([^()]+)$/.exec(base);
  if(!match || !semver.valid(match[2]) || key.includes("\\") || key.includes("://")) return;
  return {key,base,name:match[1],version:match[2]};
}
function sourceOf(record:ObjectValue):DependencyPackage["source"] {
  const tarball=own(record.resolution,"tarball");
  if(typeof tarball!=="string") return "otherSource";
  try {const url=new URL(tarball);return url.protocol==="https:" && url.hostname==="registry.npmjs.org" && !url.username && !url.password ? "publicRegistry":"otherSource";}
  catch {return "otherSource";}
}
function children(from:string,snapshot:ObjectValue,record:ObjectValue,parent:Edge):Edge[] {
  const peers=declarations({peerDependencies:record.peerDependencies,peerDependenciesMeta:record.peerDependenciesMeta});
  const rows:Edge[]=[];
  for(const field of ["dependencies","optionalDependencies"] as const) {
    const dependencies=snapshot[field];
    if(dependencies===undefined) continue;
    if(!object(dependencies)) throw new Error("Malformed pnpm snapshot dependencies");
    for(const [requested,ref] of Object.entries(dependencies)) {
      if(typeof ref!=="string") throw new Error("Malformed pnpm snapshot reference");
      if(field==="dependencies" && own(snapshot.optionalDependencies,requested)!==undefined) continue;
      const peer=peers.find(edge=>edge.requested===requested),optional=field==="optionalDependencies" || peer?.optional===true;
      rows.push({from,requested,range:peer?.range??ref,ref,kind:peer?"peer":optional?"optional":"production",role:parent.role,direct:false,optional:parent.optional||optional,mayBeAbsent:optional,importer:false});
    }
  }
  for(const peer of peers) if(!rows.some(edge=>edge.requested===peer.requested)) rows.push({...peer,from,role:parent.role,direct:false,optional:parent.optional||peer.optional,mayBeAbsent:peer.optional,importer:false});
  return rows;
}

/** pnpm v9 adapter, including pnpm 12 environment + project YAML documents. No target code executes. */
export function pnpmInventory(pkg:Pick<PackageInput,"entryPoint"|"root"|"name">,root:string,file:string,inventory:DependencyInventory,inputs:Set<string>,rows:Map<string,DependencyPackage>):void {
  const relative=(target:string)=>slash(path.relative(root,target)),manifest=relative(pkg.entryPoint),directory=path.dirname(file);
  const text=input(file,root,inputs),{lock,document}=projectLock(text);
  if(!inventory.lockfiles.some(row=>row.file===relative(file))) inventory.lockfiles.push({file:relative(file),version:9,sha256:hash(text),projectDocument:document,...(document===2?{environmentDocument:"excluded" as const}:{})});
  const configFile=path.join(directory,"pnpm-workspace.yaml");let config:ObjectValue={};
  if(fs.existsSync(configFile)) {
    const text=input(configFile,root,inputs),docs=yamlDocuments(text);
    if(docs.length!==1) throw new Error("pnpm workspace configuration must be one YAML document");
    config=docs[0];inventory.configurationInputs??=[];
    if(!inventory.configurationInputs.some(row=>row.file===relative(configFile))) inventory.configurationInputs.push({file:relative(configFile),sha256:hash(text)});
  }
  const rootFile=path.join(directory,"package.json"),rootManifest=fs.existsSync(rootFile)?JSON.parse(input(rootFile,root,inputs)) as ObjectValue:{};
  const selectedManifest=JSON.parse(input(pkg.entryPoint,root,inputs)) as ObjectValue;
  const manifestRow=inventory.manifests.find(row=>row.file===manifest)!;
  manifestRow.overrides={manifest:own(rootManifest.pnpm,"overrides")??{},workspace:config.overrides??{},lockfile:lock.overrides??{}};
  const start=slash(path.relative(directory,pkg.root))||".";
  const queue=importerEdges(start,selectedManifest,lock,config,rootManifest,true),visited=new Set<string>();
  const gap=(reason:string,edge:Edge)=>inventory.gaps.push({manifest,reason,dependency:edge.requested,from:edge.from,range:edge.range});
  for(let index=0;index<queue.length;index++) {
    const edge=queue[index];
    if(index>=100000) {gap("Dependency traversal limit exceeded",edge);break;}
    if(!edge.ref) {
      if(edge.mayBeAbsent) inventory.notices.push({manifest,from:edge.from,dependency:edge.requested,range:edge.range,reason:"Optional dependency/peer is absent from the lockfile; no missing-package deduction"});
      else gap("Unresolved pnpm dependency/peer in lockfile",edge);
      continue;
    }
    try {
      let name:string,version:string,source:DependencyPackage["source"],location:string,next:()=>Edge[];
      if(edge.ref.startsWith("link:")) {
        if(!edge.importer || edge.ref.includes("\\") || path.posix.isAbsolute(edge.ref.slice(5)) || edge.ref.slice(5).includes(":")) throw new Error("Unsupported pnpm link reference");
        const target=path.resolve(directory,edge.from,edge.ref.slice(5),"package.json");
        if(!within(root,target) || !fs.existsSync(target)) throw new Error("pnpm workspace manifest unavailable or outside repository");
        const data:unknown=JSON.parse(input(target,root,inputs));
        if(!object(data) || typeof data.name!=="string" || typeof data.version!=="string" || !semver.valid(data.version)) throw new Error("Missing pnpm workspace name/version");
        name=data.name;version=data.version;source="workspace";
        location=slash(path.relative(directory,path.dirname(target)))||".";
        const linked=importerEdges(location,data,lock,config,rootManifest,false);
        next=()=>linked.map(child=>({...child,direct:false,role:edge.role,optional:edge.optional||child.optional}));
      } else {
        const id=identity(edge.requested,edge.ref);
        if(!id) throw new Error("Unsupported pnpm package reference (file/git/custom source or non-semver identity)");
        const record=own(lock.packages,id.base),snapshot=own(lock.snapshots,id.key);
        if(!object(record) || !object(snapshot)) throw new Error("Missing pnpm package record or exact peer-context snapshot");
        name=id.name;version=id.version;source=sourceOf(record);location=id.key;
        next=()=>children(location,snapshot,record,edge);
      }
      const spec=registrySpec(edge.requested,edge.resolvedRange??edge.range);
      if(spec && (spec.name!==name || semver.validRange(spec.range) && !semver.satisfies(version,spec.range))) inventory.notices.push({manifest,from:edge.from,dependency:edge.requested,range:edge.range,version,reason:"Locked identity/version differs from requested range; alias/override or peer policy may explain this. Installation validity is not assessed"});
      const id=hash(`${name}|${version}|${source}`),row=rows.get(id)??{id,name,version,source,locations:[],uses:[]};rows.set(id,row);
      const install=`${relative(file)}#${source==="workspace"?"importers":"snapshots"}/${location}`;
      if(!row.locations.includes(install)) row.locations.push(install);
      const use:DependencyUse={project:pkg.name,manifest,from:edge.from,requested:edge.requested,range:edge.range,...(edge.resolvedRange?{resolvedRange:edge.resolvedRange}:{}),kind:edge.role,direct:edge.direct,optional:edge.optional};
      if(!row.uses.some(existing=>JSON.stringify(existing)===JSON.stringify(use))) row.uses.push(use);
      const key=`${install}|${edge.role}|${edge.optional}`;if(visited.has(key)) continue;visited.add(key);
      queue.push(...next());
    } catch(error) {gap(error instanceof Error?error.message:"pnpm dependency inventory unavailable",edge);}
  }
}

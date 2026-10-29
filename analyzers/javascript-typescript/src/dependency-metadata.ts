import semver from "semver";
import { hash } from "./evidence.js";
import { registrySpec, type DependencyInventory, type DependencyPackage } from "./dependency-inventory.js";

type Json=Record<string,unknown>;
const object=(value:unknown):value is Json=>!!value && typeof value==="object" && !Array.isArray(value);
export interface MetadataResult { status:"available"|"unavailable"; data?:unknown; sha256?:string; reason?:string; }
export interface RegistryReader {
  package(name:string):Promise<MetadataResult>;
  advisories(packages:Record<string,string[]>):Promise<MetadataResult>;
}
export interface PackageObservation {
  packageId:string; name:string; version:string;
  metadata: {status:"available"|"unavailable";reason?:string;sha256?:string};
  deprecated?:string|null; latest?:string|null;
  upgrades?:{manifest:string;range:string;wanted:string|null;newerWithinRange:boolean;latestOutsideRange:boolean|null;reason?:string}[];
  nodeEngine?:string|null;
  vulnerabilities:{status:"available"|"unavailable";reason?:string;advisories?:{id:string;title:string;url:string;severity:string;range:string}[]};
}
export interface DependencyMetadata {
  version:1;status:"notRequested"|"available"|"partial"|"unavailable";
  source:"https://registry.npmjs.org"; checkedAtUtc?:string;
  scope:"public-registry-lockfile-packages"; observations:PackageObservation[];
  availablePackages:number; unavailablePackages:number; advisoryResponseSha256?:string;
}
export const noMetadata=():DependencyMetadata=>({version:1,status:"notRequested",source:"https://registry.npmjs.org",scope:"public-registry-lockfile-packages",observations:[],availablePackages:0,unavailablePackages:0});
const unavailable=(reason:string):MetadataResult=>({status:"unavailable",reason});

/** Explicit opt-in, bounded public-registry reads. No credentials, redirects, installs or target scripts. */
export function publicRegistryReader(options:{timeoutMs?:number;maxBytes?:number;fetch?:typeof fetch}={}):RegistryReader {
  const request=async(route:string,body?:unknown):Promise<MetadataResult>=>{
    try {
      const response=await (options.fetch??fetch)(`https://registry.npmjs.org/${route}`,{
        method:body?"POST":"GET",redirect:"error",signal:AbortSignal.timeout(options.timeoutMs??10000),
        headers:{accept:body?"application/json":"application/vnd.npm.install-v1+json",...(body?{"content-type":"application/json"}:{})},
        ...(body?{body:JSON.stringify(body)}:{})});
      if(!response.ok) {await response.body?.cancel();return unavailable(`Registry HTTP ${response.status}`);}
      const limit=options.maxBytes??5*1024*1024;
      if(Number(response.headers.get("content-length"))>limit) {await response.body?.cancel();return unavailable("Registry response exceeds size limit");}
      if(!response.body) return unavailable("Empty registry response");
      const reader=response.body.getReader(),chunks:Uint8Array[]=[];let size=0;
      for(;;) {const {done,value}=await reader.read();if(done) break;size+=value.length;if(size>limit) {await reader.cancel();return unavailable("Registry response exceeds size limit");}chunks.push(value);}
      const text=Buffer.concat(chunks).toString("utf8"),data:unknown=JSON.parse(text);
      return {status:"available",data,sha256:hash(text)};
    } catch { return unavailable("Registry request failed, timed out, or returned invalid JSON"); }
  };
  return {package:name=>request(encodeURIComponent(name)),advisories:packages=>request("-/npm/v1/security/advisories/bulk",packages)};
}
function packageDetails(pkg:DependencyPackage,result:MetadataResult):Pick<PackageObservation,"metadata"|"deprecated"|"latest"|"upgrades"|"nodeEngine"> {
  const data=result.data;
  if(result.status!=="available" || !object(data) || data.name!==pkg.name || !object(data.versions) || !object(data.versions[pkg.version]))
    return {metadata:{status:"unavailable",reason:result.reason??"Installed package/version metadata is missing or malformed"}};
  const installed=data.versions[pkg.version] as Json;
  if(installed.version!==pkg.version || (installed.deprecated!==undefined && typeof installed.deprecated!=="string") ||
    installed.engines!==undefined && (!object(installed.engines) || installed.engines.node!==undefined && typeof installed.engines.node!=="string"))
    return {metadata:{status:"unavailable",reason:"Installed-version metadata is malformed"}};
  const latest=object(data["dist-tags"]) && typeof data["dist-tags"].latest==="string" && semver.valid(data["dist-tags"].latest) && object(data.versions[data["dist-tags"].latest]) ? data["dist-tags"].latest : null;
  const versions=Object.keys(data.versions).filter(version=>semver.valid(version));
  const upgrades=pkg.uses.filter(use=>use.direct).map(use=>{
    const spec=registrySpec(use.requested,use.resolvedRange??use.range),range=spec?.name===pkg.name?spec.range:undefined;
    const wanted=range && semver.validRange(range)?semver.maxSatisfying(versions,range):null;
    return {manifest:use.manifest,range:use.range,wanted,newerWithinRange:!!wanted && semver.gt(wanted,pkg.version),
      latestOutsideRange:latest && range && semver.validRange(range)?!semver.satisfies(latest,range):null,
      ...(!range?{reason:"Original range is unsupported or names a different package replaced by an alias/override"}:{})};
  });
  return {metadata:{status:"available",sha256:result.sha256},deprecated:typeof installed.deprecated==="string"?installed.deprecated:null,latest,upgrades,
    nodeEngine:object(installed.engines) && typeof installed.engines.node==="string"?installed.engines.node:null};
}
function advisoryDetails(pkg:DependencyPackage,result:MetadataResult):PackageObservation["vulnerabilities"] {
  if(result.status!=="available" || !object(result.data) || "error" in result.data)
    return {status:"unavailable",reason:result.reason??"Malformed advisory response"};
  const entries=Object.hasOwn(result.data,pkg.name)?result.data[pkg.name]:[];
  if(!Array.isArray(entries)) return {status:"unavailable",reason:"Malformed package advisory list"};
  const advisories=new Map<string,{id:string;title:string;url:string;severity:string;range:string}>();
  for(const row of entries) {
    if(!object(row) || row.name!==pkg.name || !["string","number"].includes(typeof row.id) || typeof row.title!=="string" ||
      typeof row.url!=="string" || typeof row.vulnerable_versions!=="string" || !semver.validRange(row.vulnerable_versions) ||
      typeof row.severity!=="string" || !["low","moderate","high","critical","info"].includes(row.severity))
      return {status:"unavailable",reason:"Malformed advisory fields"};
    if(!/^https:\/\//.test(row.url)) return {status:"unavailable",reason:"Invalid advisory URL"};
    if(semver.satisfies(pkg.version,row.vulnerable_versions,{includePrerelease:true})) {
      const id=String(row.id);advisories.set(id,{id,title:row.title,url:row.url,severity:row.severity,range:row.vulnerable_versions});
    }
  }
  return {status:"available",advisories:[...advisories.values()].sort((a,b)=>a.id.localeCompare(b.id,"en"))};
}

export async function dependencyMetadata(inventory:DependencyInventory,reader:RegistryReader=publicRegistryReader(),options:{maxPackages?:number;deadlineMs?:number}={}):Promise<DependencyMetadata> {
  const maximum=options.maxPackages??100,deadline=Date.now()+(options.deadlineMs??60000);
  if(!Number.isSafeInteger(maximum) || maximum<1 || maximum>1000) throw new Error("Metadata package limit must be an integer from 1 to 1000");
  const direct=new Set(inventory.packages.filter(pkg=>pkg.uses.some(use=>use.direct)).map(pkg=>pkg.name));
  const names=[...new Set(inventory.packages.filter(pkg=>pkg.source==="publicRegistry").map(pkg=>pkg.name))]
    .sort((a,b)=>Number(direct.has(b))-Number(direct.has(a)) || a.localeCompare(b,"en")).slice(0,maximum);
  const payload:Record<string,string[]>=Object.create(null);
  for(const name of names) payload[name]=[...new Set(inventory.packages.filter(pkg=>pkg.source==="publicRegistry" && pkg.name===name).map(pkg=>pkg.version))];
  const results=new Map<string,MetadataResult>();let cursor=0;
  const safe=async(work:()=>Promise<MetadataResult>)=>{try{return await work();}catch{return unavailable("Registry reader failed");}};
  await Promise.all(Array.from({length:Math.min(4,names.length)},async()=>{
    while(cursor<names.length) {const name=names[cursor++];results.set(name,Date.now()>=deadline?unavailable("Metadata time budget exhausted"):await safe(()=>reader.package(name)));}
  }));
  const audit=names.length && Date.now()<deadline?await safe(()=>reader.advisories(payload)):unavailable("No eligible packages or metadata time budget exhausted");
  const observations:PackageObservation[]=inventory.packages.map(pkg=>{
    const eligible=pkg.source==="publicRegistry";
    const metadata=eligible?(results.get(pkg.name)??unavailable("Metadata package limit exceeded")):unavailable("Workspace, private/other source, or registry provenance unavailable");
    return {packageId:pkg.id,name:pkg.name,version:pkg.version,...packageDetails(pkg,metadata),
      vulnerabilities:eligible && Object.hasOwn(payload,pkg.name)?advisoryDetails(pkg,audit):{status:"unavailable",reason:metadata.reason}};
  });
  const available=observations.filter(row=>row.metadata.status==="available" && row.vulnerabilities.status==="available").length;
  const anyAvailable=observations.some(row=>row.metadata.status==="available" || row.vulnerabilities.status==="available");
  return {version:1,status:observations.length && available===observations.length?"available":anyAvailable?"partial":"unavailable",source:"https://registry.npmjs.org",scope:"public-registry-lockfile-packages",
    checkedAtUtc:new Date().toISOString(),observations,availablePackages:available,unavailablePackages:observations.length-available,...(audit.sha256?{advisoryResponseSha256:audit.sha256}:{})};
}

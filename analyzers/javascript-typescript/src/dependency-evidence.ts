import { hash, type Dimension, type Evidence, type Finding } from "./evidence.js";
import type { DependencyInventory } from "./dependency-inventory.js";
import { dependencyMetadata, noMetadata, type DependencyMetadata, type RegistryReader } from "./dependency-metadata.js";

export function dependencyEvidence(inventory:DependencyInventory,metadata:DependencyMetadata=noMetadata(),metadataMode:"offline"|"public-registry"=metadata.status==="notRequested"?"offline":"public-registry"):Dimension {
  const findings:Finding[]=[];
  const add=(id:string,name:string,category:string,message:string,observations:Record<string,unknown>)=>findings.push({
    category,ruleId:`javascript-typescript/dependencyManagement/${category}`,fingerprint:hash(`${id}|${category}`),
    severity:"info",confidence:"high",package:name,message,observations:{...observations,scoreDisposition:"excludedUnscored"}});
  for(const row of metadata.observations) {
    if(row.deprecated) add(row.packageId,row.name,"deprecatedPackage",`${row.name} ${row.version} is deprecated: ${row.deprecated}`,{packageId:row.packageId,version:row.version});
    if(row.upgrades?.some(upgrade=>upgrade.newerWithinRange)) add(row.packageId,row.name,"updateWithinDeclaredRange",`${row.name} has an update within a declared version range; compatibility and intent require review.`,{packageId:row.packageId,upgrades:row.upgrades});
    for(const advisory of row.vulnerabilities.advisories??[]) add(`${row.packageId}|${advisory.id}`,row.name,"reportedVulnerability",`${row.name} ${row.version}: ${advisory.title}`,{packageId:row.packageId,advisory,reachability:"notAssessed"});
  }
  return {status:"skipped",basis:"Dependency inventory and registry observations only; numerical scoring is not calibrated. Missing evidence is not a code defect.",
    scope:{id:"javascript-typescript/dependencyManagement/lock-observations-v1",coverage:"partial",
      includes:["npm-lockfile-v2-v3","pnpm-lockfile-v9","selected-workspace-reachability","public-registry-metadata-if-requested"],
      excludes:["dependency-scoring","runtime-reachability","upgrade-safety","private-registry-authentication","yarn-bun-lockfiles","pnpm-file-git-custom-resolutions","pnpm-environment-tooling-document","implicit-registry-provenance"]},
    findings,dependencyInventory:inventory,dependencyMetadata:metadata,metadataMode};
}
export async function enrichDependencies(evidence:Evidence,reader?:RegistryReader):Promise<void> {
  if(evidence.dimensions.dependencyManagement.metadataMode!=="public-registry") throw new Error("Analyze with dependencyMetadata: true before requesting registry observations");
  const inventory=evidence.dimensions.dependencyManagement.dependencyInventory as DependencyInventory;
  if(!inventory) throw new Error("Dependency inventory is required before metadata acquisition");
  evidence.dimensions.dependencyManagement=dependencyEvidence(inventory,await dependencyMetadata(inventory,reader));
}

import fs from "node:fs";
import path from "node:path";
import { afterEach, describe, expect, it } from "vitest";
import { dependencyInventory } from "../src/dependency-inventory.js";
import { dependencyMetadata, publicRegistryReader, type RegistryReader } from "../src/dependency-metadata.js";
import { dependencyEvidence, enrichDependencies } from "../src/dependency-evidence.js";
import { analyze } from "../src/analyzer.js";
import { compare,validateEvidence } from "../dist/evidence-tools.js";

const roots:string[]=[];
function fixture(manifest:object,packages:object,extra:Record<string,object|string>={}) {
  const base=path.resolve("../../TestResults");fs.mkdirSync(base,{recursive:true});
  const root=fs.mkdtempSync(path.join(base,"dependency-tests-"));roots.push(root);fs.mkdirSync(path.join(root,".git"));
  for(const [file,data] of Object.entries({"package.json":{name:"example",...manifest},"package-lock.json":{lockfileVersion:3,packages:{"":manifest,...packages}},"index.js":"export function value(){return 1;}",...extra})) {
    const target=path.join(root,file);fs.mkdirSync(path.dirname(target),{recursive:true});fs.writeFileSync(target,typeof data==="string"?data:JSON.stringify(data));
  }
  return {root,read:(selected=["."])=>dependencyInventory(selected.map(dir=>({root:path.join(root,dir),entryPoint:path.join(root,dir,"package.json"),name:dir})),root)};
}
const locked=(name:string,version="1.0.0",more:object={})=>({version,resolved:`https://registry.npmjs.org/${name}/-/${name.split("/").pop()}-${version}.tgz`,...more});
const available=(data:unknown)=>({status:"available" as const,data,sha256:"fixture-response"});
afterEach(()=>{for(const root of roots.splice(0))fs.rmSync(root,{recursive:true,force:true});});

describe("npm dependency inventory",()=>{
  it("counts a package/version once while preserving duplicate installs and production/development paths",()=>{
    const f=fixture({dependencies:{a:"^1"},devDependencies:{b:"^1"}},{
      "node_modules/a":locked("a","1.0.0",{dependencies:{shared:"^1"}}),
      "node_modules/b":locked("b","1.0.0",{dependencies:{shared:"^1"}}),
      "node_modules/shared":locked("shared"),"node_modules/b/node_modules/shared":locked("shared")});
    const {inventory,inputs}=f.read();expect(inventory.status).toBe("complete");expect(inventory.packages).toHaveLength(3);
    const shared=inventory.packages.find(p=>p.name==="shared")!;
    expect(shared.locations).toHaveLength(2);expect(shared.uses.map(use=>[use.kind,use.direct]).sort()).toEqual([["development",false],["production",false]]);
    expect(inputs).toContain(path.join(f.root,"package-lock.json"));
  });
  it("resolves workspace links using the enclosing lockfile and excludes unrelated packages",()=>{
    const f=fixture({workspaces:["packages/*"]},{
      "packages/a":{name:"a",version:"1.0.0",dependencies:{b:"*"}},
      "packages/b":{name:"b",version:"1.0.0",dependencies:{leaf:"^1"}},
      "node_modules/b":{link:true,resolved:"packages/b"},"node_modules/leaf":locked("leaf"),"node_modules/unrelated":locked("unrelated")},
      {"packages/a/package.json":{name:"a",version:"1.0.0",dependencies:{b:"*"}},"packages/b/package.json":{name:"b",version:"1.0.0",dependencies:{leaf:"^1"}}});
    const {inventory}=f.read(["packages/a"]);expect(inventory.status).toBe("complete");
    expect(inventory.packages.map(p=>[p.name,p.source])).toEqual([["b","workspace"],["leaf","publicRegistry"]]);
    expect(inventory.packages[1].uses[0].manifest).toBe("packages/a/package.json");
  });
  it("retains aliases, optional dependencies, peers, and prereleases",()=>{
    const f=fixture({dependencies:{alias:"npm:@scope/lib@1.0.0-beta.1",opt:"^1"},optionalDependencies:{opt:"^2"},peerDependencies:{peer:"^1"}},{
      "node_modules/alias":locked("@scope/lib","1.0.0-beta.1",{name:"@scope/lib"}),"node_modules/opt":locked("opt","2.0.0"),"node_modules/peer":locked("peer")});
    const {inventory}=f.read();expect(inventory.gaps).toEqual([]);
    expect(inventory.packages.find(p=>p.name==="@scope/lib")!.uses[0].requested).toBe("alias");
    expect(inventory.packages.find(p=>p.name==="opt")!.uses.map(u=>u.kind)).toEqual(["optional"]);
    expect(inventory.packages.find(p=>p.name==="peer")!.uses[0].kind).toBe("peer");
  });
  it("bounds cyclic dependency traversal",()=>{
    const f=fixture({dependencies:{a:"*"}},{"node_modules/a":locked("a","1.0.0",{dependencies:{b:"*"}}),"node_modules/b":locked("b","1.0.0",{dependencies:{a:"*"}})});
    expect(f.read().inventory.packages).toHaveLength(2);
  });
  it.each([1,4])("withholds unsupported npm lockfile version %i",version=>{
    const f=fixture({dependencies:{a:"*"}},{},{"package-lock.json":{lockfileVersion:version,packages:{}}});
    expect(f.read().inventory.status).toBe("gaps");expect(f.read().inventory.packages).toEqual([]);
  });
  it("preserves manifest declarations when pnpm, Yarn, or a missing lock prevents resolution",()=>{
    for(const manager of ["pnpm@10.0.0","yarn@4.0.0",undefined]) {
      const f=fixture({dependencies:{a:"^1"},...(manager?{packageManager:manager}:{})},{});
      fs.rmSync(path.join(f.root,"package-lock.json"));const {inventory}=f.read();
      expect(inventory.status).toBe("gaps");expect(inventory.manifests[0].declarations).toHaveLength(1);
    }
  });
  it("withholds stale, malformed, and unsafe lockfile evidence",()=>{
    for(const replacement of [{lockfileVersion:3,packages:{"":{dependencies:{a:"^2"}}}},'{broken',
      {lockfileVersion:3,packages:{"":{dependencies:{a:"^1"}},"../outside":{version:"1.0.0"}}}]) {
      const f=fixture({dependencies:{a:"^1"}},{},{"package-lock.json":replacement});
      expect(f.read().inventory.status).toBe("gaps");expect(f.read().inventory.packages).toEqual([]);
    }
  });
  it("does not treat unresolved, incompatible, or versionless packages as current",()=>{
    const f=fixture({dependencies:{missing:"^1",wrong:"^2",invalid:"*"}},{"node_modules/wrong":locked("wrong"),"node_modules/invalid":{}});
    expect(f.read().inventory.gaps).toHaveLength(2);expect(f.read().inventory.packages.map(p=>p.name)).toEqual(["wrong"]);
    expect(f.read().inventory.notices[0]).toMatchObject({dependency:"wrong",range:"^2",version:"1.0.0"});
  });
  it("honors absent optional peers and retains lock versions selected by override policy",()=>{
    const f=fixture({dependencies:{a:"^1"},overrides:{leaf:"2.0.0"}},{"node_modules/a":locked("a","1.0.0",{
      dependencies:{leaf:"^1"},peerDependencies:{optional:"*"},peerDependenciesMeta:{optional:{optional:true}}}),"node_modules/leaf":locked("leaf","2.0.0")});
    const {inventory}=f.read();expect(inventory.status).toBe("complete");expect(inventory.notices).toHaveLength(2);
    expect(inventory.manifests[0].overrides).toEqual({leaf:"2.0.0"});expect(inventory.packages.find(p=>p.name==="leaf")!.version).toBe("2.0.0");
  });
  it("keeps private sources local and does not expose registry credentials",()=>{
    const f=fixture({dependencies:{private:"1.0.0"}},{"node_modules/private":{version:"1.0.0",resolved:"https://secret:token@private.example/pkg.tgz"}});
    expect(f.read().inventory.packages[0].source).toBe("otherSource");expect(JSON.stringify(f.read().inventory)).not.toContain("token");
  });
  it("does not excuse a missing required child merely because its installed parent is optional",()=>{
    const f=fixture({optionalDependencies:{a:"*"}},{"node_modules/a":locked("a","1.0.0",{dependencies:{missing:"*"}})});
    expect(f.read().inventory.status).toBe("gaps");expect(f.read().inventory.gaps[0].dependency).toBe("missing");
  });
  it("retains replacement identities declared through npm overrides",()=>{
    const f=fixture({dependencies:{original:"^3"},overrides:{original:"npm:replacement@1.0.0"}},{"node_modules/original":locked("replacement","1.0.0",{name:"replacement"})});
    const {inventory}=f.read();expect(inventory.status).toBe("complete");expect(inventory.packages[0].name).toBe("replacement");
    expect(inventory.packages[0].uses[0].requested).toBe("original");expect(inventory.notices.some(n=>n.reason.includes("identity"))).toBe(true);
  });
  it("gives shrinkwrap precedence over package-lock",()=>{
    const f=fixture({dependencies:{a:"*"}},{"node_modules/a":locked("a")},{"npm-shrinkwrap.json":{lockfileVersion:2,packages:{"":{dependencies:{a:"*"}},"node_modules/a":locked("a","2.0.0")}}});
    expect(f.read().inventory.packages[0].version).toBe("2.0.0");
  });
});

describe("dependency feed observations",()=>{
  const feed=(name:string)=>available({name,"dist-tags":{latest:"2.0.0"},versions:{"1.0.0":{version:"1.0.0",deprecated:"Use the supported line",engines:{node:">=20"}},"1.1.0":{version:"1.1.0"},"2.0.0":{version:"2.0.0"}}});
  it("separates compatible-range candidates from major releases and respects intentional exact pins",async()=>{
    const f=fixture({dependencies:{a:"^1",b:"1.0.0"}},{"node_modules/a":locked("a"),"node_modules/b":locked("b")});
    const inventory=f.read().inventory,metadata=await dependencyMetadata(inventory,{package:async name=>feed(name),advisories:async()=>available({})});
    expect(metadata.status).toBe("available");expect(metadata.observations[0].upgrades![0]).toMatchObject({wanted:"1.1.0",newerWithinRange:true,latestOutsideRange:true});
    expect(metadata.observations[1].upgrades![0]).toMatchObject({wanted:"1.0.0",newerWithinRange:false,latestOutsideRange:true});
    const dimension=dependencyEvidence(inventory,metadata);expect(dimension.score).toBeUndefined();expect(dimension.findings.every(f=>f.severity==="info")).toBe(true);
    expect(dimension.findings.filter(f=>f.category==="updateWithinDeclaredRange").map(f=>f.package)).toEqual(["a"]);
  });
  it("matches advisories to exact installed versions and deduplicates repeated reports",async()=>{
    const f=fixture({dependencies:{a:"1.0.0"}},{"node_modules/a":locked("a")});
    const row={id:42,name:"a",title:"Example advisory",url:"https://example.com/advisory",severity:"high",vulnerable_versions:"<1.1.0"};
    const metadata=await dependencyMetadata(f.read().inventory,{package:async()=>({status:"unavailable",reason:"HTTP 404"}),advisories:async()=>available({a:[row,row,{...row,id:43,vulnerable_versions:">=2"}]})});
    expect(metadata.status).toBe("partial");expect(metadata.observations[0].vulnerabilities.advisories).toHaveLength(1);
    expect(dependencyEvidence(f.read().inventory,metadata).findings).toHaveLength(1);
  });
  it("respects explicit prerelease ranges and preserves unknown latest tags",async()=>{
    const f=fixture({dependencies:{a:"^1.0.0-beta.1"}},{"node_modules/a":locked("a","1.0.0-beta.1")});
    const metadata=await dependencyMetadata(f.read().inventory,{package:async()=>available({name:"a",versions:{"1.0.0-beta.1":{version:"1.0.0-beta.1"},"1.0.0-beta.2":{version:"1.0.0-beta.2"},"2.0.0":{version:"2.0.0"}}}),advisories:async()=>available({})});
    expect(metadata.observations[0].latest).toBeNull();expect(metadata.observations[0].upgrades![0]).toMatchObject({wanted:"1.0.0-beta.2",latestOutsideRange:null});
  });
  it("preserves independent metadata when advisory assessment fails",async()=>{
    const f=fixture({dependencies:{a:"^1"}},{"node_modules/a":locked("a")});
    const metadata=await dependencyMetadata(f.read().inventory,{package:async name=>feed(name),advisories:async()=>({status:"unavailable",reason:"timeout"})});
    expect(metadata.status).toBe("partial");expect(metadata.observations[0].deprecated).toBeTruthy();
    expect(metadata.observations[0].vulnerabilities).toEqual({status:"unavailable",reason:"timeout"});
  });
  it("never contacts private registries or sends their package names to audit",async()=>{
    const f=fixture({dependencies:{private:"*",a:"*"}},{"node_modules/private":{version:"1.0.0",resolved:"https://private.example/package.tgz"},"node_modules/a":locked("a")});
    const names:string[]=[],payloads:unknown[]=[];
    const metadata=await dependencyMetadata(f.read().inventory,{package:async name=>{names.push(name);return feed(name);},advisories:async payload=>{payloads.push(payload);return available({});}});
    expect(names).toEqual(["a"]);expect(payloads).toEqual([{a:["1.0.0"]}]);expect(metadata.status).toBe("partial");
  });
  it("reports malformed feeds, request errors, and bounded-query omissions as unavailable",async()=>{
    const f=fixture({dependencies:{a:"*",b:"*"}},{"node_modules/a":locked("a"),"node_modules/b":locked("b")});
    const bad:RegistryReader={package:async()=>{throw new Error("failure");},advisories:async()=>available({error:"failed"})};
    const metadata=await dependencyMetadata(f.read().inventory,bad,{maxPackages:1});
    expect(metadata.observations.every(o=>o.metadata.status==="unavailable" && o.vulnerabilities.status==="unavailable")).toBe(true);
    expect(metadata.observations[1].metadata.reason).toContain("limit");
    const malformed=await dependencyMetadata(f.read().inventory,{package:async()=>available({versions:[]}),advisories:async()=>available({a:[{id:42}],b:[]})});
    expect(malformed.observations[0].vulnerabilities.status).toBe("unavailable");
    expect(malformed.observations[1].vulnerabilities.status).toBe("available");
  });
  it("enforces response size and HTTP limits without disabling TLS or following redirects",async()=>{
    const calls:RequestInit[]=[];
    const reader=publicRegistryReader({maxBytes:8,fetch:(async(_url,options)=>{calls.push(options!);return new Response('{"large":"response"}');}) as typeof fetch});
    expect((await reader.package("a")).status).toBe("unavailable");expect(calls[0].redirect).toBe("error");
    const denied=publicRegistryReader({fetch:(async()=>new Response("denied",{status:403})) as typeof fetch});
    expect((await denied.package("a")).reason).toContain("403");
  });
  it("integrates local evidence without changing source scores and makes network modes incompatible",async()=>{
    const f=fixture({dependencies:{a:"*"}},{"node_modules/a":locked("a")});
    const local=analyze({project:path.join(f.root,"package.json")},"0.5.0-dev"),online=analyze({project:path.join(f.root,"package.json"),dependencyMetadata:true},"0.5.0-dev");
    validateEvidence(local.evidence);expect(local.evidence.analysis.status).toBe("complete");
    expect(local.evidence.dimensions.dependencyManagement.status).toBe("skipped");
    expect(local.evidence.dimensions.dependencyManagement.dependencyMetadata).toMatchObject({status:"notRequested"});
    expect(local.csv).toBe(online.csv);expect(()=>compare(local.evidence,online.evidence)).toThrow();
    const reader:RegistryReader={package:async name=>feed(name),advisories:async()=>available({})};
    await expect(enrichDependencies(local.evidence,reader)).rejects.toThrow("dependencyMetadata: true");
    await enrichDependencies(online.evidence,reader);validateEvidence(online.evidence);
    expect(online.evidence.dimensions.dependencyManagement.dependencyMetadata).toMatchObject({status:"available"});
  });
});

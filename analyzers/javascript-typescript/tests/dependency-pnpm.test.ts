import fs from "node:fs";
import path from "node:path";
import { stringify } from "yaml";
import { afterEach, describe, expect, it } from "vitest";
import { dependencyInventory } from "../src/dependency-inventory.js";
import { dependencyMetadata } from "../src/dependency-metadata.js";
import { analyze } from "../src/analyzer.js";

const roots:string[]=[];
const record=(version:string,specifier="^1")=>({specifier,version});
const packageRecord={resolution:{integrity:"sha512-fixture"}};
function fixture(manifest:object,lock:object|string,extra:Record<string,object|string>={}) {
  const base=path.resolve("../../TestResults");fs.mkdirSync(base,{recursive:true});
  const root=fs.mkdtempSync(path.join(base,"pnpm-tests-"));roots.push(root);
  fs.mkdirSync(path.join(root,".git"));
  for(const [name,data] of Object.entries({"package.json":{name:"example",packageManager:"pnpm@10.0.0",...manifest},"index.js":"export const value=1;","pnpm-lock.yaml":lock,...extra})) {
    const file=path.join(root,name);fs.mkdirSync(path.dirname(file),{recursive:true});
    fs.writeFileSync(file,typeof data==="string"?data:name.endsWith(".yaml")?stringify(data,{aliasDuplicateObjects:false}):JSON.stringify(data));
  }
  return {root,read:(dirs=["."])=>dependencyInventory(dirs.map(dir=>({name:dir,root:path.join(root,dir),entryPoint:path.join(root,dir,"package.json")})),root)};
}
const lock=(importer:object,packages:object={"a@1.0.0":packageRecord},snapshots:object={"a@1.0.0":{}},extra:object={})=>({lockfileVersion:"9.0",importers:{".":importer},packages,snapshots,...extra});
const simple=()=>lock({dependencies:{a:record("1.0.0")}});
afterEach(()=>{for(const root of roots.splice(0)) fs.rmSync(root,{recursive:true,force:true});});

describe("pnpm v9 dependency inventory",()=>{
  it("reads a standalone v9 lockfile without installing dependencies",()=>{
    const f=fixture({dependencies:{a:"^1"}},simple());
    const {inventory,inputs}=f.read();expect(inventory.status).toBe("complete");
    expect(inventory.packages[0]).toMatchObject({name:"a",version:"1.0.0",source:"otherSource"});
    expect(inventory.packages[0].uses[0]).toMatchObject({direct:true,kind:"production"});
    expect(inputs).toContain(path.join(f.root,"pnpm-lock.yaml"));
    expect(fs.existsSync(path.join(f.root,"node_modules"))).toBe(false);
  });
  it("selects the project document after an environment document and excludes tool packages explicitly",()=>{
    const env=lock({packageManagerDependencies:{pnpm:record("12.4.2","12.4.2")}}, {"pnpm@12.4.2":packageRecord},{"pnpm@12.4.2":{}});
    const f=fixture({dependencies:{a:"^1"}},`---\n${stringify(env)}---\n${stringify(simple())}`);
    const {inventory}=f.read();expect(inventory.status).toBe("complete");expect(inventory.packages.map(row=>row.name)).toEqual(["a"]);
    expect(inventory.lockfiles[0]).toMatchObject({projectDocument:2,environmentDocument:"excluded"});
  });
  it("does not treat an environment-only lock as an empty complete project graph",()=>{
    for(const manifest of [{dependencies:{a:"^1"}},{}]) {
      const f=fixture(manifest,lock({packageManagerDependencies:{pnpm:record("12.4.2")}}));
      expect(f.read().inventory.status).toBe("gaps");expect(f.read().inventory.packages).toEqual([]);
    }
  });
  it("groups peer variants once while retaining both contexts and their transitive resolutions",()=>{
    const f=fixture({dependencies:{a:"^1"},devDependencies:{alias:"npm:a@^1"}},lock(
      {dependencies:{a:record("1.0.0(peer@1.0.0)")},devDependencies:{alias:record("a@1.0.0(peer@2.0.0)","npm:a@^1")}},
      {"a@1.0.0":{...packageRecord,peerDependencies:{peer:"*"}},"peer@1.0.0":packageRecord,"peer@2.0.0":packageRecord},
      {"a@1.0.0(peer@1.0.0)":{dependencies:{peer:"1.0.0"}},"a@1.0.0(peer@2.0.0)":{dependencies:{peer:"2.0.0"}},"peer@1.0.0":{},"peer@2.0.0":{}}));
    const {inventory}=f.read();expect(inventory.status).toBe("complete");expect(inventory.packages).toHaveLength(3);
    const a=inventory.packages.find(row=>row.name==="a")!;
    expect(a.locations).toHaveLength(2);expect(a.uses.map(use=>use.kind).sort()).toEqual(["development","production"]);
    expect(inventory.packages.find(row=>row.version==="2.0.0")!.uses[0]).toMatchObject({kind:"development",direct:false});
  });
  it("traverses workspace links and cycles within the selected reachability, excluding linked dev dependencies",()=>{
    const a={name:"a",version:"1.0.0",dependencies:{b:"workspace:*"}},b={name:"b",version:"1.0.0",dependencies:{a:"workspace:*",leaf:"^1"},devDependencies:{tool:"^1"}};
    const f=fixture({},lock({}, {"leaf@1.0.0":packageRecord,"tool@1.0.0":packageRecord,"unrelated@1.0.0":packageRecord},{"leaf@1.0.0":{},"tool@1.0.0":{},"unrelated@1.0.0":{}},{importers:{
      "packages/a":{dependencies:{b:record("link:../b","workspace:*")}},
      "packages/b":{dependencies:{a:record("link:../a","workspace:*"),leaf:record("1.0.0")},devDependencies:{tool:record("1.0.0")}}}}),
      {"packages/a/package.json":a,"packages/b/package.json":b});
    const {inventory,inputs}=f.read(["packages/a"]);expect(inventory.gaps).toEqual([]);
    expect(inventory.packages.map(row=>row.name)).toEqual(["a","b","leaf"]);
    expect(inventory.packages.find(row=>row.name==="b")!.source).toBe("workspace");expect(inputs).toContain(path.join(f.root,"packages/b/package.json"));
  });
  it("resolves default/named catalogs and records ranges/config hashes without replacing the declaration",()=>{
    const f=fixture({dependencies:{a:"catalog:",beta:"catalog:next"}},lock({dependencies:{a:record("1.0.0","catalog:"),beta:record("2.0.0-beta.1","catalog:next")}},
      {"a@1.0.0":packageRecord,"beta@2.0.0-beta.1":packageRecord},{"a@1.0.0":{},"beta@2.0.0-beta.1":{}},
      {catalogs:{default:{a:record("1.0.0")},next:{beta:record("2.0.0-beta.1","^2.0.0-beta.1")}}}),
      {"pnpm-workspace.yaml":{catalog:{a:"^1"},catalogs:{next:{beta:"^2.0.0-beta.1"}}}});
    const {inventory,inputs}=f.read();expect(inventory.gaps).toEqual([]);
    expect(inventory.packages[0].uses[0]).toMatchObject({range:"catalog:",resolvedRange:"^1"});
    expect(inventory.configurationInputs?.[0].sha256).toHaveLength(64);expect(inputs).toContain(path.join(f.root,"pnpm-workspace.yaml"));
  });
  it("accepts catalogs expanded by recorded overrides, including omitted overridden catalog entries",()=>{
    const f=fixture({dependencies:{a:"catalog:"}},lock({dependencies:{a:record("1.0.0","^1")}},undefined,undefined,{overrides:{a:"^1"}}),
      {"pnpm-workspace.yaml":{catalog:{a:"^1"},overrides:{a:"catalog:"}}});
    expect(f.read().inventory.status).toBe("complete");
    expect(f.read().inventory.manifests[0].overrides).toMatchObject({workspace:{a:"catalog:"},lockfile:{a:"^1"}});
  });
  it("honors explicit replacement overrides without asserting installation validity",()=>{
    const f=fixture({dependencies:{a:"^1"},pnpm:{overrides:{a:"npm:b@2.0.0"}}},lock({dependencies:{a:record("b@2.0.0","npm:b@2.0.0")}},
      {"b@2.0.0":packageRecord},{"b@2.0.0":{}},{overrides:{a:"npm:b@2.0.0"}}));
    expect(f.read().inventory.status).toBe("complete");expect(f.read().inventory.packages[0].name).toBe("b");expect(f.read().inventory.notices).toHaveLength(1);
  });
  it("keeps optional peers as notices, and required children as gaps even under optional parents",()=>{
    const f=fixture({optionalDependencies:{a:"^1"}},lock({optionalDependencies:{a:record("1.0.0")}},
      {"a@1.0.0":{...packageRecord,peerDependencies:{peer:"^1"},peerDependenciesMeta:{peer:{optional:true}}}},
      {"a@1.0.0":{dependencies:{missing:"1.0.0"}}}));
    const {inventory}=f.read();expect(inventory.gaps).toHaveLength(1);expect(inventory.gaps[0].dependency).toBe("missing");
    expect(inventory.notices[0].dependency).toBe("peer");expect(inventory.packages[0].uses[0].optional).toBe(true);
  });
  it("retains a peer also present in dev dependencies as a separate declared use",()=>{
    const f=fixture({devDependencies:{a:"^1"},peerDependencies:{a:"*"}},lock({devDependencies:{a:record("1.0.0")}}));
    expect(f.read().inventory.packages[0].uses.map(use=>use.kind).sort()).toEqual(["development","peer"]);
  });
  it.each([5.4,6,10])("reports unsupported format %s as unavailable",version=>{
    const f=fixture({dependencies:{a:"^1"}},{...simple(),lockfileVersion:version});
    expect(f.read().inventory.status).toBe("gaps");expect(f.read().inventory.packages).toEqual([]);
  });
  it.each([
    'lockfileVersion: 9\nlockfileVersion: 9\nimporters: {}',
    'lockfileVersion: 9\nimporters: &a {}\npackages: *a',
    'lockfileVersion: 9\nimporters: !custom {}',
    `${stringify(simple())}---\n${stringify(simple())}`,
    'lockfileVersion: 9\nimporters: [broken',
  ])("withholds malformed, aliased, or ambiguous YAML",text=>{
    const f=fixture({dependencies:{a:"^1"}},text);expect(f.read().inventory.status).toBe("gaps");expect(f.read().inventory.packages).toEqual([]);
  });
  it("withholds stale manifests, catalog settings, and missing importers",()=>{
    const fixtures=[fixture({dependencies:{a:"^2"}},simple()),fixture({dependencies:{a:"^1"}}, {...simple(),importers:{}}),
      fixture({dependencies:{a:"catalog:"}},lock({dependencies:{a:record("1.0.0","catalog:")}},undefined,undefined,{catalogs:{default:{a:record("1.0.0")}}}),{"pnpm-workspace.yaml":{catalog:{a:"^2"}}})];
    for(const f of fixtures){expect(f.read().inventory.status).toBe("gaps");expect(f.read().inventory.packages).toEqual([]);}
  });
  it("does not fall back to another peer snapshot when the exact context is absent",()=>{
    const f=fixture({dependencies:{a:"^1"}},lock({dependencies:{a:record("1.0.0(peer@2.0.0)")}},undefined,{"a@1.0.0(peer@1.0.0)":{}}));
    expect(f.read().inventory.gaps[0].reason).toContain("exact peer-context");expect(f.read().inventory.packages).toEqual([]);
  });
  it("does not read unsafe workspace links or unsupported file/git identities",()=>{
    for(const ref of ["link:../../outside","link:C:/outside","file:../archive.tgz","git+https://example.com/repo"]) {
      const f=fixture({dependencies:{a:"^1"}},lock({dependencies:{a:record(ref)}}));
      expect(f.read().inventory.status).toBe("gaps");expect(f.read().inventory.packages).toEqual([]);
    }
  });
  it("does not query private or implicit registry identities; public tarballs and catalog ranges can be observed",async()=>{
    const f=fixture({dependencies:{a:"catalog:",private:"^1",implicit:"^1"}},lock(
      {dependencies:{a:record("1.0.0","catalog:"),private:record("1.0.0"),implicit:record("1.0.0")}},
      {"a@1.0.0":{resolution:{tarball:"https://registry.npmjs.org/a/-/a-1.0.0.tgz"}},"private@1.0.0":{resolution:{tarball:"https://secret:token@private.example/a.tgz"}},"implicit@1.0.0":packageRecord},
      {"a@1.0.0":{},"private@1.0.0":{},"implicit@1.0.0":{}},{catalogs:{default:{a:record("1.0.0")}}}),{"pnpm-workspace.yaml":{catalog:{a:"^1"}}});
    const {inventory}=f.read(),names:string[]=[],payloads:unknown[]=[];
    const available=(data:unknown)=>({status:"available" as const,data,sha256:"test"});
    const metadata=await dependencyMetadata(inventory,{package:async name=>{names.push(name);return available({name,versions:{"1.0.0":{version:"1.0.0"},"1.1.0":{version:"1.1.0"}},"dist-tags":{latest:"1.1.0"}});},advisories:async payload=>{payloads.push(payload);return available({});}});
    expect(names).toEqual(["a"]);expect(payloads).toEqual([{a:["1.0.0"]}]);expect(metadata.status).toBe("partial");
    expect(metadata.observations[0].upgrades?.[0].wanted).toBe("1.1.0");expect(JSON.stringify(inventory)).not.toContain("token");
  });
  it("selects the declared manager and reports ambiguous undeclared lockfiles",()=>{
    const f=fixture({dependencies:{a:"^1"}},simple(),{"package-lock.json":{lockfileVersion:3,packages:{}}});
    expect(f.read().inventory.status).toBe("complete");
    fs.writeFileSync(path.join(f.root,"package.json"),JSON.stringify({name:"example",dependencies:{a:"^1"}}));
    expect(f.read().inventory.gaps[0].reason).toContain("ambiguous");
  });
  it("recognizes an undeclared pnpm lock and preserves input protection through analysis",()=>{
    const f=fixture({packageManager:undefined,dependencies:{a:"^1"}},simple(),{"pnpm-workspace.yaml":{packages:["."]}});
    const result=analyze({project:path.join(f.root,"package.json")},"0.5.0-dev");
    expect(result.evidence.analysis.status).toBe("complete");expect(result.inputs).toContain(path.join(f.root,"pnpm-lock.yaml"));expect(result.inputs).toContain(path.join(f.root,"pnpm-workspace.yaml"));
    expect(result.evidence.dimensions.dependencyManagement).toMatchObject({status:"skipped",dependencyInventory:{status:"complete"},dependencyMetadata:{status:"notRequested"}});
  });
});

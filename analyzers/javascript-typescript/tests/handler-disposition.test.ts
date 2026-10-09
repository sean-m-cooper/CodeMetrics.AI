import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { afterEach, describe, expect, it } from "vitest";
import { analyze } from "../src/analyzer.js";
import { errorHandlingDecision } from "../src/error-handling-scoring.js";
import { compare, validateEvidence } from "../dist/evidence-tools.js";

const roots: string[] = [];
function run(code: string, files: Record<string, string> = {}) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "handler-disposition-")); roots.push(root);
  for (const [name, value] of Object.entries({ "package.json": '{"name":"handlers"}', "code.ts": code, ...files }))
    fs.writeFileSync(path.join(root, name), value);
  const evidence = analyze({ project: path.join(root, "package.json") }, "0.4.0-dev").evidence;
  validateEvidence(evidence); return evidence;
}
function rows(code: string) { return (run(code).dimensions.errorHandling.handlerEvidence as any).handlers; }
afterEach(() => { for (const root of roots.splice(0)) fs.rmSync(root, { recursive: true, force: true }); });

describe("failure disposition population", () => {
  it.each([[2,2,0],[100,2,9],[100,20,8],[100,50,5],[16,5,6.9],[100,0,10]])("scores %i bodies and %i issues as %s", (n,b,score) => {
    expect(errorHandlingDecision(n,b,0,0)?.finalScore).toBe(score);
  });
  it("withholds unknown and absent populations and rejects invalid arithmetic", () => {
    expect(errorHandlingDecision(0,0,0,0)).toBeUndefined();
    expect(errorHandlingDecision(100,0,1,0)).toBeUndefined();
    expect(errorHandlingDecision(100,0,0,1)).toBeUndefined();
    for (const args of [[2,3,0,0],[2,1,2,0],[2,-1,0,0],[2,0,0,0.5]])
      expect(() => errorHandlingDecision(...args as [number,number,number,number])).toThrow();
  });
  it("scores a shared swallowed rejection once and exposes policy effects", () => {
    const e=run(`import {ignore} from './ignore'; Promise.reject(1).catch(ignore);
      Promise.reject(2).then(undefined,ignore); try {work()} catch(e) {throw e}`, {"ignore.ts":"export function ignore(){}"});
    const d=e.dimensions.errorHandling;
    expect(d.score).toBe(5); expect((d.dispositionEvidence as any).totalHandlers).toBe(2);
    expect(d.findings).toHaveLength(1); expect(d.findings[0].severity).toBe("warning");
    expect(d.scoringDecision?.findingEffects[0].effect).toBe("policyInput");
    const before=structuredClone(e);before.analysis.ruleset="javascript-typescript-2026-10-04-local-handlers";
    expect(()=>compare(e,before)).toThrow("ruleset");
  });
});
describe("bounded contextual handler classification", () => {
  it("recognizes caller-supplied error callbacks without guessing callback names", () => {
    const data=rows(`function a(deliver){try{work()}catch(e){deliver(e)}}
      function b(deliver){deliver=()=>{};try{work()}catch(e){deliver(e)}}
      function c(deliver){[deliver]=[()=>{}];try{work()}catch(e){deliver(e)}}
      function d(){function deliver(e){};try{work()}catch(e){deliver(e)}}
      function f(deliver){try{work()}catch(e){e=null;deliver(e)}}
      function g(deliver){try{work()}catch(e){deliver({ok:false,message:e.message})}}
      function h(deliver){try{work()}catch(e){e=null;deliver({e})}}
      function i(deliver){try{work()}catch(e){deliver({e})}}`);
    expect(data.map((h:any)=>h.disposition.classification)).toEqual(["propagated","unknown","unknown","unknown","unknown","propagated","unknown","propagated"]);
  });
  it("recognizes explicit result containers while keeping arbitrary calls and mutable values unknown", () => {
    const data=rows(`function a(){try{work()}catch(e){return {ok:false,error:e}}}
      const fallback={ok:false}; function b(){try{work()}catch{return fallback}}
      function c(){try{work()}catch{return new Error('failed')}}
      let changing=false;function d(){try{work()}catch{return changing}}
      function e(){try{work()}catch{return unknown()}}`);
    expect(data.map((h:any)=>h.disposition.classification)).toEqual(["failureResult","failureResult","failureResult","unknown","unknown"]);
  });
  it("resolves logging wrappers and declared filtered loggers to payload-bearing console sinks", () => {
    const data=rows(`function report(message){console.error(message)}
      function quiet(message){console.error('unrelated')}
      function conditional(message){if(flag)console.error(message)}
      class Logger {
        /** Write a message to the log. */
        log(...message){if(hidden)return; console.error(...message)}
        error(...message){this.log(...message)}
      }
      const logger=new Logger();
      function a(){try{work()}catch(e){report(e)}}
      function b(){try{work()}catch(e){quiet(e)}}
      function c(){try{work()}catch(e){conditional(e)}}
      function d(){try{work()}catch(e){logger.error(e)}}
      function broken(message){return;console.error(message)}
      function e(){try{work()}catch(e){broken(e)}}
      function misdirect(message){second('unrelated',message)}
      function second(output,ignored){console.error(output)}
      function f(){try{work()}catch(e){misdirect(e)}}
      function changed(message){message='unrelated';console.error(message)}
      function g(){try{work()}catch(e){changed(e)}}`);
    expect(data.map((h:any)=>h.disposition.classification)).toEqual(["reported","unknown","unknown","reported","unknown","unknown","unknown"]);
  });
  it("checks every use of a private optional-result helper before crediting its sentinel", () => {
    const data=rows(`function maybe(){try{return read()}catch{return undefined}}
      function use(){var v=maybe();if(v&&v.ok)return v;v=maybe();if(v&&v.ok)return v;}
      function unsafe(){try{return read()}catch{return undefined}}
      const leaked=unsafe();
      function mixed(){try{return read()}catch{return undefined}}
      function useMixed(){var v=mixed();if(v&&v.ok)return v; return v.ok;}
      function changed(){try{return read()}catch{return undefined}}
      function useChanged(){var v=changed();if(v){v=undefined;return v.ok}}
      export function publicResult(){try{return read()}catch{return undefined}}
      function usePublic(){var v=publicResult();if(v&&v.ok)return v;}
      function aliasedExport(){try{return read()}catch{return undefined}}
      function useAlias(){var v=aliasedExport();if(v&&v.ok)return v;}
      export {aliasedExport as optional};`);
    expect(data.map((h:any)=>h.disposition.classification)).toEqual(["fallback","unknown","unknown","unknown","unknown","unknown"]);
  });
  it("keeps CommonJS helpers private only when no shorthand reference escapes", () => {
    const e=run('', {"module.js":`module.exports={};
      function maybe(){try{return read()}catch{return undefined}}
      function use(){var v=maybe();if(v&&v.ok)return v;}
      function escaped(){try{return read()}catch{return undefined}}
      function useEscaped(){var v=escaped();if(v&&v.ok)return v;}
      module.exports.bad={escaped};`});
    expect((e.dimensions.errorHandling.handlerEvidence as any).handlers.map((h:any)=>h.disposition.classification)).toEqual(["fallback","unknown"]);
    const shadowed=run('', {"module.js":`var module={exports:{}};module.exports={};
      function maybe(){try{return read()}catch{return undefined}}
      function use(){var v=maybe();if(v&&v.ok)return v;}`});
    expect((shadowed.dimensions.errorHandling.handlerEvidence as any).handlers[0].disposition.classification).toBe("unknown");
  });
  it("recognizes explicit outcomes without equating arbitrary code with handling", () => {
    const data=rows(`function a(){try{work()}catch(e){throw e}}
      function b(){try{work()}catch{return {ok:false}}}
      function c(){try{work()}catch(e){console.error(e)}}
      function d(){try{work()}catch{let unused=1}}
      function e(){try{work()}catch{function unused(){throw new Error()}}}
      function f(){try{work()}catch{if(flag)throw new Error()}}
      function g(){try{work()}catch{if(flag)throw new Error();else return false}}
      function h(){try{work()}catch{return void log()}}`);
    expect(data.map((h:any)=>h.disposition.classification)).toEqual([
      "propagated","failureResult","reported","unknown","unknown","unknown","failureResult","unknown"]);
  });
  it("honors body rationale and attached overload intent, not names or unrelated comments", () => {
    const data=rows(`/** A function that does nothing. */
      function noop():void;
      function noop(){}
      Promise.reject(1).catch(noop); Promise.reject(2).catch(noop);
      function ignore(){} Promise.reject(1).catch(ignore);
      // Parse the input.
      try{work()}catch{}
      try{work()}catch{/* Deliberately ignored: optional cleanup. */}
      // Optional configuration may be absent.
      try{work()}catch{}
      try{work()}catch{/* Ignore optional cleanup failure. */ cleanupState()}
      try{work()}catch{function unrelated(){/* Ignore optional work */}}
      try{work()}catch{/** Ignore optional work. */ function unrelated(){}}
      try{work()}catch{const pattern=/[/*] optional/;}`);
    expect(data.map((h:any)=>h.disposition.classification)).toEqual(["documented","unexplainedSwallowing","unknown","documented","documented","documented","unknown","unknown","unknown"]);
    expect(data[0].uses).toHaveLength(2);
  });
  it("recognizes a guarded parse fallback without executing target code", () => {
    const data=rows(`function parse(value:string){let parsed;
      try{parsed=JSON.parse(value)}catch{}
      if(parsed===undefined)return {ok:false};
      return {ok:true,value:parsed};}
      function get(){try{return read()}catch{return undefined}}
      function continuation(){try{work()}catch{} doOtherWork();}`);
    expect(data.map((h:any)=>h.disposition.classification)).toEqual(["fallback","unknown","unknown"]);
  });
  it("does not infer a fallback from a guard on different state or a conditional return", () => {
    const data=rows(`function wrong(){let parsed;try{parsed=read()}catch{}if(other===undefined)return false;}
      function assigned(){let parsed=1;try{parsed=read()}catch{}if(parsed===undefined)return false;}
      function shadow(undefined:any){let parsed;try{parsed=read()}catch{}if(parsed===undefined)return false;}
      function mutated(){let parsed;try{parsed=read(parsed=1)}catch{}if(parsed===undefined)return false;}
      function hoisted(){parsed=1;var parsed;try{parsed=read()}catch{}if(parsed===undefined)return false;}
      function conditional(){try{work()}catch{}if(flag)return false;}`);
    expect(data.every((h:any)=>h.disposition.classification==="unknown")).toBe(true);
  });
  it("withholds unknown bodies and callback gaps instead of rewarding them", () => {
    const e=run(`function ignore(){} Promise.reject(1).catch(ignore); Promise.reject(1).catch(missing);
      function arbitrary(){try{work()}catch{customLog()}}`);
    expect(e.dimensions.errorHandling.status).toBe("skipped");
    expect(e.dimensions.errorHandling.score).toBeUndefined();
    expect((e.dimensions.errorHandling.dispositionEvidence as any).unknownHandlers).toBe(1);
    expect(e.dimensions.errorHandling.findings.every(f=>f.severity==="info")).toBe(true);
  });
  it("does not penalize result-dependent Promise fallbacks or borrow one use to classify every use", () => {
    const data=rows(`function ignore(){} Promise.reject(1).catch(ignore);
      const recovered=Promise.reject(1).catch(ignore).then(value=>value ?? 42);
      function caller(){return Promise.reject(1).catch(()=>undefined)}
      const result=Promise.reject(1).catch(()=>{});`);
    expect(data).toHaveLength(3);
    expect(data[0].uses).toHaveLength(2);
    expect(data.every((h:any)=>h.disposition.classification==="unknown")).toBe(true);
  });
  it("does not credit local console lookalikes or finally-overridden outcomes", () => {
    const data=rows(`function local(){const console={error(e:any){}};try{work()}catch(e){console.error(e)}}
      function overridden(){try{work()}catch(e){throw e}finally{return}}
      function outer(){try{try{work()}catch(e){throw e}}finally{return}}
      function nested(){if(flag){try{work()}catch{}}return undefined;}`);
    expect(data.every((h:any)=>h.disposition.classification==="unknown")).toBe(true);
  });
});

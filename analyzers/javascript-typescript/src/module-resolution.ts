import path from "node:path";
import ts from "typescript";
import type { PackageInput } from "./discovery.js";

export const canonicalFile = (file: string) => {
  const resolved = path.resolve(ts.sys.realpath?.(file) ?? file).replaceAll("\\", "/");
  return ts.sys.useCaseSensitiveFileNames ? resolved : resolved.toLowerCase();
};

// A failed installed-package lookup can mean an intentionally private subpath
// or missing build output. A same-named workspace is not a substitute version.
function hasInstalledPackage(specifier: string, importer: string): boolean {
  if (specifier.startsWith(".") || path.isAbsolute(specifier)) return false;
  const parts = specifier.split("/");
  const name = parts.slice(0, specifier.startsWith("@") ? 2 : 1).join("/");
  for (let directory = path.dirname(importer); ; directory = path.dirname(directory)) {
    if (ts.sys.directoryExists(path.join(directory, "node_modules", name))) return true;
    if (path.dirname(directory) === directory) return false;
  }
}

// A read-only virtual node_modules view lets TypeScript interpret workspace
// manifests/exports without installing packages or guessing source entry points.
function workspaceHost(packages: PackageInput[]): ts.ModuleResolutionHost {
  const names = new Map<string, string>();
  const ambiguous = new Set<string>();
  for (const pkg of packages) {
    if (names.has(pkg.name)) ambiguous.add(pkg.name);
    else names.set(pkg.name, pkg.root);
  }
  for (const name of ambiguous) names.delete(name);
  function mapped(file: string): string | undefined {
    const normalized = file.replaceAll("\\", "/");
    const index = normalized.lastIndexOf("/node_modules/");
    if (index < 0) return undefined;
    const tail = normalized.slice(index + 14).split("/");
    const count = tail[0].startsWith("@") ? 2 : 1;
    const root = names.get(tail.slice(0, count).join("/"));
    return root ? path.join(root, ...tail.slice(count)) : undefined;
  }
  return {
    fileExists: file => ts.sys.fileExists(mapped(file) ?? file),
    readFile: file => ts.sys.readFile(mapped(file) ?? file),
    directoryExists: directory => {
      const normalized = directory.replaceAll("\\", "/").replace(/\/$/, "");
      if (normalized.endsWith("/node_modules")) return names.size > 0;
      const scope = normalized.match(/\/node_modules\/(@[^/]+)$/)?.[1];
      if (scope && [...names.keys()].some(name => name.startsWith(scope + "/"))) return true;
      return ts.sys.directoryExists(mapped(directory) ?? directory);
    },
    realpath: file => ts.sys.realpath?.(mapped(file) ?? file) ?? mapped(file) ?? file,
    getCurrentDirectory: ts.sys.getCurrentDirectory,
  };
}

export function moduleResolver(pkg: PackageInput, packages: PackageInput[], program: ts.Program) {
  const options = pkg.selection === "default-source-scan" ? { ...pkg.options, moduleResolution: ts.ModuleResolutionKind.Node10 } : pkg.options;
  const canonical = (file: string) => ts.sys.useCaseSensitiveFileNames ? file : file.toLowerCase();
  const cache = ts.createModuleResolutionCache(pkg.root, canonical, options);
  const workspaceCache = ts.createModuleResolutionCache(pkg.root, canonical, options);
  const host = workspaceHost(packages);
  return (source: ts.SourceFile, literal: ts.StringLiteralLike) => {
    const mode = program.getModeForUsageLocation(source, literal);
    const ordinary = ts.resolveModuleName(literal.text, source.fileName, options, ts.sys, cache, undefined, mode).resolvedModule;
    if (ordinary) return { resolved: ordinary, via: "typescript" as const };
    if (hasInstalledPackage(literal.text, source.fileName)) return { resolved: undefined, via: "unresolved" as const };
    const workspace = ts.resolveModuleName(literal.text, source.fileName, options, host, workspaceCache, undefined, mode).resolvedModule;
    return { resolved: workspace, via: workspace ? "workspaceManifest" as const : "unresolved" as const };
  };
}

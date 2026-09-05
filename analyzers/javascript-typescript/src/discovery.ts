import fs from "node:fs";
import path from "node:path";
import ts from "typescript";

export const excludedDirectories = new Set(["node_modules", ".git", ".worktrees", ".scorecard", "dist", "build", ".next", "out", "coverage", "vendor"]);
const slash = (file: string) => file.replaceAll("\\", "/");
export function exclusion(file: string): string | undefined {
  const name = slash(file);
  if (name.split("/").some(part => excludedDirectories.has(part))) return "Build output, dependencies, or metadata";
  if (/\.d\.[cm]?ts$/.test(name)) return "Type declarations";
  if (/(^|\/)(__tests__|tests?|fixtures)(\/|$)|\.(test|spec)\.[cm]?[jt]sx?$/.test(name)) return "Test or fixture source";
  if (/(\.generated\.|\.g\.|\.min\.)/.test(name)) return "Generated source";
  return undefined;
}
export function walk(root: string): string[] {
  const files: string[] = [];
  for (const entry of fs.readdirSync(root, { withFileTypes: true }).sort((a,b) => a.name < b.name ? -1 : 1)) {
    if (entry.isSymbolicLink()) continue;
    const full = path.join(root, entry.name);
    if (entry.isDirectory() && !excludedDirectories.has(entry.name)) files.push(...walk(full));
    else if (entry.isFile()) files.push(full);
  }
  return files;
}
export interface PackageInput { name: string; root: string; entryPoint: string; files: string[]; options: ts.CompilerOptions; selection: unknown; }
export function repositoryRoot(root: string): string {
  for (let directory = root; ; directory = path.dirname(directory)) {
    if (fs.existsSync(path.join(directory, ".git"))) return directory;
    if (path.dirname(directory) === directory) return root;
  }
}
export function discover(project?: string, config?: string) {
  const entryPoint = path.resolve(project ?? (config ? path.join(path.dirname(config), "package.json") : "package.json"));
  if (!fs.existsSync(entryPoint)) throw new Error(`Package manifest does not exist: ${entryPoint}`);
  const root = path.dirname(entryPoint);
  const manifest = JSON.parse(fs.readFileSync(entryPoint, "utf8"));
  const workspacePatterns: unknown = Array.isArray(manifest.workspaces) ? manifest.workspaces : manifest.workspaces?.packages;
  const manifests = [entryPoint];
  if (Array.isArray(workspacePatterns)) {
    if (!workspacePatterns.every(pattern => typeof pattern === "string" && !pattern.startsWith("!") && !pattern.includes("..")))
      throw new Error("Workspace patterns must be relative positive globs without '..'.");
    manifests.push(...ts.sys.readDirectory(root, [".json"], ["**/node_modules/**", "**/.git/**", "**/.worktrees/**"],
      workspacePatterns.map(pattern => `${pattern.replace(/\/$/, "")}/package.json`)));
  }
  const packages: PackageInput[] = [];
  const skipped: { name: string; reason: string }[] = [];
  const owned = new Set<string>();
  // Children own their files before the workspace root is scanned.
  const manifestPaths = [...new Set(manifests.map(file => path.resolve(file)))];
  for (const manifestPath of manifestPaths.sort((a,b) => b.length - a.length || a.localeCompare(b, "en"))) {
    const packageRoot = path.dirname(manifestPath);
    const data = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
    const configPath = config && manifestPath === entryPoint ? path.resolve(config) : path.join(packageRoot, "tsconfig.json");
    if (config && manifestPath === entryPoint && !fs.existsSync(configPath)) throw new Error(`tsconfig does not exist: ${configPath}`);
    let files: string[];
    let selection: unknown = "default-source-scan";
    let options: ts.CompilerOptions = { allowJs: true, jsx: ts.JsxEmit.Preserve, target: ts.ScriptTarget.Latest, noEmit: true };
    if (fs.existsSync(configPath)) {
      const read = ts.readConfigFile(configPath, ts.sys.readFile);
      if (read.error) throw new Error(ts.flattenDiagnosticMessageText(read.error.messageText, "\n"));
      const parsed = ts.parseJsonConfigFileContent(read.config, ts.sys, path.dirname(configPath), { noEmit: true }, configPath);
      const errors = parsed.errors.filter(error => error.code !== 18003);
      if (errors.length) throw new Error(errors.map(error => ts.flattenDiagnosticMessageText(error.messageText, "\n")).join("\n"));
      files = parsed.fileNames; options = parsed.options;
      selection = { include: parsed.raw.include, exclude: parsed.raw.exclude, files: parsed.raw.files, references: parsed.raw.references };
    } else files = walk(packageRoot).filter(file => /\.[cm]?[jt]sx?$/.test(file));
    const accepted: string[] = [];
    for (const file of files.map(file => path.resolve(file)).sort()) {
      if (manifestPaths.some(other => other !== manifestPath && path.dirname(other).startsWith(packageRoot + path.sep) && file.startsWith(path.dirname(other) + path.sep))) continue;
      const relative = slash(path.relative(root, file));
      if (owned.has(file)) continue;
      owned.add(file);
      const reason = relative.startsWith("../") ? "Outside repository" : exclusion(relative);
      if (reason) { skipped.push({ name: relative, reason }); continue; }
      if (/^\s*(?:\/\/|\/\*)[^\n]*(?:@generated|<auto-generated)/im.test(fs.readFileSync(file, "utf8")))
        skipped.push({ name: relative, reason: "Generated source marker" });
      else accepted.push(file);
    }
    packages.push({ name: data.name ?? path.basename(packageRoot), root: packageRoot, entryPoint: manifestPath, files: accepted, options, selection });
  }
  return { root, repositoryRoot: repositoryRoot(root), entryPoint, name: manifest.name ?? path.basename(root), packages, skipped };
}

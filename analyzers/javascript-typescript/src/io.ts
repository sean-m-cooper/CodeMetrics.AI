import fs from "node:fs";
import path from "node:path";
import { randomUUID } from "node:crypto";
export function write(file: string, value: unknown) {
  fs.mkdirSync(path.dirname(path.resolve(file)), { recursive: true });
  const temporary = file + "." + randomUUID() + ".tmp";
  try { fs.writeFileSync(temporary, typeof value === "string" ? value : JSON.stringify(value, null, 2) + "\n"); fs.renameSync(temporary, file); }
  finally { if (fs.existsSync(temporary)) fs.unlinkSync(temporary); }
}
export function distinctOutputs(inputs: (string | undefined)[], outputs: (string | undefined)[]) {
  const normalize = (file: string) => process.platform === "win32" ? path.resolve(file).toLowerCase() : path.resolve(file);
  const targets = outputs.filter((file): file is string => !!file).map(normalize);
  if (new Set(targets).size !== targets.length || inputs.some(file => file && targets.includes(normalize(file))))
    throw new Error("Output paths must be distinct and must not overwrite inputs.");
}

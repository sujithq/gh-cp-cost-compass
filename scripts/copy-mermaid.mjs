import { createHash } from "node:crypto";
import { copyFile, mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const source = resolve(root, "node_modules/mermaid/dist/mermaid.min.js");
const destination = resolve(
  root,
  "src/CopilotUsageSimulator.Web/wwwroot/vendor/mermaid/mermaid.min.js");
const vendorDirectory = dirname(destination);
const flowDocument = resolve(root, "docs/decision-flow.md");
const flowDestination = resolve(
  root,
  "src/CopilotUsageSimulator.Web/wwwroot/content/decision-flow.mmd");

await mkdir(vendorDirectory, { recursive: true });
await copyFile(source, destination);
await copyFile(
  resolve(root, "node_modules/mermaid/LICENSE"),
  resolve(vendorDirectory, "LICENSE"));

const packageMetadata = JSON.parse(await readFile(
  resolve(root, "node_modules/mermaid/package.json"), "utf8"));
const runtime = await readFile(destination);
await writeFile(
  resolve(vendorDirectory, "manifest.json"),
  `${JSON.stringify({
    version: packageMetadata.version,
    sha256: createHash("sha256").update(runtime).digest("hex")
  }, null, 2)}\n`,
  "utf8");

const document = await readFile(flowDocument, "utf8");
const diagram = document.match(/```mermaid\r?\n([\s\S]*?)\r?\n```/)?.[1];
if (!diagram) {
  throw new Error(`No Mermaid block found in ${flowDocument}`);
}

await mkdir(dirname(flowDestination), { recursive: true });
await writeFile(flowDestination, `${diagram}\n`, "utf8");
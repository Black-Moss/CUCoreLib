import { access, mkdir, rename, rm } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const projectRoot = path.resolve(__dirname, "..");
const distDir = path.join(projectRoot, "dist");
const generatedDocsDir = path.join(distDir, "pages", "docs");
const finalDocsDir = path.join(distDir, "docs");
const pagesDir = path.join(distDir, "pages");

if (await exists(generatedDocsDir)) {
  await rm(finalDocsDir, { recursive: true, force: true });
  await mkdir(path.dirname(finalDocsDir), { recursive: true });
  await rename(generatedDocsDir, finalDocsDir);
}

await rm(pagesDir, { recursive: true, force: true });

async function exists(targetPath) {
  try {
    await access(targetPath);
    return true;
  } catch {
    return false;
  }
}

import { mkdir, readdir, rm, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const projectRoot = path.resolve(__dirname, "..");
const pagesRoot = path.join(projectRoot, "pages");

const docsPagesModule = await import(pathToFileURL(path.join(projectRoot, "src", "docsPages.ts")).href);
const machineExportModule = await import(pathToFileURL(path.join(projectRoot, "src", "machineExport.ts")).href);

const pages = docsPagesModule.pages;
const enabledPageIds = machineExportModule.machineExportEnabledPageIds;

await mkdir(pagesRoot, { recursive: true });

const docsRoot = path.join(pagesRoot, "docs");
await mkdir(docsRoot, { recursive: true });
const expectedPageIds = new Set(enabledPageIds.filter((pageId) => pageId !== "tools"));

for (const entry of await readdir(docsRoot, { withFileTypes: true })) {
  if (!entry.isDirectory() || expectedPageIds.has(entry.name)) {
    continue;
  }

  await rm(path.join(docsRoot, entry.name), { recursive: true, force: true });
}

for (const pageId of enabledPageIds) {
  if (pageId === "tools") {
    continue;
  }

  const page = pages.find((entry) => entry.id === pageId);
  if (!page) {
    continue;
  }

  const pageDir = path.join(docsRoot, pageId);
  await mkdir(pageDir, { recursive: true });
  await writeFile(path.join(pageDir, "index.html"), renderPage(page), "utf8");
}

function renderPage(page) {
  const title = `${page.title} | CUCoreLib Docs`;
  const description = page.lead.trim();
  const canonicalUrl = `https://cucorelib.jimmyking.dev/docs/${encodeURIComponent(page.id)}/`;
  const structuredData = renderStructuredData(page, description, canonicalUrl);
  return `<!doctype html>
<html lang="en" style="background: #111; color: #fff;">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <meta name="color-scheme" content="dark" />
    <meta name="description" content="${escapeHtml(description)}" />
    <meta name="robots" content="index,follow" />
    <link rel="canonical" href="${canonicalUrl}" />
    <meta property="og:type" content="website" />
    <meta property="og:title" content="${escapeHtml(title)}" />
    <meta property="og:description" content="${escapeHtml(description)}" />
    <meta property="og:url" content="${canonicalUrl}" />
    <meta property="og:site_name" content="CUCoreLib Docs" />
    <meta name="twitter:card" content="summary" />
    <meta name="twitter:title" content="${escapeHtml(title)}" />
    <meta name="twitter:description" content="${escapeHtml(description)}" />
    <link rel="icon" type="image/svg+xml" href="/favicon.svg" />
    <link rel="alternate icon" type="image/png" href="/favicon.png" />
    ${structuredData}
    <title>${escapeHtml(title)}</title>
  </head>
  <body style="margin: 0; background: #111; color: #fff;">
    <div id="app"></div>
    <noscript>
      <main>
        <h1>${escapeHtml(page.title)}</h1>
        <p>${escapeHtml(description)}</p>
        <p>This documentation page uses JavaScript for the interactive app interface.</p>
        <p>Machine-readable docs are available at <a href="/api/cucorelib-docs.v1.json">/api/cucorelib-docs.v1.json</a>.</p>
      </main>
    </noscript>
    <script type="module" src="/src/main.ts"></script>
  </body>
</html>
`;
}

function renderStructuredData(page, description, canonicalUrl) {
  if (page.id !== "setup") {
    return "";
  }

  const payload = {
    "@context": "https://schema.org",
    "@type": "TechArticle",
    headline: page.title,
    description,
    url: canonicalUrl,
    about: ["CUCoreLib", "Casualties Unknown", "BepInEx"],
    isPartOf: "CUCoreLib Docs"
  };

  return `<script type="application/ld+json">${escapeScriptJson(JSON.stringify(payload))}</script>`;
}

function escapeHtml(value) {
  return String(value)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

function escapeScriptJson(value) {
  return String(value).replace(/</g, "\\u003c");
}

import { defineConfig } from "vite";
import { readdirSync, statSync } from "node:fs";
import path from "node:path";

const projectRoot = ".";
const pagesDir = path.resolve(projectRoot, "pages");
const htmlInputs = { index: path.resolve(projectRoot, "index.html") };

for (const entry of readdirSync(pagesDir, { withFileTypes: true })) {
  if (!entry.isDirectory()) {
    continue;
  }

  for (const child of readdirSync(path.resolve(pagesDir, entry.name), { withFileTypes: true })) {
    if (!child.isDirectory()) {
      continue;
    }

    const htmlPath = path.resolve(pagesDir, entry.name, child.name, "index.html");
    if (!statSync(htmlPath).isFile()) {
      continue;
    }

    htmlInputs[`${entry.name}/${child.name}/index`] = htmlPath;
  }
}

export default defineConfig({
  root: ".",
  plugins: [
    {
      name: "docs-route-rewrite",
      configureServer(server) {
        server.middlewares.use((req, _res, next) => {
          if (!req.url) {
            next();
            return;
          }

          const url = new URL(req.url, "http://127.0.0.1");
          const match = url.pathname.match(/^\/docs\/([^/]+)\/?$/);
          if (!match) {
            next();
            return;
          }

          req.url = `/pages/docs/${match[1]}/index.html${url.search}`;
          next();
        });
      }
    }
  ],
  build: {
    rollupOptions: {
      input: htmlInputs
    }
  },
  server: {
    host: "127.0.0.1",
    port: 5174,
    strictPort: false
  },
  preview: {
    host: "127.0.0.1",
    port: 4174,
    strictPort: false
  }
});

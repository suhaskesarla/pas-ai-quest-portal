import http from "node:http";
import https from "node:https";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.dirname(fileURLToPath(import.meta.url));
const port = Number(process.env.PORT || 8080);
const apiOrigin = process.env.PAS_API_ORIGIN ? new URL(process.env.PAS_API_ORIGIN) : null;
if (apiOrigin && (apiOrigin.pathname !== "/" || apiOrigin.search || apiOrigin.hash ||
    (apiOrigin.protocol !== "https:" && !(apiOrigin.protocol === "http:" && ["localhost", "127.0.0.1"].includes(apiOrigin.hostname))))) {
  throw new Error("PAS_API_ORIGIN must be an HTTPS origin (or local HTTP for testing).");
}

const contentTypes = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".mjs": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".svg": "image/svg+xml",
  ".png": "image/png",
  ".jpg": "image/jpeg",
  ".jpeg": "image/jpeg",
  ".webp": "image/webp",
  ".ico": "image/x-icon",
  ".woff": "font/woff",
  ".woff2": "font/woff2",
};

function safePath(urlPath) {
  const pathname = decodeURIComponent(new URL(urlPath, "http://localhost").pathname);
  const candidate = path.resolve(root, `.${pathname}`);
  return candidate === root || candidate.startsWith(`${root}${path.sep}`) ? candidate : null;
}

const server = http.createServer((req, res) => {
  if (req.url === "/api" || req.url?.startsWith("/api/") || req.url?.startsWith("/api?")) {
    if (!apiOrigin) {
      res.writeHead(503).end("API origin is not configured");
      return;
    }
    const target = new URL(req.url, apiOrigin);
    const headers = { ...req.headers, host: target.host, "x-forwarded-host": req.headers.host, "x-forwarded-proto": "https" };
    delete headers.connection;
    const transport = target.protocol === "https:" ? https : http;
    const upstream = transport.request(target, { method: req.method, headers }, (response) => {
      const responseHeaders = { ...response.headers };
      delete responseHeaders.connection;
      delete responseHeaders["transfer-encoding"];
      res.writeHead(response.statusCode || 502, responseHeaders);
      response.pipe(res);
    });
    upstream.on("error", () => {
      if (!res.headersSent) res.writeHead(502).end("API unavailable");
      else res.destroy();
    });
    req.pipe(upstream);
    return;
  }
  const requested = req.url === "/" ? "/index.html" : req.url;
  let candidate;
  try { candidate = safePath(requested); }
  catch { res.writeHead(400).end("Invalid path"); return; }
  if (!candidate) { res.writeHead(403).end("Forbidden"); return; }

  const serve = (file) => {
    const ext = path.extname(file).toLowerCase();
    res.statusCode = 200;
    res.setHeader("Content-Type", contentTypes[ext] || "application/octet-stream");
    res.setHeader("X-Content-Type-Options", "nosniff");

    // Vite hashed assets can be cached aggressively; index.html should not.
    if (path.basename(file) === "index.html") {
      res.setHeader("Cache-Control", "no-cache");
    } else if (file.includes(`${path.sep}assets${path.sep}`)) {
      res.setHeader("Cache-Control", "public, max-age=31536000, immutable");
    }

    fs.createReadStream(file).pipe(res);
  };

  fs.stat(candidate, (err, stat) => {
    if (!err && stat.isFile()) {
      serve(candidate);
      return;
    }

    // SPA fallback for React Router / deep links.
    const index = path.join(root, "index.html");
    fs.stat(index, (indexErr, indexStat) => {
      if (indexErr || !indexStat.isFile()) {
        res.statusCode = 404;
        res.end("Not found");
        return;
      }
      serve(index);
    });
  });
});

server.listen(port, "0.0.0.0", () => {
  console.log(`PAS AI Quest frontend listening on port ${port}`);
});

import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { cpSync, existsSync } from "node:fs";
import { resolve } from "node:path";
export default defineConfig({
  base: "./",
  plugins: [
    react(),
    {
      name: "local-pdf-resources",
      configureServer(server) {
        server.middlewares.use("/pdfjs", (req, res, next) => {
          const path = resolve(
            "node_modules/pdfjs-dist",
            (req.url ?? "").split("?")[0].replace(/^\//, ""),
          );
          const base = resolve("node_modules/pdfjs-dist");
          if (!path.startsWith(base + "\\") && !path.startsWith(base + "/"))
            return next();
          if (!existsSync(path)) return next();
          import("node:fs").then((fs) => {
            res.setHeader(
              "Content-Type",
              path.endsWith(".wasm")
                ? "application/wasm"
                : "application/octet-stream",
            );
            fs.createReadStream(path).on("error", next).pipe(res);
          });
        });
      },
      closeBundle() {
        for (const dir of ["cmaps", "standard_fonts", "wasm", "iccs"])
          if (existsSync(`node_modules/pdfjs-dist/${dir}`))
            cpSync(`node_modules/pdfjs-dist/${dir}`, `dist/pdfjs/${dir}`, {
              recursive: true,
            });
      },
    },
  ],
  server: { fs: { allow: [".."] } },
  build: {
    rollupOptions: {
      output: {
        manualChunks: {
          "pdf-tools": ["pdf-lib"],
          "pdf-renderer": ["pdfjs-dist"],
        },
      },
    },
  },
});

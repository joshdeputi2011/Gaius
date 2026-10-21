# Gaius web

React + TypeScript + Vite. Run `npm ci`, then `npm run dev`. Build with `npm run build`. The generated `dist` is a static application. All document persistence uses IndexedDB on the user's browser origin. No document server or cloud storage is configured.

Run `npm test` for engine/format tests and `npm run test:e2e` for headless Chrome workflows. `CHROME_PATH` overrides the browser executable in `playwright.config.ts`. See the root README for supported formats and compatibility limits.

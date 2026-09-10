import path from "node:path";

import { defineConfig } from "vitest/config";

/*
 * `.mts` rather than `.ts`: this app is CommonJS by default, and Vite's newer
 * native config loader refuses ESM syntax in a file it loads as CommonJS. The
 * extension settles it without adding "type": "module" to the whole package,
 * which would change how Next loads its own config too.
 */

export default defineConfig({
  resolve: {
    // Mirrors the `@/*` path alias in tsconfig.json. Defined by hand rather than
    // pulling in vite-tsconfig-paths: one line against one more dependency.
    alias: {
      "@": path.resolve(import.meta.dirname, "."),

      // See the stub for why. Without this, importing any module that carries
      // the server-only marker fails before a single assertion runs.
      "server-only": path.resolve(import.meta.dirname, "test/server-only-stub.ts"),
    },
  },
  test: {
    // Everything under test here is server-side: session sealing, OAuth helpers,
    // API error mapping. None of it touches a DOM, so jsdom would be dead weight.
    environment: "node",
    include: ["lib/**/*.test.ts"],
    env: {
      // The sealing helpers read this. A fixed value keeps sealed output
      // reproducible within a run.
      SESSION_COOKIE_PASSWORD: "test-session-cookie-password-32-chars-plus",
    },
  },
});

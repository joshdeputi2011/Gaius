import { it, expect } from "vitest";
import { existsSync } from "node:fs";
import { spawnSync } from "node:child_process";
import cases from "../../Engine/schema/formula-cases.json";
import { Workbook } from "../../Engine/typescript/formula";
it("matches the shared calculation specification", () => {
  for (const sample of cases) {
    const workbook = new Workbook({
      ...sample.cells,
      Z999: sample.expression,
    } as Record<string, string>);
    expect(workbook.display("Z999"), sample.expression).toBe(sample.expected);
  }
});
const executable = new URL(
  "../../Engine/native/engine-cli.exe",
  import.meta.url,
).pathname.replace(/^\/(\w:)/, "$1");
it.skipIf(!existsSync(executable))(
  "native engine matches every shared calculation case",
  () => {
    const result = spawnSync(executable, [], {
      input: cases
        .map((c) => `${c.expression}\n${JSON.stringify(c.cells ?? {})}\n`)
        .join(""),
      encoding: "utf8",
    });
    expect(result.status, result.stderr).toBe(0);
    expect(result.stdout.trim().split(/\r?\n/)).toEqual(
      cases.map((c) => c.expected),
    );
  },
);

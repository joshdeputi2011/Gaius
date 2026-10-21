import { it, expect } from "vitest";
import { writeZip, readZip } from "../../Engine/typescript/zip";
it("round trips ZIP entries with UTF-8 names and content", async () => {
  const entries = {
    "[Content_Types].xml": "<Types/>",
    "word/document.xml": "Hello 世界",
  };
  const bytes = writeZip(entries);
  const result = await readZip(bytes);
  expect(
    Object.fromEntries(
      Object.entries(result).map(([k, v]) => [k, new TextDecoder().decode(v)]),
    ),
  ).toEqual(entries);
});
it("rejects truncated ZIP files and corrupted data", async () => {
  await expect(readZip(new Uint8Array([1, 2, 3]))).rejects.toThrow();
  const zip = writeZip({ a: "hello" });
  zip[31] ^= 1;
  await expect(readZip(zip)).rejects.toThrow("integrity");
});

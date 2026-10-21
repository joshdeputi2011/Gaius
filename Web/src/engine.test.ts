import { describe, it, expect } from "vitest";
import { Workbook } from "../../Engine/typescript/formula";
import {
  createFile,
  parseFile,
  parseCSV,
  exportCSV,
  columnName,
} from "../../Engine/typescript/model";
describe("calculation engine", () => {
  it("calculates references, ranges, precedence and lazy IF", () => {
    const w = new Workbook({
      A1: "12",
      A2: "8",
      B1: "=SUM(A1:A2)*2",
      B2: "=IF(B1=40,ROUND(10/3,2),1/0)",
      C1: "=-2^2",
      C2: "=2^3^2",
    });
    expect(w.display("B1")).toBe("40");
    expect(w.display("B2")).toBe("3.33");
    expect(w.display("C1")).toBe("-4");
    expect(w.display("C2")).toBe("512");
  });
  it("reports errors without executing arbitrary code", () => {
    const w = new Workbook({
      A1: "=B1",
      B1: "=A1",
      C1: "=1/0",
      D1: "=alert(1)",
      E1: "=SUM(A1:ZZ999999)",
    });
    expect(w.display("A1")).toBe("#CYCLE!");
    expect(w.display("C1")).toBe("#DIV/0!");
    expect(w.display("D1")).toBe("#NAME?");
    expect(w.display("E1")).toBe("#SIZE!");
  });
});
describe("document interchange", () => {
  it("round trips each supported document type", () => {
    for (const kind of [
      "document",
      "spreadsheet",
      "presentation",
      "image",
      "pdf",
    ] as const) {
      const file = createFile(kind);
      expect(parseFile(JSON.stringify(file))).toEqual(file);
    }
  });
  it("handles multiline CSV, escaped quotes and formula-safe export", () => {
    expect(parseCSV('"a,b","say ""hi"""\r\n"two\nlines",3')).toEqual([
      ["a,b", 'say "hi"'],
      ["two\nlines", "3"],
    ]);
    expect(columnName(26)).toBe("AA");
    expect(
      exportCSV({ A1: '=HYPERLINK("bad")' }, () => '=HYPERLINK("bad")'),
    ).toContain("'=HYPERLINK");
  });
  it("rejects malformed files and executable image payloads", () => {
    expect(() => parseFile("{}")).toThrow();
    const file = createFile("image");
    file.image.dataUrl = 'data:image/svg+xml,<svg onload="alert(1)"/>';
    expect(() => parseFile(JSON.stringify(file))).toThrow();
  });
});

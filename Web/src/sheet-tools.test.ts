import { describe, it, expect } from "vitest";
import {
  rangeAddresses,
  sortSheet,
  transposeRange,
} from "../../Engine/typescript/sheet-tools";
import { createFile, parseFile } from "../../Engine/typescript/model";
describe("workbook operations", () => {
  it("rejects invalid ranges and limits expansion", () => {
    expect(rangeAddresses("B2:A1")).toEqual(["A1", "B1", "A2", "B2"]);
    expect(() => rangeAddresses("A0:B1")).toThrow();
    expect(() => rangeAddresses("A1:ZZZ99999")).toThrow();
  });
  it("sorts records with styles while preserving the header", () => {
    const sheet = {
      cells: { A1: "Label", B1: "Value", A2: "B", B2: "12", A3: "A", B3: "4" },
      rows: 100,
      columns: 26,
      styles: { A2: { bold: true } },
    };
    const sorted = sortSheet(sheet, "A");
    expect(sorted.cells).toMatchObject({
      A1: "Label",
      A2: "A",
      B2: "4",
      A3: "B",
      B3: "12",
    });
    expect(sorted.styles?.A3).toEqual({ bold: true });
    expect(sheet.cells.A2).toBe("B");
  });
  it("transposes without corrupting overlapping source cells", () => {
    const sheet = {
      cells: { A1: "one", B1: "two", A2: "three", B2: "four" },
      rows: 100,
      columns: 26,
    };
    expect(transposeRange(sheet, "A1:B2").cells).toEqual({
      A1: "one",
      A2: "two",
      B1: "three",
      B2: "four",
    });
  });
  it("restores the selected worksheet and keeps slide objects portable", () => {
    const f = createFile("spreadsheet");
    f.worksheets = [
      { cells: { A1: "first" }, rows: 100, columns: 26 },
      { cells: { A1: "second" }, rows: 100, columns: 26 },
    ];
    f.activeSheet = 1;
    expect(parseFile(JSON.stringify(f)).sheet.cells.A1).toBe("second");
    const s = createFile("presentation");
    s.slides[0].objects = [
      {
        id: "object",
        kind: "image",
        dataUrl: "https://example.com/tracking.png",
        x: 0,
        y: 0,
        width: 20,
        height: 20,
        color: "#185abd",
      },
    ];
    expect(() => parseFile(JSON.stringify(s))).toThrow();
  });
});

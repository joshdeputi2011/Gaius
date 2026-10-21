import { columnIndex, columnName, type Worksheet } from "./model";
export function rangeAddresses(range: string): string[] {
  const m = /^\$?([A-Z]+)\$?([1-9]\d*)(?::\$?([A-Z]+)\$?([1-9]\d*))?$/i.exec(
    range.trim(),
  );
  if (!m) throw new Error("Use a range such as A1:B12.");
  const c1 = columnIndex(m[1].toUpperCase()),
    c2 = columnIndex((m[3] ?? m[1]).toUpperCase()),
    r1 = +m[2],
    r2 = +(m[4] ?? m[2]);
  if (
    (Math.abs(c2 - c1) + 1) * (Math.abs(r2 - r1) + 1) > 100000 ||
    Math.max(r1, r2) > 10000 ||
    Math.max(c1, c2) > 255
  )
    throw new Error("Choose a smaller range (up to 100,000 cells).");
  const out: string[] = [];
  for (let r = Math.min(r1, r2); r <= Math.max(r1, r2); r++)
    for (let c = Math.min(c1, c2); c <= Math.max(c1, c2); c++)
      out.push(`${columnName(c)}${r}`);
  return out;
}
export function sortSheet(
  sheet: Worksheet,
  column: string,
  descending = false,
): Worksheet {
  const refs = Object.keys(sheet.cells),
    last = Math.max(1, ...refs.map((a) => +(a.match(/\d+/)?.[0] ?? 1)));
  const rows = Array.from({ length: Math.max(0, last - 1) }, (_, i) => i + 2);
  rows.sort(
    (a, b) =>
      (sheet.cells[`${column}${a}`] ?? "").localeCompare(
        sheet.cells[`${column}${b}`] ?? "",
        undefined,
        { numeric: true, sensitivity: "base" },
      ) * (descending ? -1 : 1),
  );
  const cells: Record<string, string> = {},
    styles: NonNullable<Worksheet["styles"]> = {};
  for (let c = 0; c < sheet.columns; c++) {
    const col = columnName(c);
    if (sheet.cells[`${col}1`] !== undefined)
      cells[`${col}1`] = sheet.cells[`${col}1`];
    if (sheet.styles?.[`${col}1`]) styles[`${col}1`] = sheet.styles[`${col}1`];
    rows.forEach((source, i) => {
      const old = `${col}${source}`,
        target = `${col}${i + 2}`;
      if (sheet.cells[old] !== undefined) cells[target] = sheet.cells[old];
      if (sheet.styles?.[old]) styles[target] = sheet.styles[old];
    });
  }
  return { ...sheet, cells, styles };
}
export function transposeRange(sheet: Worksheet, range: string): Worksheet {
  const refs = rangeAddresses(range),
    first = refs[0],
    startCol = columnIndex(first.match(/[A-Z]+/)![0]),
    startRow = +first.match(/\d+/)![0],
    cells = { ...sheet.cells };
  for (const ref of refs) delete cells[ref];
  for (const ref of refs) {
    const c = columnIndex(ref.match(/[A-Z]+/)![0]),
      r = +ref.match(/\d+/)![0];
    if (sheet.cells[ref] !== undefined)
      cells[
        `${columnName(startCol + r - startRow)}${startRow + c - startCol}`
      ] = sheet.cells[ref];
  }
  return { ...sheet, cells };
}

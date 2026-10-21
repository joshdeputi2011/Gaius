import { type OfficeFile, type CellStyle } from "./model";
import { Workbook } from "./formula";
import { writeZip } from "./zip";
const x = (s: string) =>
  s
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
export function exportXlsx(file: OfficeFile) {
  const files: Record<string, string> = {},
    sheets = file.worksheets?.length
      ? file.worksheets
      : [{ ...file.sheet, name: "Sheet 1" }],
    ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main",
    office =
      "http://schemas.openxmlformats.org/officeDocument/2006/relationships",
    styles: CellStyle[] = [{}],
    styleMap = new Map<string, number>([["{}", 0]]);
  const rels = (body: string) =>
    `<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">${body}</Relationships>`;
  const rel = (id: string, type: string, target: string) =>
    `<Relationship Id="${id}" Type="${office}/${type}" Target="${target}"/>`;
  files["_rels/.rels"] = rels(rel("rId1", "officeDocument", "xl/workbook.xml"));
  files["xl/workbook.xml"] =
    `<workbook xmlns="${ns}" xmlns:r="${office}"><bookViews><workbookView activeTab="${file.activeSheet ?? 0}"/></bookViews><sheets>${sheets.map((s, i) => `<sheet name="${x((s.name ?? `Sheet ${i + 1}`).replace(/[\\/?*\[\]:]/g, " ").slice(0, 31) || `Sheet ${i + 1}`)}" sheetId="${i + 1}" r:id="rId${i + 1}"/>`).join("")}</sheets><calcPr fullCalcOnLoad="1"/></workbook>`;
  files["xl/_rels/workbook.xml.rels"] = rels(
    sheets
      .map((_, i) =>
        rel(`rId${i + 1}`, "worksheet", `worksheets/sheet${i + 1}.xml`),
      )
      .join("") + rel("styles", "styles", "styles.xml"),
  );
  sheets.forEach((sheet, i) => {
    const rows = new Map<number, string[]>(),
      wb = new Workbook(sheet.cells),
      refs = [
        ...new Set([
          ...Object.keys(sheet.cells),
          ...Object.keys(sheet.styles ?? {}),
        ]),
      ];
    refs.sort(
      (a, b) =>
        a.length - b.length || a.localeCompare(b, undefined, { numeric: true }),
    );
    for (const address of refs) {
      if (!/^[A-Z]+[1-9]\d*$/.test(address)) continue;
      const value = sheet.cells[address] ?? "",
        style = sheet.styles?.[address] ?? {},
        key = JSON.stringify(style);
      let id = styleMap.get(key);
      if (id === undefined) {
        id = styles.length;
        styleMap.set(key, id);
        styles.push(style);
      }
      let cell: string;
      if (value.startsWith("=")) {
        const cached = wb.display(address),
          error = cached.startsWith("#"),
          bool = cached === "TRUE" || cached === "FALSE",
          isNumber = cached !== "" && Number.isFinite(Number(cached));
        cell = `<c r="${address}" s="${id}"${error ? ' t="e"' : bool ? ' t="b"' : isNumber ? "" : ' t="str"'}><f>${x(value.slice(1))}</f><v>${x(bool ? (cached === "TRUE" ? "1" : "0") : cached)}</v></c>`;
      } else if (value.trim() && Number.isFinite(Number(value)))
        cell = `<c r="${address}" s="${id}"><v>${Number(value)}</v></c>`;
      else
        cell = `<c r="${address}" s="${id}" t="inlineStr"><is><t xml:space="preserve">${x(value)}</t></is></c>`;
      const row = +address.match(/\d+/)![0];
      rows.set(row, [...(rows.get(row) ?? []), cell]);
    }
    files[`xl/worksheets/sheet${i + 1}.xml`] =
      `<worksheet xmlns="${ns}"><sheetViews><sheetView workbookViewId="0">${sheet.frozen ? '<pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/>' : ""}</sheetView></sheetViews><sheetData>${[
        ...rows,
      ]
        .sort(([a], [b]) => a - b)
        .map(([r, cells]) => `<row r="${r}">${cells.join("")}</row>`)
        .join("")}</sheetData></worksheet>`;
  });
  const color = (s: string | undefined) =>
    s && /^#[a-f0-9]{6}$/i.test(s) ? s.slice(1).toUpperCase() : "242424";
  files["xl/styles.xml"] =
    `<styleSheet xmlns="${ns}"><numFmts count="1"><numFmt numFmtId="164" formatCode="&quot;₹&quot;#,##0.00"/></numFmts><fonts count="${styles.length}">${styles.map((s) => `<font><sz val="11"/><name val="Calibri"/>${s.bold ? "<b/>" : ""}${s.italic ? "<i/>" : ""}<color rgb="FF${color(s.color)}"/></font>`).join("")}</fonts><fills count="${styles.length + 2}"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill>${styles.map((s) => `<fill><patternFill patternType="${s.background ? "solid" : "none"}">${s.background ? `<fgColor rgb="FF${color(s.background)}"/><bgColor indexed="64"/>` : ""}</patternFill></fill>`).join("")}</fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="${styles.length}">${styles.map((s, i) => `<xf numFmtId="${s.format === "number" ? 2 : s.format === "currency" ? 164 : s.format === "percent" ? 10 : 0}" fontId="${i}" fillId="${s.background ? i + 2 : 0}" borderId="0" xfId="0" applyFont="1" applyFill="1" applyNumberFormat="1"${s.align ? ' applyAlignment="1"' : ""}>${s.align ? `<alignment horizontal="${s.align}"/>` : ""}</xf>`).join("")}</cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>`;
  files["[Content_Types].xml"] =
    `<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>${sheets.map((_, i) => `<Override PartName="/xl/worksheets/sheet${i + 1}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>`).join("")}</Types>`;
  return { bytes: writeZip(files), extension: "xlsx" };
}

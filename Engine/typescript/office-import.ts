import {
  createFile,
  columnIndex,
  type CellStyle,
  type Worksheet,
} from "./model";
import { htmlToDelta } from "./rich-content";
const w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main",
  s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main",
  a = "http://schemas.openxmlformats.org/drawingml/2006/main",
  r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
const escape = (v: string) =>
  v
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
const nodes = (el: Document | Element, ns: string, name: string) => [
  ...el.getElementsByTagNameNS(ns, name),
];
export function partPath(base: string, target: string) {
  const out: string[] = [];
  for (const segment of (target.startsWith("/")
    ? target
    : `${base}/${target}`
  ).split("/")) {
    if (!segment || segment === ".") continue;
    if (segment === "..") out.pop();
    else out.push(segment);
  }
  return out.join("/");
}
function parse(files: Record<string, Uint8Array>, path: string) {
  if (!files[path]) throw new Error(`Missing Office file part: ${path}`);
  const doc = new DOMParser().parseFromString(
    new TextDecoder().decode(files[path]),
    "application/xml",
  );
  if (doc.querySelector("parsererror") || doc.doctype)
    throw new Error("Invalid Office XML.");
  return doc;
}
function dataURL(bytes: Uint8Array, path: string) {
  const ext = path.split(".").pop()?.toLowerCase(),
    mime = ext === "jpg" || ext === "jpeg" ? "jpeg" : ext;
  if (!["png", "jpeg", "gif", "webp", "bmp"].includes(mime ?? "")) return "";
  let binary = "";
  for (let p = 0; p < bytes.length; p += 8192)
    binary += String.fromCharCode(...bytes.subarray(p, p + 8192));
  return `data:image/${mime};base64,${btoa(binary)}`;
}
export function importDocx(files: Record<string, Uint8Array>, title: string) {
  const file = createFile("document", title),
    doc = parse(files, "word/document.xml"),
    relationships = files["word/_rels/document.xml.rels"]
      ? [
          ...parse(files, "word/_rels/document.xml.rels").getElementsByTagName(
            "Relationship",
          ),
        ]
      : [];
  const image = (run: Element) =>
    nodes(run, a, "blip")
      .map((blip) => {
        const id = blip.getAttributeNS(r, "embed"),
          rel = relationships.find(
            (el) =>
              el.getAttribute("Id") === id &&
              el.getAttribute("TargetMode") !== "External",
          ),
          path = rel ? partPath("word", rel.getAttribute("Target") ?? "") : "";
        if (!path || !files[path]) return "";
        const url = dataURL(files[path], path);
        const wp =
            "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing",
          extent = nodes(run, wp, "extent")[0],
          anchor = nodes(run, wp, "anchor")[0];
        const wrap = anchor
          ? nodes(anchor, wp, "wrapSquare").length
            ? nodes(anchor, wp, "align")[0]?.textContent === "right"
              ? "square-right"
              : "square-left"
            : nodes(anchor, wp, "wrapTopAndBottom").length
              ? "top-bottom"
              : anchor.getAttribute("behindDoc") === "1"
                ? "behind"
                : "front"
          : "inline";
        const width = Number(extent?.getAttribute("cx") ?? 2476500) / 9525,
          height = Number(extent?.getAttribute("cy") ?? 1428750) / 9525;
        return url
          ? `<img src="${url}" alt="Embedded image" data-wrap="${wrap}" style="max-width:100%;object-fit:contain;width:${width}px;height:${height}px;display:${wrap === "inline" ? "inline-block" : "block"};float:${wrap === "square-left" ? "left" : wrap === "square-right" ? "right" : "none"}">`
          : "";
      })
      .join("");
  function run(el: Element) {
    let text = nodes(el, w, "t")
      .map((t) => escape(t.textContent ?? ""))
      .join("");
    if (
      nodes(el, w, "b").some(
        (v) => !["0", "false"].includes(v.getAttributeNS(w, "val") ?? ""),
      )
    )
      text = `<strong>${text}</strong>`;
    if (nodes(el, w, "i").length) text = `<em>${text}</em>`;
    if (nodes(el, w, "u").length) text = `<u>${text}</u>`;
    const size = nodes(el, w, "sz")[0]?.getAttributeNS(w, "val"),
      color = nodes(el, w, "color")[0]?.getAttributeNS(w, "val"),
      font = nodes(el, w, "rFonts")[0]?.getAttributeNS(w, "ascii"),
      style = [
        size && Number.isFinite(+size) ? `font-size:${+size / 2}pt` : "",
        color && /^[a-f0-9]{6}$/i.test(color) ? `color:#${color}` : "",
        font ? `font-family:${escape(font)}` : "",
      ]
        .filter(Boolean)
        .join(";");
    return `${style ? `<span style="${style}">${text}</span>` : text}${nodes(
      el,
      w,
      "br",
    )
      .map(() => "<br>")
      .join("")}${image(el)}`;
  }
  function paragraph(p: Element) {
    const heading = nodes(p, w, "pStyle")[0]
        ?.getAttributeNS(w, "val")
        ?.match(/^Heading([1-6])$/i),
      tag = heading ? `h${heading[1]}` : "p",
      align = nodes(p, w, "jc")[0]?.getAttributeNS(w, "val");
    return `<${tag}${align && ["left", "right", "center", "both"].includes(align) ? ` style="text-align:${align === "both" ? "justify" : align}"` : ""}>${nodes(p, w, "r").map(run).join("")}</${tag}>`;
  }
  function block(el: Element): string {
    if (el.localName === "tbl")
      return `<table><tbody>${[...el.children]
        .filter((e) => e.localName === "tr")
        .map(
          (tr) =>
            `<tr>${[...tr.children]
              .filter((e) => e.localName === "tc")
              .map(
                (tc) =>
                  `<td>${[...tc.children]
                    .filter((e) => e.localName === "p")
                    .map(paragraph)
                    .join("")}</td>`,
              )
              .join("")}</tr>`,
        )
        .join("")}</tbody></table>`;
    return el.localName === "p" ? paragraph(el) : "";
  }
  file.content.html = [...(nodes(doc, w, "body")[0]?.children ?? [])]
    .map(block)
    .join("");
  file.content.text = nodes(doc, w, "p")
    .map((p) =>
      nodes(p, w, "t")
        .map((t) => t.textContent ?? "")
        .join(""),
    )
    .join("\n");
  file.content.delta = htmlToDelta(file.content.html);
  const page = nodes(doc, w, "pgSz")[0];
  if (page)
    file.layout = {
      orientation:
        page.getAttributeNS(w, "orient") === "landscape"
          ? "landscape"
          : "portrait",
      columns: Number(nodes(doc, w, "cols")[0]?.getAttributeNS(w, "num")) || 1,
      pageSize:
        Number(page.getAttributeNS(w, "h")) === 20160 ||
        Number(page.getAttributeNS(w, "w")) === 20160
          ? "legal"
          : Number(page.getAttributeNS(w, "w")) === 12240 ||
              Number(page.getAttributeNS(w, "h")) === 12240
            ? "letter"
            : "a4",
      customMargins: Object.fromEntries(
        ["top", "right", "bottom", "left"].map((side) => [
          side,
          (Number(nodes(doc, w, "pgMar")[0]?.getAttributeNS(w, side) ?? 1440) *
            25.4) /
            1440,
        ]),
      ) as { top: number; right: number; bottom: number; left: number },
      columnGap:
        (Number(nodes(doc, w, "cols")[0]?.getAttributeNS(w, "space") ?? 567) *
          25.4) /
        1440,
    };
  return file;
}
export function importXlsx(files: Record<string, Uint8Array>, title: string) {
  const file = createFile("spreadsheet", title),
    book = parse(files, "xl/workbook.xml"),
    rels = [
      ...parse(files, "xl/_rels/workbook.xml.rels").getElementsByTagName(
        "Relationship",
      ),
    ],
    shared = files["xl/sharedStrings.xml"]
      ? nodes(parse(files, "xl/sharedStrings.xml"), s, "si").map((si) =>
          nodes(si, s, "t")
            .map((t) => t.textContent ?? "")
            .join(""),
        )
      : [];
  const stylePath = rels
      .find((el) => el.getAttribute("Type") === `${r}/styles`)
      ?.getAttribute("Target"),
    styleDoc = stylePath ? parse(files, partPath("xl", stylePath)) : null,
    styles: CellStyle[] = [];
  if (styleDoc) {
    const fonts = nodes(styleDoc, s, "fonts")[0]?.children ?? [],
      fills = nodes(styleDoc, s, "fills")[0]?.children ?? [],
      xfs = nodes(styleDoc, s, "cellXfs")[0]?.children ?? [];
    for (const xf of xfs) {
      const font = fonts[Number(xf.getAttribute("fontId")) || 0],
        fill = fills[Number(xf.getAttribute("fillId")) || 0],
        color = font ? nodes(font, s, "color")[0]?.getAttribute("rgb") : null,
        bg = fill ? nodes(fill, s, "fgColor")[0]?.getAttribute("rgb") : null,
        num = Number(xf.getAttribute("numFmtId")),
        align = nodes(xf, s, "alignment")[0]?.getAttribute("horizontal");
      styles.push({
        ...(font && nodes(font, s, "b").length ? { bold: true } : {}),
        ...(font && nodes(font, s, "i").length ? { italic: true } : {}),
        ...(color && /^[a-f0-9]{8}$/i.test(color)
          ? { color: `#${color.slice(2)}` }
          : {}),
        ...(bg && /^[a-f0-9]{8}$/i.test(bg)
          ? { background: `#${bg.slice(2)}` }
          : {}),
        ...(num === 2
          ? { format: "number" as const }
          : num === 10
            ? { format: "percent" as const }
            : num === 164
              ? { format: "currency" as const }
              : {}),
        ...(align && ["left", "right", "center"].includes(align)
          ? { align: align as CellStyle["align"] }
          : {}),
      });
    }
  }
  const worksheets: Worksheet[] = [];
  for (const sheetElement of nodes(book, s, "sheet")) {
    const id = sheetElement.getAttributeNS(r, "id"),
      rel = rels.find(
        (el) =>
          el.getAttribute("Id") === id &&
          el.getAttribute("TargetMode") !== "External",
      ),
      target = rel?.getAttribute("Target");
    if (!target) throw new Error("Worksheet relationship is missing.");
    const doc = parse(files, partPath("xl", target)),
      sheet: Worksheet = {
        name:
          sheetElement.getAttribute("name") ?? `Sheet ${worksheets.length + 1}`,
        cells: {},
        rows: 100,
        columns: 26,
        styles: {},
        frozen: nodes(doc, s, "pane").some(
          (p) => p.getAttribute("state") === "frozen",
        ),
      };
    for (const cell of nodes(doc, s, "c")) {
      const address = cell.getAttribute("r") ?? "";
      if (!/^[A-Z]+[1-9]\d*$/.test(address)) continue;
      const row = +address.match(/\d+/)![0],
        col = columnIndex(address.match(/[A-Z]+/)![0]);
      if (row > 10000 || col >= 100)
        throw new Error(
          "Workbook limit: 10,000 rows and 100 columns per sheet.",
        );
      const formula = nodes(cell, s, "f")[0]?.textContent,
        raw = nodes(cell, s, "v")[0]?.textContent ?? "",
        type = cell.getAttribute("t");
      sheet.cells[address] = formula
        ? `=${formula}`
        : type === "s"
          ? (shared[Number(raw)] ?? "")
          : type === "inlineStr"
            ? nodes(cell, s, "t")
                .map((t) => t.textContent ?? "")
                .join("")
            : type === "b"
              ? raw === "1"
                ? "TRUE"
                : "FALSE"
              : raw;
      const style = styles[Number(cell.getAttribute("s")) || 0];
      if (style && Object.keys(style).length) sheet.styles![address] = style;
      sheet.rows = Math.max(sheet.rows, row);
      sheet.columns = Math.max(sheet.columns, col + 1);
    }
    worksheets.push(sheet);
    if (worksheets.length > 100)
      throw new Error("Workbook limit: 100 worksheets.");
  }
  if (!worksheets.length) throw new Error("Workbook has no worksheets.");
  file.worksheets = worksheets;
  file.activeSheet = Math.min(
    worksheets.length - 1,
    Math.max(
      0,
      Number(nodes(book, s, "workbookView")[0]?.getAttribute("activeTab")) || 0,
    ),
  );
  file.sheet = worksheets[file.activeSheet];
  return file;
}

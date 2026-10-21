import { createFile, type OfficeFile } from "./model";
import { Workbook } from "./formula";
import { readZip, writeZip } from "./zip";
const xml = (s: string) =>
  s
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
const declaration = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>';
const relNS = "http://schemas.openxmlformats.org/package/2006/relationships",
  officeNS =
    "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
const wNS = "http://schemas.openxmlformats.org/wordprocessingml/2006/main",
  sNS = "http://schemas.openxmlformats.org/spreadsheetml/2006/main",
  pNS = "http://schemas.openxmlformats.org/presentationml/2006/main",
  aNS = "http://schemas.openxmlformats.org/drawingml/2006/main";
function relationships(items: [string, string, string][]) {
  return `${declaration}<Relationships xmlns="${relNS}">${items.map(([id, type, target]) => `<Relationship Id="${id}" Type="${officeNS}/${type}" Target="${target}"/>`).join("")}</Relationships>`;
}
function contentTypes(items: [string, string][]) {
  return `${declaration}<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/>${items.map(([name, type]) => `<Override PartName="/${name}" ContentType="application/vnd.openxmlformats-officedocument.${type}+xml"/>`).join("")}</Types>`;
}
export function exportOffice(file: OfficeFile): {
  bytes: Uint8Array;
  extension: string;
} {
  const files: Record<string, string> = {};
  if (file.kind === "document") {
    files["_rels/.rels"] = relationships([
      ["rId1", "officeDocument", "word/document.xml"],
    ]);
    files["[Content_Types].xml"] = contentTypes([
      ["word/document.xml", "wordprocessingml.document.main"],
    ]);
    const parsed = new DOMParser().parseFromString(
      file.content.html,
      "text/html",
    );
    const paragraphs: string[] = [];
    const run = (
      node: Node,
      inherited: { bold?: boolean; italic?: boolean; underline?: boolean } = {},
    ): string => {
      if (node.nodeType === Node.TEXT_NODE)
        return (
          node.textContent
            ?.split("\n")
            .map(
              (text, i) =>
                `${i ? "<w:r><w:br/></w:r>" : ""}<w:r><w:rPr>${inherited.bold ? "<w:b/>" : ""}${inherited.italic ? "<w:i/>" : ""}${inherited.underline ? '<w:u w:val="single"/>' : ""}</w:rPr><w:t xml:space="preserve">${xml(text)}</w:t></w:r>`,
            )
            .join("") ?? ""
        );
      if (!(node instanceof Element)) return "";
      if (node.tagName === "BR") return "<w:r><w:br/></w:r>";
      return [...node.childNodes]
        .map((child) =>
          run(child, {
            bold: inherited.bold || ["B", "STRONG"].includes(node.tagName),
            italic: inherited.italic || ["I", "EM"].includes(node.tagName),
            underline: inherited.underline || node.tagName === "U",
          }),
        )
        .join("");
    };
    for (const child of parsed.body.childNodes)
      paragraphs.push(`<w:p>${run(child)}</w:p>`);
    files["word/document.xml"] =
      `${declaration}<w:document xmlns:w="${wNS}"><w:body>${paragraphs.join("")}<w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/></w:sectPr></w:body></w:document>`;
    return { bytes: writeZip(files), extension: "docx" };
  }
  if (file.kind === "spreadsheet") {
    files["_rels/.rels"] = relationships([
      ["rId1", "officeDocument", "xl/workbook.xml"],
    ]);
    files["xl/_rels/workbook.xml.rels"] = relationships([
      ["rId1", "worksheet", "worksheets/sheet1.xml"],
    ]);
    files["[Content_Types].xml"] = contentTypes([
      ["xl/workbook.xml", "spreadsheetml.sheet.main"],
      ["xl/worksheets/sheet1.xml", "spreadsheetml.worksheet"],
    ]);
    files["xl/workbook.xml"] =
      `${declaration}<workbook xmlns="${sNS}" xmlns:r="${officeNS}"><sheets><sheet name="Sheet 1" sheetId="1" r:id="rId1"/></sheets><calcPr calcId="0" fullCalcOnLoad="1"/></workbook>`;
    const rows = new Map<number, string[]>(),
      workbook = new Workbook(file.sheet.cells);
    for (const [address, value] of Object.entries(file.sheet.cells)) {
      if (!/^[A-Z]+[1-9]\d*$/.test(address) || !value) continue;
      const row = Number(address.match(/\d+/)![0]);
      const cells = rows.get(row) ?? [];
      let cell: string;
      if (value.startsWith("=")) {
        const cached = workbook.display(address);
        const error = cached.startsWith("#"),
          bool = cached === "TRUE" || cached === "FALSE";
        cell = `<c r="${address}"${error ? ' t="e"' : bool ? ' t="b"' : !Number.isFinite(Number(cached)) ? ' t="str"' : ""}><f>${xml(value.slice(1))}</f><v>${xml(bool ? (cached === "TRUE" ? "1" : "0") : cached)}</v></c>`;
      } else if (value.trim() && Number.isFinite(Number(value)))
        cell = `<c r="${address}"><v>${Number(value)}</v></c>`;
      else
        cell = `<c r="${address}" t="inlineStr"><is><t xml:space="preserve">${xml(value)}</t></is></c>`;
      cells.push(cell);
      rows.set(row, cells);
    }
    files["xl/worksheets/sheet1.xml"] =
      `${declaration}<worksheet xmlns="${sNS}"><sheetData>${[...rows]
        .sort(([a], [b]) => a - b)
        .map(
          ([r, cells]) =>
            `<row r="${r}">${cells
              .sort((a, b) => {
                const col = (s: string) => s.match(/r="([A-Z]+)/)![1];
                return (
                  col(a).length - col(b).length || col(a).localeCompare(col(b))
                );
              })
              .join("")}</row>`,
        )
        .join("")}</sheetData></worksheet>`;
    return { bytes: writeZip(files), extension: "xlsx" };
  }
  if (file.kind === "presentation") {
    files["_rels/.rels"] = relationships([
      ["rId1", "officeDocument", "ppt/presentation.xml"],
    ]);
    files["[Content_Types].xml"] = contentTypes([
      ["ppt/presentation.xml", "presentationml.presentation.main"],
      ...file.slides.map(
        (_, i) =>
          [`ppt/slides/slide${i + 1}.xml`, "presentationml.slide"] as [
            string,
            string,
          ],
      ),
    ]);
    files["ppt/_rels/presentation.xml.rels"] = relationships(
      file.slides.map((_, i) => [
        `rId${i + 1}`,
        "slide",
        `slides/slide${i + 1}.xml`,
      ]),
    );
    files["ppt/presentation.xml"] =
      `${declaration}<p:presentation xmlns:p="${pNS}" xmlns:r="${officeNS}"><p:sldIdLst>${file.slides.map((_, i) => `<p:sldId id="${256 + i}" r:id="rId${i + 1}"/>`).join("")}</p:sldIdLst><p:sldSz cx="12192000" cy="6858000"/><p:notesSz cx="6858000" cy="9144000"/></p:presentation>`;
    file.slides.forEach((s, i) => {
      const shape = (
        id: number,
        name: string,
        text: string,
        y: number,
        size: number,
      ) =>
        `<p:sp><p:nvSpPr><p:cNvPr id="${id}" name="${name}"/><p:cNvSpPr txBox="1"/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x="900000" y="${y}"/><a:ext cx="10392000" cy="${id === 2 ? 1400000 : 3000000}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:noFill/></p:spPr><p:txBody><a:bodyPr/><a:lstStyle/>${text
          .split("\n")
          .map(
            (line) =>
              `<a:p><a:r><a:rPr lang="en-US" sz="${size}"/><a:t>${xml(line)}</a:t></a:r></a:p>`,
          )
          .join("")}</p:txBody></p:sp>`;
      files[`ppt/slides/slide${i + 1}.xml`] =
        `${declaration}<p:sld xmlns:p="${pNS}" xmlns:a="${aNS}" xmlns:r="${officeNS}"><p:cSld><p:bg><p:bgPr><a:solidFill><a:srgbClr val="${s.background.slice(1)}"/></a:solidFill><a:effectLst/></p:bgPr></p:bg><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/><a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/></a:xfrm></p:grpSpPr>${shape(2, "Title", s.title, 1400000, 4000)}${shape(3, "Body", s.body, 3000000, 2400)}</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>`;
    });
    return { bytes: writeZip(files), extension: "pptx" };
  }
  throw new Error("This file type does not have an Office export.");
}
export async function importOffice(
  bytes: Uint8Array,
  extension: string,
  title: string,
): Promise<OfficeFile> {
  const files = await readZip(bytes),
    decoder = new TextDecoder();
  const parse = (path: string) => {
    if (!files[path]) throw new Error(`Missing Office file part: ${path}`);
    const doc = new DOMParser().parseFromString(
      decoder.decode(files[path]),
      "application/xml",
    );
    if (doc.querySelector("parsererror") || doc.doctype)
      throw new Error("Invalid Office XML.");
    return doc;
  };
  const nodes = (el: Document | Element, ns: string, name: string) => [
    ...el.getElementsByTagNameNS(ns, name),
  ];
  const doc = createFile(
    extension === "docx"
      ? "document"
      : extension === "xlsx"
        ? "spreadsheet"
        : "presentation",
    title,
  );
  if (extension === "docx") {
    const source = parse("word/document.xml");
    const paragraphs = nodes(source, wNS, "p");
    doc.content.text = paragraphs
      .map((p) =>
        nodes(p, wNS, "t")
          .map((t) => t.textContent ?? "")
          .join(""),
      )
      .join("\n");
    doc.content.html = paragraphs
      .map(
        (p) =>
          `<p>${nodes(p, wNS, "r")
            .map((r) => {
              let text = nodes(r, wNS, "t")
                .map((t) => xml(t.textContent ?? ""))
                .join("");
              if (
                nodes(r, wNS, "b").some(
                  (b) =>
                    !["0", "false"].includes(
                      b.getAttributeNS(wNS, "val") ?? "",
                    ),
                )
              )
                text = `<strong>${text}</strong>`;
              if (nodes(r, wNS, "i").length) text = `<em>${text}</em>`;
              if (nodes(r, wNS, "u").length) text = `<u>${text}</u>`;
              return (
                text +
                nodes(r, wNS, "br")
                  .map(() => "<br>")
                  .join("")
              );
            })
            .join("")}</p>`,
      )
      .join("");
  } else if (extension === "xlsx") {
    let shared: string[] = [];
    if (files["xl/sharedStrings.xml"])
      shared = nodes(parse("xl/sharedStrings.xml"), sNS, "si").map((si) =>
        nodes(si, sNS, "t")
          .map((t) => t.textContent ?? "")
          .join(""),
      );
    const workbook = parse("xl/workbook.xml"),
      first = nodes(workbook, sNS, "sheet")[0];
    if (!first) throw new Error("Workbook has no sheets.");
    const id = first.getAttributeNS(officeNS, "id"),
      rels = parse("xl/_rels/workbook.xml.rels");
    const target = nodes(rels, relNS, "Relationship")
      .find((r) => r.getAttribute("Id") === id)
      ?.getAttribute("Target");
    if (!target) throw new Error("Worksheet relationship is missing.");
    const path = target.startsWith("/") ? target.slice(1) : `xl/${target}`;
    const sheet = parse(path);
    for (const cell of nodes(sheet, sNS, "c")) {
      const address = cell.getAttribute("r") ?? "";
      if (!/^[A-Z]+[1-9]\d*$/.test(address)) continue;
      const formula = nodes(cell, sNS, "f")[0]?.textContent,
        raw = nodes(cell, sNS, "v")[0]?.textContent ?? "";
      doc.sheet.cells[address] = formula
        ? `=${formula}`
        : cell.getAttribute("t") === "s"
          ? (shared[Number(raw)] ?? "")
          : cell.getAttribute("t") === "inlineStr"
            ? nodes(cell, sNS, "t")
                .map((t) => t.textContent ?? "")
                .join("")
            : cell.getAttribute("t") === "b"
              ? raw === "1"
                ? "TRUE"
                : "FALSE"
              : raw;
    }
  } else {
    const presentation = parse("ppt/presentation.xml"),
      rels = parse("ppt/_rels/presentation.xml.rels");
    doc.slides = nodes(presentation, pNS, "sldId").map((id) => {
      const rel = nodes(rels, relNS, "Relationship").find(
        (r) => r.getAttribute("Id") === id.getAttributeNS(officeNS, "id"),
      );
      const target = rel?.getAttribute("Target");
      if (!target) throw new Error("Slide relationship is missing.");
      const slide = parse(
        target.startsWith("/") ? target.slice(1) : `ppt/${target}`,
      );
      const shapes = nodes(slide, pNS, "sp").map((sp) =>
        nodes(sp, aNS, "p")
          .map((p) =>
            nodes(p, aNS, "t")
              .map((t) => t.textContent ?? "")
              .join(""),
          )
          .join("\n"),
      );
      const background = nodes(slide, pNS, "bg")[0];
      const color = background
        ? nodes(background, aNS, "srgbClr")[0]?.getAttribute("val")
        : null;
      return {
        id: crypto.randomUUID(),
        title: shapes[0] ?? "",
        body: shapes.slice(1).join("\n"),
        background:
          color && /^[a-f0-9]{6}$/i.test(color) ? `#${color}` : "#ffffff",
      };
    });
    if (!doc.slides.length) throw new Error("Presentation has no slides.");
  }
  return doc;
}

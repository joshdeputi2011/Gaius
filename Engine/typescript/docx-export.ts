import type { OfficeFile } from "./model";
import { writeZip } from "./zip";
import { cssHex } from "./rich-content";
const escape = (s: string) =>
  s
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
const office =
  "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
export function imageDimensions(bytes: Uint8Array): [number, number] {
  if (bytes[0] === 137 && bytes.length > 24) {
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    return [view.getUint32(16), view.getUint32(20)];
  }
  if (bytes[0] === 255 && bytes[1] === 216) {
    let p = 2;
    while (p + 9 < bytes.length) {
      if (bytes[p] !== 255) {
        p++;
        continue;
      }
      const marker = bytes[p + 1],
        length = bytes[p + 2] * 256 + bytes[p + 3];
      if ([192, 193, 194].includes(marker))
        return [
          bytes[p + 7] * 256 + bytes[p + 8],
          bytes[p + 5] * 256 + bytes[p + 6],
        ];
      if (length < 2) break;
      p += length + 2;
    }
  }
  return [640, 400];
}
export function exportDocx(file: OfficeFile) {
  const files: Record<string, string | Uint8Array> = {},
    parsed = new DOMParser().parseFromString(file.content.html, "text/html"),
    rels: string[] = [];
  let imageId = 0;
  const types = new Map<string, string>();
  function run(
    node: Node,
    inherited: Record<string, string | boolean> = {},
  ): string {
    if (node.nodeType === Node.TEXT_NODE) {
      const style = `${inherited.bold ? "<w:b/>" : ""}${inherited.italic ? "<w:i/>" : ""}${inherited.underline ? '<w:u w:val="single"/>' : ""}${inherited.strike ? "<w:strike/>" : ""}${inherited.font ? `<w:rFonts w:ascii="${escape(String(inherited.font))}" w:hAnsi="${escape(String(inherited.font))}"/>` : ""}${inherited.size ? `<w:sz w:val="${Math.round(Number(inherited.size) * 2)}"/>` : ""}${inherited.color ? `<w:color w:val="${inherited.color}"/>` : ""}${inherited.background ? `<w:shd w:fill="${inherited.background}"/>` : ""}`;
      return (node.textContent ?? "")
        .split("\n")
        .map(
          (text, i) =>
            `${i ? "<w:r><w:br/></w:r>" : ""}<w:r><w:rPr>${style}</w:rPr><w:t xml:space="preserve">${escape(text)}</w:t></w:r>`,
        )
        .join("");
    }
    if (!(node instanceof HTMLElement)) return "";
    if (node.tagName === "BR") return "<w:r><w:br/></w:r>";
    if (node.tagName === "IMG") {
      const m =
        /^data:image\/(png|jpeg|gif|webp);base64,([a-z0-9+/=\s]+)$/i.exec(
          node.getAttribute("src") ?? "",
        );
      if (!m) return "";
      const bytes = Uint8Array.from(atob(m[2]), (c) => c.charCodeAt(0)),
        extension = m[1].toLowerCase() === "jpeg" ? "jpg" : m[1].toLowerCase(),
        id = ++imageId,
        name = `image${id}.${extension}`;
      types.set(extension, `image/${m[1]}`);
      files[`word/media/${name}`] = bytes;
      rels.push(
        `<Relationship Id="image${id}" Type="${office}/image" Target="media/${name}"/>`,
      );
      const [w, h] = imageDimensions(bytes),
        width = Math.min(
          640,
          parseFloat(node.style.width) ||
            (node as HTMLImageElement).width ||
            Math.min(w, 640),
        ),
        height = Math.min(
          1200,
          parseFloat(node.style.height) || (width * h) / Math.max(1, w),
        ),
        cx = Math.round(width * 9525),
        cy = Math.round(height * 9525);
      const graphic = `<a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic><pic:nvPicPr><pic:cNvPr id="0" name="${name}"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed="image${id}"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="${cx}" cy="${cy}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic>`;
      const properties = `<wp:docPr id="${id}" name="${name}" descr="${escape(node.getAttribute("alt") ?? "Image")}"/>`,
        wrap = node.dataset.wrap;
      if (wrap && wrap !== "inline") {
        const mode = wrap.startsWith("square")
          ? '<wp:wrapSquare wrapText="bothSides"/>'
          : wrap === "top-bottom"
            ? "<wp:wrapTopAndBottom/>"
            : "<wp:wrapNone/>";
        return `<w:r><w:drawing><wp:anchor distT="114300" distB="114300" distL="114300" distR="114300" simplePos="0" relativeHeight="${id}" behindDoc="${wrap === "behind" ? 1 : 0}" locked="0" layoutInCell="1" allowOverlap="1"><wp:simplePos x="0" y="0"/><wp:positionH relativeFrom="column"><wp:align>${wrap === "square-right" ? "right" : "left"}</wp:align></wp:positionH><wp:positionV relativeFrom="paragraph"><wp:posOffset>${Math.round((parseFloat(node.style.top) || 0) * 9525)}</wp:posOffset></wp:positionV><wp:extent cx="${cx}" cy="${cy}"/>${mode}${properties}${graphic}</wp:anchor></w:drawing></w:r>`;
      }
      return `<w:r><w:drawing><wp:inline distT="0" distB="0" distL="0" distR="0"><wp:extent cx="${cx}" cy="${cy}"/>${properties}${graphic}</wp:inline></w:drawing></w:r>`;
    }
    const style = {
      ...inherited,
      bold:
        inherited.bold ||
        ["B", "STRONG"].includes(node.tagName) ||
        ["bold", "700", "800"].includes(node.style.fontWeight),
      italic: inherited.italic || ["I", "EM"].includes(node.tagName),
      underline: inherited.underline || node.tagName === "U",
      strike: inherited.strike || node.tagName === "S",
      font: node.style.fontFamily || inherited.font,
      size: parseFloat(node.style.fontSize)
        ? String(parseFloat(node.style.fontSize))
        : inherited.size,
      color: cssHex(node.style.color)?.slice(1) ?? inherited.color,
      background:
        cssHex(node.style.backgroundColor)?.slice(1) ?? inherited.background,
    };
    return [...node.childNodes].map((n) => run(n, style)).join("");
  }
  function paragraph(node: Node) {
    const el = node instanceof HTMLElement ? node : null,
      heading = el?.tagName.match(/^H([1-6])$/),
      align = el?.style.textAlign;
    return `<w:p><w:pPr>${heading ? `<w:pStyle w:val="Heading${heading[1]}"/>` : ""}${align ? `<w:jc w:val="${align === "justify" ? "both" : escape(align)}"/>` : ""}</w:pPr>${run(node)}${el?.style.breakAfter === "page" ? '<w:r><w:br w:type="page"/></w:r>' : ""}</w:p>`;
  }
  function block(node: Node): string {
    if (node instanceof HTMLElement && node.tagName === "TABLE")
      return `<w:tbl><w:tblPr><w:tblW w:w="0" w:type="auto"/><w:tblBorders>${["top", "left", "bottom", "right", "insideH", "insideV"].map((side) => `<w:${side} w:val="single" w:sz="4" w:color="A9B9CD"/>`).join("")}</w:tblBorders></w:tblPr>${[...node.querySelectorAll("tr")].map((tr) => `<w:tr>${[...tr.children].map((td) => `<w:tc><w:tcPr><w:tcW w:w="2400" w:type="dxa"/></w:tcPr>${paragraph(td)}</w:tc>`).join("")}</w:tr>`).join("")}</w:tbl>`;
    if (node instanceof HTMLElement && ["UL", "OL"].includes(node.tagName))
      return [...node.children]
        .map(
          (li, i) =>
            `<w:p><w:r><w:t xml:space="preserve">${node.tagName === "OL" ? `${i + 1}. ` : "• "}</w:t></w:r>${run(li)}</w:p>`,
        )
        .join("");
    return paragraph(node);
  }
  const landscape = file.layout?.orientation === "landscape",
    margin =
      file.layout?.margins === "narrow"
        ? 720
        : file.layout?.margins === "wide"
          ? 2160
          : 1440;
  const page =
    file.layout?.pageSize === "letter"
      ? [12240, 15840]
      : file.layout?.pageSize === "legal"
        ? [12240, 20160]
        : [11906, 16838];
  const marginAttrs = (["top", "right", "bottom", "left"] as const)
    .map(
      (side) =>
        ` w:${side}="${file.layout?.customMargins ? Math.round((file.layout.customMargins[side] * 1440) / 25.4) : margin}"`,
    )
    .join("");
  files["_rels/.rels"] =
    `<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="${office}/officeDocument" Target="word/document.xml"/></Relationships>`;
  files["word/document.xml"] =
    `<?xml version="1.0" encoding="UTF-8"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="${office}" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"><w:body>${[...parsed.body.childNodes].map(block).join("")}<w:sectPr><w:pgSz w:w="${page[landscape ? 1 : 0]}" w:h="${page[landscape ? 0 : 1]}"${landscape ? ' w:orient="landscape"' : ""}/><w:pgMar${marginAttrs}/>${file.layout?.borderWidth ? `<w:pgBorders w:offsetFrom="page">${["top", "right", "bottom", "left"].map((side) => `<w:${side} w:val="single" w:sz="${file.layout!.borderWidth! * 8}" w:color="${(file.layout!.borderColor ?? "#64748b").slice(1)}"/>`).join("")}</w:pgBorders>` : ""}<w:cols w:num="${file.layout?.columns ?? 1}" w:space="${Math.round(((file.layout?.columnGap ?? 10) * 1440) / 25.4)}"/></w:sectPr></w:body></w:document>`;
  files["word/_rels/document.xml.rels"] =
    `<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">${rels.join("")}</Relationships>`;
  files["[Content_Types].xml"] =
    `<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/>${[...types].map(([ext, type]) => `<Default Extension="${ext}" ContentType="${type}"/>`).join("")}<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>`;
  return { bytes: writeZip(files), extension: "docx" };
}

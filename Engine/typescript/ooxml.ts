import { createFile, type OfficeFile } from "./model";
import { shapeKinds, shapePreset } from "./drawing";
import { Workbook } from "./formula";
import { readZip, writeZip } from "./zip";
import { exportDocx } from "./docx-export";
import { exportXlsx } from "./xlsx-export";
import { importDocx, importXlsx, partPath } from "./office-import";
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
  const files: Record<string, string | Uint8Array> = {};
  if (file.kind === "document") return exportDocx(file);
  if (file.kind === "spreadsheet") return exportXlsx(file);
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
    files["[Content_Types].xml"] = (
      files["[Content_Types].xml"] as string
    ).replace(
      "</Types>",
      '<Default Extension="png" ContentType="image/png"/><Default Extension="jpg" ContentType="image/jpeg"/><Default Extension="jpeg" ContentType="image/jpeg"/><Default Extension="gif" ContentType="image/gif"/><Default Extension="webp" ContentType="image/webp"/></Types>',
    );
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
      const mediaRels: [string, string, string][] = [];
      const objects = (s.objects ?? [])
        .map((o, j) => {
          const id = j + 4,
            x = Math.round(o.x * 121920),
            y = Math.round(o.y * 68580),
            cx = Math.round(o.width * 121920),
            cy = Math.round(o.height * 68580),
            position = `<a:xfrm rot="${Math.round((o.rotation ?? 0) * 60000)}"><a:off x="${x}" y="${y}"/><a:ext cx="${cx}" cy="${cy}"/></a:xfrm>`;
          if (o.kind === "image") {
            const m =
              /^data:image\/(png|jpeg|gif|webp);base64,([a-z0-9+/=\s]+)$/i.exec(
                o.dataUrl ?? "",
              );
            if (!m) return "";
            const ext = m[1] === "jpeg" ? "jpg" : m[1],
              name = `image-${i + 1}-${j}.${ext}`,
              rid = `image${j}`;
            files[`ppt/media/${name}`] = Uint8Array.from(atob(m[2]), (c) =>
              c.charCodeAt(0),
            );
            mediaRels.push([rid, "image", `../media/${name}`]);
            return `<p:pic><p:nvPicPr><p:cNvPr id="${id}" name="Gaius image ${id}"/><p:cNvPicPr/><p:nvPr/></p:nvPicPr><p:blipFill><a:blip r:embed="${rid}"/><a:stretch><a:fillRect/></a:stretch></p:blipFill><p:spPr>${position}<a:prstGeom prst="rect"><a:avLst/></a:prstGeom></p:spPr></p:pic>`;
          }
          const geometry =
              shapePreset[o.kind] ??
              (shapeKinds.includes(o.kind as (typeof shapeKinds)[number])
                ? o.kind
                : "rect"),
            isText = o.kind === "text" || o.kind === "wordart";
          return `<p:sp><p:nvSpPr><p:cNvPr id="${id}" name="Gaius ${o.kind} ${id}"/><p:cNvSpPr${isText ? ' txBox="1"' : ""}/><p:nvPr/></p:nvSpPr><p:spPr>${position}<a:prstGeom prst="${geometry}"><a:avLst/></a:prstGeom>${isText ? "<a:noFill/>" : `<a:solidFill><a:srgbClr val="${xml(o.color.slice(1))}"/></a:solidFill>`}<a:ln w="${Math.round((o.strokeWidth ?? 0) * 9525)}">${o.strokeWidth ? `<a:solidFill><a:srgbClr val="${xml((o.stroke ?? "#185abd").slice(1))}"/></a:solidFill>` : "<a:noFill/>"}</a:ln></p:spPr>${
            isText
              ? `<p:txBody><a:bodyPr/><a:lstStyle/>${(o.text ?? "")
                  .split("\n")
                  .map(
                    (line) =>
                      `<a:p><a:r><a:rPr sz="${(o.fontSize ?? 24) * 100}"${o.kind === "wordart" ? ' b="1"' : ""}><a:solidFill><a:srgbClr val="${xml(o.color.slice(1))}"/></a:solidFill></a:rPr><a:t>${xml(line)}</a:t></a:r></a:p>`,
                  )
                  .join("")}</p:txBody>`
              : ""
          }</p:sp>`;
        })
        .join("");
      files[`ppt/slides/_rels/slide${i + 1}.xml.rels`] =
        relationships(mediaRels);
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
        `${declaration}<p:sld xmlns:p="${pNS}" xmlns:a="${aNS}" xmlns:r="${officeNS}"><p:cSld><p:bg><p:bgPr><a:solidFill><a:srgbClr val="${s.background.slice(1)}"/></a:solidFill><a:effectLst/></p:bgPr></p:bg><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/><a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/></a:xfrm></p:grpSpPr>${shape(2, "Title", s.title, 1400000, 4000)}${shape(3, "Body", s.body, 3000000, 2400)}${objects}</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr>${s.transition && ["fade", "push", "zoom"].includes(s.transition) ? `<p:transition spd="med">${s.transition === "fade" ? "<p:fade/>" : s.transition === "push" ? '<p:push dir="l"/>' : '<p:zoom dir="in"/>'}</p:transition>` : ""}</p:sld>`;
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
  if (extension === "docx") return importDocx(files, title);
  if (extension === "xlsx") return importXlsx(files, title);
  {
    const presentation = parse("ppt/presentation.xml"),
      rels = parse("ppt/_rels/presentation.xml.rels");
    doc.slides = nodes(presentation, pNS, "sldId").map((id) => {
      const rel = nodes(rels, relNS, "Relationship").find(
        (r) => r.getAttribute("Id") === id.getAttributeNS(officeNS, "id"),
      );
      const target = rel?.getAttribute("Target");
      if (!target) throw new Error("Slide relationship is missing.");
      const slidePath = partPath("ppt", target),
        slide = parse(slidePath),
        parts = slidePath.split("/"),
        slideName = parts.pop()!,
        slideDir = parts.join("/"),
        slideRelsPath = `${slideDir}/_rels/${slideName}.rels`,
        slideRels = files[slideRelsPath]
          ? nodes(parse(slideRelsPath), relNS, "Relationship")
          : [];
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
      const objects: NonNullable<OfficeFile["slides"][number]["objects"]> = [];
      const geometry = (el: Element) => {
        const xfrm = nodes(el, aNS, "xfrm")[0],
          off = xfrm ? nodes(xfrm, aNS, "off")[0] : null,
          ext = xfrm ? nodes(xfrm, aNS, "ext")[0] : null;
        return {
          x: Number(off?.getAttribute("x") ?? 0) / 121920,
          y: Number(off?.getAttribute("y") ?? 0) / 68580,
          width: Number(ext?.getAttribute("cx") ?? 3000000) / 121920,
          height: Number(ext?.getAttribute("cy") ?? 1500000) / 68580,
        };
      };
      nodes(slide, pNS, "sp")
        .slice(2)
        .forEach((sp) => {
          const preset = nodes(sp, aNS, "prstGeom")[0]?.getAttribute("prst"),
            text = nodes(sp, aNS, "t")
              .map((t) => t.textContent ?? "")
              .join("\n"),
            name = nodes(sp, pNS, "cNvPr")[0]?.getAttribute("name") ?? "",
            run = nodes(sp, aNS, "rPr")[0],
            fill = nodes(sp, aNS, "srgbClr")[0]?.getAttribute("val"),
            kind = text
              ? name.includes("wordart")
                ? "wordart"
                : "text"
              : ((Object.entries(shapePreset).find(
                  ([, value]) => value === preset,
                )?.[0] ??
                  (shapeKinds.includes(preset as (typeof shapeKinds)[number])
                    ? preset!
                    : "rectangle")) as (typeof shapeKinds)[number]);
          objects.push({
            id: crypto.randomUUID(),
            kind,
            text,
            ...geometry(sp),
            fontSize: Number(run?.getAttribute("sz") ?? 2400) / 100,
            color: fill && /^[a-f0-9]{6}$/i.test(fill) ? `#${fill}` : "#185abd",
            rotation:
              Number(nodes(sp, aNS, "xfrm")[0]?.getAttribute("rot") ?? 0) /
              60000,
            stroke: `#${nodes(nodes(sp, aNS, "ln")[0] ?? sp, aNS, "srgbClr")[0]?.getAttribute("val") ?? "185abd"}`,
            strokeWidth:
              Number(nodes(sp, aNS, "ln")[0]?.getAttribute("w") ?? 0) / 9525,
          });
        });
      nodes(slide, pNS, "pic").forEach((pic) => {
        const embed = nodes(pic, aNS, "blip")[0]?.getAttributeNS(
            officeNS,
            "embed",
          ),
          relationship = slideRels.find(
            (r) =>
              r.getAttribute("Id") === embed &&
              r.getAttribute("TargetMode") !== "External",
          ),
          path = relationship
            ? partPath(slideDir, relationship.getAttribute("Target") ?? "")
            : "",
          data = files[path];
        if (!data) return;
        const ext = path.split(".").pop()?.toLowerCase(),
          type = ext === "jpg" ? "jpeg" : ext;
        if (!["png", "jpeg", "gif", "webp", "bmp"].includes(type ?? "")) return;
        let binary = "";
        for (let at = 0; at < data.length; at += 8192)
          binary += String.fromCharCode(...data.subarray(at, at + 8192));
        objects.push({
          id: crypto.randomUUID(),
          kind: "image",
          dataUrl: `data:image/${type};base64,${btoa(binary)}`,
          ...geometry(pic),
          color: "#185abd",
        });
      });
      const color = background
        ? nodes(background, aNS, "srgbClr")[0]?.getAttribute("val")
        : null;
      return {
        id: crypto.randomUUID(),
        title: shapes[0] ?? "",
        body: shapes[1] ?? "",
        objects,
        transition: nodes(slide, pNS, "transition")[0]?.firstElementChild
          ?.localName as "fade" | "push" | "zoom" | undefined,
        background:
          color && /^[a-f0-9]{6}$/i.test(color) ? `#${color}` : "#ffffff",
      };
    });
    if (!doc.slides.length) throw new Error("Presentation has no slides.");
  }
  return doc;
}

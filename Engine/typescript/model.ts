export type Kind =
  "document" | "spreadsheet" | "presentation" | "image" | "pdf";
export type Slide = {
  id: string;
  title: string;
  body: string;
  background: string;
};
export interface OfficeFile {
  schemaVersion: 1;
  id: string;
  title: string;
  kind: Kind;
  updatedAt: string;
  content: { text: string; html: string };
  sheet: { cells: Record<string, string>; rows: number; columns: number };
  slides: Slide[];
  image: {
    dataUrl: string;
    rotation: number;
    brightness: number;
    contrast: number;
    grayscale: boolean;
  };
  pdf: { dataUrl: string };
}
export const kinds: Kind[] = [
  "document",
  "spreadsheet",
  "presentation",
  "image",
  "pdf",
];
export const labels: Record<Kind, string> = {
  document: "Write",
  spreadsheet: "Sheets",
  presentation: "Slides",
  image: "Image",
  pdf: "PDF",
};
export function createFile(kind: Kind, title = `Untitled ${kind}`): OfficeFile {
  return {
    schemaVersion: 1,
    id: crypto.randomUUID(),
    title,
    kind,
    updatedAt: new Date().toISOString(),
    content: { text: "", html: "<p><br></p>" },
    sheet: { cells: {}, rows: 100, columns: 26 },
    slides: [
      {
        id: crypto.randomUUID(),
        title: "Untitled presentation",
        body: "Add a subtitle",
        background: "#ffffff",
      },
    ],
    image: {
      dataUrl: "",
      rotation: 0,
      brightness: 100,
      contrast: 100,
      grayscale: false,
    },
    pdf: { dataUrl: "" },
  };
}
export function parseFile(json: string): OfficeFile {
  const value = JSON.parse(json);
  if (
    value?.schemaVersion !== 1 ||
    !kinds.includes(value.kind) ||
    typeof value.id !== "string" ||
    typeof value.title !== "string"
  )
    throw new Error("This is not a supported Gaius document.");
  const doc = { ...createFile(value.kind), ...value } as OfficeFile;
  if (
    typeof doc.content?.text !== "string" ||
    typeof doc.content?.html !== "string" ||
    !doc.sheet?.cells ||
    typeof doc.sheet.cells !== "object" ||
    Array.isArray(doc.sheet.cells) ||
    !Object.values(doc.sheet.cells).every((v) => typeof v === "string") ||
    !Array.isArray(doc.slides) ||
    !doc.slides.every(
      (s) =>
        typeof s.title === "string" &&
        typeof s.body === "string" &&
        /^#[0-9a-f]{6}$/i.test(s.background),
    ) ||
    typeof doc.image?.dataUrl !== "string" ||
    typeof doc.pdf?.dataUrl !== "string"
  )
    throw new Error("The document contains invalid content.");
  if (
    doc.image.dataUrl &&
    !/^data:image\/(png|jpeg|webp|gif|bmp);base64,/i.test(doc.image.dataUrl)
  )
    throw new Error("Unsupported image data.");
  if (
    doc.pdf.dataUrl &&
    !doc.pdf.dataUrl.startsWith("data:application/pdf;base64,")
  )
    throw new Error("Invalid PDF data.");
  doc.sheet.rows = Math.max(
    100,
    Math.min(10000, Number(doc.sheet.rows) || 100),
  );
  doc.sheet.columns = Math.max(
    26,
    Math.min(100, Number(doc.sheet.columns) || 26),
  );
  doc.image.rotation = Number(doc.image.rotation) || 0;
  doc.image.brightness = Math.max(
    0,
    Math.min(200, Number(doc.image.brightness) || 100),
  );
  doc.image.contrast = Math.max(
    0,
    Math.min(200, Number(doc.image.contrast) || 100),
  );
  return doc;
}
export function columnName(index: number): string {
  let s = "";
  for (let n = index + 1; n; n = Math.floor((n - 1) / 26))
    s = String.fromCharCode(65 + ((n - 1) % 26)) + s;
  return s;
}
export function columnIndex(name: string): number {
  return [...name].reduce((n, c) => n * 26 + c.charCodeAt(0) - 64, 0) - 1;
}
export function parseCSV(text: string): string[][] {
  const rows: string[][] = [];
  let row: string[] = [],
    field = "",
    quoted = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (c === '"') {
      if (quoted && text[i + 1] === '"') {
        field += '"';
        i++;
      } else quoted = !quoted;
    } else if (c === "," && !quoted) {
      row.push(field);
      field = "";
    } else if ((c === "\n" || c === "\r") && !quoted) {
      if (c === "\r" && text[i + 1] === "\n") i++;
      rows.push([...row, field]);
      row = [];
      field = "";
    } else field += c;
  }
  if (quoted) throw new Error("CSV has an unclosed quoted field.");
  if (field || row.length) rows.push([...row, field]);
  return rows;
}
export function exportCSV(
  cells: Record<string, string>,
  evaluate: (ref: string) => string,
): string {
  const keys = Object.keys(cells).filter(
    (k) => /^[A-Z]+[1-9]\d*$/.test(k) && cells[k],
  );
  const rows = Math.max(1, ...keys.map((k) => Number(k.match(/\d+/)![0]))),
    cols = Math.max(
      1,
      ...keys.map((k) => columnIndex(k.match(/[A-Z]+/)![0]) + 1),
    );
  if (rows * cols > 1000000) throw new Error("The export is too large.");
  return Array.from({ length: rows }, (_, r) =>
    Array.from({ length: cols }, (_, c) => {
      let value = evaluate(`${columnName(c)}${r + 1}`);
      if (/^[=+\-@\t\r]/.test(value) && !/^-?\d+(\.\d+)?$/.test(value))
        value = `'${value}`;
      return `"${value.replaceAll('"', '""')}"`;
    }).join(","),
  ).join("\r\n");
}

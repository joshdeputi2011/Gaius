export type Kind =
  "document" | "spreadsheet" | "presentation" | "image" | "pdf";
export type Slide = {
  id: string;
  title: string;
  body: string;
  background: string;
  notes?: string;
  transition?: "none" | "fade" | "push" | "zoom" | "morph";
  duration?: number;
  objects?: SlideObject[];
};
export type SlideObject = {
  id: string;
  kind: "text" | "wordart" | "image" | "rectangle" | "ellipse" | "triangle";
  text?: string;
  dataUrl?: string;
  x: number;
  y: number;
  width: number;
  height: number;
  color: string;
  fontSize?: number;
  animation?: "none" | "fade" | "fly" | "zoom";
};
export type CellStyle = {
  bold?: boolean;
  italic?: boolean;
  color?: string;
  background?: string;
  format?: "general" | "number" | "currency" | "percent";
  align?: "left" | "center" | "right";
};
export type Worksheet = {
  name?: string;
  cells: Record<string, string>;
  rows: number;
  columns: number;
  styles?: Record<string, CellStyle>;
  charts?: ChartSpec[];
  frozen?: boolean;
  filter?: { column: string; value: string };
};
export type ChartSpec = {
  id: string;
  type: "bar" | "line" | "pie";
  range: string;
  title: string;
};
export interface OfficeFile {
  schemaVersion: 1;
  id: string;
  title: string;
  kind: Kind;
  updatedAt: string;
  content: { text: string; html: string; [key: string]: unknown };
  layout?: {
    orientation?: "portrait" | "landscape";
    margins?: "normal" | "narrow" | "wide";
    columns?: number;
  };
  comments?: { id: string; text: string; createdAt: string }[];
  sheet: Worksheet;
  worksheets?: Worksheet[];
  activeSheet?: number;
  slides: Slide[];
  image: {
    dataUrl: string;
    rotation: number;
    brightness: number;
    contrast: number;
    grayscale: boolean;
    flipX?: boolean;
    flipY?: boolean;
    blur?: number;
    watermark?: string;
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
  if (doc.worksheets) {
    if (
      !Array.isArray(doc.worksheets) ||
      doc.worksheets.length > 100 ||
      !doc.worksheets.every(
        (s) =>
          s &&
          typeof s.cells === "object" &&
          !Array.isArray(s.cells) &&
          Object.values(s.cells).every((v) => typeof v === "string"),
      )
    )
      throw new Error("Invalid worksheets.");
    doc.activeSheet = Math.max(
      0,
      Math.min(doc.worksheets.length - 1, Number(doc.activeSheet) || 0),
    );
    if (doc.worksheets.length) doc.sheet = doc.worksheets[doc.activeSheet];
  }
  for (const sheet of doc.worksheets ?? [doc.sheet]) {
    sheet.rows = Math.max(100, Math.min(10000, Number(sheet.rows) || 100));
    sheet.columns = Math.max(26, Math.min(100, Number(sheet.columns) || 26));
    if (Object.keys(sheet.cells).some((k) => !/^[A-Z]+[1-9]\d*$/.test(k)))
      throw new Error("Invalid cell address.");
    if (
      sheet.styles &&
      (typeof sheet.styles !== "object" ||
        Array.isArray(sheet.styles) ||
        !Object.entries(sheet.styles).every(
          ([ref, s]) =>
            /^[A-Z]+[1-9]\d*$/.test(ref) &&
            s &&
            (!s.color || /^#[a-f0-9]{6}$/i.test(s.color)) &&
            (!s.background || /^#[a-f0-9]{6}$/i.test(s.background)) &&
            (!s.align || ["left", "center", "right"].includes(s.align)) &&
            (!s.format ||
              ["general", "number", "currency", "percent"].includes(s.format)),
        ))
    )
      throw new Error("Invalid cell formatting.");
    if (
      sheet.charts &&
      (!Array.isArray(sheet.charts) ||
        sheet.charts.length > 100 ||
        !sheet.charts.every(
          (c) =>
            c &&
            ["bar", "line", "pie"].includes(c.type) &&
            typeof c.id === "string" &&
            typeof c.title === "string" &&
            typeof c.range === "string" &&
            /^[A-Z]+[1-9]\d*(?::[A-Z]+[1-9]\d*)?$/.test(c.range),
        ))
    )
      throw new Error("Invalid charts.");
    if (
      sheet.filter &&
      (!/^[A-Z]+$/.test(sheet.filter.column) ||
        typeof sheet.filter.value !== "string")
    )
      throw new Error("Invalid filter.");
  }
  for (const slide of doc.slides) {
    if (
      slide.objects &&
      (!Array.isArray(slide.objects) ||
        slide.objects.length > 500 ||
        !slide.objects.every(
          (o) =>
            o &&
            [
              "text",
              "wordart",
              "image",
              "rectangle",
              "ellipse",
              "triangle",
            ].includes(o.kind) &&
            [o.x, o.y, o.width, o.height].every(Number.isFinite) &&
            /^#[a-f0-9]{6}$/i.test(o.color) &&
            (o.text === undefined || typeof o.text === "string") &&
            (!o.dataUrl ||
              /^data:image\/(png|jpeg|webp|gif|bmp);base64,/i.test(o.dataUrl)),
        ))
    )
      throw new Error("Invalid slide objects.");
    if (
      slide.transition &&
      !["none", "fade", "push", "zoom", "morph"].includes(slide.transition)
    )
      slide.transition = "none";
    if (slide.duration !== undefined)
      slide.duration = Math.max(1, Math.min(120, Number(slide.duration) || 5));
  }
  if (
    doc.comments &&
    (!Array.isArray(doc.comments) ||
      !doc.comments.every(
        (c) =>
          c &&
          typeof c.id === "string" &&
          typeof c.text === "string" &&
          typeof c.createdAt === "string",
      ))
  )
    throw new Error("Invalid comments.");
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

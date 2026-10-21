export type DeltaOperation = {
  insert: string | Record<string, string>;
  attributes?: Record<string, string | boolean | number>;
};
export function cssHex(value: string) {
  if (/^#[a-f0-9]{6}$/i.test(value)) return value;
  const m = /^rgba?\((\d+),\s*(\d+),\s*(\d+)/.exec(value);
  return m
    ? `#${m
        .slice(1, 4)
        .map((n) => Math.min(255, +n).toString(16).padStart(2, "0"))
        .join("")}`
    : undefined;
}
export function htmlToDelta(html: string): DeltaOperation[] {
  const doc = new DOMParser().parseFromString(html, "text/html"),
    ops: DeltaOperation[] = [];
  function insert(
    value: DeltaOperation["insert"],
    attrs: NonNullable<DeltaOperation["attributes"]> = {},
  ) {
    if (value === "") return;
    ops.push(
      Object.keys(attrs).length
        ? { insert: value, attributes: attrs }
        : { insert: value },
    );
  }
  function walk(
    node: Node,
    attrs: NonNullable<DeltaOperation["attributes"]> = {},
  ) {
    if (node.nodeType === Node.TEXT_NODE) {
      insert(node.textContent ?? "", attrs);
      return;
    }
    if (!(node instanceof HTMLElement)) return;
    const tag = node.tagName,
      style = node.style,
      next = { ...attrs };
    if (
      ["B", "STRONG"].includes(tag) ||
      ["bold", "700", "800"].includes(style.fontWeight)
    )
      next.bold = true;
    if (["I", "EM"].includes(tag) || style.fontStyle === "italic")
      next.italic = true;
    if (tag === "U") next.underline = true;
    if (tag === "S") next.strike = true;
    if (tag === "SUP") next.script = "super";
    if (tag === "SUB") next.script = "sub";
    if (style.fontSize)
      next.size = String(
        parseFloat(style.fontSize) *
          (style.fontSize.endsWith("pt") ? 4 / 3 : 1),
      );
    if (style.fontFamily) next.font = style.fontFamily.replaceAll('"', "");
    const color = cssHex(style.color),
      background = cssHex(style.backgroundColor);
    if (color) next.color = color;
    if (background) next.background = background;
    if (
      tag === "A" &&
      /^(https?:\/\/|mailto:)/i.test(node.getAttribute("href") ?? "")
    )
      next.link = node.getAttribute("href")!;
    if (tag === "IMG") {
      const src = node.getAttribute("src") ?? "";
      if (/^data:image\/(png|jpeg|webp|gif|bmp);base64,/i.test(src))
        insert({ image: src });
      return;
    }
    if (tag === "TABLE") {
      insert({
        "gaius-table": JSON.stringify(
          [...node.querySelectorAll("tr")].map((tr) =>
            [...tr.children].map((td) => td.textContent ?? ""),
          ),
        ),
      });
      insert("\n");
      return;
    }
    if (tag === "BR") {
      insert("\n");
      return;
    }
    if (tag === "DIV" && style.width && style.height && background) {
      const c = document.createElement("canvas");
      c.width = Math.max(1, Math.min(1200, parseFloat(style.width) || 240));
      c.height = Math.max(1, Math.min(1200, parseFloat(style.height) || 100));
      const context = c.getContext("2d");
      if (context) {
        context.fillStyle = background;
        context.fillRect(0, 0, c.width, c.height);
        insert({ image: c.toDataURL() });
        insert("\n");
      }
      return;
    }
    for (const child of node.childNodes) walk(child, next);
    if (
      [
        "P",
        "DIV",
        "H1",
        "H2",
        "H3",
        "H4",
        "H5",
        "H6",
        "LI",
        "BLOCKQUOTE",
      ].includes(tag)
    ) {
      const block: NonNullable<DeltaOperation["attributes"]> = {};
      if (/^H[1-6]$/.test(tag)) block.header = +tag[1];
      if (style.textAlign && style.textAlign !== "left")
        block.align = style.textAlign;
      if (tag === "LI")
        block.list =
          node.parentElement?.tagName === "OL" ? "ordered" : "bullet";
      if (tag === "BLOCKQUOTE") block.blockquote = true;
      insert("\n", block);
    }
  }
  for (const node of doc.body.childNodes) walk(node);
  if (
    !ops.length ||
    typeof ops.at(-1)!.insert !== "string" ||
    !(ops.at(-1)!.insert as string).endsWith("\n")
  )
    insert("\n");
  return ops;
}

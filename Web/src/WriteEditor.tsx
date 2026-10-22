import { useEffect, useRef, useState } from "react";
import type { OfficeFile } from "../../Engine/typescript/model";
import { clipboardImage, download, readDataURL, safeHTML } from "./storage";
import { Command, Group, Ribbon, useCommand, useRibbon } from "./ribbon";
import { escapeHTML } from "./visuals";
import ChartDialog from "./ChartDialog";
import { DrawCommands } from "./Handwriting";
import { htmlToDelta } from "../../Engine/typescript/rich-content";
import { shapeKinds, shapeLabel } from "../../Engine/typescript/drawing";
import { shapePNG } from "./drawing";
export default function WriteEditor({
  file,
  onChange,
  onError,
}: {
  file: OfficeFile;
  onChange: (f: OfficeFile) => void;
  onError?: (e: unknown) => void;
}) {
  const tab = useRibbon(),
    editor = useRef<HTMLDivElement>(null),
    selection = useRef<Range | null>(null),
    last = useRef(""),
    image = useRef<HTMLInputElement>(null);
  const [find, setFind] = useState(false),
    [query, setQuery] = useState(""),
    [replacement, setReplacement] = useState(""),
    [message, setMessage] = useState(""),
    [zoom, setZoom] = useState(100),
    [proof, setProof] = useState(true),
    [comments, setComments] = useState(false),
    [chart, setChart] = useState(false),
    [shapeColor, setShapeColor] = useState("#185abd");
  const [rulers, setRulers] = useState(true),
    [thumbnails, setThumbnails] = useState(false),
    [gridlines, setGridlines] = useState(false),
    [view, setView] = useState("print"),
    [selectedElement, setSelectedElement] = useState<HTMLElement | null>(null),
    [propertyVersion, setPropertyVersion] = useState(0);
  const paperRef = useRef<HTMLElement>(null);
  const objectDrag = useRef<{
    element: HTMLElement;
    x: number;
    y: number;
    width: number;
    height: number;
  } | null>(null);
  useEffect(() => {
    if (editor.current && file.content.html !== last.current) {
      editor.current.innerHTML = safeHTML(file.content.html);
      last.current = file.content.html;
    }
  }, [file.id, file.content.html]);
  function remember() {
    const s = window.getSelection();
    if (s?.rangeCount && editor.current?.contains(s.anchorNode))
      selection.current = s.getRangeAt(0).cloneRange();
  }
  function restore() {
    editor.current?.focus();
    const s = window.getSelection();
    if (!editor.current || !s) return;
    if (
      !selection.current ||
      !editor.current.contains(selection.current.startContainer)
    ) {
      selection.current = document.createRange();
      selection.current.selectNodeContents(editor.current);
      selection.current.collapse(false);
    }
    s.removeAllRanges();
    s.addRange(selection.current);
  }
  function emit() {
    if (!editor.current) return;
    last.current = safeHTML(editor.current.innerHTML);
    onChange({
      ...file,
      content: {
        ...file.content,
        rtf: undefined,
        objects: undefined,
        html: last.current,
        text: editor.current.innerText,
        delta: htmlToDelta(last.current),
      },
    });
  }
  function command(name: string, value?: string) {
    restore();
    document.execCommand(name, false, value);
    remember();
    emit();
  }
  function findNext() {
    if (!editor.current || !query) return;
    const walker = document.createTreeWalker(
      editor.current,
      NodeFilter.SHOW_TEXT,
    );
    let node: Node | null;
    const start = selection.current?.endContainer,
      offset = selection.current?.endOffset ?? 0;
    let past = !start;
    const matches: { node: Node; index: number }[] = [];
    while ((node = walker.nextNode())) {
      const text = node.textContent ?? "";
      let at = text.toLocaleLowerCase().indexOf(query.toLocaleLowerCase());
      while (at >= 0) {
        matches.push({ node, index: at });
        at = text
          .toLocaleLowerCase()
          .indexOf(query.toLocaleLowerCase(), at + Math.max(query.length, 1));
      }
    }
    const found =
      matches.find((m) => {
        if (m.node === start) {
          past = true;
          return m.index >= offset;
        }
        return past;
      }) ?? matches[0];
    if (!found) {
      setMessage("No matches");
      return;
    }
    const range = document.createRange();
    range.setStart(found.node, found.index);
    range.setEnd(found.node, found.index + query.length);
    selection.current = range;
    restore();
    found.node.parentElement?.scrollIntoView({ block: "center" });
    setMessage(`${matches.length} matches`);
  }
  function replaceAll() {
    if (!editor.current || !query) return;
    const walker = document.createTreeWalker(
      editor.current,
      NodeFilter.SHOW_TEXT,
    );
    let node: Node | null,
      count = 0;
    while ((node = walker.nextNode())) {
      const text = node.textContent ?? "";
      const pattern = new RegExp(
        query.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"),
        "gi",
      );
      node.textContent = text.replace(pattern, () => {
        count++;
        return replacement;
      });
    }
    emit();
    setMessage(`Replaced ${count} matches`);
  }
  function layout(patch: NonNullable<OfficeFile["layout"]>) {
    onChange({ ...file, layout: { ...file.layout, ...patch } });
  }
  function insertShape(kind: string) {
    command(
      "insertHTML",
      `<img src="${shapePNG(kind, shapeColor)}" alt="${shapeLabel(kind)}" data-shape="${kind}" data-fill="${shapeColor}" data-stroke="#185abd" data-stroke-width="0" data-wrap="square-left" style="float:left;display:block;object-fit:contain;width:260px;height:130px;max-width:100%;margin:12px"><p><br></p>`,
    );
  }
  async function insertPicture(source: Blob, name = "Picture") {
    const data = await readDataURL(source);
    const img = new Image();
    img.src = data;
    await img.decode();
    const width = Math.min(420, img.naturalWidth),
      height = (width * img.naturalHeight) / Math.max(1, img.naturalWidth);
    command(
      "insertHTML",
      `<img src="${data}" alt="${escapeHTML(name)}" data-wrap="square-left" style="float:left;display:block;object-fit:contain;width:${width}px;height:${height}px;max-width:100%;margin:12px"><p><br></p>`,
    );
  }
  function editSelected(action: (element: HTMLElement) => void) {
    if (!selectedElement?.isConnected) {
      setSelectedElement(null);
      return;
    }
    action(selectedElement);
    emit();
    setPropertyVersion((v) => v + 1);
  }
  function wrapSelected(wrap: string) {
    editSelected((el) => {
      el.dataset.wrap = wrap;
      el.style.position = "";
      el.style.zIndex = "";
      el.style.clear = "";
      el.style.float =
        wrap === "square-left"
          ? "left"
          : wrap === "square-right"
            ? "right"
            : "none";
      el.style.display = wrap === "inline" ? "inline-block" : "block";
      el.style.margin = wrap === "inline" ? "0 6px" : "12px";
      if (wrap === "top-bottom") el.style.clear = "both";
      if (wrap === "front" || wrap === "behind") {
        el.style.position = "absolute";
        el.style.zIndex = wrap === "front" ? "2" : "0";
        el.style.left = "0px";
        el.style.top = "0px";
      }
    });
  }
  function recolorShape(key: string, value: string) {
    editSelected((el) => {
      el.dataset[key] = value;
      if (el instanceof HTMLImageElement && el.dataset.shape)
        el.src = shapePNG(
          el.dataset.shape,
          el.dataset.fill || "#185abd",
          el.dataset.stroke,
          Number(el.dataset.strokeWidth) || 0,
        );
    });
  }
  function selectedBlock() {
    const node = selection.current?.startContainer;
    const el = node instanceof HTMLElement ? node : node?.parentElement;
    return el?.closest<HTMLElement>("p,h1,h2,h3,li,td,div") ?? editor.current;
  }
  function tableEdit(action: string) {
    editSelected((el) => {
      const table = el as HTMLTableElement;
      if (action === "row") {
        const row = table.insertRow();
        for (let c = 0; c < (table.rows[0]?.cells.length || 1); c++)
          row.insertCell().textContent = "Cell";
      }
      if (action === "column")
        for (const row of table.rows) row.insertCell().textContent = "Cell";
      if (action === "remove-row" && table.rows.length > 1)
        table.deleteRow(table.rows.length - 1);
      if (action === "remove-column" && table.rows[0]?.cells.length > 1)
        for (const row of table.rows) row.deleteCell(row.cells.length - 1);
    });
  }
  function insertText(label: string, html: (value: string) => string) {
    const value = prompt(label);
    if (value) command("insertHTML", html(escapeHTML(value)));
  }
  useCommand((c) => {
    if (c === "find") {
      setFind(true);
    }
    if (c === "replace") {
      setFind(true);
    }
    if (c === "proofing") setProof((p) => !p);
    if (c === "insert-image") image.current?.click();
    if (c === "wordart")
      insertText(
        "WordArt text",
        (v) =>
          `<p><span style="font-size:48px;font-weight:bold;color:#185abd;text-shadow:2px 3px 0 #bdcfea">${v}</span></p>`,
      );
  });
  const words = file.content.text.trim().split(/\s+/).filter(Boolean).length;
  const insertTable = () => {
    const value = prompt("Table dimensions (rows x columns)", "3x3"),
      m = value?.match(/^(\d+)\s*x\s*(\d+)$/i);
    if (!m || +m[1] > 50 || +m[2] > 20 || !+m[1] || !+m[2]) return;
    command(
      "insertHTML",
      `<table><tbody>${Array.from({ length: +m[1] }, () => `<tr>${"<td>Cell</td>".repeat(+m[2])}</tr>`).join("")}</tbody></table><p><br></p>`,
    );
  };
  return (
    <>
      <input
        ref={image}
        type="file"
        hidden
        accept="image/png,image/jpeg,image/webp"
        onChange={async (e) => {
          const f = e.target.files?.[0];
          if (f)
            try {
              await insertPicture(f, f.name);
            } catch (err) {
              onError?.(err);
            }
          e.target.value = "";
        }}
      />
      <Ribbon>
        {tab === "Draw" && (
          <DrawCommands
            onInsert={(png) =>
              command(
                "insertHTML",
                `<p><img src="${png}" alt="Handwriting"></p>`,
              )
            }
          />
        )}
        {tab === "Home" && (
          <>
            <Group label="Clipboard">
              <Command label="Undo" glyph="↶" onClick={() => command("undo")} />
              <Command label="Redo" glyph="↷" onClick={() => command("redo")} />
              <Command
                label="Paste"
                glyph="▣"
                onClick={async () => {
                  try {
                    const image = await clipboardImage();
                    if (image) await insertPicture(image);
                    else
                      command(
                        "insertText",
                        await navigator.clipboard.readText(),
                      );
                  } catch {
                    onError?.(
                      new Error("Use Ctrl+V or your device's Paste command."),
                    );
                  }
                }}
              />
            </Group>
            <Group label="Font">
              <select
                aria-label="Font"
                onChange={(e) => command("fontName", e.target.value)}
              >
                {[
                  "Calibri",
                  "Arial",
                  "Georgia",
                  "Times New Roman",
                  "Verdana",
                  "Courier New",
                ].map((f) => (
                  <option key={f}>{f}</option>
                ))}
              </select>
              <select
                aria-label="Font size"
                defaultValue="3"
                onChange={(e) => command("fontSize", e.target.value)}
              >
                {[8, 10, 12, 14, 18, 24, 36].map((n, i) => (
                  <option value={i + 1} key={n}>
                    {n}
                  </option>
                ))}
              </select>
              {[
                ["Bold", "bold", "B"],
                ["Italic", "italic", "𝑰"],
                ["Underline", "underline", "U̲"],
                ["Strikethrough", "strikeThrough", "S̶"],
                ["Superscript", "superscript", "x²"],
                ["Subscript", "subscript", "x₂"],
              ].map(([label, cmd, glyph]) => (
                <Command
                  key={cmd}
                  label={label}
                  glyph={glyph}
                  onClick={() => command(cmd)}
                />
              ))}
              <label className="color-label">
                A
                <input
                  aria-label="Text color"
                  type="color"
                  onChange={(e) => command("foreColor", e.target.value)}
                />
              </label>
              <label className="color-label">
                ▰
                <input
                  aria-label="Highlight color"
                  type="color"
                  defaultValue="#fff3a3"
                  onChange={(e) => command("hiliteColor", e.target.value)}
                />
              </label>
            </Group>
            <Group label="Paragraph">
              <Command
                label="Paragraph border"
                glyph="▣"
                onClick={() => {
                  const block = selectedBlock();
                  if (block) {
                    block.style.border = block.style.border
                      ? ""
                      : "1px solid #64748b";
                    block.style.padding = "8px";
                    emit();
                  }
                }}
              />
              {[
                ["Align left", "justifyLeft", "☰"],
                ["Align center", "justifyCenter", "≡"],
                ["Align right", "justifyRight", "☰"],
                ["Justify", "justifyFull", "▤"],
                ["Bulleted list", "insertUnorderedList", "•"],
                ["Numbered list", "insertOrderedList", "1."],
                ["Indent", "indent", "⇥"],
                ["Outdent", "outdent", "⇤"],
              ].map(([label, cmd, glyph]) => (
                <Command
                  key={cmd}
                  label={label}
                  glyph={glyph}
                  onClick={() => command(cmd)}
                />
              ))}
            </Group>
            <Group label="Styles">
              <select
                aria-label="Paragraph style"
                onChange={(e) => command("formatBlock", e.target.value)}
              >
                <option value="p">Normal text</option>
                {[1, 2, 3].map((n) => (
                  <option key={n} value={`h${n}`}>
                    Heading {n}
                  </option>
                ))}
                <option value="blockquote">Quotation</option>
              </select>
              <Command
                label="Clear formatting"
                glyph="A×"
                onClick={() => command("removeFormat")}
              />
            </Group>
            <Group label="Editing">
              <Command
                label="Find / replace"
                glyph="⌕"
                onClick={() => setFind(!find)}
              />
            </Group>
          </>
        )}
        {tab === "Insert" && (
          <>
            <Group label="Pages">
              <Command
                label="Page break"
                glyph="↵"
                onClick={() =>
                  command(
                    "insertHTML",
                    '<div style="break-after:page"><br></div><p><br></p>',
                  )
                }
              />
              <Command
                label="Horizontal line"
                glyph="―"
                onClick={() => command("insertHorizontalRule")}
              />
            </Group>
            <Group label="Illustrations">
              <Command
                label="Picture"
                glyph="▧"
                onClick={() => image.current?.click()}
              />
              <Command label="Table" glyph="▦" onClick={insertTable} />
              <Command
                label="WordArt"
                glyph="𝓐"
                onClick={() =>
                  insertText(
                    "WordArt text",
                    (v) =>
                      `<p><span style="font-size:48px;font-weight:bold;color:#185abd;text-shadow:2px 3px 0 #bdcfea">${v}</span></p>`,
                  )
                }
              />
              <label>
                Shapes
                <select
                  aria-label="Insert shape"
                  value=""
                  onChange={(e) => {
                    if (e.target.value) insertShape(e.target.value);
                  }}
                >
                  <option value="">Choose a shape</option>
                  {shapeKinds.map((kind) => (
                    <option key={kind} value={kind}>
                      {shapeLabel(kind)}
                    </option>
                  ))}
                </select>
              </label>
              {["rectangle", "ellipse", "triangle"].map((kind, i) => (
                <Command
                  key={kind}
                  label={shapeLabel(kind)}
                  glyph={["▭", "◯", "△"][i]}
                  onClick={() => insertShape(kind)}
                />
              ))}
              <label className="color-label">
                Shape color
                <input
                  type="color"
                  aria-label="Shape color"
                  value={shapeColor}
                  onChange={(e) => setShapeColor(e.target.value)}
                />
              </label>
              <Command label="Chart" glyph="▥" onClick={() => setChart(true)} />
            </Group>
            <Group label="Links & symbols">
              <Command
                label="Hyperlink"
                glyph="↗"
                onClick={() => {
                  const url = prompt(
                    "Link address (https:// or mailto:)",
                    "https://",
                  );
                  if (url && /^(https?:\/\/|mailto:)/i.test(url))
                    command("createLink", url);
                }}
              />
              <Command
                label="Symbol"
                glyph="Ω"
                onClick={() =>
                  insertText(
                    "Symbol or equation text",
                    (v) => `<span>${v}</span>`,
                  )
                }
              />
              <Command
                label="Date & time"
                glyph="◷"
                onClick={() =>
                  command("insertText", new Date().toLocaleString())
                }
              />
            </Group>
          </>
        )}
        {tab === "Design" && (
          <>
            <Group label="Typography">
              {["Calibri", "Georgia", "Verdana"].map((font) => (
                <Command
                  key={font}
                  label={font}
                  glyph="Aa"
                  onClick={() =>
                    onChange({
                      ...file,
                      content: { ...file.content, fontFamily: font },
                    })
                  }
                />
              ))}
            </Group>
            <Group label="Page accents">
              <Command
                label="Title style"
                glyph="T"
                onClick={() => command("formatBlock", "h1")}
              />
              <Command
                label="Quote style"
                glyph="❝"
                onClick={() => command("formatBlock", "blockquote")}
              />
            </Group>
          </>
        )}
        {tab === "Layout" && (
          <>
            <Group label="Page setup">
              <select
                aria-label="Orientation"
                value={file.layout?.orientation ?? "portrait"}
                onChange={(e) =>
                  layout({
                    orientation: e.target.value as "portrait" | "landscape",
                  })
                }
              >
                <option value="portrait">Portrait</option>
                <option value="landscape">Landscape</option>
              </select>
              <select
                aria-label="Margins"
                value={file.layout?.margins ?? "normal"}
                onChange={(e) =>
                  layout({
                    margins: e.target.value as "normal" | "narrow" | "wide",
                  })
                }
              >
                {["normal", "narrow", "wide"].map((m) => (
                  <option key={m}>{m}</option>
                ))}
              </select>
              <select
                aria-label="Columns"
                value={file.layout?.columns ?? 1}
                onChange={(e) => layout({ columns: +e.target.value })}
              >
                {[1, 2, 3, 4].map((n) => (
                  <option key={n} value={n}>
                    {n} column{n > 1 ? "s" : ""}
                  </option>
                ))}
              </select>
              <label>
                Paper
                <select
                  aria-label="Paper size"
                  value={file.layout?.pageSize ?? "a4"}
                  onChange={(e) =>
                    layout({
                      pageSize: e.target.value as "a4" | "letter" | "legal",
                    })
                  }
                >
                  {["a4", "letter", "legal"].map((size) => (
                    <option key={size} value={size}>
                      {size.toUpperCase()}
                    </option>
                  ))}
                </select>
              </label>
              <Command
                label="Custom margins"
                glyph="▣"
                onClick={() => {
                  const value = prompt(
                    "Margins in mm: top, right, bottom, left",
                    Object.values(
                      file.layout?.customMargins ?? {
                        top: 25.4,
                        right: 25.4,
                        bottom: 25.4,
                        left: 25.4,
                      },
                    ).join(", "),
                  );
                  if (!value) return;
                  const parts = value.split(/[, ]+/).map(Number);
                  if (
                    parts.length === 4 &&
                    parts.every((n) => Number.isFinite(n) && n >= 0 && n <= 80)
                  )
                    layout({
                      customMargins: {
                        top: parts[0],
                        right: parts[1],
                        bottom: parts[2],
                        left: parts[3],
                      },
                    });
                }}
              />
              <label>
                Column gap (mm)
                <input
                  type="number"
                  aria-label="Column gap"
                  min="0"
                  max="30"
                  value={file.layout?.columnGap ?? 10}
                  onChange={(e) =>
                    layout({
                      columnGap: Math.max(0, Math.min(30, +e.target.value)),
                    })
                  }
                />
              </label>
              <Command
                label="Page border"
                glyph="□"
                onClick={() =>
                  layout({
                    borderWidth: file.layout?.borderWidth ? 0 : 1,
                    borderColor: "#64748b",
                  })
                }
              />
            </Group>
            <Group label="Spacing">
              <Command
                label="Increase indent"
                glyph="⇥"
                onClick={() => command("indent")}
              />
              <Command
                label="Decrease indent"
                glyph="⇤"
                onClick={() => command("outdent")}
              />
            </Group>
          </>
        )}
        {tab === "References" && (
          <>
            <Group label="References">
              <Command
                label="Table of contents"
                glyph="☷"
                onClick={() => {
                  const headings = [
                    ...(editor.current?.querySelectorAll("h1,h2,h3") ?? []),
                  ];
                  command(
                    "insertHTML",
                    `<h2>Contents</h2><ul>${headings.map((h) => `<li>${escapeHTML(h.textContent ?? "")}</li>`).join("")}</ul><p><br></p>`,
                  );
                }}
              />
              <Command
                label="Footnote"
                glyph="¹"
                onClick={() =>
                  insertText(
                    "Footnote text",
                    (v) => `<sup>*</sup><p style="font-size:10px">* ${v}</p>`,
                  )
                }
              />
              <Command
                label="Citation"
                glyph="❞"
                onClick={() =>
                  insertText(
                    "Citation (author, year, source)",
                    (v) => `<p><em>${v}</em></p>`,
                  )
                }
              />
            </Group>
          </>
        )}
        {tab === "Review" && (
          <>
            <Group label="Proofing">
              <Command
                label="Spelling"
                glyph="ABC✓"
                active={proof}
                onClick={() => setProof(!proof)}
              />
              <Command
                label="Word count"
                glyph="123"
                onClick={() =>
                  setMessage(
                    `${words} words · ${file.content.text.length} characters · ${Math.max(1, Math.ceil(words / 200))} minute read`,
                  )
                }
              />
            </Group>
            <Group label="Comments">
              <Command
                label="New comment"
                glyph="⊕"
                onClick={() => {
                  const text = prompt("Comment");
                  if (text)
                    onChange({
                      ...file,
                      comments: [
                        ...(file.comments ?? []),
                        {
                          id: crypto.randomUUID(),
                          text,
                          createdAt: new Date().toISOString(),
                        },
                      ],
                    });
                  setComments(true);
                }}
              />
              <Command
                label="Show comments"
                glyph="☷"
                active={comments}
                onClick={() => setComments(!comments)}
              />
            </Group>
          </>
        )}
        {tab === "View" && (
          <>
            <Group label="Document view">
              <label>
                Layout
                <select
                  aria-label="Document view"
                  value={view}
                  onChange={(e) => setView(e.target.value)}
                >
                  <option value="print">Print layout</option>
                  <option value="web">Web layout</option>
                  <option value="read">Reading view</option>
                </select>
              </label>
              <Command
                label="Rulers"
                glyph="↔"
                active={rulers}
                onClick={() => setRulers(!rulers)}
              />
              <Command
                label="Thumbnails"
                glyph="▤"
                active={thumbnails}
                onClick={() => setThumbnails(!thumbnails)}
              />
              <Command
                label="Gridlines"
                glyph="▦"
                active={gridlines}
                onClick={() => setGridlines(!gridlines)}
              />
              <Command
                label="Page width"
                glyph="↔"
                onClick={() =>
                  setZoom(
                    Math.max(
                      50,
                      Math.min(
                        180,
                        Math.floor(
                          (((paperRef.current?.parentElement?.clientWidth ??
                            860) -
                            80) /
                            (paperRef.current?.offsetWidth ?? 794)) *
                            100,
                        ),
                      ),
                    ),
                  )
                }
              />
            </Group>
            <Group label="Zoom">
              <Command
                label="Zoom out"
                glyph="−"
                onClick={() => setZoom(Math.max(50, zoom - 10))}
              />
              <Command
                label={`${zoom}%`}
                glyph="⌕"
                onClick={() => setZoom(100)}
              />
              <Command
                label="Zoom in"
                glyph="+"
                onClick={() => setZoom(Math.min(180, zoom + 10))}
              />
            </Group>
            <Group label="Export">
              <Command
                label="HTML"
                glyph="<>"
                onClick={() =>
                  download(
                    `<!doctype html><meta charset="utf-8"><body>${safeHTML(file.content.html)}</body>`,
                    `${file.title}.html`,
                    "text/html",
                  )
                }
              />
              <Command
                label="Plain text"
                glyph="¶"
                onClick={() =>
                  download(file.content.text, `${file.title}.txt`, "text/plain")
                }
              />
            </Group>
          </>
        )}
      </Ribbon>
      {find && (
        <div className="find-bar">
          <input
            aria-label="Find text"
            placeholder="Find in document"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") findNext();
            }}
          />
          <button onClick={findNext}>Find next</button>
          <input
            aria-label="Replace with"
            placeholder="Replace with"
            value={replacement}
            onChange={(e) => setReplacement(e.target.value)}
          />
          <button
            onClick={() => {
              restore();
              if (
                window.getSelection()?.toString().toLocaleLowerCase() ===
                query.toLocaleLowerCase()
              )
                command("insertText", replacement);
              findNext();
            }}
          >
            Replace
          </button>
          <button onClick={replaceAll}>Replace all</button>
          <button onClick={() => setFind(false)} aria-label="Close find">
            ✕
          </button>
        </div>
      )}
      {message && (
        <div className="inline-message" role="status">
          {message}
          <button onClick={() => setMessage("")}>✕</button>
        </div>
      )}
      <div
        className={`document-workspace view-${view} ${gridlines ? "document-grid" : ""}`}
      >
        {thumbnails && (
          <aside
            className="document-thumbnails"
            aria-label="Document navigation"
          >
            <h3>Document</h3>
            <button
              onClick={() =>
                paperRef.current?.scrollIntoView({ block: "start" })
              }
            >
              <div
                className="document-miniature"
                dangerouslySetInnerHTML={{
                  __html: safeHTML(file.content.html),
                }}
              />
              Page overview
            </button>
            {Array.from(editor.current?.querySelectorAll("h1,h2,h3") ?? []).map(
              (heading, i) => (
                <button
                  key={i}
                  onClick={() =>
                    heading.scrollIntoView({
                      block: "center",
                      behavior: "smooth",
                    })
                  }
                >
                  {heading.textContent}
                </button>
              ),
            )}
          </aside>
        )}
        {rulers && (
          <div className="page-ruler" aria-label="Horizontal ruler in inches">
            {Array.from({ length: 17 }, (_, i) => (
              <span key={i}>{i % 2 === 0 ? i / 2 : "·"}</span>
            ))}
          </div>
        )}
        <article
          ref={paperRef}
          className={`paper ${file.layout?.orientation ?? "portrait"}`}
          style={{
            zoom: zoom / 100,
            width:
              view === "web"
                ? "calc(100% - 64px)"
                : file.layout?.pageSize === "letter"
                  ? 816
                  : file.layout?.pageSize === "legal"
                    ? 816
                    : undefined,
            minHeight: file.layout?.pageSize === "legal" ? 1344 : undefined,
            border: file.layout?.borderWidth
              ? `${file.layout.borderWidth}px solid ${file.layout.borderColor}`
              : undefined,
            padding: file.layout?.customMargins
              ? `${file.layout.customMargins.top}mm ${file.layout.customMargins.right}mm ${file.layout.customMargins.bottom}mm ${file.layout.customMargins.left}mm`
              : file.layout?.margins === "narrow"
                ? "28px"
                : file.layout?.margins === "wide"
                  ? "90px"
                  : undefined,
          }}
        >
          {rulers && (
            <div
              className="vertical-ruler"
              aria-label="Vertical ruler in inches"
            >
              {Array.from({ length: 11 }, (_, i) => (
                <span key={i}>{i}</span>
              ))}
            </div>
          )}
          <div
            ref={editor}
            className="word-content"
            style={{
              columnCount: file.layout?.columns ?? 1,
              columnGap: `${file.layout?.columnGap ?? 10}mm`,
              fontFamily: String(file.content.fontFamily ?? "Calibri"),
            }}
            contentEditable={view !== "read"}
            suppressContentEditableWarning
            role="textbox"
            aria-label="Document content"
            aria-multiline
            spellCheck={proof}
            data-placeholder="Start writing…"
            onInput={emit}
            onKeyUp={remember}
            onMouseUp={remember}
            onClick={(e) => {
              const el = (e.target as HTMLElement).closest<HTMLElement>(
                "img,table",
              );
              setSelectedElement(el);
            }}
            onPointerDown={(e) => {
              if (!(e.target instanceof HTMLImageElement)) return;
              const el = e.target;
              setSelectedElement(el);
              if (el.style.position !== "absolute") return;
              e.preventDefault();
              objectDrag.current = {
                element: el,
                x: e.clientX,
                y: e.clientY,
                width: parseFloat(el.style.left) || 0,
                height: parseFloat(el.style.top) || 0,
              };
              el.setPointerCapture(e.pointerId);
            }}
            onPointerMove={(e) => {
              const drag = objectDrag.current;
              if (!drag) return;
              drag.element.style.left = `${Math.max(0, drag.width + ((e.clientX - drag.x) * 100) / zoom)}px`;
              drag.element.style.top = `${Math.max(0, drag.height + ((e.clientY - drag.y) * 100) / zoom)}px`;
            }}
            onPointerUp={() => {
              if (objectDrag.current) {
                objectDrag.current = null;
                emit();
                setPropertyVersion((v) => v + 1);
              }
            }}
            onBlur={remember}
            onPaste={(e) => {
              e.preventDefault();
              const picture = Array.from(e.clipboardData.files).find((f) =>
                f.type.startsWith("image/"),
              );
              if (picture) {
                remember();
                void insertPicture(picture, picture.name).catch(onError);
              } else {
                const html = e.clipboardData.getData("text/html");
                command(
                  html ? "insertHTML" : "insertText",
                  html ? safeHTML(html) : e.clipboardData.getData("text/plain"),
                );
              }
            }}
          />
        </article>
        {selectedElement?.isConnected && (
          <aside
            className="document-object-properties"
            aria-label="Object formatting"
            data-version={propertyVersion}
          >
            <h3>
              {selectedElement.tagName === "TABLE"
                ? "Table"
                : selectedElement.dataset.shape
                  ? "Shape"
                  : "Picture"}{" "}
              format
            </h3>
            <button
              aria-label="Close object formatting"
              onClick={() => setSelectedElement(null)}
            >
              Close
            </button>
            <label>
              Wrap text
              <select
                aria-label="Wrap text"
                value={selectedElement.dataset.wrap ?? "inline"}
                onChange={(e) => wrapSelected(e.target.value)}
              >
                {[
                  ["square-left", "Square · left"],
                  ["square-right", "Square · right"],
                  ["top-bottom", "Top and bottom"],
                  ["inline", "In line with text"],
                  ...(selectedElement.tagName === "TABLE"
                    ? []
                    : [
                        ["front", "In front of text"],
                        ["behind", "Behind text"],
                      ]),
                ].map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </label>
            {["width", "height"].map((key) => (
              <label key={key}>
                {key} (px)
                <input
                  aria-label={`Picture ${key}`}
                  type="number"
                  min="20"
                  max="2000"
                  value={Math.round(
                    parseFloat(
                      selectedElement.style[key as "width" | "height"],
                    ) ||
                      selectedElement.getBoundingClientRect()[
                        key as "width" | "height"
                      ],
                  )}
                  onChange={(e) =>
                    editSelected((el) => {
                      const value = Math.max(
                        20,
                        Math.min(2000, +e.target.value),
                      );
                      const ratio =
                        el.offsetHeight / Math.max(1, el.offsetWidth);
                      el.style[key as "width" | "height"] = `${value}px`;
                      if (el.tagName === "IMG" && !el.dataset.shape)
                        el.style[key === "width" ? "height" : "width"] =
                          `${key === "width" ? value * ratio : value / ratio}px`;
                    })
                  }
                />
              </label>
            ))}
            {selectedElement.dataset.shape && (
              <>
                <label>
                  Fill
                  <input
                    aria-label="Shape fill"
                    type="color"
                    value={selectedElement.dataset.fill ?? "#185abd"}
                    onChange={(e) => recolorShape("fill", e.target.value)}
                  />
                </label>
                <label>
                  Outline
                  <input
                    aria-label="Shape outline"
                    type="color"
                    value={selectedElement.dataset.stroke ?? "#185abd"}
                    onChange={(e) => recolorShape("stroke", e.target.value)}
                  />
                </label>
                <label>
                  Outline width
                  <input
                    aria-label="Shape outline width"
                    type="number"
                    min="0"
                    max="10"
                    value={selectedElement.dataset.strokeWidth ?? "0"}
                    onChange={(e) =>
                      recolorShape(
                        "strokeWidth",
                        String(Math.max(0, Math.min(10, +e.target.value))),
                      )
                    }
                  />
                </label>
              </>
            )}
            <label>
              Rotation
              <input
                type="number"
                aria-label="Picture rotation"
                min="-360"
                max="360"
                value={selectedElement.dataset.rotation ?? "0"}
                onChange={(e) =>
                  editSelected((el) => {
                    el.dataset.rotation = String(
                      Math.max(-360, Math.min(360, +e.target.value)),
                    );
                    el.style.transform = `rotate(${el.dataset.rotation}deg)`;
                  })
                }
              />
            </label>
            <label>
              Border color
              <input
                aria-label="Object border color"
                type="color"
                onChange={(e) =>
                  editSelected((el) => {
                    el.style.border = `2px solid ${e.target.value}`;
                  })
                }
              />
            </label>
            {selectedElement instanceof HTMLImageElement && (
              <label>
                Alt text
                <input
                  aria-label="Picture alt text"
                  value={selectedElement.alt}
                  onChange={(e) =>
                    editSelected((el) => {
                      (el as HTMLImageElement).alt = e.target.value;
                    })
                  }
                />
              </label>
            )}
            {selectedElement.tagName === "TABLE" && (
              <>
                {[
                  ["row", "Add row"],
                  ["column", "Add column"],
                  ["remove-row", "Remove last row"],
                  ["remove-column", "Remove last column"],
                ].map(([action, label]) => (
                  <button key={action} onClick={() => tableEdit(action)}>
                    {label}
                  </button>
                ))}
                <label>
                  Shading
                  <input
                    type="color"
                    aria-label="Table shading"
                    onChange={(e) =>
                      editSelected((el) => {
                        el.style.backgroundColor = e.target.value;
                      })
                    }
                  />
                </label>
              </>
            )}
            <button
              onClick={() => {
                editSelected((el) => el.remove());
                setSelectedElement(null);
              }}
            >
              Delete object
            </button>
          </aside>
        )}
        {comments && (
          <aside className="comment-pane">
            <h3>Comments</h3>
            {(file.comments ?? []).map((c) => (
              <section key={c.id}>
                <p>{c.text}</p>
                <small>{new Date(c.createdAt).toLocaleString()}</small>
                <button
                  aria-label="Delete comment"
                  onClick={() =>
                    onChange({
                      ...file,
                      comments: file.comments?.filter((x) => x.id !== c.id),
                    })
                  }
                >
                  Delete
                </button>
              </section>
            ))}
          </aside>
        )}
      </div>
      {chart && (
        <ChartDialog
          onClose={() => setChart(false)}
          onInsert={(data, title) =>
            command(
              "insertHTML",
              `<p><img src="${data}" alt="${escapeHTML(title)}" style="max-width:100%;width:600px"></p><p><br></p>`,
            )
          }
        />
      )}
      <footer className="statusbar">
        <span>{words} words</span>
        <span>{file.content.text.length} characters</span>
        <div />
        <span>{proof ? "Spelling on" : "Spelling off"}</span>
        <span>{zoom}%</span>
      </footer>
    </>
  );
}

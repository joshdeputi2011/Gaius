import { useEffect, useRef, useState } from "react";
import { PDFDocument, StandardFonts, degrees, rgb } from "pdf-lib";
import * as pdfjs from "pdfjs-dist";
import workerURL from "pdfjs-dist/build/pdf.worker.min.mjs?url";
import type { TextItem } from "pdfjs-dist/types/src/display/api";
import type { OfficeFile } from "../../Engine/typescript/model";
import { Command, Group, Modal, Ribbon, useCommand, useRibbon } from "./ribbon";
import { download, readDataURL } from "./storage";
pdfjs.GlobalWorkerOptions.workerSrc = workerURL;
type TextBox = {
  text: string;
  x: number;
  y: number;
  width: number;
  height: number;
  angle: number;
};
function pageSelection(range: string, count: number) {
  const out = new Set<number>();
  for (const part of range.split(",")) {
    const m = /^(\d+)(?:\s*-\s*(\d+))?$/.exec(part.trim());
    if (!m) throw new Error("Use page numbers like 1,3-5.");
    const a = +m[1],
      b = +(m[2] ?? m[1]);
    if (a < 1 || b > count || b < a)
      throw new Error(`Choose pages between 1 and ${count}.`);
    for (let i = a; i <= b; i++) out.add(i - 1);
  }
  return [...out];
}
export default function PDFEditor({
  file,
  onChange,
  onError,
}: {
  file: OfficeFile;
  onChange: (f: OfficeFile) => void;
  onError?: (e: unknown) => void;
}) {
  const tab = useRibbon(),
    input = useRef<HTMLInputElement>(null),
    merge = useRef<HTMLInputElement>(null),
    images = useRef<HTMLInputElement>(null),
    canvas = useRef<HTMLCanvasElement>(null),
    cancel = useRef(false),
    [doc, setDoc] = useState<pdfjs.PDFDocumentProxy | null>(null),
    [page, setPage] = useState(1),
    [range, setRange] = useState("1"),
    [zoom, setZoom] = useState(1),
    [size, setSize] = useState({ width: 600, height: 800 }),
    [text, setText] = useState<TextBox[]>([]),
    [busy, setBusy] = useState(false),
    [phase, setPhase] = useState(""),
    [target, setTarget] = useState(500),
    [pending, setPending] = useState<Uint8Array | null>(null),
    [result, setResult] = useState(""),
    [edit, setEdit] = useState(false),
    [replacement, setReplacement] = useState<{
      box: TextBox;
      text: string;
    } | null>(null),
    [query, setQuery] = useState(""),
    [find, setFind] = useState(false),
    [textView, setTextView] = useState(false);
  useEffect(() => {
    let active = true;
    setDoc(null);
    if (!file.pdf.dataUrl) return;
    const data = Uint8Array.from(atob(file.pdf.dataUrl.split(",")[1]), (c) =>
        c.charCodeAt(0),
      ),
      base = new URL("pdfjs/", new URL(import.meta.env.BASE_URL, location.href))
        .href,
      task = pdfjs.getDocument({
        data,
        cMapUrl: `${base}cmaps/`,
        cMapPacked: true,
        standardFontDataUrl: `${base}standard_fonts/`,
        wasmUrl: `${base}wasm/`,
        iccUrl: `${base}iccs/`,
      });
    task.promise
      .then((d) => {
        if (active) {
          setDoc(d);
          setPage((p) => Math.min(d.numPages, Math.max(1, p)));
        }
      })
      .catch((e) => {
        if (active) onError?.(e);
      });
    return () => {
      active = false;
      task.destroy();
    };
  }, [file.pdf.dataUrl]);
  useEffect(() => {
    if (!doc || !canvas.current) return;
    let active = true,
      render: pdfjs.RenderTask | undefined;
    doc
      .getPage(page)
      .then(async (p) => {
        const viewport = p.getViewport({ scale: zoom }),
          c = canvas.current;
        if (!active || !c) return;
        c.width = Math.ceil(viewport.width);
        c.height = Math.ceil(viewport.height);
        setSize({ width: viewport.width, height: viewport.height });
        render = p.render({ canvas: c, viewport });
        await render.promise;
        const content = await p.getTextContent();
        if (!active) return;
        const ctx = c.getContext("2d")!;
        const boxes = content.items
          .filter((x): x is TextItem => "str" in x)
          .map((item) => {
            const t = pdfjs.Util.transform(viewport.transform, item.transform),
              height = Math.hypot(t[2], t[3]);
            ctx.font = `${height}px sans-serif`;
            return {
              text: item.str,
              x: t[4],
              y: t[5] - height,
              width: Math.abs(item.width * zoom),
              height,
              angle: Math.atan2(t[1], t[0]),
            };
          });
        setText(boxes);
      })
      .catch((e) => {
        if (active && (e as Error).name !== "RenderingCancelledException")
          onError?.(e);
      });
    return () => {
      active = false;
      render?.cancel();
    };
  }, [doc, page, zoom]);
  useEffect(
    () => () => {
      cancel.current = true;
    },
    [],
  );
  function bytes() {
    return Uint8Array.from(atob(file.pdf.dataUrl.split(",")[1]), (c) =>
      c.charCodeAt(0),
    );
  }
  async function apply(buffer: Uint8Array) {
    if (buffer.length > 40 * 1024 * 1024)
      throw new Error("Processed PDF exceeds the 40 MB workspace limit.");
    const dataUrl = await readDataURL(
      new File([new Uint8Array(buffer)], "output.pdf", {
        type: "application/pdf",
      }),
    );
    onChange({ ...file, pdf: { ...file.pdf, dataUrl } });
    setPending(null);
  }
  async function run(operation: (pdf: PDFDocument) => Promise<PDFDocument>) {
    if (!doc || busy) return;
    setBusy(true);
    setPhase("Processing PDF…");
    try {
      const output = await operation(await PDFDocument.load(bytes()));
      await apply(await output.save());
    } catch (e) {
      onError?.(e);
    } finally {
      setBusy(false);
      setPhase("");
    }
  }
  async function load(f?: File) {
    if (!f) return;
    try {
      if (f.size > 40 * 1024 * 1024)
        throw new Error("Choose a PDF smaller than 40 MB.");
      await PDFDocument.load(await f.arrayBuffer());
      await apply(new Uint8Array(await f.arrayBuffer()));
    } catch (e) {
      onError?.(e);
    }
  }
  async function rasterPage(p: pdfjs.PDFPageProxy, scale: number) {
    const viewport = p.getViewport({ scale }),
      c = document.createElement("canvas");
    c.width = Math.ceil(viewport.width);
    c.height = Math.ceil(viewport.height);
    await p.render({ canvas: c, viewport, background: "white" }).promise;
    return c;
  }
  async function compress() {
    if (!doc || busy) return;
    if (!Number.isFinite(target) || target < 1) {
      onError?.(new Error("Enter a positive target size in KB."));
      return;
    }
    if (doc.numPages > 100) {
      onError?.(
        new Error("Target compression supports up to 100 pages at a time."),
      );
      return;
    }
    setBusy(true);
    cancel.current = false;
    setPending(null);
    setResult("");
    try {
      let best: Uint8Array | null = null;
      const limit = target * 1024;
      for (const [scale, quality] of [
        [1.5, 0.85],
        [1.15, 0.65],
        [0.85, 0.45],
        [0.6, 0.25],
        [0.4, 0.16],
      ]) {
        const out = await PDFDocument.create();
        for (let n = 1; n <= doc.numPages; n++) {
          if (cancel.current)
            throw new Error("Compression cancelled. Original unchanged.");
          setPhase(
            `Compressing page ${n}/${doc.numPages} · ${Math.round(scale * 72)} dpi`,
          );
          const p = await doc.getPage(n),
            c = await rasterPage(
              p,
              Math.min(
                scale,
                1600 / Math.max(p.view[2] - p.view[0], p.view[3] - p.view[1]),
              ),
            ),
            view = p.getViewport({ scale: 1 }),
            img = await out.embedJpg(
              await fetch(c.toDataURL("image/jpeg", quality)).then((r) =>
                r.arrayBuffer(),
              ),
            );
          out
            .addPage([view.width, view.height])
            .drawImage(img, {
              x: 0,
              y: 0,
              width: view.width,
              height: view.height,
            });
          c.width = c.height = 1;
          await new Promise((resolve) => setTimeout(resolve, 0));
        }
        const buffer = await out.save();
        if (!best || buffer.length < best.length) best = buffer;
        if (buffer.length <= limit) break;
      }
      if (!best) return;
      setPending(best);
      setResult(
        `${(best.length / 1024).toFixed(1)} KB · ${best.length <= limit ? "Target reached" : "Target could not be reached at the available settings"} · original ${(bytes().length / 1024).toFixed(1)} KB`,
      );
    } catch (e) {
      onError?.(e);
    } finally {
      setBusy(false);
      setPhase("");
    }
  }
  async function replaceText() {
    if (!doc || !replacement || busy) return;
    setBusy(true);
    try {
      if (Math.abs(replacement.box.angle) > 0.01)
        throw new Error("Replacement currently supports horizontal text.");
      const p = await doc.getPage(page),
        c = await rasterPage(p, 2),
        view = p.getViewport({ scale: 1 }),
        box = replacement.box,
        ctx = c.getContext("2d")!,
        factor = 2 / zoom;
      ctx.fillStyle = "white";
      ctx.fillRect(
        box.x * factor - 1,
        box.y * factor - 2,
        box.width * factor + 3,
        box.height * factor + 5,
      );
      const out = await PDFDocument.create(),
        source = await PDFDocument.load(bytes()),
        font = await out.embedFont(StandardFonts.Helvetica);
      font.encodeText(replacement.text);
      for (let i = 0; i < source.getPageCount(); i++) {
        if (i !== page - 1) {
          const [copy] = await out.copyPages(source, [i]);
          out.addPage(copy);
        } else {
          const image = await out.embedPng(c.toDataURL("image/png")),
            newPage = out.addPage([view.width, view.height]);
          newPage.drawImage(image, {
            x: 0,
            y: 0,
            width: view.width,
            height: view.height,
          });
          newPage.drawText(replacement.text, {
            x: box.x / zoom,
            y: view.height - (box.y + box.height * 0.82) / zoom,
            size: Math.max(5, box.height / zoom),
            font,
            color: rgb(0, 0, 0),
            maxWidth: Math.max(10, view.width - box.x / zoom - 10),
          });
        }
      }
      await apply(await out.save());
      setReplacement(null);
      setEdit(false);
    } catch (e) {
      onError?.(e);
    } finally {
      setBusy(false);
    }
  }
  async function findText() {
    if (!doc || !query) return;
    setBusy(true);
    try {
      for (let offset = 0; offset < doc.numPages; offset++) {
        const n = ((page - 1 + offset) % doc.numPages) + 1,
          content = await (await doc.getPage(n)).getTextContent();
        if (
          content.items.some(
            (i) =>
              "str" in i &&
              i.str.toLocaleLowerCase().includes(query.toLocaleLowerCase()),
          )
        ) {
          setPage(n);
          setResult(`Match on page ${n}`);
          return;
        }
      }
      setResult("No matching text. Scanned pages require OCR.");
    } catch (e) {
      onError?.(e);
    } finally {
      setBusy(false);
    }
  }
  async function exportText() {
    if (!doc) return;
    setBusy(true);
    try {
      const pages: string[] = [];
      for (let i = 1; i <= doc.numPages; i++) {
        const t = await (await doc.getPage(i)).getTextContent();
        pages.push(
          t.items
            .map((i) => ("str" in i ? i.str + (i.hasEOL ? "\n" : " ") : ""))
            .join(""),
        );
      }
      download(pages.join("\n\n"), `${file.title}.txt`, "text/plain");
    } catch (e) {
      onError?.(e);
    } finally {
      setBusy(false);
    }
  }
  useCommand((c) => {
    if (c === "find") {
      setFind(true);
    }
  });
  const enabled = !!doc && !busy;
  return (
    <>
      <input
        ref={input}
        type="file"
        hidden
        accept="application/pdf"
        onChange={(e) => {
          load(e.target.files?.[0]);
          e.target.value = "";
        }}
      />
      <input
        ref={merge}
        type="file"
        hidden
        multiple
        accept="application/pdf"
        onChange={(e) => {
          const files = Array.from(e.target.files ?? []);
          if (files.length)
            run(async (out) => {
              let total = bytes().length;
              for (const f of files) {
                total += f.size;
                if (total > 80 * 1024 * 1024)
                  throw new Error("Combined PDFs must be smaller than 80 MB.");
                const other = await PDFDocument.load(await f.arrayBuffer());
                (await out.copyPages(other, other.getPageIndices())).forEach(
                  (p) => out.addPage(p),
                );
              }
              return out;
            });
          e.target.value = "";
        }}
      />
      <input
        ref={images}
        type="file"
        hidden
        multiple
        accept="image/png,image/jpeg"
        onChange={async (e) => {
          const files = Array.from(e.target.files ?? []);
          e.target.value = "";
          if (!files.length) return;
          setBusy(true);
          try {
            const out = await PDFDocument.create();
            let total = 0;
            if (files.length > 100) throw new Error("Choose up to 100 images.");
            for (const f of files) {
              total += f.size;
              if (total > 80 * 1024 * 1024)
                throw new Error("Combined images must be smaller than 80 MB.");
              const data = await f.arrayBuffer(),
                image =
                  f.type === "image/png"
                    ? await out.embedPng(data)
                    : await out.embedJpg(data),
                p = out.addPage([image.width, image.height]);
              p.drawImage(image, {
                x: 0,
                y: 0,
                width: image.width,
                height: image.height,
              });
            }
            await apply(await out.save());
          } catch (err) {
            onError?.(err);
          } finally {
            setBusy(false);
          }
        }}
      />
      <Ribbon>
        {tab === "Home" && (
          <>
            <Group label="PDF">
              <Command
                label="Open PDF"
                glyph="▤"
                disabled={busy}
                onClick={() => input.current?.click()}
              />
              <Command
                label="Merge files"
                glyph="⊕"
                disabled={!enabled}
                onClick={() => merge.current?.click()}
              />
              <Command
                label="Rotate selection"
                glyph="↻"
                disabled={!enabled}
                onClick={() =>
                  run(async (out) => {
                    for (const i of pageSelection(range, out.getPageCount())) {
                      const p = out.getPage(i);
                      p.setRotation(degrees(p.getRotation().angle + 90));
                    }
                    return out;
                  })
                }
              />
              <Command
                label="Find text"
                glyph="⌕"
                disabled={!enabled}
                onClick={() => setFind(!find)}
              />
            </Group>
            <Group label="Export">
              <Command
                label="Export PDF"
                glyph="↓"
                disabled={!enabled}
                onClick={() =>
                  download(
                    new Uint8Array(bytes()),
                    `${file.title.replace(/\.pdf$/i, "")}.pdf`,
                    "application/pdf",
                  )
                }
              />
            </Group>
          </>
        )}
        {tab === "Organize" && (
          <>
            <Group label="Pages">
              <Command
                label="Merge files"
                glyph="⊕"
                disabled={!enabled}
                onClick={() => merge.current?.click()}
              />
              <Command
                label="Keep selected pages"
                glyph="▤"
                disabled={!enabled}
                onClick={() =>
                  run(async (out) => {
                    const result = await PDFDocument.create();
                    (
                      await result.copyPages(
                        out,
                        pageSelection(range, out.getPageCount()),
                      )
                    ).forEach((p) => result.addPage(p));
                    return result;
                  })
                }
              />
              <Command
                label="Delete selected pages"
                glyph="×"
                disabled={!enabled}
                onClick={() =>
                  run(async (out) => {
                    const indices = pageSelection(range, out.getPageCount());
                    if (indices.length === out.getPageCount())
                      throw new Error("Keep at least one page.");
                    indices
                      .sort((a, b) => b - a)
                      .forEach((i) => out.removePage(i));
                    return out;
                  })
                }
              />
              <Command
                label="Reverse pages"
                glyph="⇄"
                disabled={!enabled}
                onClick={() =>
                  run(async (out) => {
                    const result = await PDFDocument.create();
                    (
                      await result.copyPages(
                        out,
                        out.getPageIndices().reverse(),
                      )
                    ).forEach((p) => result.addPage(p));
                    return result;
                  })
                }
              />
              <Command
                label="Rotate selection"
                glyph="↻"
                disabled={!enabled}
                onClick={() =>
                  run(async (out) => {
                    for (const i of pageSelection(range, out.getPageCount())) {
                      const p = out.getPage(i);
                      p.setRotation(degrees(p.getRotation().angle + 90));
                    }
                    return out;
                  })
                }
              />
            </Group>
          </>
        )}
        {tab === "Edit" && (
          <>
            <Group label="Text">
              <Command
                label="Select text"
                glyph="I"
                active={!edit}
                onClick={() => setEdit(false)}
              />
              <Command
                label="Replace text (flatten)"
                glyph="T↔"
                active={edit}
                disabled={!enabled}
                onClick={() => setEdit(!edit)}
              />
              <Command
                label="Add text"
                glyph="T+"
                disabled={!enabled}
                onClick={() => {
                  const value = prompt(
                    "Text to add at the top of selected pages",
                  );
                  if (value)
                    run(async (out) => {
                      const font = await out.embedFont(StandardFonts.Helvetica);
                      for (const i of pageSelection(
                        range,
                        out.getPageCount(),
                      )) {
                        const p = out.getPage(i);
                        p.drawText(value, {
                          x: 36,
                          y: p.getHeight() - 50,
                          font,
                          size: 16,
                          color: rgb(0.1, 0.2, 0.4),
                          maxWidth: p.getWidth() - 72,
                        });
                      }
                      return out;
                    });
                }}
              />
            </Group>
            <Group label="Page marks">
              <Command
                label="Watermark"
                glyph="◈"
                disabled={!enabled}
                onClick={() => {
                  const value = prompt("Watermark text", "DRAFT");
                  if (value)
                    run(async (out) => {
                      const font = await out.embedFont(StandardFonts.Helvetica);
                      for (const p of out.getPages())
                        p.drawText(value, {
                          x: 60,
                          y: p.getHeight() / 2,
                          size: 48,
                          font,
                          rotate: degrees(30),
                          opacity: 0.2,
                          color: rgb(0.2, 0.3, 0.5),
                        });
                      return out;
                    });
                }}
              />
              <Command
                label="Page numbers"
                glyph="#"
                disabled={!enabled}
                onClick={() =>
                  run(async (out) => {
                    const font = await out.embedFont(StandardFonts.Helvetica);
                    out
                      .getPages()
                      .forEach((p, i) =>
                        p.drawText(`${i + 1} / ${out.getPageCount()}`, {
                          x: p.getWidth() / 2 - 15,
                          y: 20,
                          size: 10,
                          font,
                        }),
                      );
                    return out;
                  })
                }
              />
              <Command
                label="Crop margins"
                glyph="⌗"
                disabled={!enabled}
                onClick={() => {
                  const value = prompt(
                    "Crop all four margins (points, 72 points = 1 inch)",
                    "18",
                  );
                  if (value === null) return;
                  const amount = +value;
                  run(async (out) => {
                    if (!Number.isFinite(amount) || amount < 0)
                      throw new Error("Enter a positive margin.");
                    for (const i of pageSelection(range, out.getPageCount())) {
                      const p = out.getPage(i),
                        b = p.getCropBox();
                      if (amount * 2 >= Math.min(b.width, b.height))
                        throw new Error("Margin is too large.");
                      p.setCropBox(
                        b.x + amount,
                        b.y + amount,
                        b.width - amount * 2,
                        b.height - amount * 2,
                      );
                    }
                    return out;
                  });
                }}
              />
            </Group>
          </>
        )}
        {tab === "Convert" && (
          <>
            <Group label="Convert">
              <Command
                label="Images to PDF"
                glyph="▧→▤"
                disabled={busy}
                onClick={() => images.current?.click()}
              />
              <Command
                label="Page to PNG"
                glyph="▧"
                disabled={!enabled}
                onClick={() =>
                  canvas.current?.toBlob((blob) => {
                    if (blob)
                      download(
                        blob,
                        `${file.title}-page-${page}.png`,
                        "image/png",
                      );
                  })
                }
              />
              <Command
                label="Extract text"
                glyph="¶"
                disabled={!enabled}
                onClick={exportText}
              />
            </Group>
            <p className="ribbon-hint">
              Text extraction uses existing text. Scanned documents need an OCR
              engine.
            </p>
          </>
        )}
        {tab === "Compress" && (
          <>
            <Group label="Target size">
              <label className="ribbon-field">
                Target (KB)
                <input
                  aria-label="Target PDF size KB"
                  type="number"
                  min="1"
                  value={target}
                  onChange={(e) => {
                    setTarget(+e.target.value);
                    setPending(null);
                  }}
                />
              </label>
              <Command
                label="Compress to target"
                glyph="⇲"
                disabled={!enabled}
                onClick={compress}
              />
              <Command
                label="Compact document"
                glyph="⇲"
                disabled={!enabled}
                onClick={async () => {
                  if (!doc || busy) return;
                  setBusy(true);
                  try {
                    const original = bytes(),
                      out = await PDFDocument.load(original),
                      buffer = await out.save();
                    if (buffer.length >= original.length)
                      setResult("Already compact. Original kept.");
                    else {
                      setPending(buffer);
                      setResult(
                        `Compacted to ${(buffer.length / 1024).toFixed(1)} KB from ${(original.length / 1024).toFixed(1)} KB.`,
                      );
                    }
                  } catch (e) {
                    onError?.(e);
                  } finally {
                    setBusy(false);
                  }
                }}
              />
            </Group>
            <p className="ribbon-hint">
              Target compression rebuilds pages as images. It removes selectable
              text, links, forms, and accessibility tags. Review the resulting
              size before applying.
            </p>
          </>
        )}
        {tab === "View" && (
          <>
            <Group label="Zoom">
              <Command
                label="Zoom out"
                glyph="−"
                disabled={!enabled}
                onClick={() => setZoom((z) => Math.max(0.4, z - 0.2))}
              />
              <Command
                label={`${Math.round(zoom * 100)}%`}
                glyph="⌕"
                disabled={!enabled}
                onClick={() => setZoom(1)}
              />
              <Command
                label="Zoom in"
                glyph="+"
                disabled={!enabled}
                onClick={() => setZoom((z) => Math.min(2.5, z + 0.2))}
              />
            </Group>
            <Group label="Read">
              <Command
                label="Select text"
                glyph="I"
                onClick={() => setEdit(false)}
              />
              <Command
                label="Page text"
                glyph="¶"
                onClick={() => setTextView(!textView)}
              />
            </Group>
          </>
        )}
      </Ribbon>
      {find && (
        <div className="find-bar">
          <input
            aria-label="Find PDF text"
            placeholder="Search existing text"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") findText();
            }}
          />
          <button disabled={busy} onClick={findText}>
            Find
          </button>
          <button aria-label="Close find" onClick={() => setFind(false)}>
            ✕
          </button>
        </div>
      )}
      <div className="pdf-workspace">
        <aside className="adjustments">
          <h3>Page tools</h3>
          <p>
            {doc
              ? `${doc.numPages} pages in this file`
              : "Open a PDF to get started."}
          </p>
          <label>
            Selected pages
            <input
              aria-label="Selected PDF pages"
              value={range}
              placeholder="1,3-5"
              onChange={(e) => setRange(e.target.value)}
            />
          </label>
          <button
            className="button"
            disabled={!enabled}
            onClick={() =>
              run(async (out) => {
                const result = await PDFDocument.create();
                (
                  await result.copyPages(
                    out,
                    pageSelection(range, out.getPageCount()),
                  )
                ).forEach((p) => result.addPage(p));
                return result;
              })
            }
          >
            Keep selected pages
          </button>
          <button
            className="button"
            disabled={!enabled}
            onClick={() =>
              run(async (out) => {
                const indices = pageSelection(range, out.getPageCount());
                if (indices.length === out.getPageCount())
                  throw new Error("Keep at least one page.");
                indices.sort((a, b) => b - a).forEach((i) => out.removePage(i));
                return out;
              })
            }
          >
            Delete selected pages
          </button>
          {edit && (
            <p className="hint">
              Click an existing text line to replace it. The edited page will be
              flattened.
            </p>
          )}
          {busy && (
            <>
              <p className="pdf-progress" role="status">
                {phase || "Processing…"}
              </p>
              <button
                className="button"
                onClick={() => (cancel.current = true)}
              >
                Cancel compression
              </button>
            </>
          )}
          {result && (
            <p
              className={`compression-result ${pending && pending.length > target * 1024 ? "unmet" : ""}`}
              role="status"
            >
              {result}
            </p>
          )}
          {pending && (
            <>
              <button
                className="primary-button"
                onClick={() => apply(pending).catch(onError)}
              >
                Apply compressed result
              </button>
              <button
                className="button"
                onClick={() =>
                  download(
                    new Uint8Array(pending),
                    `${file.title}-compressed.pdf`,
                    "application/pdf",
                  )
                }
              >
                Download result
              </button>
            </>
          )}
          <p className="hint">
            Changes affect your workspace copy. Export to save a portable PDF.
          </p>
        </aside>
        <div className="pdf-canvas-stage">
          {doc ? (
            <>
              <div className="pdf-navigation">
                <button
                  aria-label="Previous PDF page"
                  disabled={page <= 1 || busy}
                  onClick={() => setPage(page - 1)}
                >
                  ←
                </button>
                <span>
                  Page {page} of {doc.numPages}
                </span>
                <button
                  aria-label="Next PDF page"
                  disabled={page >= doc.numPages || busy}
                  onClick={() => setPage(page + 1)}
                >
                  →
                </button>
                <button
                  disabled={busy}
                  onClick={() => setZoom((z) => Math.max(0.4, z - 0.2))}
                >
                  −
                </button>
                <span>{Math.round(zoom * 100)}%</span>
                <button
                  disabled={busy}
                  onClick={() => setZoom((z) => Math.min(2.5, z + 0.2))}
                >
                  +
                </button>
              </div>
              <div
                className="pdf-page"
                style={{ width: size.width, height: size.height }}
              >
                <canvas ref={canvas} aria-label={`PDF page ${page}`} />
                <div className={`pdf-text-layer ${edit ? "edit-mode" : ""}`}>
                  {text.map((t, i) => (
                    <span
                      key={i}
                      style={{
                        left: t.x,
                        top: t.y,
                        fontSize: t.height,
                        width: t.width,
                        height: t.height,
                        transform: `rotate(${t.angle}rad)`,
                      }}
                      onClick={() => {
                        if (edit) setReplacement({ box: t, text: t.text });
                      }}
                    >
                      {t.text}
                    </span>
                  ))}
                </div>
              </div>
              {textView && (
                <textarea
                  aria-label="Extracted PDF page text"
                  readOnly
                  rows={10}
                  value={text.map((t) => t.text).join("\n")}
                />
              )}
            </>
          ) : (
            <button
              className="upload-area"
              onClick={() => input.current?.click()}
            >
              <h2>Your everyday PDF tools</h2>
              <span>
                Read, select text, organize, convert, and compress locally.
              </span>
            </button>
          )}
        </div>
      </div>
      <footer className="statusbar">
        <span>{doc ? `${doc.numPages} pages` : "No PDF open"}</span>
        <div />
        <span>{edit ? "Text replacement mode" : "Select text"}</span>
        <span>Processed on this device</span>
      </footer>
      {replacement && (
        <Modal title="Replace PDF text" onClose={() => setReplacement(null)}>
          <div className="dialog-form">
            <p>
              Rebuild this page with a white patch and replacement text. Other
              text on this page becomes an image; fonts, links, forms and
              accessibility structure on the page are removed. Best for
              horizontal text on white backgrounds.
            </p>
            <label>
              Replacement text
              <input
                aria-label="Replacement PDF text"
                value={replacement.text}
                onChange={(e) =>
                  setReplacement({ ...replacement, text: e.target.value })
                }
              />
            </label>
            <button
              className="primary-button"
              disabled={busy}
              onClick={replaceText}
            >
              Replace and flatten this page
            </button>
          </div>
        </Modal>
      )}
    </>
  );
}

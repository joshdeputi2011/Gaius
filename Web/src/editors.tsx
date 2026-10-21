import { useEffect, useMemo, useRef, useState } from "react";
import {
  TextBold24Regular,
  TextItalic24Regular,
  TextUnderline24Regular,
  TextAlignLeft24Regular,
  TextAlignCenter24Regular,
  TextAlignRight24Regular,
  TextBulletList24Regular,
  TextNumberListLtr24Regular,
  ArrowUndo24Regular,
  ArrowRedo24Regular,
  Add24Regular,
  Delete24Regular,
  ArrowDownload24Regular,
  ArrowClockwise24Regular,
  Image24Regular,
  DocumentPdf24Regular,
  Play24Regular,
  Dismiss24Regular,
  ArrowUp24Regular,
  ArrowDown24Regular,
  Table24Regular,
  Crop24Regular,
} from "@fluentui/react-icons";
import { PDFDocument, degrees } from "pdf-lib";
import {
  columnName,
  exportCSV,
  type OfficeFile,
} from "../../Engine/typescript/model";
import { Workbook } from "../../Engine/typescript/formula";
import { download, readDataURL, safeHTML } from "./storage";
import { Tool } from "./App";
type Props = {
  file: OfficeFile;
  onChange: (file: OfficeFile) => void;
  onError?: (error: unknown) => void;
};
export function WriteEditor({ file, onChange }: Props) {
  const ref = useRef<HTMLDivElement>(null),
    selection = useRef<Range | null>(null);
  useEffect(() => {
    if (ref.current) ref.current.innerHTML = safeHTML(file.content.html);
  }, [file.id]);
  function remember() {
    const s = window.getSelection();
    if (s?.rangeCount && ref.current?.contains(s.anchorNode))
      selection.current = s.getRangeAt(0).cloneRange();
  }
  function change() {
    if (ref.current)
      onChange({
        ...file,
        content: {
          html: safeHTML(ref.current.innerHTML),
          text: ref.current.innerText,
        },
      });
  }
  function command(cmd: string, value?: string) {
    ref.current?.focus();
    if (
      selection.current &&
      ref.current?.contains(selection.current.startContainer)
    ) {
      const s = window.getSelection();
      s?.removeAllRanges();
      s?.addRange(selection.current);
    }
    document.execCommand(cmd, false, value);
    remember();
    change();
  }
  const words = file.content.text.trim()
    ? file.content.text.trim().split(/\s+/).length
    : 0;
  return (
    <>
      <div className="ribbon">
        <div className="ribbon-group">
          <div className="ribbon-controls">
            <Tool title="Undo" onClick={() => command("undo")}>
              <ArrowUndo24Regular />
            </Tool>
            <Tool title="Redo" onClick={() => command("redo")}>
              <ArrowRedo24Regular />
            </Tool>
          </div>
          <span>History</span>
        </div>
        <div className="ribbon-group">
          <div className="ribbon-controls">
            <select
              aria-label="Paragraph style"
              defaultValue="p"
              onChange={(e) => command("formatBlock", e.target.value)}
            >
              <option value="p">Normal text</option>
              <option value="h1">Heading 1</option>
              <option value="h2">Heading 2</option>
              <option value="h3">Heading 3</option>
            </select>
            <select
              aria-label="Font"
              onChange={(e) => command("fontName", e.target.value)}
            >
              <option>Calibri</option>
              <option>Arial</option>
              <option>Georgia</option>
              <option>Times New Roman</option>
            </select>
            <select
              aria-label="Font size"
              defaultValue="3"
              onChange={(e) => command("fontSize", e.target.value)}
            >
              {[1, 2, 3, 4, 5, 6, 7].map((n, i) => (
                <option key={n} value={n}>
                  {[8, 10, 12, 14, 18, 24, 36][i]}
                </option>
              ))}
            </select>
          </div>
          <div className="ribbon-controls">
            <Tool title="Bold (Ctrl+B)" onClick={() => command("bold")}>
              <TextBold24Regular />
            </Tool>
            <Tool title="Italic (Ctrl+I)" onClick={() => command("italic")}>
              <TextItalic24Regular />
            </Tool>
            <Tool
              title="Underline (Ctrl+U)"
              onClick={() => command("underline")}
            >
              <TextUnderline24Regular />
            </Tool>
            <label className="color-label">
              A
              <input
                aria-label="Text color"
                type="color"
                defaultValue="#242424"
                onChange={(e) => command("foreColor", e.target.value)}
              />
            </label>
          </div>
          <span>Font</span>
        </div>
        <div className="ribbon-group">
          <div className="ribbon-controls">
            <Tool title="Align left" onClick={() => command("justifyLeft")}>
              <TextAlignLeft24Regular />
            </Tool>
            <Tool title="Align center" onClick={() => command("justifyCenter")}>
              <TextAlignCenter24Regular />
            </Tool>
            <Tool title="Align right" onClick={() => command("justifyRight")}>
              <TextAlignRight24Regular />
            </Tool>
          </div>
          <div className="ribbon-controls">
            <Tool
              title="Bulleted list"
              onClick={() => command("insertUnorderedList")}
            >
              <TextBulletList24Regular />
            </Tool>
            <Tool
              title="Numbered list"
              onClick={() => command("insertOrderedList")}
            >
              <TextNumberListLtr24Regular />
            </Tool>
            <Tool
              title="Insert 3 by 3 table"
              onClick={() =>
                command(
                  "insertHTML",
                  '<table border="1"><tbody>' +
                    Array.from(
                      { length: 3 },
                      () => "<tr><td>Cell</td><td>Cell</td><td>Cell</td></tr>",
                    ).join("") +
                    "</tbody></table><p><br></p>",
                )
              }
            >
              <Table24Regular />
            </Tool>
          </div>
          <span>Paragraph</span>
        </div>
        <div className="ribbon-group">
          <button
            className="button"
            onClick={() =>
              download(
                `<!doctype html><html><meta charset="utf-8"><title>${file.title.replaceAll("<", "&lt;")}</title><body>${safeHTML(file.content.html)}</body></html>`,
                `${file.title}.html`,
                "text/html",
              )
            }
          >
            <ArrowDownload24Regular />
            HTML
          </button>
          <button
            className="button"
            onClick={() =>
              download(file.content.text, `${file.title}.txt`, "text/plain")
            }
          >
            Plain text
          </button>
          <span>Export</span>
        </div>
      </div>
      <div className="document-workspace">
        <div className="page-ruler">
          {Array.from({ length: 17 }, (_, i) => (
            <span key={i}>{i % 2 === 0 ? i / 2 : "·"}</span>
          ))}
        </div>
        <article className="paper">
          <div
            ref={ref}
            className="word-content"
            contentEditable
            suppressContentEditableWarning
            role="textbox"
            aria-label="Document content"
            aria-multiline="true"
            data-placeholder="Start writing…"
            onInput={change}
            onKeyUp={remember}
            onMouseUp={remember}
            onBlur={remember}
            onPaste={(e) => {
              e.preventDefault();
              document.execCommand(
                "insertText",
                false,
                e.clipboardData.getData("text/plain"),
              );
              change();
            }}
          />
        </article>
      </div>
      <div className="statusbar">
        <span>{words} words</span>
        <span>{file.content.text.length} characters</span>
        <div />
        <span>Print layout</span>
        <span>100%</span>
      </div>
    </>
  );
}
export function SheetEditor({ file, onChange, onError }: Props) {
  const [selected, setSelected] = useState("A1"),
    [draft, setDraft] = useState(""),
    [editing, setEditing] = useState(false);
  const workbook = useMemo(
    () => new Workbook(file.sheet.cells),
    [file.sheet.cells],
  );
  useEffect(() => {
    setDraft(file.sheet.cells[selected] ?? "");
  }, [file.sheet.cells, selected]);
  function commit(value = draft) {
    if (value !== (file.sheet.cells[selected] ?? "")) {
      const cells = { ...file.sheet.cells };
      if (value) cells[selected] = value;
      else delete cells[selected];
      onChange({ ...file, sheet: { ...file.sheet, cells } });
    }
    setEditing(false);
  }
  function move(dx: number, dy: number) {
    const col = selected.match(/[A-Z]+/)![0];
    let c = 0;
    for (const ch of col) c = c * 26 + ch.charCodeAt(0) - 64;
    const row = Number(selected.match(/\d+/)![0]);
    setSelected(
      `${columnName(Math.max(0, Math.min(file.sheet.columns - 1, c - 1 + dx)))}${Math.max(1, Math.min(file.sheet.rows, row + dy))}`,
    );
  }
  function key(e: React.KeyboardEvent) {
    if (e.key === "Enter" || e.key === "Tab") {
      e.preventDefault();
      commit();
      move(
        e.key === "Tab" ? (e.shiftKey ? -1 : 1) : 0,
        e.key === "Enter" ? (e.shiftKey ? -1 : 1) : 0,
      );
    } else if (e.key === "Escape") {
      setDraft(file.sheet.cells[selected] ?? "");
      setEditing(false);
    }
  }
  return (
    <>
      <div className="ribbon compact">
        <div className="ribbon-group">
          <button
            className="button"
            onClick={() =>
              onChange({
                ...file,
                sheet: { ...file.sheet, rows: file.sheet.rows + 25 },
              })
            }
          >
            <Add24Regular />
            25 rows
          </button>
          <span>Insert</span>
        </div>
        <div className="ribbon-group">
          <button
            className="button"
            onClick={() => {
              setDraft("");
              commit("");
            }}
          >
            <Delete24Regular />
            Clear cell
          </button>
          <span>Edit</span>
        </div>
        <div className="ribbon-group">
          <button
            className="button"
            onClick={() => {
              try {
                download(
                  exportCSV(file.sheet.cells, (r) => workbook.display(r)),
                  `${file.title}.csv`,
                  "text/csv",
                );
              } catch (e) {
                onError?.(e);
              }
            }}
          >
            <ArrowDownload24Regular />
            Export CSV
          </button>
          <span>Export</span>
        </div>
        <div className="formula-help">
          Functions: SUM · AVERAGE · MIN · MAX · COUNT · IF · ROUND · ABS
        </div>
      </div>
      <div className="formula-bar">
        <span className="cell-address">{selected}</span>
        <span className="fx">ƒx</span>
        <input
          aria-label="Cell value or formula"
          placeholder="Enter a value or a formula, e.g. =SUM(A1:A10)"
          value={draft}
          onFocus={() => setEditing(true)}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={key}
          onBlur={() => commit()}
        />
      </div>
      <div className="sheet-scroll">
        <table className="spreadsheet" aria-label="Spreadsheet">
          <thead>
            <tr>
              <th className="corner" />
              {Array.from({ length: file.sheet.columns }, (_, c) => (
                <th key={c}>{columnName(c)}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {Array.from({ length: Math.min(file.sheet.rows, 500) }, (_, r) => (
              <tr key={r}>
                <th>{r + 1}</th>
                {Array.from({ length: file.sheet.columns }, (_, c) => {
                  const address = `${columnName(c)}${r + 1}`,
                    active = selected === address;
                  return (
                    <td
                      key={c}
                      className={`${active ? "selected-cell" : ""} ${file.sheet.cells[address]?.startsWith("=") ? "calculated" : ""}`}
                      onClick={() => {
                        if (editing) commit();
                        setSelected(address);
                      }}
                      onDoubleClick={() => setEditing(true)}
                    >
                      {active ? (
                        <input
                          aria-label={address}
                          value={editing ? draft : workbook.display(address)}
                          onFocus={() => {
                            setEditing(true);
                            setDraft(file.sheet.cells[address] ?? "");
                          }}
                          onChange={(e) => {
                            setDraft(e.target.value);
                            setEditing(true);
                          }}
                          onBlur={() => commit()}
                          onKeyDown={(e) => {
                            if (
                              !editing &&
                              [
                                "ArrowUp",
                                "ArrowDown",
                                "ArrowLeft",
                                "ArrowRight",
                              ].includes(e.key)
                            ) {
                              e.preventDefault();
                              move(
                                e.key === "ArrowLeft"
                                  ? -1
                                  : e.key === "ArrowRight"
                                    ? 1
                                    : 0,
                                e.key === "ArrowUp"
                                  ? -1
                                  : e.key === "ArrowDown"
                                    ? 1
                                    : 0,
                              );
                            } else key(e);
                          }}
                        />
                      ) : (
                        workbook.display(address)
                      )}
                    </td>
                  );
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="statusbar">
        <span className="sheet-tab">Sheet 1</span>
        <span>{Object.keys(file.sheet.cells).length} filled cells</span>
        <div />
        <span>
          {file.sheet.rows > 500
            ? "Showing first 500 rows · all rows included in export"
            : "Ready"}
        </span>
        <span>100%</span>
      </div>
    </>
  );
}
export function SlidesEditor({ file, onChange }: Props) {
  const [index, setIndex] = useState(0),
    [presenting, setPresenting] = useState(false);
  const slide = file.slides[Math.min(index, file.slides.length - 1)];
  useEffect(() => {
    const h = (e: KeyboardEvent) => {
      if (!presenting) return;
      if (e.key === "Escape") setPresenting(false);
      if (e.key === "ArrowRight" || e.key === " ")
        setIndex((i) => Math.min(file.slides.length - 1, i + 1));
      if (e.key === "ArrowLeft") setIndex((i) => Math.max(0, i - 1));
    };
    window.addEventListener("keydown", h);
    return () => window.removeEventListener("keydown", h);
  }, [presenting, file.slides.length]);
  function patch(values: Partial<typeof slide>) {
    onChange({
      ...file,
      slides: file.slides.map((s, i) =>
        i === index ? { ...s, ...values } : s,
      ),
    });
  }
  function add() {
    const slides = [
      ...file.slides,
      {
        id: crypto.randomUUID(),
        title: "New slide",
        body: "Add your content",
        background: "#ffffff",
      },
    ];
    onChange({ ...file, slides });
    setIndex(slides.length - 1);
  }
  function reorder(delta: number) {
    const target = index + delta;
    if (target < 0 || target >= file.slides.length) return;
    const slides = [...file.slides];
    [slides[index], slides[target]] = [slides[target], slides[index]];
    onChange({ ...file, slides });
    setIndex(target);
  }
  const escape = (s: string) =>
    s
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll('"', "&quot;");
  function exportSlide() {
    if (!slide) return;
    const body = slide.body
      .split("\n")
      .map(
        (line, i) => `<tspan x="90" dy="${i ? 42 : 0}">${escape(line)}</tspan>`,
      )
      .join("");
    download(
      `<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="720"><rect width="1280" height="720" fill="${slide.background}"/><rect x="90" y="140" width="60" height="6" fill="#c43e1c"/><text x="90" y="250" font-family="Arial" font-size="54" fill="#242424">${escape(slide.title)}</text><text x="90" y="340" font-family="Arial" font-size="28" fill="#616161">${body}</text></svg>`,
      `${file.title}-slide-${index + 1}.svg`,
      "image/svg+xml",
    );
  }
  return (
    <>
      <div className="ribbon compact">
        <button className="button" onClick={add}>
          <Add24Regular />
          New slide
        </button>
        <Tool
          title="Delete slide"
          disabled={file.slides.length <= 1}
          onClick={() => {
            onChange({
              ...file,
              slides: file.slides.filter((_, i) => i !== index),
            });
            setIndex(Math.max(0, index - 1));
          }}
        >
          <Delete24Regular />
        </Tool>
        <Tool
          title="Move slide up"
          disabled={index === 0}
          onClick={() => reorder(-1)}
        >
          <ArrowUp24Regular />
        </Tool>
        <Tool
          title="Move slide down"
          disabled={index === file.slides.length - 1}
          onClick={() => reorder(1)}
        >
          <ArrowDown24Regular />
        </Tool>
        <label className="inline-label">
          Background{" "}
          <input
            aria-label="Slide background"
            type="color"
            value={slide?.background ?? "#ffffff"}
            onChange={(e) => patch({ background: e.target.value })}
          />
        </label>
        <div className="header-spacer" />
        <button className="button" onClick={exportSlide}>
          <ArrowDownload24Regular />
          SVG
        </button>
        <button
          className="primary-button"
          onClick={() => setPresenting(true)}
          disabled={!slide}
        >
          <Play24Regular />
          Present
        </button>
      </div>
      <div className="slides-workspace">
        <aside className="slide-thumbnails">
          {file.slides.map((s, i) => (
            <button
              key={s.id}
              className={`slide-thumbnail ${i === index ? "active" : ""}`}
              onClick={() => setIndex(i)}
            >
              <span>{i + 1}</span>
              <div style={{ background: s.background }}>
                <strong>{s.title}</strong>
                <small>{s.body.slice(0, 80)}</small>
              </div>
            </button>
          ))}
        </aside>
        <div className="slide-stage">
          {slide ? (
            <div className="slide" style={{ background: slide.background }}>
              <div className="slide-accent" />
              <textarea
                className="slide-title"
                aria-label="Slide title"
                value={slide.title}
                onChange={(e) => patch({ title: e.target.value })}
              />
              <textarea
                className="slide-body"
                aria-label="Slide body"
                value={slide.body}
                onChange={(e) => patch({ body: e.target.value })}
              />
              <span className="slide-number">{index + 1}</span>
            </div>
          ) : (
            <button className="button" onClick={add}>
              Add first slide
            </button>
          )}
        </div>
      </div>
      <div className="statusbar">
        <span>
          Slide {index + 1} of {file.slides.length}
        </span>
        <div />
        <span>16:9 widescreen</span>
      </div>
      {presenting && slide && (
        <div
          className="presentation-overlay"
          role="dialog"
          aria-label="Presentation"
        >
          <Tool title="Exit presentation" onClick={() => setPresenting(false)}>
            <Dismiss24Regular />
          </Tool>
          <div
            className="slide"
            style={{ background: slide.background }}
            onClick={() =>
              setIndex((i) => Math.min(file.slides.length - 1, i + 1))
            }
          >
            <div className="slide-accent" />
            <h1>{slide.title}</h1>
            <p>{slide.body}</p>
            <span className="slide-number">
              {index + 1} / {file.slides.length}
            </span>
          </div>
          <span>← → to navigate · Esc to exit</span>
        </div>
      )}
    </>
  );
}
export function ImageEditor({ file, onChange, onError }: Props) {
  const input = useRef<HTMLInputElement>(null),
    canvas = useRef<HTMLCanvasElement>(null),
    [size, setSize] = useState({ w: 0, h: 0 }),
    [crop, setCrop] = useState(false),
    [cropBox, setCropBox] = useState({ x: 0, y: 0, w: 100, h: 100 });
  useEffect(() => {
    if (!file.image.dataUrl) return;
    let active = true;
    const image = new Image();
    image.onload = () => {
      if (!active || !canvas.current) return;
      const angle = ((file.image.rotation % 360) + 360) % 360,
        swap = angle === 90 || angle === 270;
      const c = canvas.current;
      c.width = swap ? image.height : image.width;
      c.height = swap ? image.width : image.height;
      const ctx = c.getContext("2d")!;
      ctx.filter = `brightness(${file.image.brightness}%) contrast(${file.image.contrast}%) grayscale(${file.image.grayscale ? 1 : 0})`;
      ctx.translate(c.width / 2, c.height / 2);
      ctx.rotate((angle * Math.PI) / 180);
      ctx.drawImage(image, -image.width / 2, -image.height / 2);
      setSize({ w: c.width, h: c.height });
    };
    image.onerror = () =>
      onError?.(new Error("This image could not be decoded."));
    image.src = file.image.dataUrl;
    return () => {
      active = false;
    };
  }, [file.image]);
  function patch(v: Partial<OfficeFile["image"]>) {
    onChange({ ...file, image: { ...file.image, ...v } });
  }
  async function load(f?: File) {
    if (!f) return;
    try {
      if (!/^image\/(png|jpeg|webp|gif|bmp)$/.test(f.type))
        throw new Error("Choose a PNG, JPEG, WebP, GIF, or BMP image.");
      patch({
        dataUrl: await readDataURL(f),
        rotation: 0,
        brightness: 100,
        contrast: 100,
        grayscale: false,
      });
    } catch (e) {
      onError?.(e);
    }
  }
  function applyCrop() {
    const c = canvas.current;
    if (!c) return;
    const x = Math.floor((c.width * cropBox.x) / 100),
      y = Math.floor((c.height * cropBox.y) / 100),
      w = Math.min(
        c.width - x,
        Math.max(1, Math.floor((c.width * cropBox.w) / 100)),
      ),
      h = Math.min(
        c.height - y,
        Math.max(1, Math.floor((c.height * cropBox.h) / 100)),
      );
    const out = document.createElement("canvas");
    out.width = w;
    out.height = h;
    out.getContext("2d")!.drawImage(c, x, y, w, h, 0, 0, w, h);
    patch({
      dataUrl: out.toDataURL("image/png"),
      rotation: 0,
      brightness: 100,
      contrast: 100,
      grayscale: false,
    });
    setCrop(false);
  }
  return (
    <>
      <input
        type="file"
        ref={input}
        hidden
        accept="image/png,image/jpeg,image/webp,image/gif,image/bmp"
        onChange={(e) => load(e.target.files?.[0])}
      />
      <div className="ribbon compact">
        <button className="button" onClick={() => input.current?.click()}>
          <Image24Regular />
          Open image
        </button>
        <Tool
          title="Rotate clockwise"
          disabled={!file.image.dataUrl}
          onClick={() => patch({ rotation: file.image.rotation + 90 })}
        >
          <ArrowClockwise24Regular />
        </Tool>
        <Tool
          title="Crop image"
          disabled={!file.image.dataUrl}
          onClick={() => setCrop(!crop)}
        >
          <Crop24Regular />
        </Tool>
        <label className="inline-label">
          <input
            type="checkbox"
            checked={file.image.grayscale}
            onChange={(e) => patch({ grayscale: e.target.checked })}
          />
          Grayscale
        </label>
        <div className="header-spacer" />
        <button
          className="primary-button"
          disabled={!file.image.dataUrl}
          onClick={() =>
            canvas.current?.toBlob((blob) => {
              if (blob) download(blob, `${file.title}.png`, "image/png");
            })
          }
        >
          <ArrowDownload24Regular />
          Export PNG
        </button>
      </div>
      <div className="image-workspace">
        <aside className="adjustments">
          <h3>Adjustments</h3>
          {(["brightness", "contrast"] as const).map((key) => (
            <label key={key}>
              {key[0].toUpperCase() + key.slice(1)}
              <span>{file.image[key]}%</span>
              <input
                aria-label={key}
                type="range"
                min="0"
                max="200"
                value={file.image[key]}
                onChange={(e) => patch({ [key]: Number(e.target.value) })}
              />
            </label>
          ))}
          <button
            className="button"
            onClick={() =>
              patch({
                brightness: 100,
                contrast: 100,
                grayscale: false,
                rotation: 0,
              })
            }
          >
            Reset adjustments
          </button>
          {crop && (
            <div className="crop-controls">
              <h3>Crop (% of image)</h3>
              {(["x", "y", "w", "h"] as const).map((k) => (
                <label key={k}>
                  {{ x: "Left", y: "Top", w: "Width", h: "Height" }[k]}
                  <input
                    aria-label={`Crop ${k}`}
                    type="number"
                    min={k === "x" || k === "y" ? 0 : 1}
                    max={k === "x" || k === "y" ? 99 : 100}
                    value={cropBox[k]}
                    onChange={(e) =>
                      setCropBox((v) => ({
                        ...v,
                        [k]: Math.max(
                          k === "x" || k === "y" ? 0 : 1,
                          Math.min(
                            k === "x" || k === "y" ? 99 : 100,
                            Number(e.target.value),
                          ),
                        ),
                      }))
                    }
                  />
                </label>
              ))}
              <button className="primary-button" onClick={applyCrop}>
                Apply crop
              </button>
            </div>
          )}
        </aside>
        <div className="image-stage">
          {file.image.dataUrl ? (
            <canvas ref={canvas} aria-label="Edited image" />
          ) : (
            <button
              className="upload-area"
              onClick={() => input.current?.click()}
            >
              <Image24Regular />
              <h2>Start with an image</h2>
              <span>Open a PNG, JPEG, WebP, GIF, or BMP</span>
            </button>
          )}
        </div>
      </div>
      <div className="statusbar">
        <span>{size.w ? `${size.w} × ${size.h} pixels` : "No image open"}</span>
        <div />
        <span>Edits are kept in your Gaius file</span>
      </div>
    </>
  );
}
export function PDFEditor({ file, onChange, onError }: Props) {
  const input = useRef<HTMLInputElement>(null),
    mergeInput = useRef<HTMLInputElement>(null),
    [pages, setPages] = useState(0),
    [range, setRange] = useState("1"),
    [busy, setBusy] = useState(false),
    [preview, setPreview] = useState("");
  useEffect(() => {
    let active = true,
      url = "";
    if (file.pdf.dataUrl) {
      fetch(file.pdf.dataUrl)
        .then((r) => r.blob())
        .then(async (blob) => {
          const doc = await PDFDocument.load(await blob.arrayBuffer());
          if (active) {
            setPages(doc.getPageCount());
            url = URL.createObjectURL(blob);
            setPreview(url);
          }
        })
        .catch((e) => {
          if (active) onError?.(e);
        });
    } else {
      setPages(0);
      setPreview("");
    }
    return () => {
      active = false;
      if (url) URL.revokeObjectURL(url);
    };
  }, [file.pdf.dataUrl]);
  async function run(operation: (doc: PDFDocument) => Promise<PDFDocument>) {
    setBusy(true);
    try {
      const bytes = await fetch(file.pdf.dataUrl).then((r) => r.arrayBuffer());
      const doc = await operation(await PDFDocument.load(bytes));
      const buffer = await doc.save();
      const blob = new Blob([new Uint8Array(buffer)], {
        type: "application/pdf",
      });
      onChange({
        ...file,
        pdf: {
          dataUrl: await readDataURL(
            new File([blob], "document.pdf", { type: "application/pdf" }),
          ),
        },
      });
    } catch (e) {
      onError?.(e);
    } finally {
      setBusy(false);
    }
  }
  async function load(f?: File) {
    if (!f) return;
    try {
      const dataUrl = (await readDataURL(f)).replace(
        /^data:[^;]*;/,
        "data:application/pdf;",
      );
      await PDFDocument.load(await f.arrayBuffer());
      onChange({ ...file, pdf: { dataUrl } });
    } catch (e) {
      onError?.(e);
    }
  }
  function selectedPages() {
    const result: number[] = [];
    for (const part of range.split(",")) {
      const m = part.trim().match(/^(\d+)(?:\s*-\s*(\d+))?$/);
      if (!m) throw new Error("Use page numbers like 1,3-5.");
      const start = Number(m[1]),
        end = Number(m[2] ?? m[1]);
      if (start < 1 || end > pages || end < start)
        throw new Error(`Choose pages between 1 and ${pages}.`);
      for (let p = start; p <= end; p++)
        if (!result.includes(p - 1)) result.push(p - 1);
    }
    return result;
  }
  return (
    <>
      <input
        ref={input}
        hidden
        type="file"
        accept="application/pdf"
        onChange={(e) => load(e.target.files?.[0])}
      />
      <input
        ref={mergeInput}
        hidden
        type="file"
        accept="application/pdf"
        multiple
        onChange={(e) => {
          const files = Array.from(e.target.files ?? []);
          run(async (doc) => {
            for (const f of files) {
              if (f.size > 40 * 1024 * 1024)
                throw new Error("Each PDF must be under 40 MB.");
              const source = await PDFDocument.load(await f.arrayBuffer());
              const copied = await doc.copyPages(
                source,
                source.getPageIndices(),
              );
              copied.forEach((p) => doc.addPage(p));
            }
            return doc;
          });
          e.target.value = "";
        }}
      />
      <div className="ribbon compact">
        <button
          className="button"
          disabled={busy}
          onClick={() => input.current?.click()}
        >
          <DocumentPdf24Regular />
          Open PDF
        </button>
        <button
          className="button"
          disabled={!pages || busy}
          onClick={() => mergeInput.current?.click()}
        >
          <Add24Regular />
          Merge files
        </button>
        <button
          className="button"
          disabled={!pages || busy}
          onClick={() =>
            run(async (doc) => {
              for (const i of selectedPages()) {
                const p = doc.getPage(i);
                p.setRotation(degrees(p.getRotation().angle + 90));
              }
              return doc;
            })
          }
        >
          <ArrowClockwise24Regular />
          Rotate selection
        </button>
        <div className="header-spacer" />
        <button
          className="primary-button"
          disabled={!pages || busy}
          onClick={async () => {
            try {
              download(
                await fetch(file.pdf.dataUrl).then((r) => r.blob()),
                `${file.title.replace(/\.pdf$/i, "")}.pdf`,
                "application/pdf",
              );
            } catch (e) {
              onError?.(e);
            }
          }}
        >
          <ArrowDownload24Regular />
          Export PDF
        </button>
      </div>
      <div className="pdf-workspace">
        <aside className="adjustments">
          <h3>Page tools</h3>
          <p>
            {pages
              ? `${pages} pages in this file`
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
            disabled={!pages || busy}
            onClick={() =>
              run(async (doc) => {
                const output = await PDFDocument.create();
                const copied = await output.copyPages(doc, selectedPages());
                copied.forEach((p) => output.addPage(p));
                return output;
              })
            }
          >
            Keep selected pages
          </button>
          <button
            className="button"
            disabled={!pages || busy}
            onClick={() =>
              run(async (doc) => {
                const selection = selectedPages();
                if (selection.length === pages)
                  throw new Error("Keep at least one page.");
                selection
                  .sort((a, b) => b - a)
                  .forEach((i) => doc.removePage(i));
                return doc;
              })
            }
          >
            <Delete24Regular />
            Delete selected pages
          </button>
          <p className="hint">
            Changes update this local copy. Export a PDF to save it outside the
            browser.
          </p>
          {busy && <p role="status">Processing PDF…</p>}
        </aside>
        <div className="pdf-stage">
          {preview ? (
            <iframe title="PDF preview" src={preview} />
          ) : (
            <button
              className="upload-area"
              onClick={() => input.current?.click()}
            >
              <DocumentPdf24Regular />
              <h2>Your everyday PDF tools</h2>
              <span>Merge, extract, delete, rotate, and export pages.</span>
            </button>
          )}
        </div>
      </div>
      <div className="statusbar">
        <span>{pages ? `${pages} pages` : "No PDF open"}</span>
        <div />
        <span>Processed on this device</span>
      </div>
    </>
  );
}

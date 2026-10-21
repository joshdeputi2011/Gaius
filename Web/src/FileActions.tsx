import { useEffect, useState } from "react";
import type { OfficeFile } from "../../Engine/typescript/model";
import { columnName } from "../../Engine/typescript/model";
import { Workbook } from "../../Engine/typescript/formula";
import { exportOffice } from "../../Engine/typescript/ooxml";
import { download, safeHTML } from "./storage";
import { Modal } from "./ribbon";
import { SlideObjectView } from "./visuals";
export function PrintContent({ file }: { file: OfficeFile }) {
  if (file.kind === "document")
    return (
      <article
        className={`paper print-document ${file.layout?.orientation ?? "portrait"}`}
        style={{
          padding:
            file.layout?.margins === "narrow"
              ? "24px"
              : file.layout?.margins === "wide"
                ? "90px"
                : undefined,
        }}
      >
        <div
          className="word-content"
          style={{
            columnCount: file.layout?.columns ?? 1,
            fontFamily: String(file.content.fontFamily ?? "Calibri"),
          }}
          dangerouslySetInnerHTML={{ __html: safeHTML(file.content.html) }}
        />
      </article>
    );
  if (file.kind === "presentation")
    return (
      <>
        {file.slides.map((s, i) => (
          <section
            className="slide object-slide print-slide"
            style={{ background: s.background }}
            key={s.id}
          >
            <h1>{s.title}</h1>
            <p>{s.body}</p>
            {s.objects?.map((o) => (
              <SlideObjectView key={o.id} object={o} />
            ))}
            <small className="slide-number">{i + 1}</small>
          </section>
        ))}
      </>
    );
  if (file.kind === "spreadsheet") {
    const refs = Object.keys(file.sheet.cells),
      lastRow = Math.max(1, ...refs.map((a) => +(a.match(/\d+/)?.[0] ?? 1))),
      lastCol = Math.max(
        1,
        ...refs.map((a) => {
          let c = 0;
          for (const x of a.match(/[A-Z]+/)?.[0] ?? "A")
            c = c * 26 + x.charCodeAt(0) - 64;
          return c;
        }),
      ),
      wb = new Workbook(file.sheet.cells);
    if (lastRow * lastCol > 20000)
      return (
        <p>
          This print range exceeds 20,000 cells. Export CSV or reduce the used
          range.
        </p>
      );
    return (
      <article className="paper print-sheet">
        <h2>
          {file.title} · {file.sheet.name ?? "Sheet 1"}
        </h2>
        <table>
          <thead>
            <tr>
              <th />
              {Array.from({ length: lastCol }, (_, c) => (
                <th key={c}>{columnName(c)}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {Array.from({ length: lastRow }, (_, r) => (
              <tr key={r}>
                <th>{r + 1}</th>
                {Array.from({ length: lastCol }, (_, c) => {
                  const ref = `${columnName(c)}${r + 1}`,
                    style = file.sheet.styles?.[ref];
                  return (
                    <td
                      style={{
                        fontWeight: style?.bold ? 700 : undefined,
                        color: style?.color,
                        background: style?.background,
                      }}
                      key={c}
                    >
                      {wb.display(ref)}
                    </td>
                  );
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </article>
    );
  }
  if (file.kind === "image")
    return file.image.dataUrl ? (
      <img
        className="print-image"
        src={
          document
            .querySelector<HTMLCanvasElement>(".image-stage canvas")
            ?.toDataURL() ?? file.image.dataUrl
        }
        alt={file.title}
      />
    ) : (
      <p>Open an image first.</p>
    );
  return file.pdf.dataUrl ? (
    <iframe
      className="print-pdf"
      title="PDF print preview"
      src={file.pdf.dataUrl}
    />
  ) : (
    <p>Open a PDF first.</p>
  );
}
export function PrintPreview({
  file,
  onClose,
}: {
  file: OfficeFile;
  onClose: () => void;
}) {
  const [orientation, setOrientation] = useState(
      file.layout?.orientation ??
        (file.kind === "presentation" ? "landscape" : "portrait"),
    ),
    [scale, setScale] = useState(80);
  useEffect(() => {
    const style = document.createElement("style");
    style.media = "print";
    style.textContent = `@page { size: A4 ${orientation}; margin: 12mm; }`;
    document.head.append(style);
    return () => style.remove();
  }, [orientation]);
  function print() {
    const host = window as unknown as {
      chrome?: { webview?: { postMessage: (v: unknown) => void } };
    };
    if (host.chrome?.webview)
      host.chrome.webview.postMessage({ action: "print" });
    else if (file.kind === "pdf") {
      const frame = document.querySelector<HTMLIFrameElement>(".print-pdf");
      frame?.contentWindow?.focus();
      frame?.contentWindow?.print();
    } else window.print();
  }
  return (
    <Modal title="Print preview" onClose={onClose} wide>
      <div className="print-settings">
        <label>
          Orientation
          <select
            aria-label="Print orientation"
            value={orientation}
            onChange={(e) =>
              setOrientation(e.target.value as typeof orientation)
            }
          >
            <option value="portrait">Portrait</option>
            <option value="landscape">Landscape</option>
          </select>
        </label>
        <label>
          Preview zoom
          <input
            type="range"
            aria-label="Preview zoom"
            min="40"
            max="100"
            value={scale}
            onChange={(e) => setScale(+e.target.value)}
          />
        </label>
        <button className="primary-button" onClick={print}>
          Print / Save PDF
        </button>
      </div>
      <div className="print-scroll">
        <div className="print-output" style={{ zoom: scale / 100 }}>
          <PrintContent
            file={{ ...file, layout: { ...file.layout, orientation } }}
          />
        </div>
      </div>
    </Modal>
  );
}
export async function shareFile(file: OfficeFile) {
  const result = new File(
    [JSON.stringify(file)],
    `${file.title.replace(/[<>:"/\\|?*]/g, "_")}.gaius`,
    { type: "application/json" },
  );
  if (navigator.canShare?.({ files: [result] })) {
    try {
      await navigator.share({ files: [result], title: file.title });
    } catch (e) {
      if ((e as Error).name !== "AbortError") throw e;
    }
  } else download(result, result.name, "application/json");
}
export function SaveAs({
  file,
  onClose,
  onSave,
  onError,
}: {
  file: OfficeFile;
  onClose: () => void;
  onSave: (f: OfficeFile) => void;
  onError: (e: unknown) => void;
}) {
  const [name, setName] = useState(
      `${file.title.replace(/\.[^.]+$/, "")} — Copy`,
    ),
    [format, setFormat] = useState("gaius");
  const options =
    file.kind === "document"
      ? ["gaius", "docx", "html", "txt"]
      : file.kind === "spreadsheet"
        ? ["gaius", "xlsx"]
        : file.kind === "presentation"
          ? ["gaius", "pptx"]
          : file.kind === "pdf"
            ? ["gaius", "pdf"]
            : ["gaius", "png"];
  async function save() {
    if (!name.trim()) return;
    try {
      const copy = {
        ...structuredClone(file),
        id: crypto.randomUUID(),
        title: name.trim(),
        updatedAt: new Date().toISOString(),
      };
      if (format === "gaius") {
        onSave(copy);
        download(
          JSON.stringify(copy, null, 2),
          `${copy.title}.gaius`,
          "application/json",
        );
      } else if (["docx", "xlsx", "pptx"].includes(format)) {
        const out = exportOffice(copy);
        download(new Uint8Array(out.bytes), `${copy.title}.${out.extension}`);
      } else if (format === "html")
        download(
          `<!doctype html><meta charset="utf-8">${safeHTML(copy.content.html)}`,
          `${copy.title}.html`,
          "text/html",
        );
      else if (format === "txt")
        download(copy.content.text, `${copy.title}.txt`, "text/plain");
      else if (format === "pdf") {
        if (!copy.pdf.dataUrl) throw new Error("Open a PDF first.");
        download(
          await fetch(copy.pdf.dataUrl).then((r) => r.blob()),
          `${copy.title}.pdf`,
          "application/pdf",
        );
      } else {
        const canvas = document.querySelector<HTMLCanvasElement>(
          ".image-stage canvas",
        );
        if (!canvas) throw new Error("Open an image first.");
        canvas.toBlob((blob) => {
          if (blob) download(blob, `${copy.title}.png`, "image/png");
        });
      }
      onClose();
    } catch (e) {
      onError(e);
    }
  }
  return (
    <Modal title="Save as" onClose={onClose}>
      <div className="dialog-form">
        <label>
          File name
          <input
            autoFocus
            aria-label="Save as file name"
            value={name}
            onChange={(e) => setName(e.target.value)}
          />
        </label>
        <label>
          Format
          <select
            aria-label="Save as format"
            value={format}
            onChange={(e) => setFormat(e.target.value)}
          >
            {options.map((o) => (
              <option value={o} key={o}>
                {o === "gaius"
                  ? "Gaius file + workspace copy"
                  : o.toUpperCase()}
              </option>
            ))}
          </select>
        </label>
        {["docx", "xlsx", "pptx"].includes(format) && (
          <p className="hint">
            Office export supports a subset of formatting and objects. Save a
            Gaius copy to preserve all editor features.
          </p>
        )}
        <button
          className="primary-button"
          disabled={!name.trim()}
          onClick={save}
        >
          Save copy
        </button>
      </div>
    </Modal>
  );
}

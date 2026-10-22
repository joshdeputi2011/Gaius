import { useRef, useState } from "react";
import { flushSync } from "react-dom";
import { parseCSV } from "../../Engine/typescript/model";
import { Chart } from "./visuals";
import { Modal } from "./ribbon";
export default function ChartDialog({
  onClose,
  onInsert,
}: {
  onClose: () => void;
  onInsert: (data: string, title: string) => void;
}) {
  const [title, setTitle] = useState("Results"),
    [data, setData] = useState("Q1,12\nQ2,18\nQ3,24"),
    [type, setType] = useState<"bar" | "line" | "pie">("bar"),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false),
    preview = useRef<HTMLDivElement>(null);
  const cells: Record<string, string> = {};
  let count = 0;
  try {
    for (const row of parseCSV(data)) {
      if (row.length < 2 || !row[1].trim() || !Number.isFinite(Number(row[1])))
        continue;
      if (count === 50) break;
      count++;
      cells[`A${count}`] = row[0];
      cells[`B${count}`] = row[1];
    }
  } catch {
    /* Incomplete CSV stays editable. */
  }
  async function insert() {
    const svg = preview.current?.querySelector("svg");
    if (!svg || !count) return;
    setBusy(true);
    setError("");
    let url = "";
    try {
      const source = new XMLSerializer().serializeToString(svg);
      url = URL.createObjectURL(new Blob([source], { type: "image/svg+xml" }));
      const image = new Image();
      await new Promise<void>((resolve, reject) => {
        image.onload = () => resolve();
        image.onerror = () =>
          reject(new Error("Chart image could not be generated."));
        image.src = url;
      });
      const canvas = document.createElement("canvas");
      canvas.width = 900;
      canvas.height = 540;
      const ctx = canvas.getContext("2d")!;
      ctx.fillStyle = "white";
      ctx.fillRect(0, 0, 900, 540);
      ctx.fillStyle = "#25344b";
      ctx.font = "bold 26px Arial";
      ctx.fillText(title, 40, 42, 820);
      ctx.drawImage(image, 0, 60, 900, 480);
      const png = canvas.toDataURL("image/png");
      flushSync(onClose);
      onInsert(png, title);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      if (url) URL.revokeObjectURL(url);
      setBusy(false);
    }
  }
  return (
    <Modal title="Insert chart" onClose={onClose}>
      <div className="dialog-form">
        <label>
          Title
          <input
            aria-label="Chart title"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
          />
        </label>
        <label>
          Type
          <select
            aria-label="Document chart type"
            value={type}
            onChange={(e) => setType(e.target.value as typeof type)}
          >
            <option value="bar">Bar</option>
            <option value="line">Line</option>
            <option value="pie">Pie</option>
          </select>
        </label>
        <label>
          Data: label,value on each line
          <textarea
            aria-label="Document chart data"
            rows={6}
            value={data}
            onChange={(e) => setData(e.target.value)}
          />
        </label>
        <div ref={preview}>
          <Chart
            spec={{
              id: "preview",
              type,
              title,
              range: `A1:B${Math.max(1, count)}`,
            }}
            cells={cells}
          />
        </div>
        <p className="hint">
          {count} numeric rows. The chart is inserted as a picture so it travels
          with your document.
        </p>
        {error && <p role="alert">{error}</p>}
        <button
          className="primary-button"
          disabled={!count || busy}
          onClick={insert}
        >
          {busy ? "Creating chart…" : "Insert chart picture"}
        </button>
      </div>
    </Modal>
  );
}

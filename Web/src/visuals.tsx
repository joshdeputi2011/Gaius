import { Workbook } from "../../Engine/typescript/formula";
import {
  columnName,
  type ChartSpec,
  type SlideObject,
} from "../../Engine/typescript/model";
import { rangeAddresses } from "../../Engine/typescript/sheet-tools";
import { shapeKinds } from "../../Engine/typescript/drawing";
import { ShapeVisual } from "./drawing";
export const escapeHTML = (s: string) =>
  s.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll('"', "&quot;");
const colors = [
  "#185abd",
  "#20a58a",
  "#e6a23c",
  "#885bc1",
  "#d45464",
  "#398bc5",
];
export function chartValues(cells: Record<string, string>, range: string) {
  const refs = rangeAddresses(range),
    cols = [...new Set(refs.map((r) => r.match(/[A-Z]+/)![0]))],
    rows = [...new Set(refs.map((r) => +r.match(/\d+/)![0]))],
    wb = new Workbook(cells);
  return rows
    .map((r) => ({
      label: cols.length > 1 ? wb.display(`${cols[0]}${r}`) : `${cols[0]}${r}`,
      value: Number(wb.display(`${cols.at(-1)}${r}`)),
    }))
    .filter((d) => Number.isFinite(d.value))
    .slice(0, 50);
}
export function Chart({
  spec,
  cells,
}: {
  spec: ChartSpec;
  cells: Record<string, string>;
}) {
  let data: ReturnType<typeof chartValues> = [];
  try {
    data = chartValues(cells, spec.range);
  } catch {
    /* Invalid imported range is shown empty. */
  }
  const max = Math.max(1, ...data.map((d) => Math.abs(d.value))),
    step = 520 / Math.max(1, data.length),
    total = data.reduce((s, d) => s + Math.abs(d.value), 0);
  let angle = 0;
  return (
    <figure className="office-chart">
      <figcaption>{spec.title}</figcaption>
      <svg
        viewBox="0 0 600 320"
        role="img"
        aria-label={`${spec.type} chart: ${data.map((d) => `${d.label} ${d.value}`).join(", ")}`}
      >
        {spec.type === "pie" ? (
          data.map((d, i) => {
            const start = angle;
            angle += (Math.abs(d.value) / (total || 1)) * Math.PI * 2;
            const x1 = 200 + 110 * Math.cos(start),
              y1 = 150 + 110 * Math.sin(start),
              x2 = 200 + 110 * Math.cos(angle),
              y2 = 150 + 110 * Math.sin(angle);
            return (
              <g key={i}>
                {angle - start >= Math.PI * 2 - 0.001 ? (
                  <circle
                    cx="200"
                    cy="150"
                    r="110"
                    fill={colors[i % colors.length]}
                  />
                ) : (
                  <path
                    d={`M200 150 L${x1} ${y1} A110 110 0 ${angle - start > Math.PI ? 1 : 0} 1 ${x2} ${y2} Z`}
                    fill={colors[i % colors.length]}
                  />
                )}
                <text x="340" y={35 + i * 18} fontSize="12">
                  {d.label}: {d.value}
                </text>
              </g>
            );
          })
        ) : (
          <>
            <line x1="45" y1="255" x2="580" y2="255" stroke="#bec8d5" />
            {spec.type === "line" && (
              <polyline
                points={data
                  .map(
                    (d, i) =>
                      `${50 + i * step + step / 2},${145 - (d.value / max) * 100}`,
                  )
                  .join(" ")}
                fill="none"
                stroke="#185abd"
                strokeWidth="3"
              />
            )}
            {data.map((d, i) => (
              <g key={i}>
                {spec.type === "bar" ? (
                  <rect
                    x={50 + i * step + 4}
                    y={d.value >= 0 ? 145 - (d.value / max) * 100 : 145}
                    width={Math.max(2, step - 10)}
                    height={(Math.abs(d.value) / max) * 100}
                    rx="3"
                    fill={colors[i % colors.length]}
                  />
                ) : (
                  <circle
                    cx={50 + i * step + step / 2}
                    cy={145 - (d.value / max) * 100}
                    r="4"
                    fill="#185abd"
                  />
                )}
                <text
                  x={50 + i * step + step / 2}
                  y="275"
                  textAnchor="middle"
                  fontSize="11"
                >
                  {d.label.slice(0, 12)}
                </text>
                <text
                  x={50 + i * step + step / 2}
                  y={135 - (d.value / max) * 100}
                  textAnchor="middle"
                  fontSize="11"
                >
                  {d.value}
                </text>
              </g>
            ))}
          </>
        )}
        {!data.length && (
          <text x="60" y="150">
            Select a range with numeric data.
          </text>
        )}
      </svg>
    </figure>
  );
}
export function SlideObjectView({
  object,
  presenting = false,
}: {
  object: SlideObject;
  presenting?: boolean;
}) {
  const o = object;
  return (
    <div
      className={`slide-object ${o.kind} ${presenting ? `object-animation-${o.animation ?? "none"}` : ""}`}
      style={{
        left: `${o.x}%`,
        top: `${o.y}%`,
        width: `${o.width}%`,
        height: `${o.height}%`,
        color: o.color,
        fontSize: `${o.fontSize ?? 24}px`,
        transform: `rotate(${o.rotation ?? 0}deg)`,
        opacity: o.opacity ?? 1,
        background: "transparent",
        clipPath: "none",
        borderRadius: 0,
        border:
          o.kind === "image"
            ? `${o.strokeWidth ?? 0}px solid ${o.stroke ?? "#185abd"}`
            : undefined,
      }}
    >
      {o.kind === "image" ? (
        <img
          src={o.dataUrl}
          alt={o.alt || o.text || "Slide image"}
          style={{ objectFit: "contain", width: "100%", height: "100%" }}
        />
      ) : o.kind === "text" || o.kind === "wordart" ? (
        o.text
      ) : shapeKinds.includes(o.kind as (typeof shapeKinds)[number]) ? (
        <ShapeVisual
          kind={o.kind}
          fill={o.color}
          stroke={o.stroke}
          strokeWidth={o.strokeWidth}
        />
      ) : null}
    </div>
  );
}
export function sampleChartPNG() {
  const canvas = document.createElement("canvas");
  canvas.width = 720;
  canvas.height = 360;
  const ctx = canvas.getContext("2d")!;
  ctx.fillStyle = "#f5f8fc";
  ctx.fillRect(0, 0, 720, 360);
  ctx.fillStyle = "#24334a";
  ctx.font = "bold 24px Arial";
  ctx.fillText("Quarterly results", 35, 40);
  [110, 180, 150, 230].forEach((v, i) => {
    ctx.fillStyle = colors[i];
    ctx.fillRect(50 + i * 165, 300 - v, 100, v);
    ctx.fillStyle = "#24334a";
    ctx.font = "18px Arial";
    ctx.fillText(`Q${i + 1}`, 80 + i * 165, 330);
  });
  return canvas.toDataURL();
}
export function defaultRange(cells: Record<string, string>) {
  const refs = Object.keys(cells),
    row = Math.max(2, ...refs.map((a) => +(a.match(/\d+/)?.[0] ?? 1))),
    col = Math.max(
      1,
      ...refs.map((a) =>
        (a.match(/[A-Z]+/)?.[0] ?? "A").length > 1 ? 1 : a.charCodeAt(0) - 65,
      ),
    );
  return `A1:${columnName(Math.min(col, 1))}${Math.min(row, 50)}`;
}

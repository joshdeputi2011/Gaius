import { shapePath } from "../../Engine/typescript/drawing";
export function ShapeVisual({
  kind,
  fill,
  stroke = "#185abd",
  strokeWidth = 0,
}: {
  kind: string;
  fill: string;
  stroke?: string;
  strokeWidth?: number;
}) {
  return (
    <svg
      className="shape-visual"
      viewBox="0 0 100 100"
      preserveAspectRatio="none"
      aria-hidden="true"
    >
      <path
        d={shapePath(kind)}
        fill={kind === "line" ? "none" : fill}
        stroke={stroke}
        strokeWidth={kind === "line" ? Math.max(1, strokeWidth) : strokeWidth}
        vectorEffect="non-scaling-stroke"
      />
    </svg>
  );
}
export function shapePNG(
  kind: string,
  fill: string,
  stroke = "#185abd",
  strokeWidth = 0,
  width = 640,
  height = 320,
) {
  const canvas = document.createElement("canvas");
  canvas.width = width;
  canvas.height = height;
  const ctx = canvas.getContext("2d")!;
  ctx.scale(width / 100, height / 100);
  ctx.fillStyle = fill;
  ctx.strokeStyle = stroke;
  ctx.lineWidth = kind === "line" ? Math.max(1, strokeWidth) : strokeWidth;
  const path = new Path2D(shapePath(kind));
  if (kind !== "line") ctx.fill(path);
  if (ctx.lineWidth > 0) ctx.stroke(path);
  return canvas.toDataURL("image/png");
}

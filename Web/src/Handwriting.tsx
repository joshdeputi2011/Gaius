import { createContext, useContext, useEffect, useRef, useState } from "react";
import { flushSync } from "react-dom";
import { Command, Group, Modal } from "./ribbon";

export const DrawingContext = createContext<(insert?: (png: string) => void) => void>(() => {});
export function DrawCommands({ onInsert }: { onInsert?: (png: string) => void }) {
  const open = useContext(DrawingContext);
  return <Group label="Handwriting"><Command label="Pen" glyph="✎" onClick={() => open(onInsert)} /><Command label="Highlighter" glyph="▰" onClick={() => open(onInsert)} /></Group>;
}
type Stroke = { color: string; width: number; highlight: boolean; points: { x: number; y: number }[] };
export default function Handwriting({ onClose, onInsert }: { onClose: () => void; onInsert: (png: string) => void }) {
  const canvas = useRef<HTMLCanvasElement>(null), strokes = useRef<Stroke[]>([]), undone = useRef<Stroke[]>([]), pointer = useRef<number | null>(null);
  const [tool, setTool] = useState("Pen"), [color, setColor] = useState("#185abd"), [width, setWidth] = useState(3), [revision, setRevision] = useState(0);
  const redraw = () => setRevision(n => n + 1);
  useEffect(() => {
    const ctx = canvas.current?.getContext("2d"); if (!ctx) return;
    ctx.fillStyle = "#ffffff"; ctx.fillRect(0, 0, 960, 540); ctx.lineCap = "round"; ctx.lineJoin = "round";
    for (const stroke of strokes.current) {
      if (!stroke.points.length) continue;
      ctx.strokeStyle = ctx.fillStyle = stroke.color; ctx.lineWidth = stroke.width; ctx.globalAlpha = stroke.highlight ? .4 : 1;
      const first = stroke.points[0]; ctx.beginPath(); ctx.moveTo(first.x, first.y);
      if (stroke.points.length === 1) { ctx.arc(first.x, first.y, stroke.width / 2, 0, Math.PI * 2); ctx.fill(); }
      else { for (const point of stroke.points.slice(1)) ctx.lineTo(point.x, point.y); ctx.stroke(); }
    }
    ctx.globalAlpha = 1;
  }, [revision]);
  function point(event: React.PointerEvent<HTMLCanvasElement>) {
    const rect = event.currentTarget.getBoundingClientRect();
    const p = { x: Math.max(0, Math.min(960, (event.clientX - rect.left) / rect.width * 960)), y: Math.max(0, Math.min(540, (event.clientY - rect.top) / rect.height * 540)) };
    if (tool === "Eraser") strokes.current = strokes.current.filter(s => !s.points.some(q => Math.hypot(p.x - q.x, p.y - q.y) < 16));
    else { const active = strokes.current.at(-1); if (active && active.points.length < 15000) active.points.push(p); }
    redraw();
  }
  return <Modal title="Handwriting" onClose={onClose}>
    <div className="ink-tools">
      <label>Tool<select aria-label="Drawing tool" value={tool} onChange={e => setTool(e.target.value)}>{["Pen", "Highlighter", "Eraser"].map(name => <option key={name}>{name}</option>)}</select></label>
      <label>Color<input type="color" aria-label="Ink color" value={color} onChange={e => setColor(e.target.value)} /></label>
      <label>Width<input aria-label="Pen width" type="range" min="1" max="24" value={width} onChange={e => setWidth(+e.target.value)} /></label>
      <button onClick={() => { const stroke = strokes.current.pop(); if (stroke) undone.current.push(stroke); redraw(); }}>Undo stroke</button>
      <button onClick={() => { const stroke = undone.current.pop(); if (stroke) strokes.current.push(stroke); redraw(); }}>Redo stroke</button>
      <button onClick={() => { undone.current.push(...strokes.current.reverse()); strokes.current = []; redraw(); }}>Clear</button>
    </div>
    <canvas ref={canvas} width="960" height="540" className="handwriting-canvas" aria-label="Handwriting canvas"
      onPointerDown={event => { if (pointer.current !== null) return; pointer.current = event.pointerId; event.currentTarget.setPointerCapture(event.pointerId); if (tool !== "Eraser") strokes.current.push({ color, width: tool === "Highlighter" ? Math.max(12, width) : width, highlight: tool === "Highlighter", points: [] }); undone.current = []; point(event); }}
      onPointerMove={event => { if (pointer.current === event.pointerId) point(event); }}
      onPointerUp={event => { if (pointer.current === event.pointerId) { pointer.current = null; event.currentTarget.releasePointerCapture(event.pointerId); } }}
      onPointerCancel={() => pointer.current = null} />
    <div className="dialog-actions"><button className="button primary" onClick={() => { const png = canvas.current!.toDataURL("image/png"); flushSync(onClose); onInsert(png); }}>Insert drawing</button></div>
  </Modal>;
}

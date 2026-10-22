import { useEffect, useRef, useState } from "react";
import type {
  OfficeFile,
  Slide,
  SlideObject,
} from "../../Engine/typescript/model";
import { Command, Group, Ribbon, useCommand, useRibbon } from "./ribbon";
import { SlideObjectView, escapeHTML } from "./visuals";
import { clipboardImage, download, readDataURL } from "./storage";
import { DrawCommands } from "./Handwriting";
import { shapeKinds, shapeLabel } from "../../Engine/typescript/drawing";
export default function SlidesEditor({
  file,
  onChange,
  onError,
}: {
  file: OfficeFile;
  onChange: (f: OfficeFile) => void;
  onError?: (e: unknown) => void;
}) {
  const tab = useRibbon(),
    [index, setIndex] = useState(0),
    [presenting, setPresenting] = useState(false),
    [selected, setSelected] = useState<string | null>(null),
    [notes, setNotes] = useState(false),
    [grid, setGrid] = useState(false),
    [auto, setAuto] = useState(false),
    [thumbnails, setThumbnails] = useState(true),
    input = useRef<HTMLInputElement>(null),
    stage = useRef<HTMLDivElement>(null),
    drag = useRef<{ x: number; y: number; left: number; top: number } | null>(
      null,
    );
  const current = Math.min(index, file.slides.length - 1),
    slide = file.slides[current],
    object = slide?.objects?.find((o) => o.id === selected);
  useEffect(() => {
    function key(e: KeyboardEvent) {
      if (!presenting) return;
      if (e.key === "Escape") setPresenting(false);
      if (["ArrowRight", " ", "PageDown"].includes(e.key)) {
        e.preventDefault();
        setIndex((i) => Math.min(file.slides.length - 1, i + 1));
      }
      if (["ArrowLeft", "PageUp"].includes(e.key))
        setIndex((i) => Math.max(0, i - 1));
    }
    window.addEventListener("keydown", key);
    return () => window.removeEventListener("keydown", key);
  }, [presenting, file.slides.length]);
  useEffect(() => {
    if (!presenting || !auto) return;
    const timer = window.setTimeout(
      () => setIndex((i) => (i < file.slides.length - 1 ? i + 1 : 0)),
      Math.max(1000, (slide?.duration ?? 5) * 1000),
    );
    return () => clearTimeout(timer);
  }, [presenting, auto, index, slide?.duration, file.slides.length]);
  function patch(p: Partial<Slide>) {
    onChange({
      ...file,
      slides: file.slides.map((s, i) => (i === current ? { ...s, ...p } : s)),
    });
  }
  function patchObject(p: Partial<SlideObject>) {
    patch({
      objects: slide.objects?.map((o) =>
        o.id === selected ? { ...o, ...p } : o,
      ),
    });
  }
  async function pasteImage(blob: Blob) {
    const data = await readDataURL(blob);
    const img = new Image();
    img.src = data;
    await img.decode();
    addObject("image", data, img.naturalWidth / Math.max(1, img.naturalHeight));
  }
  useEffect(() => {
    const paste = (event: ClipboardEvent) => {
      if (
        event.target instanceof HTMLInputElement ||
        event.target instanceof HTMLTextAreaElement
      )
        return;
      const image = Array.from(event.clipboardData?.files ?? []).find((f) =>
        f.type.startsWith("image/"),
      );
      if (image) {
        event.preventDefault();
        void pasteImage(image).catch(onError);
      }
    };
    document.addEventListener("paste", paste);
    return () => document.removeEventListener("paste", paste);
  });
  function add() {
    const slides = [
      ...file.slides,
      {
        id: crypto.randomUUID(),
        title: "New slide",
        body: "Add your content",
        background: slide?.background ?? "#ffffff",
        objects: [],
      },
    ];
    onChange({ ...file, slides });
    setIndex(slides.length - 1);
    setSelected(null);
  }
  function duplicate() {
    if (!slide) return;
    const slides = [...file.slides];
    slides.splice(current + 1, 0, {
      ...structuredClone(slide),
      id: crypto.randomUUID(),
    });
    onChange({ ...file, slides });
    setIndex(current + 1);
  }
  function reorder(delta: number) {
    const target = current + delta;
    if (target < 0 || target >= file.slides.length) return;
    const slides = [...file.slides];
    [slides[current], slides[target]] = [slides[target], slides[current]];
    onChange({ ...file, slides });
    setIndex(target);
  }
  function addObject(
    kind: SlideObject["kind"],
    dataUrl?: string,
    ratio = 16 / 9,
  ) {
    if (!slide) return;
    const id = crypto.randomUUID();
    const height = kind === "image" ? Math.min(65, (35 * 16) / 9 / ratio) : 20;
    const width =
      kind === "image" ? Math.min(35, (height * ratio * 9) / 16) : 25;
    patch({
      objects: [
        ...(slide.objects ?? []),
        {
          id,
          kind,
          x: kind === "image" ? (100 - width) / 2 : 15,
          y: kind === "image" ? (100 - height) / 2 : 60,
          width,
          height,
          stroke: "#185abd",
          strokeWidth: 0,
          rotation: 0,
          lockAspect: kind === "image",
          color: kind === "text" ? "#24334a" : "#185abd",
          fontSize: 32,
          text:
            kind === "wordart"
              ? "Your WordArt"
              : kind === "text"
                ? "Text box"
                : "",
          dataUrl,
          animation: "none",
        },
      ],
    });
    setSelected(id);
  }
  function addTable() {
    if (!slide) return;
    const objects: SlideObject[] = [];
    for (let row = 0; row < 3; row++) {
      for (let col = 0; col < 3; col++) {
        const position = {
          x: 10 + col * 26,
          y: 45 + row * 14,
          width: 25.5,
          height: 13.5,
        };
        objects.push({
          ...position,
          id: crypto.randomUUID(),
          kind: "rectangle",
          color: row === 0 ? "#dce8fa" : "#edf0f5",
        });
        objects.push({
          ...position,
          id: crypto.randomUUID(),
          kind: "text",
          color: "#24334a",
          fontSize: 20,
          text: row === 0 ? `Column ${col + 1}` : "Cell",
        });
      }
    }
    patch({ objects: [...(slide.objects ?? []), ...objects] });
  }
  function exportSVG() {
    if (!slide) return;
    const objects = (slide.objects ?? [])
      .map((o) => {
        const x = o.x * 12.8,
          y = o.y * 7.2,
          w = o.width * 12.8,
          h = o.height * 7.2;
        return o.kind === "image"
          ? `<image href="${o.dataUrl}" x="${x}" y="${y}" width="${w}" height="${h}"/>`
          : o.kind === "ellipse"
            ? `<ellipse cx="${x + w / 2}" cy="${y + h / 2}" rx="${w / 2}" ry="${h / 2}" fill="${escapeHTML(o.color)}"/>`
            : o.kind === "rectangle"
              ? `<rect x="${x}" y="${y}" width="${w}" height="${h}" fill="${escapeHTML(o.color)}"/>`
              : o.kind === "triangle"
                ? `<polygon points="${x + w / 2},${y} ${x + w},${y + h} ${x},${y + h}" fill="${escapeHTML(o.color)}"/>`
                : `<text x="${x}" y="${y + (o.fontSize ?? 32)}" fill="${escapeHTML(o.color)}" font-size="${o.fontSize ?? 32}" font-weight="${o.kind === "wordart" ? 700 : 400}">${escapeHTML(o.text ?? "")}</text>`;
      })
      .join("");
    download(
      `<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="720"><rect width="1280" height="720" fill="${slide.background}"/><text x="90" y="200" font-family="Arial" font-size="54">${escapeHTML(slide.title)}</text><text x="90" y="300" font-family="Arial" font-size="28">${slide.body
        .split("\n")
        .map(
          (l, i) => `<tspan x="90" dy="${i ? 40 : 0}">${escapeHTML(l)}</tspan>`,
        )
        .join("")}</text>${objects}</svg>`,
      `${file.title}-slide-${current + 1}.svg`,
      "image/svg+xml",
    );
  }
  useCommand((c) => {
    if (c === "wordart") addObject("wordart");
    if (c === "insert-image") input.current?.click();
    if (c === "present") setPresenting(true);
    if (c === "find") {
      const q = prompt("Find text in slides");
      if (q) {
        const i = file.slides.findIndex((s) =>
          `${s.title}\n${s.body}\n${s.notes ?? ""}`
            .toLowerCase()
            .includes(q.toLowerCase()),
        );
        if (i >= 0) setIndex(i);
        else onError?.(new Error("No matching slide."));
      }
    }
  });
  if (!slide) return <button onClick={add}>Add first slide</button>;
  return (
    <>
      <input
        ref={input}
        hidden
        type="file"
        accept="image/png,image/jpeg,image/webp"
        onChange={async (e) => {
          const f = e.target.files?.[0];
          if (f)
            try {
              await pasteImage(f);
            } catch (err) {
              onError?.(err);
            }
          e.target.value = "";
        }}
      />
      <Ribbon>
        {tab === "Draw" && (
          <DrawCommands onInsert={(png) => addObject("image", png)} />
        )}
        {tab === "Home" && (
          <>
            <Group label="Slides">
              <Command label="New slide" glyph="+" onClick={add} />
              <Command label="Duplicate slide" glyph="▣" onClick={duplicate} />
              <Command
                label="Delete slide"
                glyph="×"
                disabled={file.slides.length < 2}
                onClick={() => {
                  onChange({
                    ...file,
                    slides: file.slides.filter((_, i) => i !== current),
                  });
                  setIndex(Math.max(0, current - 1));
                }}
              />
              <Command
                label="Move slide up"
                glyph="↑"
                disabled={current === 0}
                onClick={() => reorder(-1)}
              />
              <Command
                label="Move slide down"
                glyph="↓"
                disabled={current === file.slides.length - 1}
                onClick={() => reorder(1)}
              />
            </Group>
            <Group label="Insert">
              <Command
                label="Text box"
                glyph="T"
                onClick={() => addObject("text")}
              />
              <Command
                label="Picture"
                glyph="▧"
                onClick={() => input.current?.click()}
              />
              <Command
                label="WordArt"
                glyph="𝓐"
                onClick={() => addObject("wordart")}
              />
            </Group>
            <Group label="Present">
              <Command
                label="Present"
                glyph="▷"
                onClick={() => setPresenting(true)}
              />
              <Command
                label="Speaker notes"
                glyph="☷"
                active={notes}
                onClick={() => setNotes(!notes)}
              />
            </Group>
          </>
        )}
        {tab === "Insert" && (
          <>
            <Group label="Text & media">
              <Command
                label="Paste picture"
                glyph="▣"
                onClick={async () => {
                  try {
                    const picture = await clipboardImage();
                    if (picture) await pasteImage(picture);
                    else
                      onError?.(
                        new Error(
                          "Copy a picture first, then paste it on the slide.",
                        ),
                      );
                  } catch {
                    onError?.(
                      new Error("Use Ctrl+V to paste a picture on the slide."),
                    );
                  }
                }}
              />
              <Command
                label="Text box"
                glyph="T"
                onClick={() => addObject("text")}
              />
              <Command
                label="WordArt"
                glyph="𝓐"
                onClick={() => addObject("wordart")}
              />
              <Command
                label="Picture"
                glyph="▧"
                onClick={() => input.current?.click()}
              />
            </Group>
            <Group label="Shapes">
              <label>
                Gallery
                <select
                  aria-label="Insert shape"
                  value=""
                  onChange={(e) => {
                    if (e.target.value)
                      addObject(e.target.value as SlideObject["kind"]);
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
              <Command label="Table" glyph="▦" onClick={addTable} />
              {["rectangle", "ellipse", "triangle"].map((s, i) => (
                <Command
                  key={s}
                  label={s[0].toUpperCase() + s.slice(1)}
                  glyph={["▭", "◯", "△"][i]}
                  onClick={() => addObject(s as SlideObject["kind"])}
                />
              ))}
            </Group>
          </>
        )}
        {tab === "Design" && (
          <>
            <Group label="Themes">
              {[
                ["Paper", "#ffffff"],
                ["Mist", "#e8f1fc"],
                ["Sage", "#e6f1eb"],
                ["Sand", "#fff2dd"],
                ["Rose", "#f8e8ec"],
              ].map(([name, color]) => (
                <Command
                  key={name}
                  label={name}
                  glyph="◧"
                  onClick={() => patch({ background: color })}
                />
              ))}
            </Group>
            <Group label="Background">
              <input
                aria-label="Slide background"
                type="color"
                value={slide.background}
                onChange={(e) => patch({ background: e.target.value })}
              />
              <Command
                label="Apply to all"
                glyph="▣"
                onClick={() =>
                  onChange({
                    ...file,
                    slides: file.slides.map((s) => ({
                      ...s,
                      background: slide.background,
                    })),
                  })
                }
              />
            </Group>
          </>
        )}
        {tab === "Transitions" && (
          <>
            <Group label="Slide transitions">
              {["none", "fade", "push", "zoom", "morph"].map((t) => (
                <Command
                  key={t}
                  label={t[0].toUpperCase() + t.slice(1)}
                  glyph={t === "morph" ? "◇→◇" : "▱"}
                  active={(slide.transition ?? "none") === t}
                  onClick={() =>
                    patch({ transition: t as Slide["transition"] })
                  }
                />
              ))}
            </Group>
            <Group label="Timing">
              <label className="ribbon-field">
                Advance after (seconds)
                <input
                  aria-label="Slide duration"
                  type="number"
                  min="1"
                  max="120"
                  value={slide.duration ?? 5}
                  onChange={(e) =>
                    patch({
                      duration: Math.max(1, Math.min(120, +e.target.value)),
                    })
                  }
                />
              </label>
              <Command
                label="Preview"
                glyph="▷"
                onClick={() => setPresenting(true)}
              />
            </Group>
            <p className="ribbon-hint">
              Morph moves matching objects between duplicated slides. Duplicate,
              reposition an object, then present.
            </p>
          </>
        )}
        {tab === "Animations" && (
          <>
            <Group label="Selected object">
              {["none", "fade", "fly", "zoom"].map((a) => (
                <Command
                  key={a}
                  label={a[0].toUpperCase() + a.slice(1)}
                  glyph="✧"
                  disabled={!object}
                  active={object?.animation === a}
                  onClick={() =>
                    patchObject({ animation: a as SlideObject["animation"] })
                  }
                />
              ))}
            </Group>
            <Group label="Preview">
              <Command
                label="Play effects"
                glyph="▷"
                onClick={() => setPresenting(true)}
              />
              <span>
                {object
                  ? `${object.kind} selected`
                  : "Select an object on the slide"}
              </span>
            </Group>
          </>
        )}
        {tab === "Slide Show" && (
          <>
            <Group label="Start">
              <Command
                label="From beginning"
                glyph="▷"
                onClick={() => {
                  setIndex(0);
                  setPresenting(true);
                }}
              />
              <Command
                label="From current slide"
                glyph="▷"
                onClick={() => setPresenting(true)}
              />
            </Group>
            <Group label="Set up">
              <Command
                label="Automatic advance"
                glyph="◷"
                active={auto}
                onClick={() => setAuto(!auto)}
              />
              <Command
                label="Speaker notes"
                glyph="☷"
                active={notes}
                onClick={() => setNotes(!notes)}
              />
            </Group>
          </>
        )}
        {tab === "Review" && (
          <Group label="Review">
            <Command
              label="Find in slides"
              glyph="⌕"
              onClick={() => {
                const q = prompt("Find in slides");
                if (q) {
                  const i = file.slides.findIndex((s) =>
                    (s.title + " " + s.body)
                      .toLowerCase()
                      .includes(q.toLowerCase()),
                  );
                  if (i >= 0) setIndex(i);
                }
              }}
            />
            <Command
              label="Speaker notes"
              glyph="☷"
              onClick={() => setNotes(!notes)}
            />
            <span>
              Spelling suggestions use your browser's language settings.
            </span>
          </Group>
        )}
        {tab === "View" && (
          <>
            <Group label="Workspace">
              <Command
                label="Thumbnails"
                glyph="▤"
                active={thumbnails}
                onClick={() => setThumbnails(!thumbnails)}
              />
              <Command
                label="Grid"
                glyph="▦"
                active={grid}
                onClick={() => setGrid(!grid)}
              />
              <Command
                label="Notes"
                glyph="☷"
                active={notes}
                onClick={() => setNotes(!notes)}
              />
              <Command label="Export SVG" glyph="↓" onClick={exportSVG} />
            </Group>
          </>
        )}
      </Ribbon>
      <div className="slides-workspace">
        <aside className="slide-thumbnails" hidden={!thumbnails}>
          {file.slides.map((s, i) => (
            <button
              key={s.id}
              className={`slide-thumbnail ${current === i ? "active" : ""}`}
              onClick={() => {
                setIndex(i);
                setSelected(null);
              }}
            >
              <span>{i + 1}</span>
              <div style={{ background: s.background }}>
                <strong>{s.title}</strong>
                <small>{s.body.slice(0, 70)}</small>
                <small>
                  {s.objects?.length ?? 0} objects · {s.transition ?? "none"}
                </small>
              </div>
            </button>
          ))}
        </aside>
        <div className="slide-stage">
          <div
            ref={stage}
            className={`slide object-slide ${grid ? "show-grid" : ""}`}
            style={{ backgroundColor: slide.background }}
            onClick={() => setSelected(null)}
          >
            <div className="slide-accent" />
            <textarea
              className="slide-title"
              aria-label="Slide title"
              value={slide.title}
              spellCheck
              onChange={(e) => patch({ title: e.target.value })}
            />
            <textarea
              className="slide-body"
              aria-label="Slide body"
              value={slide.body}
              spellCheck
              onChange={(e) => patch({ body: e.target.value })}
            />
            {(slide.objects ?? []).map((o) => (
              <div
                key={o.id}
                className={`object-handle ${selected === o.id ? "selected" : ""}`}
                style={{
                  left: `${o.x}%`,
                  top: `${o.y}%`,
                  width: `${o.width}%`,
                  height: `${o.height}%`,
                }}
                role="button"
                tabIndex={0}
                aria-label={`${o.kind} object ${o.text ?? ""}`}
                onClick={(e) => {
                  e.stopPropagation();
                  setSelected(o.id);
                }}
                onKeyDown={(e) => {
                  if (e.key === "Enter") setSelected(o.id);
                  if (
                    [
                      "ArrowLeft",
                      "ArrowRight",
                      "ArrowUp",
                      "ArrowDown",
                    ].includes(e.key)
                  ) {
                    e.preventDefault();
                    setSelected(o.id);
                    const step = e.shiftKey ? 5 : 1;
                    patch({
                      objects: slide.objects?.map((item) =>
                        item.id === o.id
                          ? {
                              ...item,
                              x: Math.max(
                                0,
                                Math.min(
                                  100 - item.width,
                                  item.x +
                                    (e.key === "ArrowRight"
                                      ? step
                                      : e.key === "ArrowLeft"
                                        ? -step
                                        : 0),
                                ),
                              ),
                              y: Math.max(
                                0,
                                Math.min(
                                  100 - item.height,
                                  item.y +
                                    (e.key === "ArrowDown"
                                      ? step
                                      : e.key === "ArrowUp"
                                        ? -step
                                        : 0),
                                ),
                              ),
                            }
                          : item,
                      ),
                    });
                  }
                }}
                onPointerDown={(e) => {
                  e.stopPropagation();
                  setSelected(o.id);
                  drag.current = {
                    x: e.clientX,
                    y: e.clientY,
                    left: o.x,
                    top: o.y,
                  };
                  e.currentTarget.setPointerCapture(e.pointerId);
                }}
                onPointerMove={(e) => {
                  if (
                    !drag.current ||
                    !(e.buttons & 1) ||
                    selected !== o.id ||
                    !stage.current
                  )
                    return;
                  const rect = stage.current.getBoundingClientRect();
                  patchObject({
                    x: Math.max(
                      0,
                      Math.min(
                        100 - o.width,
                        drag.current.left +
                          ((e.clientX - drag.current.x) / rect.width) * 100,
                      ),
                    ),
                    y: Math.max(
                      0,
                      Math.min(
                        100 - o.height,
                        drag.current.top +
                          ((e.clientY - drag.current.y) / rect.height) * 100,
                      ),
                    ),
                  });
                }}
                onPointerUp={() => (drag.current = null)}
              >
                <SlideObjectView
                  object={{ ...o, x: 0, y: 0, width: 100, height: 100 }}
                />
                {selected === o.id && (
                  <button
                    className="resize-handle"
                    aria-label="Resize object"
                    onPointerDown={(e) => {
                      e.stopPropagation();
                      e.currentTarget.setPointerCapture(e.pointerId);
                      const startX = e.clientX,
                        startY = e.clientY,
                        rect = stage.current!.getBoundingClientRect(),
                        handle = e.currentTarget;
                      let width = o.width,
                        height = o.height;
                      const move = (event: PointerEvent) => {
                        width = Math.max(
                          2,
                          Math.min(
                            100 - o.x,
                            o.width +
                              ((event.clientX - startX) / rect.width) * 100,
                          ),
                        );
                        height = o.lockAspect
                          ? (width * o.height) / o.width
                          : Math.max(
                              2,
                              Math.min(
                                100 - o.y,
                                o.height +
                                  ((event.clientY - startY) / rect.height) *
                                    100,
                              ),
                            );
                        handle.parentElement!.style.width = `${width}%`;
                        handle.parentElement!.style.height = `${height}%`;
                      };
                      const end = () => {
                        handle.removeEventListener("pointermove", move);
                        handle.removeEventListener("pointerup", end);
                        handle.removeEventListener("pointercancel", end);
                        patchObject({
                          width,
                          height: Math.min(100 - o.y, height),
                        });
                      };
                      handle.addEventListener("pointermove", move);
                      handle.addEventListener("pointerup", end, { once: true });
                      handle.addEventListener("pointercancel", end, {
                        once: true,
                      });
                    }}
                  />
                )}
              </div>
            ))}
            <span className="slide-number">{current + 1}</span>
          </div>
          {notes && (
            <textarea
              className="speaker-notes"
              aria-label="Speaker notes"
              placeholder="Add speaker notes…"
              value={slide.notes ?? ""}
              onChange={(e) => patch({ notes: e.target.value })}
            />
          )}
        </div>
        {object && (
          <aside className="object-properties">
            <h3>Format {object.kind}</h3>
            <label>
              Outline
              <input
                type="color"
                aria-label="Object outline"
                value={object.stroke ?? "#185abd"}
                onChange={(e) => patchObject({ stroke: e.target.value })}
              />
            </label>
            <label>
              Outline width
              <input
                type="number"
                min="0"
                max="20"
                aria-label="Object outline width"
                value={object.strokeWidth ?? 0}
                onChange={(e) =>
                  patchObject({
                    strokeWidth: Math.max(0, Math.min(20, +e.target.value)),
                  })
                }
              />
            </label>
            <label>
              Rotation
              <input
                type="number"
                min="-360"
                max="360"
                aria-label="Object rotation"
                value={object.rotation ?? 0}
                onChange={(e) =>
                  patchObject({
                    rotation: Math.max(-360, Math.min(360, +e.target.value)),
                  })
                }
              />
            </label>
            <label>
              <input
                type="checkbox"
                checked={object.lockAspect ?? false}
                onChange={(e) => patchObject({ lockAspect: e.target.checked })}
              />
              Lock aspect ratio
            </label>
            <button
              onClick={() => {
                const objects = [...(slide.objects ?? [])];
                const at = objects.findIndex((o) => o.id === selected);
                const [active] = objects.splice(at, 1);
                objects.push(active);
                patch({ objects });
              }}
            >
              Bring to front
            </button>
            <button
              onClick={() => {
                const objects = [...(slide.objects ?? [])];
                const at = objects.findIndex((o) => o.id === selected);
                const [active] = objects.splice(at, 1);
                objects.unshift(active);
                patch({ objects });
              }}
            >
              Send to back
            </button>
            {["text", "wordart"].includes(object.kind) && (
              <label>
                Text
                <textarea
                  aria-label="Object text"
                  value={object.text ?? ""}
                  onChange={(e) => patchObject({ text: e.target.value })}
                />
              </label>
            )}
            {(["x", "y", "width", "height"] as const).map((p) => (
              <label key={p}>
                {p} (%)
                <input
                  type="number"
                  min="0"
                  max="100"
                  aria-label={`Object ${p}`}
                  value={object[p]}
                  onChange={(e) =>
                    patchObject({
                      [p]: Math.max(
                        p === "width" || p === "height" ? 1 : 0,
                        Math.min(100, +e.target.value),
                      ),
                    })
                  }
                />
              </label>
            ))}
            <label>
              Color
              <input
                type="color"
                aria-label="Object color"
                value={object.color}
                onChange={(e) => patchObject({ color: e.target.value })}
              />
            </label>
            <label>
              Font size
              <input
                type="number"
                aria-label="Object font size"
                min="8"
                max="120"
                value={object.fontSize ?? 32}
                onChange={(e) =>
                  patchObject({
                    fontSize: Math.max(8, Math.min(120, +e.target.value)),
                  })
                }
              />
            </label>
            <button
              onClick={() => {
                patch({
                  objects: slide.objects?.filter((o) => o.id !== selected),
                });
                setSelected(null);
              }}
            >
              Delete object
            </button>
          </aside>
        )}
      </div>
      <footer className="statusbar">
        <span>
          Slide {current + 1} of {file.slides.length}
        </span>
        <span>{slide.objects?.length ?? 0} objects</span>
        <div />
        <span>{slide.transition ?? "No"} transition</span>
        <span>16:9 widescreen</span>
      </footer>
      {presenting && (
        <div
          className="presentation-overlay"
          role="dialog"
          aria-label="Presentation"
        >
          <button
            className="presentation-close"
            aria-label="Exit presentation"
            onClick={() => setPresenting(false)}
          >
            ✕
          </button>
          <div
            key={slide.transition === "morph" ? "morph-stage" : slide.id}
            className={`slide object-slide transition-${slide.transition ?? "none"}`}
            style={{ backgroundColor: slide.background }}
            onClick={() =>
              setIndex((i) => Math.min(file.slides.length - 1, i + 1))
            }
          >
            <div className="slide-accent" />
            <h1>{slide.title}</h1>
            <p>{slide.body}</p>
            {(slide.objects ?? []).map((o) => (
              <SlideObjectView
                key={o.id}
                object={o}
                presenting={slide.transition !== "morph"}
              />
            ))}
            <span className="slide-number">
              {current + 1} / {file.slides.length}
            </span>
          </div>
          <div className="presentation-controls">
            <button
              aria-label="Previous slide"
              onClick={() => setIndex((i) => Math.max(0, i - 1))}
            >
              ←
            </button>
            <span>← → to navigate · Esc to exit</span>
            <button
              aria-label="Next slide"
              onClick={() =>
                setIndex((i) => Math.min(file.slides.length - 1, i + 1))
              }
            >
              →
            </button>
          </div>
        </div>
      )}
    </>
  );
}

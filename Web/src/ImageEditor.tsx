import { useEffect, useRef, useState } from "react";
import type { OfficeFile } from "../../Engine/typescript/model";
import { download, readDataURL } from "./storage";
import { Command, Group, Ribbon, useRibbon } from "./ribbon";
function toBlob(
  canvas: HTMLCanvasElement,
  type: string,
  quality: number,
): Promise<Blob> {
  let source = canvas;
  if (type === "image/jpeg") {
    source = document.createElement("canvas");
    source.width = canvas.width;
    source.height = canvas.height;
    const ctx = source.getContext("2d")!;
    ctx.fillStyle = "white";
    ctx.fillRect(0, 0, source.width, source.height);
    ctx.drawImage(canvas, 0, 0);
  }
  return new Promise((resolve, reject) =>
    source.toBlob(
      (b) => (b ? resolve(b) : reject(new Error("Image encoding failed."))),
      type,
      quality,
    ),
  );
}
export default function ImageEditor({
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
    canvas = useRef<HTMLCanvasElement>(null),
    [size, setSize] = useState({ w: 0, h: 0 }),
    [crop, setCrop] = useState(false),
    [box, setBox] = useState({ x: 0, y: 0, w: 100, h: 100 }),
    [width, setWidth] = useState(1200),
    [height, setHeight] = useState(800),
    [lock, setLock] = useState(true),
    [format, setFormat] = useState("image/jpeg"),
    [quality, setQuality] = useState(85),
    [target, setTarget] = useState(300),
    [output, setOutput] = useState<Blob | null>(null),
    [busy, setBusy] = useState(false),
    [message, setMessage] = useState(""),
    [caption, setCaption] = useState("");
  const has = !!file.image.dataUrl;
  function patch(p: Partial<OfficeFile["image"]>) {
    setOutput(null);
    onChange({ ...file, image: { ...file.image, ...p } });
  }
  useEffect(() => {
    if (!has) return;
    let active = true;
    const image = new Image(),
      timer = setTimeout(() => {
        image.onload = () => {
          if (!active || !canvas.current) return;
          if (image.width * image.height > 40000000) {
            onError?.(new Error("Image limit: 40 megapixels."));
            return;
          }
          const angle = ((file.image.rotation % 360) + 360) % 360,
            swap = angle === 90 || angle === 270,
            c = canvas.current;
          c.width = swap ? image.height : image.width;
          c.height = swap ? image.width : image.height;
          const ctx = c.getContext("2d")!;
          ctx.filter = `brightness(${file.image.brightness}%) contrast(${file.image.contrast}%) grayscale(${file.image.grayscale ? 1 : 0}) blur(${file.image.blur ?? 0}px)`;
          ctx.translate(c.width / 2, c.height / 2);
          ctx.rotate((angle * Math.PI) / 180);
          ctx.scale(file.image.flipX ? -1 : 1, file.image.flipY ? -1 : 1);
          ctx.drawImage(image, -image.width / 2, -image.height / 2);
          ctx.setTransform(1, 0, 0, 1, 0, 0);
          ctx.filter = "none";
          if (file.image.watermark) {
            ctx.font = `bold ${Math.max(14, Math.round(c.width / 24))}px Arial`;
            ctx.fillStyle = "#ffffffcc";
            ctx.strokeStyle = "#14233a99";
            ctx.lineWidth = 2;
            ctx.textAlign = "right";
            ctx.strokeText(file.image.watermark, c.width - 20, c.height - 25);
            ctx.fillText(file.image.watermark, c.width - 20, c.height - 25);
          }
          setSize({ w: c.width, h: c.height });
          setWidth(c.width);
          setHeight(c.height);
        };
        image.onerror = () =>
          onError?.(new Error("Could not decode this image."));
        image.src = file.image.dataUrl;
      }, 60);
    return () => {
      active = false;
      clearTimeout(timer);
    };
  }, [file.image, has]);
  function bake(c: HTMLCanvasElement) {
    patch({
      dataUrl: c.toDataURL("image/png"),
      rotation: 0,
      brightness: 100,
      contrast: 100,
      grayscale: false,
      blur: 0,
      flipX: false,
      flipY: false,
      watermark: "",
    });
  }
  function applyCrop() {
    const c = canvas.current;
    if (!c) return;
    const out = document.createElement("canvas"),
      x = Math.floor((c.width * box.x) / 100),
      y = Math.floor((c.height * box.y) / 100);
    out.width = Math.max(
      1,
      Math.min(c.width - x, Math.round((c.width * box.w) / 100)),
    );
    out.height = Math.max(
      1,
      Math.min(c.height - y, Math.round((c.height * box.h) / 100)),
    );
    out
      .getContext("2d")!
      .drawImage(c, x, y, out.width, out.height, 0, 0, out.width, out.height);
    bake(out);
    setCrop(false);
  }
  function resize() {
    const c = canvas.current;
    if (!c) return;
    if (width < 1 || height < 1 || width * height > 40000000) {
      onError?.(new Error("Use positive dimensions up to 40 megapixels."));
      return;
    }
    const out = document.createElement("canvas");
    out.width = width;
    out.height = height;
    const ctx = out.getContext("2d")!;
    ctx.imageSmoothingQuality = "high";
    ctx.drawImage(c, 0, 0, width, height);
    bake(out);
  }
  async function encode(useTarget: boolean) {
    if (!canvas.current) return;
    setBusy(true);
    setMessage("");
    setOutput(null);
    try {
      let source = canvas.current,
        blob = await toBlob(source, format, quality / 100);
      if (useTarget) {
        if (!Number.isFinite(target) || target < 1)
          throw new Error("Enter a positive target size in KB.");
        if (format === "image/png")
          throw new Error("Choose JPEG or WebP for a target size.");
        const limit = target * 1024;
        let best = blob;
        for (let scale = 0; scale < 5; scale++) {
          let lo = 0.08,
            hi = quality / 100;
          for (let n = 0; n < 6; n++) {
            const q = (lo + hi) / 2,
              candidate = await toBlob(source, format, q);
            if (candidate.size < best.size) best = candidate;
            if (candidate.size <= limit) {
              blob = candidate;
              lo = q;
            } else hi = q;
          }
          if (blob.size <= limit) break;
          const smaller = document.createElement("canvas");
          smaller.width = Math.max(64, Math.round(source.width * 0.72));
          smaller.height = Math.max(64, Math.round(source.height * 0.72));
          smaller
            .getContext("2d")!
            .drawImage(source, 0, 0, smaller.width, smaller.height);
          source = smaller;
          blob = await toBlob(source, format, 0.6);
          if (blob.size < best.size) best = blob;
        }
        if (blob.size > limit) blob = best;
        setMessage(
          blob.size <= limit
            ? `Target reached: ${(blob.size / 1024).toFixed(1)} KB. Dimensions or quality may be reduced.`
            : `Smallest result ${(blob.size / 1024).toFixed(1)} KB exceeds the ${target} KB target.`,
        );
      } else setMessage(`Ready: ${(blob.size / 1024).toFixed(1)} KB`);
      setOutput(blob);
    } catch (e) {
      onError?.(e);
    } finally {
      setBusy(false);
    }
  }
  async function load(f?: File) {
    if (!f) return;
    try {
      setSize({ w: 0, h: 0 });
      patch({
        dataUrl: await readDataURL(f),
        rotation: 0,
        brightness: 100,
        contrast: 100,
        grayscale: false,
        flipX: false,
        flipY: false,
        blur: 0,
        watermark: "",
      });
    } catch (e) {
      onError?.(e);
    }
  }
  return (
    <>
      <input
        ref={input}
        hidden
        type="file"
        accept="image/png,image/jpeg,image/webp,image/gif,image/bmp"
        onChange={(e) => {
          load(e.target.files?.[0]);
          e.target.value = "";
        }}
      />
      <Ribbon>
        {tab === "Home" && (
          <>
            <Group label="Image">
              <Command
                label="Open image"
                glyph="▧"
                onClick={() => input.current?.click()}
              />
              <Command
                label="Rotate clockwise"
                glyph="↻"
                disabled={!has}
                onClick={() => patch({ rotation: file.image.rotation + 90 })}
              />
              <Command
                label="Crop image"
                glyph="⌗"
                disabled={!has}
                onClick={() => setCrop(!crop)}
              />
              <Command
                label="Grayscale"
                glyph="◐"
                disabled={!has}
                active={file.image.grayscale}
                onClick={() => patch({ grayscale: !file.image.grayscale })}
              />
            </Group>
            <Group label="Export">
              <Command
                label="Export PNG"
                glyph="↓"
                disabled={!has || busy}
                onClick={() =>
                  canvas.current?.toBlob((b) => {
                    if (b)
                      download(
                        b,
                        `${file.title.replace(/\.[^.]+$/, "")}.png`,
                        "image/png",
                      );
                  })
                }
              />
            </Group>
          </>
        )}
        {tab === "Transform" && (
          <>
            <Group label="Transform">
              <Command
                label="Rotate left"
                glyph="↶"
                disabled={!has}
                onClick={() => patch({ rotation: file.image.rotation - 90 })}
              />
              <Command
                label="Rotate right"
                glyph="↷"
                disabled={!has}
                onClick={() => patch({ rotation: file.image.rotation + 90 })}
              />
              <Command
                label="Flip horizontal"
                glyph="↔"
                disabled={!has}
                onClick={() => patch({ flipX: !file.image.flipX })}
              />
              <Command
                label="Flip vertical"
                glyph="↕"
                disabled={!has}
                onClick={() => patch({ flipY: !file.image.flipY })}
              />
              <Command
                label="Crop"
                glyph="⌗"
                disabled={!has}
                onClick={() => setCrop(!crop)}
              />
            </Group>
            <Group label="Resize">
              <label className="ribbon-field">
                Width
                <input
                  type="number"
                  aria-label="Image width"
                  disabled={!size.w}
                  value={width}
                  onChange={(e) => {
                    setWidth(+e.target.value);
                    if (lock)
                      setHeight(
                        Math.max(
                          1,
                          Math.round(
                            (+e.target.value * size.h) / (size.w || 1),
                          ),
                        ),
                      );
                  }}
                />
              </label>
              <label className="ribbon-field">
                Height
                <input
                  type="number"
                  aria-label="Image height"
                  disabled={!size.w}
                  value={height}
                  onChange={(e) => {
                    setHeight(+e.target.value);
                    if (lock)
                      setWidth(
                        Math.max(
                          1,
                          Math.round(
                            (+e.target.value * size.w) / (size.h || 1),
                          ),
                        ),
                      );
                  }}
                />
              </label>
              <Command
                label="Lock aspect ratio"
                glyph="↔"
                active={lock}
                onClick={() => setLock(!lock)}
              />
              <Command
                label="Apply resize"
                glyph="✓"
                disabled={!has}
                onClick={resize}
              />
            </Group>
          </>
        )}
        {tab === "Adjust" && (
          <Group label="Effects">
            {(["brightness", "contrast", "blur"] as const).map((p) => (
              <label className="ribbon-field" key={p}>
                {p}
                <input
                  type="range"
                  aria-label={p}
                  min="0"
                  max={p === "blur" ? 25 : 200}
                  value={file.image[p] ?? 0}
                  onChange={(e) => patch({ [p]: +e.target.value })}
                />
              </label>
            ))}
            <Command
              label="Grayscale"
              glyph="◐"
              active={file.image.grayscale}
              onClick={() => patch({ grayscale: !file.image.grayscale })}
            />
            <Command
              label="Reset adjustments"
              glyph="↺"
              onClick={() =>
                patch({
                  brightness: 100,
                  contrast: 100,
                  grayscale: false,
                  blur: 0,
                })
              }
            />
          </Group>
        )}
        {tab === "Annotate" && (
          <>
            <Group label="Watermark">
              <input
                aria-label="Watermark text"
                placeholder="Watermark text"
                value={file.image.watermark ?? ""}
                onChange={(e) => patch({ watermark: e.target.value })}
              />
            </Group>
            <Group label="Meme caption">
              <input
                aria-label="Meme caption"
                placeholder="Caption"
                value={caption}
                onChange={(e) => setCaption(e.target.value)}
              />
              <Command
                label="Add caption"
                glyph="T"
                disabled={!has || !caption}
                onClick={() => {
                  const c = canvas.current!;
                  const ctx = c.getContext("2d")!;
                  ctx.font = `bold ${Math.round(c.width / 14)}px Arial`;
                  ctx.textAlign = "center";
                  ctx.fillStyle = "white";
                  ctx.strokeStyle = "black";
                  ctx.lineWidth = Math.max(2, c.width / 300);
                  ctx.strokeText(
                    caption.toUpperCase(),
                    c.width / 2,
                    c.width / 10,
                    c.width * 0.94,
                  );
                  ctx.fillText(
                    caption.toUpperCase(),
                    c.width / 2,
                    c.width / 10,
                    c.width * 0.94,
                  );
                  bake(c);
                  setCaption("");
                }}
              />
            </Group>
            <Group label="Manual privacy blur">
              <Command
                label="Blur region"
                glyph="▧"
                disabled={!has}
                onClick={() => setCrop(true)}
              />
              <p className="ribbon-hint">
                Set a region below, then choose Blur selected region.
              </p>
            </Group>
          </>
        )}
        {["Compress", "Convert"].includes(tab) && (
          <>
            <Group label="Output format">
              <select
                aria-label="Image output format"
                value={format}
                onChange={(e) => {
                  setFormat(e.target.value);
                  setOutput(null);
                }}
              >
                <option value="image/jpeg">JPEG</option>
                <option value="image/png">PNG</option>
                <option value="image/webp">WebP</option>
              </select>
              <label className="ribbon-field">
                Quality {quality}%
                <input
                  aria-label="Image quality"
                  type="range"
                  min="10"
                  max="100"
                  value={quality}
                  onChange={(e) => {
                    setQuality(+e.target.value);
                    setOutput(null);
                  }}
                />
              </label>
            </Group>
            <Group label="Compress">
              <label className="ribbon-field">
                Target (KB)
                <input
                  aria-label="Target image size KB"
                  type="number"
                  min="1"
                  value={target}
                  onChange={(e) => {
                    setTarget(+e.target.value);
                    setOutput(null);
                  }}
                />
              </label>
              <Command
                label="Compress to target"
                glyph="⇲"
                disabled={!has || busy}
                onClick={() => encode(true)}
              />
              <Command
                label="Convert image"
                glyph="⇄"
                disabled={!has || busy}
                onClick={() => encode(false)}
              />
            </Group>
          </>
        )}
        {tab === "View" && (
          <Group label="Image details">
            <span>
              {size.w} × {size.h} pixels
            </span>
            <p className="ribbon-hint">
              Edits stay on this device. Animated images open as a single frame.
            </p>
          </Group>
        )}
      </Ribbon>
      <div className="image-workspace">
        <aside className="adjustments">
          <h3>Image tools</h3>
          {(["brightness", "contrast"] as const).map((k) => (
            <label key={k}>
              {k} · {file.image[k]}%
              <input
                aria-label={`${k} adjustment`}
                type="range"
                min="0"
                max="200"
                value={file.image[k]}
                onChange={(e) => patch({ [k]: +e.target.value })}
              />
            </label>
          ))}
          {crop && (
            <>
              <h3>Region (% of image)</h3>
              {(["x", "y", "w", "h"] as const).map((k) => (
                <label key={k}>
                  {k}
                  <input
                    aria-label={`Crop ${k}`}
                    type="number"
                    min={k === "w" || k === "h" ? 1 : 0}
                    max={k === "w" || k === "h" ? 100 : 99}
                    value={box[k]}
                    onChange={(e) =>
                      setBox({
                        ...box,
                        [k]: Math.max(
                          k === "w" || k === "h" ? 1 : 0,
                          Math.min(
                            k === "w" || k === "h" ? 100 : 99,
                            +e.target.value,
                          ),
                        ),
                      })
                    }
                  />
                </label>
              ))}
              <button className="button" onClick={applyCrop}>
                Apply crop
              </button>
              <button
                className="button"
                onClick={() => {
                  const c = canvas.current!;
                  const ctx = c.getContext("2d")!,
                    copy = document.createElement("canvas");
                  copy.width = c.width;
                  copy.height = c.height;
                  copy.getContext("2d")!.drawImage(c, 0, 0);
                  ctx.save();
                  ctx.beginPath();
                  ctx.rect(
                    (c.width * box.x) / 100,
                    (c.height * box.y) / 100,
                    (c.width * box.w) / 100,
                    (c.height * box.h) / 100,
                  );
                  ctx.clip();
                  ctx.filter = `blur(${Math.max(12, c.width / 50)}px)`;
                  ctx.drawImage(copy, 0, 0);
                  ctx.restore();
                  bake(c);
                  setCrop(false);
                }}
              >
                Blur selected region
              </button>
            </>
          )}
          {busy && <p role="status">Encoding image…</p>}
          {message && (
            <p className="compression-result" role="status">
              {message}
            </p>
          )}
          {output && (
            <button
              className="primary-button"
              onClick={() =>
                download(
                  output,
                  `${file.title.replace(/\.[^.]+$/, "")}.${output.type.split("/")[1].replace("jpeg", "jpg")}`,
                  output.type,
                )
              }
            >
              Download result
            </button>
          )}
        </aside>
        <div className="image-stage">
          {has ? (
            <canvas ref={canvas} aria-label="Edited image" />
          ) : (
            <button
              className="upload-area"
              onClick={() => input.current?.click()}
            >
              <span>▧</span>
              <h2>Start with an image</h2>
              <span>PNG, JPEG, WebP, GIF, or BMP</span>
            </button>
          )}
        </div>
      </div>
      <footer className="statusbar">
        <span>{size.w ? `${size.w} × ${size.h} pixels` : "No image open"}</span>
        <div />
        <span>Processed on this device</span>
      </footer>
    </>
  );
}

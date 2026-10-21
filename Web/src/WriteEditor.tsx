import { useEffect, useRef, useState } from "react";
import type { OfficeFile } from "../../Engine/typescript/model";
import { download, readDataURL, safeHTML } from "./storage";
import { Command, Group, Ribbon, useCommand, useRibbon } from "./ribbon";
import { escapeHTML } from "./visuals";
import ChartDialog from "./ChartDialog";
import { htmlToDelta } from "../../Engine/typescript/rich-content";
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
    [chart, setChart] = useState(false);
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
    if (
      selection.current &&
      editor.current?.contains(selection.current.startContainer)
    ) {
      s?.removeAllRanges();
      s?.addRange(selection.current);
    }
  }
  function emit() {
    if (!editor.current) return;
    last.current = safeHTML(editor.current.innerHTML);
    onChange({
      ...file,
      content: {
        ...file.content,
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
              const data = await readDataURL(f);
              command(
                "insertHTML",
                `<p><img src="${data}" alt="${escapeHTML(f.name)}" style="max-width:100%;width:420px"></p><p><br></p>`,
              );
            } catch (err) {
              onError?.(err);
            }
          e.target.value = "";
        }}
      />
      <Ribbon>
        {tab === "Home" && (
          <>
            <Group label="Clipboard">
              <Command label="Undo" glyph="↶" onClick={() => command("undo")} />
              <Command label="Redo" glyph="↷" onClick={() => command("redo")} />
              <Command
                label="Paste"
                glyph="▣"
                onClick={() =>
                  navigator.clipboard
                    .readText()
                    .then((t) => command("insertText", t))
                    .catch(() =>
                      onError?.(
                        new Error("Use Ctrl+V or your device's Paste command."),
                      ),
                    )
                }
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
              <Command
                label="Rectangle"
                glyph="▭"
                onClick={() =>
                  command(
                    "insertHTML",
                    '<div style="width:240px;height:100px;background-color:#dce8fa;border:2px solid #185abd;border-radius:8px">Shape</div><p><br></p>',
                  )
                }
              />
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
                {[1, 2, 3].map((n) => (
                  <option key={n} value={n}>
                    {n} column{n > 1 ? "s" : ""}
                  </option>
                ))}
              </select>
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
      <div className="document-workspace">
        <div className="page-ruler">
          {Array.from({ length: 17 }, (_, i) => (
            <span key={i}>{i % 2 === 0 ? i / 2 : "·"}</span>
          ))}
        </div>
        <article
          className={`paper ${file.layout?.orientation ?? "portrait"}`}
          style={{
            zoom: zoom / 100,
            padding:
              file.layout?.margins === "narrow"
                ? "28px"
                : file.layout?.margins === "wide"
                  ? "90px"
                  : undefined,
          }}
        >
          <div
            ref={editor}
            className="word-content"
            style={{
              columnCount: file.layout?.columns ?? 1,
              fontFamily: String(file.content.fontFamily ?? "Calibri"),
            }}
            contentEditable
            suppressContentEditableWarning
            role="textbox"
            aria-label="Document content"
            aria-multiline
            spellCheck={proof}
            data-placeholder="Start writing…"
            onInput={emit}
            onKeyUp={remember}
            onMouseUp={remember}
            onBlur={remember}
            onPaste={(e) => {
              e.preventDefault();
              command("insertText", e.clipboardData.getData("text/plain"));
            }}
          />
        </article>
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

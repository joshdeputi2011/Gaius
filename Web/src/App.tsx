import { lazy, Suspense, useEffect, useRef, useState } from "react";
import {
  Home24Regular,
  Document24Regular,
  Table24Regular,
  SlideText24Regular,
  Image24Regular,
  DocumentPdf24Regular,
  FolderOpen24Regular,
  Add24Regular,
  Search24Regular,
  ArrowDownload24Regular,
  Delete24Regular,
  ArrowLeft24Regular,
  ArrowUndo24Regular,
  ArrowRedo24Regular,
  CheckmarkCircle24Regular,
  ArrowUpRight24Regular,
  Copy24Regular,
  Print24Regular,
  Dismiss24Regular,
} from "@fluentui/react-icons";
import {
  createFile,
  kinds,
  labels,
  parseCSV,
  parseFile,
  columnName,
  type Kind,
  type OfficeFile,
} from "../../Engine/typescript/model";
import {
  listFiles,
  saveFile,
  removeFile,
  download,
  readDataURL,
} from "./storage";
import { RibbonContext, RibbonTabs, FileDropdown, Modal, dispatchCommand } from "./ribbon";
import { PrintPreview, SaveAs, shareFile } from "./FileActions";
const WriteEditor = lazy(() => import("./WriteEditor"));
const SheetEditor = lazy(() => import("./SheetEditor"));
const SlidesEditor = lazy(() => import("./SlidesEditor"));
const ImageEditor = lazy(() => import("./ImageEditor"));
const PDFEditor = lazy(() => import("./PDFEditor"));
import { exportOffice, importOffice } from "../../Engine/typescript/ooxml";
const icons = {
  document: Document24Regular,
  spreadsheet: Table24Regular,
  presentation: SlideText24Regular,
  image: Image24Regular,
  pdf: DocumentPdf24Regular,
};
export function FileIcon({
  kind,
  large = false,
}: {
  kind: Kind;
  large?: boolean;
}) {
  const Icon = icons[kind];
  return (
    <span className={`file-icon ${kind} ${large ? "large" : ""}`}>
      <Icon />
    </span>
  );
}
export function Tool({
  children,
  onClick,
  title,
  disabled = false,
}: {
  children: React.ReactNode;
  onClick: () => void;
  title: string;
  disabled?: boolean;
}) {
  return (
    <button
      className="tool"
      onClick={onClick}
      title={title}
      aria-label={title}
      disabled={disabled}
    >
      {children}
    </button>
  );
}
export default function App() {
  const [files, setFiles] = useState<OfficeFile[]>([]),
    [current, setCurrent] = useState<OfficeFile | null>(null),
    [section, setSection] = useState<Kind | "home">("home");
  const [query, setQuery] = useState(""),
    [status, setStatus] = useState("Saved on this device"),
    [error, setError] = useState(""),
    [ready, setReady] = useState(false),
    [notice, setNotice] = useState("");
  const [past, setPast] = useState<OfficeFile[]>([]),
    [future, setFuture] = useState<OfficeFile[]>([]);
  const [tab, setTab] = useState("Home"),
    [saveAs, setSaveAs] = useState(false),
    [printPreview, setPrintPreview] = useState(false),
    [palette, setPalette] = useState(false),
    [commandQuery, setCommandQuery] = useState("");
  const importRef = useRef<HTMLInputElement>(null),
    saveQueue = useRef(Promise.resolve()),
    latest = useRef<OfficeFile | null>(null);
  const savedRevisions = useRef(new Map<string, string>());
  useEffect(() => {
    listFiles()
      .then((items) => {
        setFiles(items);
        items.forEach((f) => savedRevisions.current.set(f.id, f.updatedAt));
        const id = new URLSearchParams(location.search).get("file");
        if (id) setCurrent(items.find((f) => f.id === id) ?? null);
        setReady(true);
      })
      .catch((e) =>
        setError(`Local storage could not be opened: ${e.message}`),
      );
  }, []);
  useEffect(() => {
    type NativeHost = {
      postMessage: (v: unknown) => void;
      addEventListener: (
        name: string,
        handler: (event: MessageEvent) => void,
      ) => void;
      removeEventListener: (
        name: string,
        handler: (event: MessageEvent) => void,
      ) => void;
    };
    const host = (window as unknown as { chrome?: { webview?: NativeHost } })
      .chrome?.webview;
    if (!host) return;
    const handler = async (event: MessageEvent) => {
      if (
        event.data?.action !== "native-files" ||
        !Array.isArray(event.data.files)
      )
        return;
      try {
        const existing = await listFiles(),
          ids = new Set(existing.map((f) => f.id));
        let imported = 0;
        for (const raw of event.data.files) {
          const f = parseFile(JSON.stringify(raw));
          if (ids.has(f.id)) continue;
          await saveFile(f);
          savedRevisions.current.set(f.id, f.updatedAt);
          imported++;
        }
        setFiles(await listFiles());
        if (imported)
          setNotice(
            `Imported ${imported} native workspace files. Original files remain in the native workspace.`,
          );
      } catch (e) {
        setError((e as Error).message);
      }
    };
    host.addEventListener("message", handler);
    host.postMessage({ action: "native-files" });
    return () => host.removeEventListener("message", handler);
  }, []);
  useEffect(() => {
    latest.current = current;
  }, [current]);
  useEffect(() => {
    const handler = () => {
      if (document.visibilityState === "visible")
        listFiles()
          .then(setFiles)
          .catch(() => {});
    };
    document.addEventListener("visibilitychange", handler);
    return () => document.removeEventListener("visibilitychange", handler);
  }, []);
  function persist(next: OfficeFile) {
    setStatus("Saving on this device…");
    saveQueue.current = saveQueue.current
      .then(() => saveFile(next, savedRevisions.current.get(next.id)))
      .then(() => {
        savedRevisions.current.set(next.id, next.updatedAt);
        setFiles((prev) => [next, ...prev.filter((f) => f.id !== next.id)]);
        if (latest.current?.updatedAt === next.updatedAt)
          setStatus("Saved on this device");
      })
      .catch((e) => {
        setStatus("Save failed");
        setError(`Could not save locally. Export a copy now. ${e.message}`);
      });
  }
  function update(next: OfficeFile, history = true) {
    if (current && history) {
      setPast((p) => [...p.slice(-49), current]);
      setFuture([]);
    }
    const stamped = { ...next, updatedAt: new Date().toISOString() };
    latest.current = stamped;
    setCurrent(stamped);
    persist(stamped);
  }
  function open(file: OfficeFile) {
    setTab("Home");
    if (files.some((f) => f.id === file.id))
      savedRevisions.current.set(file.id, file.updatedAt);
    setCurrent(file);
    latest.current = file;
    setPast([]);
    setFuture([]);
    setStatus("Saved on this device");
    history.replaceState(null, "", `?file=${file.id}`);
  }
  function create(kind: Kind) {
    const file = createFile(kind);
    open(file);
    persist(file);
  }
  function undo() {
    if (!current || !past.length) return;
    const prev = past[past.length - 1];
    setFuture((f) => [current, ...f]);
    setPast((p) => p.slice(0, -1));
    update(prev, false);
  }
  function redo() {
    if (!current || !future.length) return;
    const next = future[0];
    setPast((p) => [...p, current]);
    setFuture((f) => f.slice(1));
    update(next, false);
  }
  async function importFile(file?: File) {
    if (!file) return;
    try {
      if (file.size > 40 * 1024 * 1024)
        throw new Error("Choose a file smaller than 40 MB.");
      let doc: OfficeFile;
      const ext = file.name.split(".").pop()?.toLowerCase();
      if (ext === "gaius" || ext === "json") {
        doc = parseFile(await file.text());
        doc.id = crypto.randomUUID();
      } else if (ext === "csv") {
        doc = createFile("spreadsheet", file.name.replace(/\.csv$/i, ""));
        const rows = parseCSV(await file.text());
        if (rows.length > 10000 || rows.some((r) => r.length > 100))
          throw new Error("CSV limit: 10,000 rows and 100 columns.");
        rows.forEach((row, r) =>
          row.forEach((v, c) => {
            doc.sheet.cells[`${columnName(c)}${r + 1}`] = v;
          }),
        );
        doc.sheet.rows = Math.max(100, rows.length);
        doc.sheet.columns = Math.max(26, ...rows.map((r) => r.length));
      } else if (file.type.startsWith("image/") && !file.type.includes("svg")) {
        doc = createFile("image", file.name);
        doc.image.dataUrl = await readDataURL(file);
      } else if (ext === "pdf") {
        doc = createFile("pdf", file.name);
        doc.pdf.dataUrl = (await readDataURL(file)).replace(
          /^data:[^;]*;/,
          "data:application/pdf;",
        );
      } else if (ext === "txt" || ext === "md") {
        doc = createFile("document", file.name);
        doc.content.text = await file.text();
        const element = document.createElement("div");
        element.textContent = doc.content.text;
        doc.content.html = `<p>${element.innerHTML.replaceAll("\n", "<br>")}</p>`;
      } else
        throw new Error(
          "Open .gaius, .txt, .md, .csv, raster images, or .pdf. Microsoft Office formats are not supported yet.",
        );
      open(doc);
      persist(doc);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      if (importRef.current) importRef.current.value = "";
    }
  }
  function exportCurrent() {
    if (current)
      download(
        JSON.stringify(current, null, 2),
        `${current.title.replace(/[<>:"/\\|?*]/g, "_")}.gaius`,
        "application/json",
      );
  }
  function exportOfficeCurrent() {
    if (!current) return;
    try {
      const result = exportOffice(current);
      download(
        new Uint8Array(result.bytes),
        `${current.title}.${result.extension}`,
      );
      setNotice(
        "Office export preserves supported editable content, pictures, tables, and worksheets. Some layout, charts, and effects may differ. Keep a Gaius copy too.",
      );
    } catch (e) {
      setError((e as Error).message);
    }
  }
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "s") {
        e.preventDefault();
        if (current) {
          if (e.shiftKey) setSaveAs(true);
          else
            saveQueue.current.then(() =>
              setNotice(
                "Saved on this device. Use Save as for a portable copy.",
              ),
            );
        }
      }
      if (current && (e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "p") {
        e.preventDefault();
        setPrintPreview(true);
      }
      if (
        current &&
        (e.ctrlKey || e.metaKey) &&
        ["f", "h"].includes(e.key.toLowerCase())
      ) {
        e.preventDefault();
        dispatchCommand(e.key.toLowerCase() === "h" ? "replace" : "find");
      }
      if (current && (e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        setPalette(true);
      }
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  });
  const filtered = files.filter(
    (f) =>
      (section === "home" || section === f.kind) &&
      f.title.toLowerCase().includes(query.toLowerCase()),
  );
  const onError = (e: unknown) =>
    setError(e instanceof Error ? e.message : String(e));
  return (
    <div
      className={`app ${current ? "editing" : ""}`}
      style={
        {
          "--app-accent": current
            ? {
                document: "#185abd",
                spreadsheet: "#107c41",
                presentation: "#c43e1c",
                image: "#744da9",
                pdf: "#c42b32",
              }[current.kind]
            : "#185abd",
        } as React.CSSProperties
      }
    >
      <input
        ref={importRef}
        type="file"
        hidden
        accept=".gaius,.json,.txt,.md,.csv,.docx,.xlsx,.pptx,.pdf,image/png,image/jpeg,image/webp,image/gif,image/bmp"
        onChange={(e) => {
          const f = e.target.files?.[0];
          if (f && /\.(docx|xlsx|pptx)$/i.test(f.name)) {
            if (f.size > 40 * 1024 * 1024) {
              setError("Choose a file smaller than 40 MB.");
              return;
            }
            f.arrayBuffer()
              .then((b) =>
                importOffice(
                  new Uint8Array(b),
                  f.name.split(".").pop()!.toLowerCase(),
                  f.name.replace(/\.[^.]+$/, ""),
                ),
              )
              .then((doc) => {
                open(doc);
                persist(doc);
                setNotice(
                  "Imported supported Office content. Some layout, charts, and effects may differ. Your original file is unchanged.",
                );
              })
              .catch((e) => setError(e.message));
            e.target.value = "";
          } else importFile(f);
        }}
      />
      {notice && (
        <div className="notice-banner" role="status">
          <span>{notice}</span>
          <Tool title="Dismiss notice" onClick={() => setNotice("")}>
            <Dismiss24Regular />
          </Tool>
        </div>
      )}
      {error && (
        <div className="error-banner" role="alert">
          <span>{error}</span>
          <Tool title="Dismiss message" onClick={() => setError("")}>
            <Dismiss24Regular />
          </Tool>
        </div>
      )}
      {current ? (
        <>
          <header className="editor-header">
            <Tool
              title="Back to home"
              onClick={() => {
                setCurrent(null);
                history.replaceState(null, "", location.pathname);
              }}
            >
              <ArrowLeft24Regular />
            </Tool>
            <span className="wordmark">G</span>
            <FileIcon kind={current.kind} />
            <input
              aria-label="Document name"
              className="document-name"
              value={current.title}
              onChange={(e) => update({ ...current, title: e.target.value })}
            />
            <span className="save-status">
              <CheckmarkCircle24Regular />
              {status}
            </span>
            <div className="header-spacer" />
            <Tool
              title="Search commands (Ctrl+K)"
              onClick={() => setPalette(true)}
            >
              <Search24Regular />
            </Tool>
            <button
              className="header-share"
              onClick={() => shareFile(current).catch(onError)}
            >
              Share
            </button>
            <Tool
              title="Open in another window"
              onClick={() =>
                window.open(`?file=${current.id}`, "_blank", "noopener")
              }
            >
              <ArrowUpRight24Regular />
            </Tool>
            <button className="header-export" onClick={exportCurrent}>
              <ArrowDownload24Regular />
              Export file
            </button>
          </header>
          <div className="menu-row">
            <FileDropdown items={[
              { label: 'Open file', action: () => importRef.current?.click() },
              { label: 'Save as', action: () => setSaveAs(true) },
              { label: 'Share', action: () => { shareFile(current).catch(onError); } },
              { label: 'Print preview', action: () => setPrintPreview(true) },
              { label: 'Export Gaius', action: exportCurrent },
              ...(['document', 'spreadsheet', 'presentation'].includes(current.kind) ? [{ label: `Export ${current.kind === 'document' ? 'DOCX' : current.kind === 'spreadsheet' ? 'XLSX' : 'PPTX'}`, action: exportOfficeCurrent }] : []),
              { label: 'New window', action: () => { window.open(`?file=${current.id}`, '_blank', 'noopener'); } },
            ]} />
            <RibbonTabs kind={current.kind} selected={tab} onSelect={setTab} />
            <div className="header-spacer" />
            {["document", "spreadsheet", "presentation"].includes(
              current.kind,
            ) && (
              <button onClick={exportOfficeCurrent}>
                Export{" "}
                {current.kind === "document"
                  ? "DOCX"
                  : current.kind === "spreadsheet"
                    ? "XLSX"
                    : "PPTX"}
              </button>
            )}
            {current.kind !== "document" && (
              <>
                <Tool title="Undo" onClick={undo} disabled={!past.length}>
                  <ArrowUndo24Regular />
                </Tool>
                <Tool title="Redo" onClick={redo} disabled={!future.length}>
                  <ArrowRedo24Regular />
                </Tool>
              </>
            )}
            <Tool title="Open file" onClick={() => importRef.current?.click()}>
              <FolderOpen24Regular />
            </Tool>
            <Tool title="Print preview" onClick={() => setPrintPreview(true)}>
              <Print24Regular />
            </Tool>
          </div>
          <RibbonContext.Provider value={tab}>
            <Suspense
              fallback={
                <div className="editor-loading" role="status">
                  Opening editor…
                </div>
              }
            >
              {current.kind === "document" && (
                <WriteEditor
                  key={current.id}
                  file={current}
                  onChange={update}
                  onError={onError}
                />
              )}
              {current.kind === "spreadsheet" && (
                <SheetEditor
                  key={current.id}
                  file={current}
                  onChange={update}
                  onError={onError}
                />
              )}
              {current.kind === "presentation" && (
                <SlidesEditor
                  key={current.id}
                  file={current}
                  onChange={update}
                  onError={onError}
                />
              )}
              {current.kind === "image" && (
                <ImageEditor
                  key={current.id}
                  file={current}
                  onChange={update}
                  onError={onError}
                />
              )}
              {current.kind === "pdf" && (
                <PDFEditor
                  key={current.id}
                  file={current}
                  onChange={update}
                  onError={onError}
                />
              )}
            </Suspense>
          </RibbonContext.Provider>
          {saveAs && (
            <SaveAs
              file={current}
              onClose={() => setSaveAs(false)}
              onSave={(f) => {
                open(f);
                persist(f);
              }}
              onError={onError}
            />
          )}
          {printPreview && (
            <PrintPreview
              file={current}
              onClose={() => setPrintPreview(false)}
            />
          )}
          {palette && (
            <Modal title="Search commands" onClose={() => setPalette(false)}>
              <div className="command-palette">
                <input
                  autoFocus
                  aria-label="Search commands"
                  placeholder="Find a command…"
                  value={commandQuery}
                  onChange={(e) => setCommandQuery(e.target.value)}
                />
                {[
                  { label: "Save as", run: () => setSaveAs(true) },
                  {
                    label: "Share file",
                    run: () => shareFile(current).catch(onError),
                  },
                  { label: "Print preview", run: () => setPrintPreview(true) },
                  { label: "Find in file", run: () => dispatchCommand("find") },
                  ...ribbonTabs[current.kind].map((t) => ({
                    label: `${t} tab`,
                    run: () => setTab(t),
                  })),
                  ...(["document", "presentation"].includes(current.kind)
                    ? [
                        {
                          label: "Insert picture",
                          run: () => dispatchCommand("insert-image"),
                        },
                        {
                          label: "Insert WordArt",
                          run: () => dispatchCommand("wordart"),
                        },
                      ]
                    : []),
                  ...(current.kind === "spreadsheet"
                    ? [
                        {
                          label: "Insert chart",
                          run: () => dispatchCommand("chart"),
                        },
                      ]
                    : []),
                  ...(current.kind === "presentation"
                    ? [
                        {
                          label: "Present slides",
                          run: () => dispatchCommand("present"),
                        },
                      ]
                    : []),
                ]
                  .filter((c) =>
                    c.label.toLowerCase().includes(commandQuery.toLowerCase()),
                  )
                  .map((c) => (
                    <button
                      key={c.label}
                      onClick={() => {
                        setPalette(false);
                        setCommandQuery("");
                        c.run();
                      }}
                    >
                      {c.label}
                      <span>↵</span>
                    </button>
                  ))}
              </div>
            </Modal>
          )}
        </>
      ) : (
        <>
          <aside className="sidebar">
            <a className="brand" href="./">
              <span className="brand-mark">G</span>
              <span>
                Gaius<span className="brand-office">Office</span>
              </span>
            </a>
            <button
              className={`nav-item ${section === "home" ? "active" : ""}`}
              onClick={() => setSection("home")}
            >
              <Home24Regular />
              Home
            </button>
            <div className="nav-divider" />
            {kinds.map((kind) => (
              <button
                key={kind}
                className={`nav-item ${section === kind ? "active" : ""}`}
                onClick={() => setSection(kind)}
              >
                <FileIcon kind={kind} />
                {labels[kind]}
              </button>
            ))}
            <button
              className="nav-item open-nav"
              onClick={() => importRef.current?.click()}
            >
              <FolderOpen24Regular />
              Open a file
            </button>
          </aside>
          <main className="home">
            <header className="home-header">
              <span>{section === "home" ? "Home" : labels[section]}</span>
              <div className="search">
                <Search24Regular />
                <input
                  aria-label="Search files"
                  placeholder="Search your files"
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                />
                {query && (
                  <button
                    aria-label="Clear search"
                    onClick={() => setQuery("")}
                  >
                    <Dismiss24Regular />
                  </button>
                )}
              </div>
              <span className="avatar">G</span>
            </header>
            <section className="home-content">
              <div className="create-grid">
                {kinds
                  .filter((k) => section === "home" || section === k)
                  .map((kind) => (
                    <button
                      className={`create-card ${kind}`}
                      key={kind}
                      onClick={() => create(kind)}
                    >
                      <div className="card-top">
                        <FileIcon kind={kind} large />
                        <Add24Regular />
                      </div>
                      <strong>{labels[kind]}</strong>
                    </button>
                  ))}
              </div>
              <div className="recent-heading">
                <h2>
                  Recent files <span className="count">{filtered.length}</span>
                </h2>
                <button
                  className="button"
                  onClick={() => importRef.current?.click()}
                >
                  <FolderOpen24Regular />
                  Open from device
                </button>
              </div>
              <div className="file-list" aria-label="Recent files" aria-busy={!ready}>
                {filtered.map((file) => (
                  <div className="file-row" key={file.id}>
                    <button className="file-open" onClick={() => open(file)}>
                      <FileIcon kind={file.kind} />
                      <span className="file-details"><strong>{file.title}</strong><small>{labels[file.kind]}</small></span>
                    </button>
                    <span className="file-date">
                      {new Date(file.updatedAt).toLocaleDateString(undefined, {
                        month: "short",
                        day: "numeric",
                        year: "numeric",
                      })}
                    </span>
                    <div className="file-actions">
                      <Tool
                        title="Duplicate file"
                        onClick={() => {
                          const copy = {
                            ...structuredClone(file),
                            id: crypto.randomUUID(),
                            title: `${file.title} — Copy`,
                            updatedAt: new Date().toISOString(),
                          };
                          persist(copy);
                        }}
                      >
                        <Copy24Regular />
                      </Tool>
                      <Tool
                        title="Delete file"
                        onClick={() => {
                          if (
                            confirm(`Delete “${file.title}” from this browser?`)
                          )
                            removeFile(file.id)
                              .then(() =>
                                setFiles((f) =>
                                  f.filter((v) => v.id !== file.id),
                                ),
                              )
                              .catch(onError);
                        }}
                      >
                        <Delete24Regular />
                      </Tool>
                    </div>
                  </div>
                ))}
                {!filtered.length && (
                  <div className="empty-state">
                    <span role="status">
                      {ready
                        ? query
                          ? "No matching files"
                          : "No files yet"
                        : "Loading files…"}
                    </span>
                  </div>
                )}
              </div>
            </section>
          </main>
        </>
      )}
    </div>
  );
}

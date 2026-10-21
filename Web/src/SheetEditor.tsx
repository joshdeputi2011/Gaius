import { useEffect, useMemo, useRef, useState } from "react";
import {
  columnName,
  exportCSV,
  type CellStyle,
  type OfficeFile,
  type Worksheet,
} from "../../Engine/typescript/model";
import { Workbook } from "../../Engine/typescript/formula";
import {
  rangeAddresses,
  sortSheet,
  transposeRange,
} from "../../Engine/typescript/sheet-tools";
import { download } from "./storage";
import { Command, Group, Ribbon, useCommand, useRibbon } from "./ribbon";
import { Chart, defaultRange } from "./visuals";
const functions = [
  "SUM",
  "AVERAGE",
  "MIN",
  "MAX",
  "COUNT",
  "COUNTA",
  "COUNTBLANK",
  "IF",
  "IFERROR",
  "AND",
  "OR",
  "NOT",
  "ROUND",
  "ROUNDUP",
  "ROUNDDOWN",
  "ABS",
  "SQRT",
  "POWER",
  "MOD",
  "INT",
  "CEILING",
  "FLOOR",
  "PRODUCT",
  "MEDIAN",
  "SUMIF",
  "COUNTIF",
  "CONCAT",
  "LEN",
  "LOWER",
  "UPPER",
  "TRIM",
  "LEFT",
  "RIGHT",
  "MID",
];
export default function SheetEditor({
  file,
  onChange,
  onError,
}: {
  file: OfficeFile;
  onChange: (f: OfficeFile) => void;
  onError?: (e: unknown) => void;
}) {
  const tab = useRibbon(),
    [selected, setSelected] = useState("A1"),
    [draft, setDraft] = useState(""),
    [editing, setEditing] = useState(false),
    [range, setRange] = useState("A1:B10"),
    [filterValue, setFilterValue] = useState(file.sheet.filter?.value ?? ""),
    [filterColumn, setFilterColumn] = useState(
      file.sheet.filter?.column ?? "A",
    ),
    [chartType, setChartType] = useState<"bar" | "line" | "pie">("bar"),
    [viewStart, setViewStart] = useState(0),
    [showFormulas, setShowFormulas] = useState(false),
    [gridlines, setGridlines] = useState(true),
    [showFind, setShowFind] = useState(false),
    [find, setFind] = useState("");
  const wb = useMemo(() => new Workbook(file.sheet.cells), [file.sheet.cells]),
    scroll = useRef<HTMLDivElement>(null),
    bar = useRef<HTMLInputElement>(null);
  useEffect(
    () => setDraft(file.sheet.cells[selected] ?? ""),
    [file.sheet.cells, selected, file.activeSheet],
  );
  function changeSheet(sheet: Worksheet) {
    const worksheets = [
      ...(file.worksheets ?? [{ ...file.sheet, name: "Sheet 1" }]),
    ];
    worksheets[file.activeSheet ?? 0] = sheet;
    onChange({
      ...file,
      sheet,
      worksheets,
      activeSheet: file.activeSheet ?? 0,
    });
  }
  function guard(action: () => void) {
    try {
      action();
    } catch (e) {
      onError?.(e);
    }
  }
  function commit(value = draft) {
    if (value !== (file.sheet.cells[selected] ?? "")) {
      const cells = { ...file.sheet.cells };
      if (value) cells[selected] = value;
      else delete cells[selected];
      changeSheet({ ...file.sheet, cells });
    }
    setEditing(false);
  }
  function style(patch: CellStyle) {
    guard(() => {
      const styles = { ...file.sheet.styles };
      for (const ref of rangeAddresses(range))
        styles[ref] = { ...styles[ref], ...patch };
      changeSheet({ ...file.sheet, styles });
    });
  }
  function key(e: React.KeyboardEvent) {
    if (e.key === "Enter" || e.key === "Tab") {
      e.preventDefault();
      commit();
      const m = selected.match(/^([A-Z]+)(\d+)$/)!;
      if (e.key === "Enter") {
        const row = Math.max(
          1,
          Math.min(file.sheet.rows, +m[2] + (e.shiftKey ? -1 : 1)),
        );
        setSelected(`${m[1]}${row}`);
      } else {
        let c = 0;
        for (const x of m[1]) c = c * 26 + x.charCodeAt(0) - 64;
        setSelected(
          `${columnName(Math.max(0, Math.min(file.sheet.columns - 1, c - 1 + (e.shiftKey ? -1 : 1))))}${m[2]}`,
        );
      }
    }
    if (e.key === "Escape") {
      setDraft(file.sheet.cells[selected] ?? "");
      setEditing(false);
    }
  }
  function addChart() {
    guard(() => {
      rangeAddresses(range);
      changeSheet({
        ...file.sheet,
        charts: [
          ...(file.sheet.charts ?? []),
          {
            id: crypto.randomUUID(),
            type: chartType,
            range,
            title: `${chartType[0].toUpperCase() + chartType.slice(1)} chart`,
          },
        ],
      });
    });
  }
  function addSheet() {
    const worksheets = [
      ...(file.worksheets ?? [{ ...file.sheet, name: "Sheet 1" }]),
      {
        name: `Sheet ${(file.worksheets?.length ?? 1) + 1}`,
        cells: {},
        rows: 100,
        columns: 26,
      },
    ];
    onChange({
      ...file,
      worksheets,
      activeSheet: worksheets.length - 1,
      sheet: worksheets.at(-1)!,
    });
    setSelected("A1");
    setViewStart(0);
  }
  function switchSheet(i: number) {
    const worksheets = file.worksheets!;
    onChange({ ...file, activeSheet: i, sheet: worksheets[i] });
    setSelected("A1");
    setViewStart(0);
    if (scroll.current) scroll.current.scrollTop = 0;
  }
  function findCell() {
    const ref = Object.keys(file.sheet.cells).find((r) =>
      file.sheet.cells[r].toLowerCase().includes(find.toLowerCase()),
    );
    if (ref) {
      setSelected(ref);
      const r = +ref.match(/\d+/)![0];
      if (scroll.current) scroll.current.scrollTop = (r - 1) * 30;
    } else onError?.(new Error("No matching cell."));
  }
  useCommand((c) => {
    if (c === "find" || c === "replace") setShowFind(true);
    if (c === "chart") addChart();
  });
  const filteredRows = useMemo(
    () =>
      Array.from({ length: file.sheet.rows }, (_, i) => i + 1).filter(
        (r) =>
          r === 1 ||
          !file.sheet.filter?.value ||
          wb
            .display(`${file.sheet.filter.column}${r}`)
            .toLowerCase()
            .includes(file.sheet.filter.value.toLowerCase()),
      ),
    [file.sheet.rows, file.sheet.filter, wb],
  );
  const first = Math.min(
      Math.max(0, viewStart - 8),
      Math.max(0, filteredRows.length - 55),
    ),
    visible = filteredRows.slice(first, first + 60),
    selectedStyle = file.sheet.styles?.[selected] ?? {};
  function display(address: string) {
    const value = wb.display(address),
      style = file.sheet.styles?.[address];
    if (showFormulas) return file.sheet.cells[address] ?? "";
    if (!value || !Number.isFinite(Number(value))) return value;
    const n = Number(value);
    return style?.format === "currency"
      ? n.toLocaleString(undefined, { style: "currency", currency: "INR" })
      : style?.format === "percent"
        ? `${(n * 100).toFixed(1)}%`
        : style?.format === "number"
          ? n.toLocaleString(undefined, {
              minimumFractionDigits: 2,
              maximumFractionDigits: 2,
            })
          : value;
  }
  return (
    <>
      <Ribbon>
        {tab === "Home" && (
          <>
            <Group label="Selection">
              <label className="ribbon-field">
                Range
                <input
                  aria-label="Cell range"
                  value={range}
                  onChange={(e) => setRange(e.target.value.toUpperCase())}
                />
              </label>
              <Command
                label="Use active cell"
                glyph="▣"
                onClick={() => setRange(selected)}
              />
            </Group>
            <Group label="Font & alignment">
              <Command
                label="Bold"
                glyph="B"
                onClick={() => style({ bold: !selectedStyle.bold })}
              />
              <Command
                label="Italic"
                glyph="𝑰"
                onClick={() => style({ italic: !selectedStyle.italic })}
              />
              <label className="color-label">
                A
                <input
                  type="color"
                  aria-label="Cell text color"
                  onChange={(e) => style({ color: e.target.value })}
                />
              </label>
              <label className="color-label">
                ▰
                <input
                  type="color"
                  aria-label="Cell fill color"
                  defaultValue="#e4f2e9"
                  onChange={(e) => style({ background: e.target.value })}
                />
              </label>
              {["left", "center", "right"].map((align) => (
                <Command
                  key={align}
                  label={`Align ${align}`}
                  glyph="≡"
                  onClick={() => style({ align: align as CellStyle["align"] })}
                />
              ))}
            </Group>
            <Group label="Number">
              <select
                aria-label="Number format"
                onChange={(e) =>
                  style({ format: e.target.value as CellStyle["format"] })
                }
              >
                {["general", "number", "currency", "percent"].map((v) => (
                  <option key={v}>{v}</option>
                ))}
              </select>
              <Command
                label="AutoSum"
                glyph="Σ"
                onClick={() => {
                  setDraft(`=SUM(${range})`);
                  bar.current?.focus();
                }}
              />
            </Group>
            <Group label="Cells">
              <Command
                label="25 rows"
                glyph="+"
                onClick={() =>
                  changeSheet({
                    ...file.sheet,
                    rows: Math.min(10000, file.sheet.rows + 25),
                  })
                }
              />
              <Command
                label="Add column"
                glyph="⊞"
                onClick={() =>
                  changeSheet({
                    ...file.sheet,
                    columns: Math.min(100, file.sheet.columns + 1),
                  })
                }
              />
              <Command
                label="Clear cell"
                glyph="×"
                onClick={() => commit("")}
              />
              <Command
                label="Find"
                glyph="⌕"
                onClick={() => setShowFind(!showFind)}
              />
            </Group>
          </>
        )}
        {tab === "Insert" && (
          <>
            <Group label="Charts">
              <label className="ribbon-field">
                Data range
                <input
                  aria-label="Chart range"
                  value={range}
                  onChange={(e) => setRange(e.target.value.toUpperCase())}
                />
              </label>
              <select
                aria-label="Chart type"
                value={chartType}
                onChange={(e) =>
                  setChartType(e.target.value as typeof chartType)
                }
              >
                {["bar", "line", "pie"].map((t) => (
                  <option key={t}>{t}</option>
                ))}
              </select>
              <Command label="Insert chart" glyph="▥" onClick={addChart} />
              <Command
                label="Use data extent"
                glyph="↔"
                onClick={() => setRange(defaultRange(file.sheet.cells))}
              />
            </Group>
            <Group label="Workbook">
              <Command label="New worksheet" glyph="⊞" onClick={addSheet} />
            </Group>
          </>
        )}
        {tab === "Formulas" && (
          <>
            <Group label="Function library">
              <select
                aria-label="Insert function"
                defaultValue=""
                onChange={(e) => {
                  if (e.target.value) {
                    setDraft(`=${e.target.value}(${range})`);
                    setEditing(true);
                    bar.current?.focus();
                  }
                }}
              >
                <option value="">Choose function</option>
                {functions.map((f) => (
                  <option key={f}>{f}</option>
                ))}
              </select>
              <Command
                label="AutoSum"
                glyph="Σ"
                onClick={() => {
                  setDraft(`=SUM(${range})`);
                  setEditing(true);
                  bar.current?.focus();
                }}
              />
              <Command
                label="Show formulas"
                glyph="ƒx"
                active={showFormulas}
                onClick={() => setShowFormulas(!showFormulas)}
              />
            </Group>
            <p className="ribbon-hint">
              Enter formulas in the formula bar. IF and IFERROR evaluate only
              the selected branch.
            </p>
          </>
        )}
        {tab === "Data" && (
          <>
            <Group label="Sort">
              <select
                aria-label="Data column"
                value={filterColumn}
                onChange={(e) => setFilterColumn(e.target.value)}
              >
                {Array.from({ length: file.sheet.columns }, (_, c) => (
                  <option key={c}>{columnName(c)}</option>
                ))}
              </select>
              <Command
                label="Sort ascending"
                glyph="A↓Z"
                onClick={() =>
                  guard(() => changeSheet(sortSheet(file.sheet, filterColumn)))
                }
              />
              <Command
                label="Sort descending"
                glyph="Z↓A"
                onClick={() =>
                  guard(() =>
                    changeSheet(sortSheet(file.sheet, filterColumn, true)),
                  )
                }
              />
            </Group>
            <Group label="Filter">
              <input
                aria-label="Filter value"
                placeholder="Contains…"
                value={filterValue}
                onChange={(e) => setFilterValue(e.target.value)}
              />
              <Command
                label="Apply filter"
                glyph="▽"
                onClick={() =>
                  changeSheet({
                    ...file.sheet,
                    filter: { column: filterColumn, value: filterValue },
                  })
                }
              />
              <Command
                label="Clear filter"
                glyph="×"
                onClick={() => {
                  setFilterValue("");
                  changeSheet({ ...file.sheet, filter: undefined });
                }}
              />
            </Group>
            <Group label="Tools">
              <Command
                label="Transpose range"
                glyph="⇄"
                onClick={() =>
                  guard(() => changeSheet(transposeRange(file.sheet, range)))
                }
              />
              <Command
                label="Remove duplicate rows"
                glyph="≠"
                onClick={() => {
                  const last = Math.max(
                      1,
                      ...Object.keys(file.sheet.cells).map(
                        (r) => +r.match(/\d+/)![0],
                      ),
                    ),
                    seen = new Set<string>(),
                    cells: Record<string, string> = {};
                  let next = 1;
                  for (let r = 1; r <= last; r++) {
                    const vals = Array.from(
                      { length: file.sheet.columns },
                      (_, c) => file.sheet.cells[`${columnName(c)}${r}`] ?? "",
                    );
                    const key = JSON.stringify(vals);
                    if (r > 1 && seen.has(key)) continue;
                    seen.add(key);
                    vals.forEach((v, c) => {
                      if (v) cells[`${columnName(c)}${next}`] = v;
                    });
                    next++;
                  }
                  changeSheet({ ...file.sheet, cells });
                }}
              />
            </Group>
          </>
        )}
        {tab === "Page Layout" && (
          <Group label="Print setup">
            <Command
              label="Portrait"
              glyph="▯"
              onClick={() =>
                onChange({
                  ...file,
                  layout: { ...file.layout, orientation: "portrait" },
                })
              }
            />
            <Command
              label="Landscape"
              glyph="▭"
              onClick={() =>
                onChange({
                  ...file,
                  layout: { ...file.layout, orientation: "landscape" },
                })
              }
            />
            <p className="ribbon-hint">
              Use Print preview in File to review the used cells before
              printing.
            </p>
          </Group>
        )}
        {tab === "Review" && (
          <Group label="Workbook review">
            <Command
              label="Find errors"
              glyph="!"
              onClick={() => {
                const errors = Object.keys(file.sheet.cells).filter((r) =>
                  wb.display(r).startsWith("#"),
                );
                onError?.(
                  new Error(
                    errors.length
                      ? `Formula errors: ${errors.join(", ")}`
                      : "No formula errors in filled cells.",
                  ),
                );
              }}
            />
            <Command label="Find" glyph="⌕" onClick={() => setShowFind(true)} />
            <span>{Object.keys(file.sheet.cells).length} populated cells</span>
          </Group>
        )}
        {tab === "View" && (
          <>
            <Group label="Worksheet view">
              <Command
                label="Freeze first row"
                glyph="⊟"
                active={!!file.sheet.frozen}
                onClick={() =>
                  changeSheet({ ...file.sheet, frozen: !file.sheet.frozen })
                }
              />
              <Command
                label="Gridlines"
                glyph="▦"
                active={gridlines}
                onClick={() => setGridlines(!gridlines)}
              />
              <Command
                label="Show formulas"
                glyph="ƒx"
                onClick={() => setShowFormulas(!showFormulas)}
              />
            </Group>
            <Group label="Export">
              <Command
                label="Export CSV"
                glyph="↓"
                onClick={() =>
                  guard(() =>
                    download(
                      exportCSV(file.sheet.cells, (r) => wb.display(r)),
                      `${file.title}.csv`,
                      "text/csv",
                    ),
                  )
                }
              />
            </Group>
          </>
        )}
      </Ribbon>
      {showFind && (
        <div className="find-bar">
          <input
            aria-label="Find in cells"
            value={find}
            onChange={(e) => setFind(e.target.value)}
            placeholder="Find a cell by its contents"
          />
          <button onClick={findCell}>Find</button>
          <button aria-label="Close find" onClick={() => setShowFind(false)}>
            ✕
          </button>
        </div>
      )}
      <div className="formula-bar">
        <input
          className="cell-address"
          aria-label="Go to cell"
          value={selected}
          onChange={(e) => {
            if (/^[A-Z]+[1-9]\d*$/.test(e.target.value.toUpperCase()))
              setSelected(e.target.value.toUpperCase());
          }}
        />
        <span className="fx">ƒx</span>
        <input
          ref={bar}
          aria-label="Cell value or formula"
          value={draft}
          onFocus={() => setEditing(true)}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={key}
          onBlur={() => commit()}
        />
      </div>
      <div
        className="sheet-scroll"
        ref={scroll}
        onScroll={(e) =>
          setViewStart(Math.floor(e.currentTarget.scrollTop / 30))
        }
      >
        <table
          className={`spreadsheet ${gridlines ? "" : "no-gridlines"} ${file.sheet.frozen ? "freeze-row" : ""}`}
          aria-label="Spreadsheet"
        >
          <thead>
            <tr>
              <th className="corner" />
              {Array.from({ length: file.sheet.columns }, (_, c) => (
                <th key={c}>{columnName(c)}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {first > 0 && (
              <tr aria-hidden="true" className="virtual-spacer">
                <td
                  colSpan={file.sheet.columns + 1}
                  style={{ height: first * 30, padding: 0 }}
                />
              </tr>
            )}
            {visible.map((r) => (
              <tr key={r}>
                <th>{r}</th>
                {Array.from({ length: file.sheet.columns }, (_, c) => {
                  const address = `${columnName(c)}${r}`,
                    active = selected === address,
                    s = file.sheet.styles?.[address];
                  return (
                    <td
                      key={address}
                      style={{
                        fontWeight: s?.bold ? 700 : undefined,
                        fontStyle: s?.italic ? "italic" : undefined,
                        color: s?.color,
                        background: s?.background,
                        textAlign: s?.align,
                      }}
                      className={`${active ? "selected-cell" : ""} ${file.sheet.cells[address]?.startsWith("=") ? "calculated" : ""}`}
                      onClick={() => {
                        if (editing) commit();
                        setSelected(address);
                      }}
                      onDoubleClick={() => setEditing(true)}
                      onPaste={(e) => {
                        const text = e.clipboardData.getData("text/plain");
                        if (!text.includes("\t") && !text.includes("\n"))
                          return;
                        e.preventDefault();
                        const rows = text.replaceAll("\r", "").split("\n");
                        if (
                          rows.length > 10000 ||
                          rows.some((row) => row.split("\t").length > 100)
                        ) {
                          onError?.(
                            new Error(
                              "Paste up to 10,000 rows and 100 columns.",
                            ),
                          );
                          return;
                        }
                        const cells = { ...file.sheet.cells };
                        rows.forEach((row, i) =>
                          row.split("\t").forEach((v, j) => {
                            if (
                              c + j < file.sheet.columns &&
                              r + i <= file.sheet.rows
                            )
                              cells[`${columnName(c + j)}${r + i}`] = v;
                          }),
                        );
                        changeSheet({ ...file.sheet, cells });
                      }}
                    >
                      {active ? (
                        <input
                          aria-label={address}
                          value={editing ? draft : display(address)}
                          onFocus={() => {
                            setEditing(true);
                            setDraft(file.sheet.cells[address] ?? "");
                          }}
                          onChange={(e) => {
                            setDraft(e.target.value);
                            setEditing(true);
                          }}
                          onKeyDown={key}
                          onBlur={() => commit()}
                        />
                      ) : (
                        display(address)
                      )}
                    </td>
                  );
                })}
              </tr>
            ))}
            {first + visible.length < filteredRows.length && (
              <tr aria-hidden="true" className="virtual-spacer">
                <td
                  colSpan={file.sheet.columns + 1}
                  style={{
                    height: (filteredRows.length - first - visible.length) * 30,
                    padding: 0,
                  }}
                />
              </tr>
            )}
          </tbody>
        </table>
      </div>
      {!!file.sheet.charts?.length && (
        <div className="sheet-charts">
          {file.sheet.charts.map((spec) => (
            <div key={spec.id}>
              <Chart spec={spec} cells={file.sheet.cells} />
              <button
                aria-label="Delete chart"
                onClick={() =>
                  changeSheet({
                    ...file.sheet,
                    charts: file.sheet.charts?.filter((c) => c.id !== spec.id),
                  })
                }
              >
                Remove chart
              </button>
            </div>
          ))}
        </div>
      )}
      <footer className="statusbar">
        <div className="worksheet-tabs">
          {(file.worksheets ?? [{ ...file.sheet, name: "Sheet 1" }]).map(
            (s, i) => (
              <button
                className={i === (file.activeSheet ?? 0) ? "active" : ""}
                key={i}
                onClick={() => file.worksheets && switchSheet(i)}
                onDoubleClick={() => {
                  const name = prompt("Worksheet name", s.name);
                  if (name) changeSheet({ ...file.sheet, name });
                }}
              >
                {s.name ?? `Sheet ${i + 1}`}
              </button>
            ),
          )}
          <button aria-label="Add worksheet" onClick={addSheet}>
            +
          </button>
        </div>
        <span>{Object.keys(file.sheet.cells).length} filled cells</span>
        <div />
        <span>
          {file.sheet.filter?.value
            ? `${filteredRows.length - 1} matching rows`
            : "Ready"}
        </span>
        <span>{file.sheet.rows} rows</span>
      </footer>
    </>
  );
}

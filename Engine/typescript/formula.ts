import { columnIndex, columnName } from "./model";
type Node = { type: string; value?: string; args?: Node[] };
type Value = number | string | boolean | Value[];
class FormulaError extends Error {}
const fail = (message: string): never => {
  throw new FormulaError(message);
};
class Parser {
  tokens: string[];
  pos = 0;
  constructor(input: string) {
    this.tokens = [];
    const re =
      /\s*(\d*\.\d+(?:e[+-]?\d+)?|\d+(?:e[+-]?\d+)?|"(?:[^"]|"")*"|\$?[A-Za-z_]+\$?\d*|<>|<=|>=|[+\-*/^(),:=<>])/iy;
    let at = 0;
    while (at < input.length) {
      if (!input.slice(at).trim()) break;
      re.lastIndex = at;
      const m = re.exec(input);
      if (!m) fail("#PARSE!");
      this.tokens.push(
        m![1].startsWith('"') ? m![1] : m![1].replaceAll("$", ""),
      );
      at = re.lastIndex;
    }
  }
  peek() {
    return this.tokens[this.pos];
  }
  take() {
    return this.tokens[this.pos++];
  }
  parse(): Node {
    const n = this.expr(0);
    if (this.pos !== this.tokens.length) fail("#PARSE!");
    return n;
  }
  expr(min: number): Node {
    let left = this.primary();
    const precedence: Record<string, number> = {
      "=": 1,
      "<>": 1,
      "<": 1,
      ">": 1,
      "<=": 1,
      ">=": 1,
      "+": 2,
      "-": 2,
      "*": 3,
      "/": 3,
      "^": 4,
    };
    while (precedence[this.peek()] >= min) {
      const op = this.take(),
        p = precedence[op];
      left = {
        type: "binary",
        value: op,
        args: [left, this.expr(p + (op === "^" ? 0 : 1))],
      };
    }
    return left;
  }
  primary(): Node {
    const token = this.take();
    if (!token) return fail("#PARSE!");
    if (token === "+" || token === "-")
      return { type: "unary", value: token, args: [this.expr(4)] };
    if (token === "(") {
      const n = this.expr(0);
      if (this.take() !== ")") fail("#PARSE!");
      return n;
    }
    if (/^\d|^\./.test(token)) return { type: "number", value: token };
    if (token.startsWith('"'))
      return {
        type: "string",
        value: token.slice(1, -1).replaceAll('""', '"'),
      };
    if (/^[A-Z_]+\d*$/i.test(token)) {
      const name = token.toUpperCase();
      if (this.peek() === "(") {
        this.take();
        const args: Node[] = [];
        if (this.peek() !== ")") {
          do {
            args.push(this.expr(0));
            if (this.peek() !== ",") break;
            this.take();
          } while (true);
        }
        if (this.take() !== ")") fail("#PARSE!");
        return { type: "function", value: name, args };
      }
      if (this.peek() === ":") {
        this.take();
        const end = this.take();
        if (!/^[A-Z]+[1-9]\d*$/i.test(end)) fail("#REF!");
        return { type: "range", value: `${name}:${end.toUpperCase()}` };
      }
      return { type: "ref", value: name };
    }
    return fail("#PARSE!");
  }
}
const numeric = (value: Value): number => {
  if (Array.isArray(value)) return fail("#VALUE!");
  const n = value === "" ? 0 : Number(value);
  return Number.isFinite(n) ? n : fail("#VALUE!");
};
export class Workbook {
  private cache = new Map<string, Value>();
  constructor(public cells: Record<string, string>) {}
  value(ref: string, active = new Set<string>()): Value {
    ref = ref.toUpperCase();
    if (this.cache.has(ref)) return this.cache.get(ref)!;
    if (active.has(ref)) return fail("#CYCLE!");
    if (active.size > 200) return fail("#DEPTH!");
    const raw = this.cells[ref] ?? "";
    if (!raw.startsWith("="))
      return raw !== "" && Number.isFinite(Number(raw)) ? Number(raw) : raw;
    active.add(ref);
    try {
      const value = this.eval(new Parser(raw.slice(1)).parse(), active);
      this.cache.set(ref, value);
      return value;
    } finally {
      active.delete(ref);
    }
  }
  display(ref: string): string {
    try {
      const v = this.value(ref);
      if (Array.isArray(v)) return "#VALUE!";
      if (typeof v === "number")
        return Number.isFinite(v) ? String(Number(v.toPrecision(12))) : "#NUM!";
      return typeof v === "boolean" ? String(v).toUpperCase() : v;
    } catch (e) {
      return e instanceof FormulaError ? e.message : "#ERROR!";
    }
  }
  private eval(n: Node, active: Set<string>): Value {
    const args = n.args ?? [];
    const val = n.value!;
    if (n.type === "number") return Number(val);
    if (n.type === "string") return val;
    if (n.type === "ref") {
      if (val === "TRUE" || val === "FALSE") return val === "TRUE";
      if (!/^[A-Z]+[1-9]\d*$/.test(val)) return fail("#NAME?");
      return this.value(val, active);
    }
    if (n.type === "range") {
      const [a, b] = val.split(":").map((x) => ({
        col: columnIndex(x.match(/[A-Z]+/)![0]),
        row: Number(x.match(/\d+/)![0]),
      }));
      if (
        (Math.abs(a.col - b.col) + 1) * (Math.abs(a.row - b.row) + 1) >
        100000
      )
        return fail("#SIZE!");
      const values: Value[] = [];
      for (let r = Math.min(a.row, b.row); r <= Math.max(a.row, b.row); r++)
        for (let c = Math.min(a.col, b.col); c <= Math.max(a.col, b.col); c++)
          values.push(this.value(`${columnName(c)}${r}`, active));
      return values;
    }
    if (n.type === "unary")
      return (val === "-" ? -1 : 1) * numeric(this.eval(args[0], active));
    if (n.type === "binary") {
      const a = this.eval(args[0], active),
        b = this.eval(args[1], active);
      if (val === "=") return a === b;
      if (val === "<>") return a !== b;
      const x = numeric(a),
        y = numeric(b);
      switch (val) {
        case "+":
          return x + y;
        case "-":
          return x - y;
        case "*":
          return x * y;
        case "/":
          return y === 0 ? fail("#DIV/0!") : x / y;
        case "^":
          return x ** y;
        case "<":
          return x < y;
        case ">":
          return x > y;
        case "<=":
          return x <= y;
        case ">=":
          return x >= y;
      }
    }
    if (n.type === "function") {
      if (val === "IF") {
        if (args.length < 2 || args.length > 3) return fail("#ARGS!");
        return this.eval(args[0], active)
          ? this.eval(args[1], active)
          : args[2]
            ? this.eval(args[2], active)
            : false;
      }
      if (val === "IFERROR") {
        if (args.length !== 2) return fail("#ARGS!");
        try {
          const result = this.eval(args[0], active);
          if (typeof result === "number" && !Number.isFinite(result))
            return this.eval(args[1], active);
          return result;
        } catch (e) {
          if (e instanceof FormulaError) return this.eval(args[1], active);
          throw e;
        }
      }
      if (val === "COUNTIF" || val === "SUMIF") {
        if (args.length < 2 || args.length > (val === "SUMIF" ? 3 : 2))
          return fail("#ARGS!");
        const source = this.eval(args[0], active),
          criterion = this.eval(args[1], active),
          sums = args[2] ? this.eval(args[2], active) : source;
        const list = Array.isArray(source) ? source : [source],
          sumList = Array.isArray(sums) ? sums : [sums];
        if (list.length !== sumList.length) return fail("#VALUE!");
        const criteria = String(criterion),
          m = /^(<=|>=|<>|=|<|>)(.*)$/.exec(criteria),
          op = m?.[1] ?? "=",
          expected = m?.[2] ?? criteria;
        const lower = (s: string) =>
          s.replace(/[A-Z]/g, (c) => c.toLowerCase());
        const compare = (v: Value) => {
          const a = typeof v === "number" ? v : lower(String(v)),
            b =
              expected !== "" && Number.isFinite(Number(expected))
                ? Number(expected)
                : lower(expected);
          if (op === "=") return String(a) === String(b);
          if (op === "<>") return String(a) !== String(b);
          if (typeof a !== "number" || typeof b !== "number") return false;
          return op === "<"
            ? a < b
            : op === ">"
              ? a > b
              : op === "<="
                ? a <= b
                : a >= b;
        };
        return list.reduce<number>(
          (sum, v, i) =>
            sum +
            (compare(v)
              ? val === "COUNTIF"
                ? 1
                : typeof sumList[i] === "number"
                  ? (sumList[i] as number)
                  : 0
              : 0),
          0,
        );
      }
      const all: (number | string | boolean)[] = [];
      const flatten = (value: Value): void => {
        if (Array.isArray(value)) value.forEach(flatten);
        else all.push(value);
      };
      args.forEach((a) => flatten(this.eval(a, active)));
      const numbers = all.filter((x): x is number => typeof x === "number");
      switch (val) {
        case "SUM":
          return numbers.reduce((a, b) => a + b, 0);
        case "AVERAGE":
          return numbers.length
            ? numbers.reduce((a, b) => a + b, 0) / numbers.length
            : fail("#DIV/0!");
        case "MIN":
          return numbers.length ? Math.min(...numbers) : 0;
        case "MAX":
          return numbers.length ? Math.max(...numbers) : 0;
        case "COUNT":
          return numbers.length;
        case "COUNTA":
          return all.filter((x) => x !== "").length;
        case "COUNTBLANK":
          return all.filter((x) => x === "").length;
        case "AND":
          if (!all.length) return fail("#ARGS!");
          return all.every(Boolean);
        case "OR":
          if (!all.length) return fail("#ARGS!");
          return all.some(Boolean);
        case "NOT":
          if (all.length !== 1) return fail("#ARGS!");
          return !all[0];
        case "PRODUCT":
          return numbers.length ? numbers.reduce((a, b) => a * b, 1) : 0;
        case "MEDIAN": {
          if (!numbers.length) return fail("#NUM!");
          const s = numbers.slice().sort((a, b) => a - b),
            m = Math.floor(s.length / 2);
          return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2;
        }
        case "CONCAT":
        case "CONCATENATE":
          return all
            .map((x) =>
              typeof x === "boolean" ? String(x).toUpperCase() : String(x),
            )
            .join("");
        case "LEN":
          if (all.length !== 1) return fail("#ARGS!");
          return Array.from(String(all[0])).length;
        case "LOWER":
        case "UPPER":
        case "TRIM": {
          if (all.length !== 1) return fail("#ARGS!");
          const s = String(all[0]);
          return val === "LOWER"
            ? s.replace(/[A-Z]/g, (c) => c.toLowerCase())
            : val === "UPPER"
              ? s.replace(/[a-z]/g, (c) => c.toUpperCase())
              : s.replace(/^ +| +$/g, "").replace(/ +/g, " ");
        }
        case "LEFT":
        case "RIGHT": {
          if (all.length < 1 || all.length > 2) return fail("#ARGS!");
          const count = Math.trunc(numeric(all[1] ?? 1));
          if (count < 0) return fail("#VALUE!");
          const s = Array.from(String(all[0]));
          return (
            val === "LEFT" ? s.slice(0, count) : count ? s.slice(-count) : []
          ).join("");
        }
        case "MID": {
          if (all.length !== 3) return fail("#ARGS!");
          const start = Math.trunc(numeric(all[1])),
            count = Math.trunc(numeric(all[2]));
          if (start < 1 || count < 0) return fail("#VALUE!");
          return Array.from(String(all[0]))
            .slice(start - 1, start - 1 + count)
            .join("");
        }
        case "SQRT":
          if (all.length !== 1) return fail("#ARGS!");
          return numeric(all[0]) < 0
            ? fail("#NUM!")
            : Math.sqrt(numeric(all[0]));
        case "POWER":
          if (all.length !== 2) return fail("#ARGS!");
          return numeric(all[0]) ** numeric(all[1]);
        case "INT":
          if (all.length !== 1) return fail("#ARGS!");
          return Math.floor(numeric(all[0]));
        case "MOD": {
          if (all.length !== 2) return fail("#ARGS!");
          const a = numeric(all[0]),
            b = numeric(all[1]);
          return b === 0 ? fail("#DIV/0!") : a - b * Math.floor(a / b);
        }
        case "CEILING":
        case "FLOOR": {
          if (all.length < 1 || all.length > 2) return fail("#ARGS!");
          const x = numeric(all[0]),
            s = Math.abs(numeric(all[1] ?? 1));
          return s === 0
            ? 0
            : (val === "CEILING" ? Math.ceil(x / s) : Math.floor(x / s)) * s;
        }
        case "ROUNDUP":
        case "ROUNDDOWN": {
          if (all.length < 1 || all.length > 2) return fail("#ARGS!");
          const x = numeric(all[0]),
            p = 10 ** numeric(all[1] ?? 0);
          return (
            (Math.sign(x) *
              (val === "ROUNDUP"
                ? Math.ceil(Math.abs(x) * p)
                : Math.floor(Math.abs(x) * p))) /
            p
          );
        }
        case "ABS":
          if (all.length !== 1) return fail("#ARGS!");
          return Math.abs(numeric(all[0]));
        case "ROUND":
          if (all.length < 1 || all.length > 2) return fail("#ARGS!");
          {
            const p = 10 ** numeric(all[1] ?? 0);
            const x = numeric(all[0]);
            return (
              (Math.sign(x) * Math.round(Math.abs(x) * p + Number.EPSILON)) / p
            );
          }
        default:
          return fail("#NAME?");
      }
    }
    return fail("#PARSE!");
  }
}

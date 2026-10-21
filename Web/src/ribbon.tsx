import {
  createContext,
  useContext,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type ReactNode,
} from "react";
import type { Kind } from "../../Engine/typescript/model";

export const ribbonTabs: Record<Kind, string[]> = {
  document: [
    "Home",
    "Insert",
    "Design",
    "Layout",
    "References",
    "Review",
    "View",
  ],
  spreadsheet: [
    "Home",
    "Insert",
    "Page Layout",
    "Formulas",
    "Data",
    "Review",
    "View",
  ],
  presentation: [
    "Home",
    "Insert",
    "Design",
    "Transitions",
    "Animations",
    "Slide Show",
    "Review",
    "View",
  ],
  pdf: ["Home", "Organize", "Edit", "Convert", "Compress", "View"],
  image: [
    "Home",
    "Transform",
    "Adjust",
    "Annotate",
    "Compress",
    "Convert",
    "View",
  ],
};
export const RibbonContext = createContext("Home");
export const useRibbon = () => useContext(RibbonContext);
export function RibbonTabs({ kind, selected, onSelect }: { kind: Kind; selected: string; onSelect: (tab: string) => void }) {
  const ref = useRef<HTMLDivElement>(null);
  const [pill, setPill] = useState({ left: 0, width: 0 });
  useLayoutEffect(() => {
    const row = ref.current;
    if (!row) return;
    const measure = () => {
      const button = row.querySelector<HTMLButtonElement>('[aria-selected="true"]');
      if (button) setPill({ left: button.offsetLeft, width: button.offsetWidth });
    };
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(row);
    row.querySelectorAll('button').forEach(button => observer.observe(button));
    document.fonts.ready.then(measure);
    return () => observer.disconnect();
  }, [kind, selected]);
  return <div ref={ref} className="ribbon-tabs" role="tablist" aria-label="Ribbon tabs">
    <span className="ribbon-pill" aria-hidden="true" style={{ width: pill.width, transform: `translateX(${pill.left}px)` }} />
    {ribbonTabs[kind].map((tab, index, tabs) => <button key={tab} role="tab" aria-selected={selected === tab} tabIndex={selected === tab ? 0 : -1} className={selected === tab ? 'selected' : ''} onClick={() => onSelect(tab)} onKeyDown={event => {
      const next = event.key === 'ArrowRight' ? (index + 1) % tabs.length : event.key === 'ArrowLeft' ? (index + tabs.length - 1) % tabs.length : event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 : -1;
      if (next >= 0) { event.preventDefault(); onSelect(tabs[next]); const button = ref.current?.querySelectorAll('button')[next]; button?.focus(); button?.scrollIntoView({ block: 'nearest', inline: 'nearest' }); }
    }}>{tab}</button>)}
  </div>;
}
export function FileDropdown({ items }: { items: { label: string; action: () => void }[] }) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    if (!open) return;
    const outside = (event: PointerEvent) => { if (!ref.current?.contains(event.target as Node)) setOpen(false); };
    document.addEventListener('pointerdown', outside);
    ref.current?.querySelector<HTMLButtonElement>('[role="menuitem"]')?.focus();
    return () => document.removeEventListener('pointerdown', outside);
  }, [open]);
  return <div className="file-dropdown" ref={ref} onBlur={event => { if (!event.currentTarget.contains(event.relatedTarget)) setOpen(false); }} onKeyDown={event => {
    if (event.key === 'Escape') { setOpen(false); trigger.current?.focus(); }
    if (open && ['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
      event.preventDefault();
      const buttons = Array.from(ref.current?.querySelectorAll<HTMLButtonElement>('[role="menuitem"]') ?? []);
      const current = buttons.indexOf(document.activeElement as HTMLButtonElement);
      buttons[event.key === 'Home' ? 0 : event.key === 'End' ? buttons.length - 1 : (current + (event.key === 'ArrowUp' ? -1 : 1) + buttons.length) % buttons.length]?.focus();
    }
  }}>
    <button ref={trigger} className="file-trigger" aria-haspopup="menu" aria-expanded={open} onClick={() => setOpen(!open)} onKeyDown={event => { if (!open && event.key === 'ArrowDown') { event.preventDefault(); setOpen(true); } }}>File <span aria-hidden="true">⌄</span></button>
    {open && <div className="file-dropdown-menu" role="menu" aria-label="File">{items.map(item => <button key={item.label} role="menuitem" onClick={() => { setOpen(false); trigger.current?.focus(); item.action(); }}>{item.label}</button>)}</div>}
  </div>;
}
export function Ribbon({ children }: { children: ReactNode }) {
  return (
    <div
      className="ribbon professional-ribbon"
      role="region"
      aria-label="Ribbon commands"
    >
      {children}
    </div>
  );
}
export function Group({
  label,
  children,
}: {
  label: string;
  children: ReactNode;
}) {
  return (
    <div className="ribbon-group">
      <div className="ribbon-controls">{children}</div>
      <span>{label}</span>
    </div>
  );
}
export function Command({
  label,
  glyph,
  onClick,
  disabled = false,
  active = false,
}: {
  label: string;
  glyph?: string;
  onClick: () => void;
  disabled?: boolean;
  active?: boolean;
}) {
  return (
    <button
      className={`ribbon-command ${active ? "active" : ""}`}
      disabled={disabled}
      onMouseDown={(e) => e.preventDefault()}
      onClick={onClick}
      aria-label={label}
      title={label}
    >
      <b aria-hidden="true">{glyph ?? label.charAt(0)}</b>
      <span>{label}</span>
    </button>
  );
}
export function Modal({
  title,
  children,
  onClose,
  wide = false,
}: {
  title: string;
  children: ReactNode;
  onClose: () => void;
  wide?: boolean;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const dialog = ref.current;
    dialog?.showModal();
    return () => dialog?.close();
  }, []);
  return (
    <dialog
      ref={ref}
      className={`office-dialog ${wide ? "wide" : ""}`}
      onCancel={onClose}
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
      aria-label={title}
    >
      <header>
        <h2>{title}</h2>
        <button aria-label="Close dialog" onClick={onClose}>
          ✕
        </button>
      </header>
      {children}
    </dialog>
  );
}
export function useCommand(handler: (command: string) => void) {
  useEffect(() => {
    const listener = (e: Event) => handler((e as CustomEvent<string>).detail);
    window.addEventListener("gaius-command", listener);
    return () => window.removeEventListener("gaius-command", listener);
  });
}
export function dispatchCommand(command: string) {
  window.dispatchEvent(new CustomEvent("gaius-command", { detail: command }));
}

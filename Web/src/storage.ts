import { parseFile, type OfficeFile } from "../../Engine/typescript/model";
const connection = new Promise<IDBDatabase>((resolve, reject) => {
  const request = indexedDB.open("gaius-office", 1);
  request.onupgradeneeded = () =>
    request.result.createObjectStore("files", { keyPath: "id" });
  request.onsuccess = () => resolve(request.result);
  request.onerror = () => reject(request.error);
});
export async function listFiles(): Promise<OfficeFile[]> {
  const db = await connection;
  return new Promise((resolve, reject) => {
    const request = db.transaction("files").objectStore("files").getAll();
    request.onsuccess = () => {
      try {
        resolve(
          request.result
            .map((v) => parseFile(JSON.stringify(v)))
            .sort((a, b) => b.updatedAt.localeCompare(a.updatedAt)),
        );
      } catch (e) {
        reject(e);
      }
    };
    request.onerror = () => reject(request.error);
  });
}
export async function saveFile(
  file: OfficeFile,
  expectedRevision?: string,
): Promise<void> {
  const db = await connection;
  return new Promise((resolve, reject) => {
    const tx = db.transaction("files", "readwrite"),
      store = tx.objectStore("files");
    let conflict = false;
    const check = store.get(file.id);
    check.onsuccess = () => {
      if (
        expectedRevision &&
        (!check.result || check.result.updatedAt !== expectedRevision)
      ) {
        conflict = true;
        tx.abort();
      } else store.put(file);
    };
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error);
    tx.onabort = () =>
      reject(
        conflict
          ? new Error(
              "This file changed in another window. Export your copy before reopening the latest version.",
            )
          : (tx.error ?? new Error("Storage transaction aborted.")),
      );
  });
}
export async function removeFile(id: string): Promise<void> {
  const db = await connection;
  return new Promise((resolve, reject) => {
    const tx = db.transaction("files", "readwrite");
    tx.objectStore("files").delete(id);
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error);
  });
}
export function download(
  data: BlobPart,
  filename: string,
  type = "application/octet-stream",
) {
  const url = URL.createObjectURL(new Blob([data], { type }));
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  a.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
export function readDataURL(file: File): Promise<string> {
  if (file.size > 40 * 1024 * 1024)
    return Promise.reject(new Error("Choose a file smaller than 40 MB."));
  return new Promise((resolve, reject) => {
    const r = new FileReader();
    r.onload = () => resolve(String(r.result));
    r.onerror = () => reject(r.error);
    r.readAsDataURL(file);
  });
}
export function safeHTML(html: string): string {
  const parsed = new DOMParser().parseFromString(html, "text/html");
  const allowed = new Set([
    "P",
    "BR",
    "DIV",
    "SPAN",
    "B",
    "STRONG",
    "I",
    "EM",
    "U",
    "S",
    "H1",
    "H2",
    "H3",
    "UL",
    "OL",
    "LI",
    "BLOCKQUOTE",
    "TABLE",
    "TBODY",
    "TR",
    "TD",
    "TH",
    "FONT",
    "HR",
  ]);
  const styles = new Set([
    "text-align",
    "font-weight",
    "font-style",
    "text-decoration",
    "color",
    "background-color",
    "font-size",
    "font-family",
  ]);
  for (const el of Array.from(parsed.body.querySelectorAll("*"))) {
    if (!allowed.has(el.tagName)) {
      el.remove();
      continue;
    }
    for (const attr of Array.from(el.attributes)) {
      if (attr.name === "style") {
        const source = (el as HTMLElement).style;
        for (const name of Array.from(source))
          if (
            !styles.has(name) ||
            /url\s*\(|expression\s*\(/i.test(source.getPropertyValue(name))
          )
            source.removeProperty(name);
      } else if (!(
        el.tagName === "FONT" && ["color", "face", "size"].includes(attr.name)
      ))
        el.removeAttribute(attr.name);
    }
  }
  return parsed.body.innerHTML;
}

// Minimal ZIP container for Office Open XML. Writes stored entries; reads stored
// and raw-DEFLATE entries. Rejects encryption, ZIP64, and oversized archives.
const encoder = new TextEncoder(),
  decoder = new TextDecoder();
function crc32(bytes: Uint8Array): number {
  let crc = 0xffffffff;
  for (const byte of bytes) {
    crc ^= byte;
    for (let j = 0; j < 8; j++) crc = (crc >>> 1) ^ (crc & 1 ? 0xedb88320 : 0);
  }
  return (crc ^ 0xffffffff) >>> 0;
}
export function writeZip(
  files: Record<string, string | Uint8Array>,
): Uint8Array {
  const entries = Object.entries(files).map(([name, value]) => ({
    name: encoder.encode(name),
    bytes: typeof value === "string" ? encoder.encode(value) : value,
  }));
  const parts: Uint8Array[] = [],
    central: Uint8Array[] = [];
  let offset = 0,
    centralSize = 0;
  for (const entry of entries) {
    const crc = crc32(entry.bytes),
      header = new Uint8Array(30 + entry.name.length),
      h = new DataView(header.buffer);
    h.setUint32(0, 0x04034b50, true);
    h.setUint16(4, 20, true);
    h.setUint16(6, 0x800, true);
    h.setUint32(14, crc, true);
    h.setUint32(18, entry.bytes.length, true);
    h.setUint32(22, entry.bytes.length, true);
    h.setUint16(26, entry.name.length, true);
    header.set(entry.name, 30);
    parts.push(header, entry.bytes);
    const record = new Uint8Array(46 + entry.name.length),
      c = new DataView(record.buffer);
    c.setUint32(0, 0x02014b50, true);
    c.setUint16(4, 20, true);
    c.setUint16(6, 20, true);
    c.setUint16(8, 0x800, true);
    c.setUint32(16, crc, true);
    c.setUint32(20, entry.bytes.length, true);
    c.setUint32(24, entry.bytes.length, true);
    c.setUint16(28, entry.name.length, true);
    c.setUint32(42, offset, true);
    record.set(entry.name, 46);
    central.push(record);
    centralSize += record.length;
    offset += header.length + entry.bytes.length;
  }
  const end = new Uint8Array(22),
    e = new DataView(end.buffer);
  e.setUint32(0, 0x06054b50, true);
  e.setUint16(8, entries.length, true);
  e.setUint16(10, entries.length, true);
  e.setUint32(12, centralSize, true);
  e.setUint32(16, offset, true);
  const result = new Uint8Array(offset + centralSize + 22);
  let at = 0;
  for (const part of [...parts, ...central, end]) {
    result.set(part, at);
    at += part.length;
  }
  return result;
}
export async function readZip(
  bytes: Uint8Array,
): Promise<Record<string, Uint8Array>> {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  let end = -1;
  for (let p = bytes.length - 22; p >= Math.max(0, bytes.length - 65557); p--)
    if (view.getUint32(p, true) === 0x06054b50) {
      end = p;
      break;
    }
  if (end < 0) throw new Error("Invalid ZIP archive.");
  const count = view.getUint16(end + 10, true);
  let offset = view.getUint32(end + 16, true),
    total = 0;
  if (
    count > 2000 ||
    view.getUint16(end + 4, true) ||
    view.getUint16(end + 6, true)
  )
    throw new Error("Unsupported or oversized ZIP archive.");
  const result: Record<string, Uint8Array> = Object.create(null);
  for (let i = 0; i < count; i++) {
    if (
      offset + 46 > bytes.length ||
      view.getUint32(offset, true) !== 0x02014b50
    )
      throw new Error("Invalid ZIP directory.");
    const flags = view.getUint16(offset + 8, true),
      method = view.getUint16(offset + 10, true),
      crc = view.getUint32(offset + 16, true),
      compressed = view.getUint32(offset + 20, true),
      size = view.getUint32(offset + 24, true),
      length = view.getUint16(offset + 28, true),
      extra = view.getUint16(offset + 30, true),
      comment = view.getUint16(offset + 32, true),
      local = view.getUint32(offset + 42, true);
    total += size;
    if (
      flags & 1 ||
      total > 64 * 1024 * 1024 ||
      size === 0xffffffff ||
      local + 30 > bytes.length
    )
      throw new Error(
        "Encrypted, ZIP64, or oversized Office files are not supported.",
      );
    const name = decoder.decode(
      bytes.subarray(offset + 46, offset + 46 + length),
    );
    if (view.getUint32(local, true) !== 0x04034b50)
      throw new Error("Invalid ZIP entry.");
    const start =
      local +
      30 +
      view.getUint16(local + 26, true) +
      view.getUint16(local + 28, true);
    if (start + compressed > bytes.length)
      throw new Error("Truncated ZIP entry.");
    let data = bytes.slice(start, start + compressed);
    if (method === 8) {
      const reader = new Blob([new Uint8Array(data)])
        .stream()
        .pipeThrough(
          new DecompressionStream("deflate-raw" as CompressionFormat),
        )
        .getReader();
      const chunks: Uint8Array[] = [];
      let received = 0;
      try {
        while (true) {
          const { value, done } = await reader.read();
          if (done) break;
          received += value.length;
          if (received > size || received > 64 * 1024 * 1024) {
            await reader.cancel();
            throw new Error("Invalid ZIP decompression size.");
          }
          chunks.push(value);
        }
      } finally {
        reader.releaseLock();
      }
      data = new Uint8Array(received);
      let at = 0;
      for (const chunk of chunks) {
        data.set(chunk, at);
        at += chunk.length;
      }
    } else if (method !== 0)
      throw new Error("Unsupported ZIP compression method.");
    if (data.length !== size || crc32(data) !== crc)
      throw new Error("Office file failed its integrity check.");
    result[name] = data;
    offset += 46 + length + extra + comment;
  }
  return result;
}

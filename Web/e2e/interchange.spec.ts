import { test, expect, type Page } from "@playwright/test";
import { readFile } from "node:fs/promises";
import { createFile, type OfficeFile } from "../../Engine/typescript/model";
import { readZip } from "../../Engine/typescript/zip";
async function open(page: Page, file: OfficeFile) {
  await page
    .locator("input[type=file]")
    .first()
    .setInputFiles({
      name: "source.gaius",
      mimeType: "application/json",
      buffer: Buffer.from(JSON.stringify(file)),
    });
}
async function exported(page: Page, label: string, path: string) {
  const event = page.waitForEvent("download");
  await page.getByRole("button", { name: label, exact: true }).click();
  await (await event).saveAs(path);
  return readZip(new Uint8Array(await readFile(path)));
}
async function reopen(page: Page, path: string) {
  await page.getByRole("button", { name: "Back to home", exact: true }).click();
  await page.locator("input[type=file]").first().setInputFiles(path);
}
test.beforeEach(async ({ page }) => page.goto("/"));
test("document chart uses entered data and persists as an embedded picture", async ({
  page,
}) => {
  await page.locator(".create-card.document").click();
  await page.getByRole("tab", { name: "Insert", exact: true }).click();
  await page.getByRole("button", { name: "Chart", exact: true }).click();
  await page.getByLabel("Chart title", { exact: true }).fill("Sales");
  await page
    .getByLabel("Document chart data")
    .fill("North,10\nSouth,20\nWest,15");
  await page
    .getByRole("button", { name: "Insert chart picture", exact: true })
    .click();
  await expect(page.locator('.word-content img[alt="Sales"]')).toHaveCount(1);
  await expect(
    page.getByText("Saved on this device", { exact: true }),
  ).toBeVisible();
  await page.reload();
  await expect(page.locator('.word-content img[alt="Sales"]')).toHaveCount(1);
});
test("DOCX contains real media and table parts, and reopens as editable content", async ({
  page,
}, info) => {
  const file = createFile("document", "Illustrated report"),
    png =
      "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jS1cAAAAASUVORK5CYII=";
  file.content.html = `<h1>Report heading</h1><p><strong>Bold text</strong></p><table><tbody><tr><td>North</td><td>12</td></tr></tbody></table><p><img src="data:image/png;base64,${png}" style="width:80px;max-width:100%"></p>`;
  file.content.text = "Report heading\nBold text\nNorth 12";
  await open(page, file);
  const path = info.outputPath("report.docx"),
    parts = await exported(page, "Export DOCX", path);
  expect(parts["word/media/image1.png"]).toBeTruthy();
  const xml = new TextDecoder().decode(parts["word/document.xml"]);
  expect(xml).toContain("<w:tbl>");
  expect(xml).toContain("<w:drawing>");
  await reopen(page, path);
  await expect(page.locator(".word-content img")).toHaveCount(1);
  await expect(page.locator(".word-content table")).toContainText("North");
  await expect(page.locator(".word-content h1")).toHaveText("Report heading");
});
test("XLSX keeps worksheets, formatting and formulas across export and import", async ({
  page,
}, info) => {
  const file = createFile("spreadsheet", "Workbook");
  file.worksheets = [
    {
      name: "Summary",
      rows: 100,
      columns: 26,
      cells: { A1: "Revenue", B1: "12", B2: "=B1*2" },
      styles: { B1: { bold: true, background: "#e6f1eb", format: "currency" } },
    },
    {
      name: "Details",
      rows: 100,
      columns: 26,
      cells: { A1: "Second worksheet" },
    },
  ];
  file.activeSheet = 0;
  file.sheet = file.worksheets[0];
  await open(page, file);
  const path = info.outputPath("workbook.xlsx"),
    parts = await exported(page, "Export XLSX", path);
  expect(parts["xl/worksheets/sheet2.xml"]).toBeTruthy();
  expect(new TextDecoder().decode(parts["xl/styles.xml"])).toContain(
    'numFmtId="164"',
  );
  await reopen(page, path);
  await expect(page.locator(".worksheet-tabs button")).toHaveCount(3);
  await expect(
    page.locator("table.spreadsheet tbody tr").first().locator("td").nth(1),
  ).toHaveCSS("font-weight", "700");
  await page.getByRole("button", { name: "Details", exact: true }).click();
  await expect(page.getByLabel("A1", { exact: true })).toHaveValue(
    "Second worksheet",
  );
});
test("PPTX retains editable shape and WordArt text objects", async ({
  page,
}, info) => {
  const file = createFile("presentation", "Objects");
  file.slides[0].objects = [
    {
      id: "wordart",
      kind: "wordart",
      text: "Annual results",
      x: 20,
      y: 55,
      width: 45,
      height: 20,
      color: "#185abd",
      fontSize: 36,
    },
    {
      id: "shape",
      kind: "ellipse",
      x: 70,
      y: 60,
      width: 15,
      height: 20,
      color: "#20a58a",
    },
  ];
  file.slides[0].transition = "fade";
  await open(page, file);
  const path = info.outputPath("objects.pptx"),
    parts = await exported(page, "Export PPTX", path);
  expect(new TextDecoder().decode(parts["ppt/slides/slide1.xml"])).toContain(
    'prst="ellipse"',
  );
  await reopen(page, path);
  await expect(page.locator(".object-handle")).toHaveCount(2);
  await expect(page.locator(".slide-object.wordart")).toContainText(
    "Annual results",
  );
});

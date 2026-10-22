import { test, expect, type Page } from "@playwright/test";
import { PDFDocument, StandardFonts } from "pdf-lib";
import { createFile } from "../../Engine/typescript/model";
const png = Buffer.from(
  "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jS1cAAAAASUVORK5CYII=",
  "base64",
);
async function tab(page: Page, name: string) {
  await page.getByRole("tab", { name, exact: true }).click();
}
async function openGaius(page: Page, file: ReturnType<typeof createFile>) {
  await page
    .locator("input[type=file]")
    .first()
    .setInputFiles({
      name: "test.gaius",
      mimeType: "application/json",
      buffer: Buffer.from(JSON.stringify(file)),
    });
}
test.beforeEach(async ({ page }) => {
  await page.goto("/");
});
test("document pictures, WordArt, find/replace, print preview and save as persist", async ({
  page,
}, info) => {
  await page.locator(".create-card.document").click();
  await expect(
    page.getByRole("tab", { name: "Home", exact: true }),
  ).toHaveAttribute("aria-selected", "true");
  await page
    .getByRole("textbox", { name: "Document content" })
    .fill("Quarter one. Quarter two.");
  await tab(page, "Review");
  await page
    .getByRole("button", { name: "Find / replace", exact: true })
    .count();
  await page.keyboard.press("Control+h");
  await page.getByLabel("Find text", { exact: true }).fill("Quarter");
  await page.getByLabel("Replace with").fill("Period");
  await page.getByRole("button", { name: "Replace all", exact: true }).click();
  await expect(
    page.getByRole("textbox", { name: "Document content" }),
  ).toContainText("Period one. Period two.");
  await tab(page, "Insert");
  await page
    .locator('input[type=file][accept="image/png,image/jpeg,image/webp"]')
    .setInputFiles({ name: "pixel.png", mimeType: "image/png", buffer: png });
  await expect(page.locator(".word-content img")).toHaveCount(1);
  page.once("dialog", (d) => d.accept("Gaius WordArt"));
  await page.getByRole("button", { name: "WordArt", exact: true }).click();
  await expect(page.locator(".word-content")).toContainText("Gaius WordArt");
  await page.keyboard.press("Control+s");
  await expect(
    page.getByText("Saved on this device", { exact: true }),
  ).toBeVisible();
  await page.keyboard.press("Control+s");
  await page.reload();
  await expect(page.locator(".word-content img")).toHaveCount(1);
  await expect(page.locator(".word-content")).toContainText("Gaius WordArt");
  await page
    .getByRole("button", { name: "Print preview", exact: true })
    .click();
  await expect(
    page.getByRole("dialog", { name: "Print preview" }).locator("img"),
  ).toHaveCount(1);
  await page.screenshot({ path: info.outputPath("print-preview.png") });
  await page.getByRole("button", { name: "Close dialog" }).click();
  await page.getByRole("button", { name: "File", exact: true }).click();
  await page.getByRole("menuitem", { name: "Save as", exact: true }).click();
  await page.getByLabel("Save as file name").fill("Portable copy");
  const downloaded = page.waitForEvent("download");
  await page.getByRole("button", { name: "Save copy", exact: true }).click();
  expect((await downloaded).suggestedFilename()).toBe("Portable copy.gaius");
  await expect(page.getByLabel("Document name")).toHaveValue("Portable copy");
});
test("sheet formulas, filters, charts and multiple worksheets work with bounded rendering", async ({
  page,
}, info) => {
  const file = createFile("spreadsheet", "Analysis");
  file.sheet.cells = {
    A1: "Category",
    B1: "Sales",
    A2: "Alpha",
    B2: "1",
    A3: "Beta",
    B3: "4",
    A4: "Alpha",
    B4: "9",
  };
  file.sheet.rows = 5000;
  await openGaius(page, file);
  expect(await page.locator(".spreadsheet tbody tr").count()).toBeLessThan(80);
  await page
    .locator(".spreadsheet tbody tr")
    .first()
    .locator("td")
    .nth(2)
    .click();
  const formula = page.getByLabel("Cell value or formula");
  await formula.fill('=IFERROR(1/0,SUMIF(B2:B4,">1",B2:B4))');
  await formula.press("Enter");
  await expect(
    page.locator(".spreadsheet tbody tr").first().locator("td").nth(2),
  ).toHaveText("13");
  await tab(page, "Insert");
  await page.getByLabel("Chart range", { exact: true }).fill("A1:B4");
  await page.getByRole("button", { name: "Insert chart", exact: true }).click();
  await expect(page.locator(".office-chart svg")).toBeVisible();
  await tab(page, "Data");
  await page.getByLabel("Filter value").fill("Alpha");
  await page.getByRole("button", { name: "Apply filter", exact: true }).click();
  await expect(page.locator(".spreadsheet tbody tr")).toHaveCount(3);
  await expect(page.locator("table.spreadsheet")).not.toContainText("Beta");
  await tab(page, "Insert");
  await page
    .getByRole("button", { name: "New worksheet", exact: true })
    .click();
  await expect(page.locator(".worksheet-tabs button")).toHaveCount(3);
  await page.locator(".worksheet-tabs button").first().click();
  await expect(page.locator(".office-chart")).toHaveCount(1);
  await page.screenshot({ path: info.outputPath("analysis.png") });
  await page.keyboard.press("Control+s");
  await page.reload();
  await expect(page.locator(".office-chart")).toHaveCount(1);
});
test("slide objects, transitions, animations and notes survive reload and presentation", async ({
  page,
}, info) => {
  await page.locator(".create-card.presentation").click();
  await tab(page, "Insert");
  await page.getByRole("button", { name: "WordArt", exact: true }).click();
  await page.getByLabel("Object text").fill("Annual results");
  await page.getByLabel("Object x", { exact: true }).fill("20");
  await tab(page, "Animations");
  await page.getByRole("button", { name: "Fly", exact: true }).click();
  await tab(page, "Home");
  await page
    .getByRole("button", { name: "Duplicate slide", exact: true })
    .click();
  await tab(page, "Transitions");
  await page.getByRole("button", { name: "Morph", exact: true }).click();
  await tab(page, "Home");
  await page
    .getByRole("button", { name: "Speaker notes", exact: true })
    .click();
  await page
    .getByRole("textbox", { name: "Speaker notes", exact: true })
    .fill("Explain the growth.");
  await page.getByRole("button", { name: "Present", exact: true }).click();
  await expect(
    page.locator(".presentation-overlay .slide-object.wordart"),
  ).toContainText("Annual results");
  await page.screenshot({ path: info.outputPath("slide-objects.png") });
  await page.keyboard.press("Escape");
  await page.keyboard.press("Control+s");
  await page.reload();
  await expect(page.locator(".object-handle")).toHaveCount(1);
  await expect(page.locator(".slide-thumbnail")).toHaveCount(2);
});
test("PDF selectable text, replacement, extraction and target compression produce real PDFs", async ({
  page,
}, info) => {
  const pdf = await PDFDocument.create(),
    font = await pdf.embedFont(StandardFonts.Helvetica);
  pdf
    .addPage([400, 300])
    .drawText("Original line", { x: 40, y: 220, size: 20, font });
  const file = createFile("pdf", "Text proof");
  file.pdf.dataUrl = `data:application/pdf;base64,${Buffer.from(await pdf.save()).toString("base64")}`;
  await openGaius(page, file);
  await expect(page.locator(".pdf-text-layer")).toContainText("Original line");
  await tab(page, "Edit");
  await page
    .getByRole("button", { name: "Replace text (flatten)", exact: true })
    .click();
  await page.locator(".pdf-text-layer span").first().click();
  await page.getByLabel("Replacement PDF text").fill("Updated line");
  await page
    .getByRole("button", { name: "Replace and flatten this page" })
    .click();
  await expect(page.locator(".pdf-text-layer")).toContainText("Updated line");
  await expect(page.locator(".pdf-text-layer")).not.toContainText(
    "Original line",
  );
  await tab(page, "Compress");
  await page.getByLabel("Target PDF size KB").fill("30");
  await page
    .getByRole("button", { name: "Compress to target", exact: true })
    .click();
  await expect(
    page.getByRole("button", { name: "Download result", exact: true }),
  ).toBeVisible();
  const event = page.waitForEvent("download");
  await page
    .getByRole("button", { name: "Download result", exact: true })
    .click();
  const path = info.outputPath("compressed.pdf");
  await (await event).saveAs(path);
  const { readFile } = await import("node:fs/promises");
  const result = await PDFDocument.load(await readFile(path));
  expect(result.getPageCount()).toBe(1);
});
test("image resize, watermark and target compression can be downloaded on a phone layout", async ({
  page,
}, info) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.locator(".create-card.image").click();
  await page
    .locator(
      'input[type=file][accept="image/png,image/jpeg,image/webp,image/gif,image/bmp"]',
    )
    .setInputFiles({ name: "pixel.png", mimeType: "image/png", buffer: png });
  await expect(page.locator(".image-stage canvas")).toBeVisible();
  await tab(page, "Transform");
  await page.getByLabel("Image width", { exact: true }).fill("400");
  await page.getByRole("button", { name: "Apply resize", exact: true }).click();
  await expect(page.locator(".image-stage canvas")).toHaveAttribute(
    "width",
    "400",
  );
  await tab(page, "Annotate");
  await page.getByLabel("Watermark text").fill("Gaius");
  await tab(page, "Compress");
  await page.getByLabel("Target image size KB").fill("20");
  await page
    .getByRole("button", { name: "Compress to target", exact: true })
    .click();
  await expect(
    page.getByRole("button", { name: "Download result" }),
  ).toBeVisible();
  const download = page.waitForEvent("download");
  await page.getByRole("button", { name: "Download result" }).click();
  expect((await download).suggestedFilename()).toMatch(/\.jpg$/);
  await page.screenshot({ path: info.outputPath("phone-image.png") });
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth > innerWidth,
    ),
  ).toBe(false);
});

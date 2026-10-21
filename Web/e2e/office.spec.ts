import { test, expect } from "@playwright/test";
import { readFile } from "node:fs/promises";
import { PDFDocument } from "pdf-lib";
test.beforeEach(async ({ page }) => {
  await page.goto("/");
  await expect(page.getByText("A clear desk, ready for work")).toBeVisible();
});
test("writes, saves, reloads and round trips DOCX locally", async ({
  page,
}, info) => {
  const external: string[] = [];
  page.on("request", (r) => {
    if (
      !r.url().startsWith("http://127.0.0.1:5173") &&
      !r.url().startsWith("data:") &&
      !r.url().startsWith("blob:")
    )
      external.push(r.url());
  });
  await page.locator(".create-card.document").click();
  await page.getByLabel("Document name").fill("Project brief");
  await page
    .getByRole("textbox", { name: "Document content" })
    .fill("A useful office suite.");
  await expect(page.getByText("Saved on this device")).toBeVisible();
  await page.reload();
  await expect(
    page.getByRole("textbox", { name: "Document content" }),
  ).toContainText("A useful office suite.");
  const download = page.waitForEvent("download");
  await page.getByRole("button", { name: "Export DOCX" }).click();
  const file = await download;
  const path = info.outputPath("brief.docx");
  await file.saveAs(path);
  await page.getByRole("button", { name: "Back to home" }).click();
  await page
    .locator("input[type=file]")
    .first()
    .setInputFiles({
      name: "brief.docx",
      mimeType:
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
      buffer: await readFile(path),
    });
  await expect(
    page.getByRole("textbox", { name: "Document content" }),
  ).toContainText("A useful office suite.");
  expect(external).toEqual([]);
  await page.screenshot({ path: info.outputPath("write.png"), fullPage: true });
});
test("calculates cells, exports XLSX and reopens it", async ({
  page,
}, info) => {
  await page.locator(".create-card.spreadsheet").click();
  const bar = page.getByLabel("Cell value or formula");
  await bar.fill("12");
  await bar.press("Enter");
  await bar.fill("8");
  await bar.press("Enter");
  await page
    .locator(".spreadsheet tbody tr")
    .nth(0)
    .locator("td")
    .nth(1)
    .click();
  await bar.fill("=SUM(A1:A2)*2");
  await bar.press("Enter");
  await expect(
    page.locator(".spreadsheet tbody tr").nth(0).locator("td").nth(1),
  ).toHaveText("40");
  const d = page.waitForEvent("download");
  await page.getByRole("button", { name: "Export XLSX" }).click();
  const path = info.outputPath("sheet.xlsx");
  await (await d).saveAs(path);
  await page.getByRole("button", { name: "Back to home" }).click();
  await page.locator("input[type=file]").first().setInputFiles(path);
  await expect(
    page.locator(".spreadsheet tbody tr").nth(0).locator("td").nth(1),
  ).toHaveText("40");
});
test("edits and presents slides with PPTX round trip", async ({
  page,
}, info) => {
  await page.locator(".create-card.presentation").click();
  await page.getByLabel("Slide title").fill("Quarterly review");
  await page.getByLabel("Slide body").fill("Revenue increased.\nNext steps.");
  await page.getByRole("button", { name: "New slide", exact: true }).click();
  await page.getByLabel("Slide title").fill("Thank you");
  await page.getByRole("button", { name: "Present", exact: true }).click();
  await expect(
    page.getByRole("dialog", { name: "Presentation" }),
  ).toBeVisible();
  await page.keyboard.press("Escape");
  const d = page.waitForEvent("download");
  await page.getByRole("button", { name: "Export PPTX" }).click();
  const path = info.outputPath("deck.pptx");
  await (await d).saveAs(path);
  await page.getByRole("button", { name: "Back to home" }).click();
  await page.locator("input[type=file]").first().setInputFiles(path);
  await expect(page.locator(".slide-thumbnail")).toHaveCount(2);
  await expect(page.getByLabel("Slide title")).toHaveValue("Quarterly review");
});
test("extracts PDF pages and exports actual PDF bytes", async ({
  page,
}, info) => {
  const pdf = await PDFDocument.create();
  pdf.addPage();
  pdf.addPage();
  pdf.addPage();
  await page
    .locator("input[type=file]")
    .first()
    .setInputFiles({
      name: "three.pdf",
      mimeType: "application/pdf",
      buffer: Buffer.from(await pdf.save()),
    });
  await expect(page.getByText("3 pages in this file")).toBeVisible();
  await page.getByLabel("Selected PDF pages").fill("2-3");
  await page.getByRole("button", { name: "Keep selected pages" }).click();
  await expect(page.getByText("2 pages in this file")).toBeVisible();
  const d = page.waitForEvent("download");
  await page.getByRole("button", { name: "Export PDF", exact: true }).click();
  const path = info.outputPath("extracted.pdf");
  await (await d).saveAs(path);
  expect((await PDFDocument.load(await readFile(path))).getPageCount()).toBe(2);
});
test("home and narrow layout remain usable", async ({ page }, info) => {
  await page.screenshot({
    path: info.outputPath("home-desktop.png"),
    fullPage: true,
  });
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.locator(".create-card.document")).toBeVisible();
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
  await page.screenshot({
    path: info.outputPath("home-mobile.png"),
    fullPage: true,
  });
});

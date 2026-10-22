import { test, expect } from "@playwright/test";

test("compact home, File dropdown and sliding ribbon work with mouse and keyboard", async ({
  page,
}, info) => {
  await page.goto("/");
  await expect(page.getByText("No files yet", { exact: true })).toBeVisible();
  await expect(
    page.locator(".file-list-head, .welcome, .local-note, .eyebrow"),
  ).toHaveCount(0);
  await page.screenshot({ path: info.outputPath("home.png") });
  await page.locator(".create-card.document").click();
  const home = page.getByRole("tab", { name: "Home", exact: true });
  const insert = page.getByRole("tab", { name: "Insert", exact: true });
  const pill = page.locator(".ribbon-pill");
  await expect(home).toHaveAttribute("aria-selected", "true");
  const first = await pill.boundingBox();
  await insert.click();
  await expect(insert).toHaveAttribute("aria-selected", "true");
  await expect
    .poll(async () => Math.round((await pill.boundingBox())!.x))
    .toBe(Math.round((await insert.boundingBox())!.x));
  expect((await pill.boundingBox())!.x).toBeGreaterThan(first!.x);
  await insert.press("ArrowRight");
  await expect(
    page.getByRole("tab", { name: "Draw", exact: true }),
  ).toBeFocused();
  await page.getByRole("button", { name: "File", exact: true }).click();
  await expect(
    page.getByRole("menu", { name: "File", exact: true }),
  ).toBeVisible();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await page.screenshot({ path: info.outputPath("file-dropdown.png") });
  await page.keyboard.press("Escape");
  await expect(page.getByRole("menu")).toHaveCount(0);
  await expect(
    page.getByRole("button", { name: "File", exact: true }),
  ).toBeFocused();
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole("button", { name: "File", exact: true }).click();
  await page
    .getByRole("menuitem", { name: "Print preview", exact: true })
    .click();
  await expect(
    page.getByRole("dialog", { name: "Print preview", exact: true }),
  ).toBeVisible();
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth,
    ),
  ).toBeTruthy();
});

test("handwriting inserts visible pixels and survives reopening the document", async ({ page }) => {
  await page.goto("/");
  await page.locator(".create-card.document").click();
  await page.getByRole("tab", {name: "Draw", exact: true}).click();
  await page.getByRole("button", {name: "Pen", exact: true}).click();
  const canvas = page.getByLabel("Handwriting canvas");
  const bounds = (await canvas.boundingBox())!;
  await page.mouse.move(bounds.x + bounds.width * .3, bounds.y + bounds.height * .4);
  await page.mouse.down();
  await page.mouse.move(bounds.x + bounds.width * .7, bounds.y + bounds.height * .6, {steps: 20});
  await page.mouse.up();
  expect(await canvas.evaluate(node => { const canvas = node as HTMLCanvasElement; const pixel = canvas.getContext("2d")!.getImageData(480,270,1,1).data; return pixel[2] > pixel[0]; })).toBeTruthy();
  await page.getByRole("button", {name: "Insert drawing", exact: true}).click();
  await expect(page.locator(".word-content img[alt=Handwriting]")).toHaveCount(1);
  await page.keyboard.press("Control+s");
  await expect(page.getByText("Saved on this device", {exact:true})).toBeVisible();
  await page.keyboard.press("Control+s");
  await page.reload();
  await expect(page.locator(".word-content img[alt=Handwriting]")).toHaveCount(1);
});

import { test, expect, type Page } from "@playwright/test";
import { readFile } from "node:fs/promises";
import { readZip } from "../../Engine/typescript/zip";

const tab = (page: Page, name: string) =>
  page.getByRole("tab", { name, exact: true }).click();
async function save(page: Page) {
  await page.keyboard.press("Control+s");
  await expect(
    page.getByText("Saved on this device", { exact: true }),
  ).toBeVisible();
}
test.beforeEach(async ({ page }) => {
  await page.goto("/");
});
test("ribbon Paste inserts clipboard image data in both Write and Slides", async ({
  page,
}) => {
  await page.evaluate(async () => {
    const canvas = document.createElement("canvas");
    canvas.width = 80;
    canvas.height = 320;
    canvas.getContext("2d")!.fillRect(0, 0, 80, 320);
    const blob = await new Promise<Blob>((resolve) =>
      canvas.toBlob((value) => resolve(value!), "image/png"),
    );
    Object.defineProperty(navigator.clipboard, "read", {
      configurable: true,
      value: async () => [{ types: ["image/png"], getType: async () => blob }],
    });
  });
  await page.locator(".create-card.document").click();
  await page.getByRole("button", { name: "Paste", exact: true }).click();
  await expect(page.locator(".word-content img")).toHaveCount(1);
  await save(page);
  await page.getByRole("button", { name: "Back to home", exact: true }).click();
  await page.locator(".create-card.presentation").click();
  await tab(page, "Insert");
  await page
    .getByRole("button", { name: "Paste picture", exact: true })
    .click();
  await expect(page.locator(".object-handle img")).toHaveCount(1);
  const frame = await page.locator(".object-handle").boundingBox();
  expect(frame!.width / frame!.height).toBeCloseTo(0.25, 1);
  const stage = await page.locator(".object-slide").boundingBox();
  expect(frame!.y + frame!.height).toBeLessThanOrEqual(
    stage!.y + stage!.height + 1,
  );
  expect(
    await page
      .locator(".object-handle img")
      .evaluate(
        (el) =>
          (el as HTMLImageElement).naturalHeight /
          (el as HTMLImageElement).naturalWidth,
      ),
  ).toBe(4);
});

test("a new file offers cancel, discard and save without silently creating a saved file", async ({
  page,
}) => {
  await page.locator(".create-card.document").click();
  await page.getByLabel("Document name").fill("Discard this draft");
  await page
    .getByRole("textbox", { name: "Document content" })
    .fill("Unsaved content");
  await expect(
    page.getByText("Unsaved new file", { exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Back to home" }).click();
  const prompt = page.getByRole("dialog", { name: "Save new file?" });
  await prompt.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(page.getByLabel("Document name")).toHaveValue(
    "Discard this draft",
  );
  await page.getByRole("button", { name: "Back to home" }).click();
  await prompt.getByRole("button", { name: "Discard", exact: true }).click();
  await expect(page.getByText("No files yet", { exact: true })).toBeVisible();
  await page.locator(".create-card.document").click();
  await page.getByLabel("Document name").fill("Keep this draft");
  await page
    .getByRole("textbox", { name: "Document content" })
    .fill("Preserved content");
  await page.getByRole("button", { name: "Back to home" }).click();
  await prompt.getByRole("button", { name: "Save", exact: true }).click();
  await page.getByText("Keep this draft", { exact: true }).click();
  await expect(page.locator(".word-content")).toContainText(
    "Preserved content",
  );
});

test("pasted tall pictures keep their proportions, resize, wrap and survive reopening", async ({
  page,
}) => {
  await page.locator(".create-card.document").click();
  await page.locator(".word-content").evaluate(async (editor) => {
    const canvas = document.createElement("canvas");
    canvas.width = 100;
    canvas.height = 400;
    canvas.getContext("2d")!.fillRect(0, 0, 100, 400);
    const blob = await new Promise<Blob>((resolve) =>
      canvas.toBlob((b) => resolve(b!), "image/png"),
    );
    const clipboard = new DataTransfer();
    clipboard.items.add(new File([blob], "tall.png", { type: "image/png" }));
    editor.dispatchEvent(
      new ClipboardEvent("paste", {
        clipboardData: clipboard,
        bubbles: true,
        cancelable: true,
      }),
    );
  });
  const picture = page.locator(".word-content img");
  await expect(picture).toHaveCount(1);
  expect(
    await picture.evaluate((el) => (el as HTMLImageElement).style.height),
  ).toBe("400px");
  await picture.click();
  await page.getByLabel("Picture width", { exact: true }).fill("80");
  await page
    .getByLabel("Wrap text", { exact: true })
    .selectOption("square-right");
  await expect(picture).toHaveCSS("float", "right");
  expect(
    await picture.evaluate((el) =>
      Math.round(
        el.getBoundingClientRect().height / el.getBoundingClientRect().width,
      ),
    ),
  ).toBe(4);
  await save(page);
  await page.reload();
  await expect(picture).toHaveAttribute("data-wrap", "square-right");
  await expect(picture).toHaveCSS("object-fit", "contain");
});

test("editable shapes, view controls, page settings and anchored DOCX export work together", async ({
  page,
}, info) => {
  await page.locator(".create-card.document").click();
  await tab(page, "Insert");
  await page.getByLabel("Insert shape", { exact: true }).selectOption("star");
  const shape = page.locator('.word-content img[data-shape="star"]');
  await shape.click();
  await page.getByLabel("Shape fill", { exact: true }).fill("#107c41");
  await page.getByLabel("Shape outline width", { exact: true }).fill("2");
  await page.getByLabel("Picture width", { exact: true }).fill("300");
  await page.getByLabel("Close object formatting").click();
  await tab(page, "Layout");
  await page.getByLabel("Columns", { exact: true }).selectOption("2");
  await page.getByLabel("Paper size", { exact: true }).selectOption("letter");
  page.once("dialog", (dialog) => dialog.accept("10, 15, 20, 25"));
  await page
    .getByRole("button", { name: "Custom margins", exact: true })
    .click();
  await tab(page, "View");
  await page.getByRole("button", { name: "Rulers", exact: true }).click();
  await expect(page.getByLabel("Horizontal ruler in inches")).toHaveCount(0);
  await page.getByRole("button", { name: "Thumbnails", exact: true }).click();
  await expect(page.getByLabel("Document navigation")).toBeVisible();
  await save(page);
  await page.reload();
  await expect(shape).toHaveAttribute("data-fill", "#107c41");
  await expect(shape).toHaveCSS("width", "300px");
  const downloaded = page.waitForEvent("download");
  await page.getByRole("button", { name: "Export DOCX", exact: true }).click();
  const path = info.outputPath("shapes.docx");
  await (await downloaded).saveAs(path);
  const parts = await readZip(new Uint8Array(await readFile(path)));
  const xml = new TextDecoder().decode(parts["word/document.xml"]);
  expect(xml).toContain("<wp:anchor");
  expect(xml).toContain("<wp:wrapSquare");
  expect(xml).toContain('w:num="2"');
  expect(xml).toContain('w:w="12240"');
  expect(xml).toContain('w:top="567"');
});

test("dark chrome can retain a white document page and remembers the preference", async ({
  page,
}, info) => {
  await page.getByRole("button", { name: "Appearance", exact: true }).click();
  await page.getByLabel("Theme", { exact: true }).selectOption("dark");
  await page.getByRole("button", { name: "Close dialog" }).click();
  await page.locator(".create-card.document").click();
  await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
  await page
    .locator(".word-content")
    .fill(
      "Quarterly planning\nA clear view of the work ahead.\nReview the schedule, share the draft and keep decisions together.",
    );
  await expect(page.locator(".paper")).toHaveCSS(
    "background-color",
    "rgb(255, 255, 255)",
  );
  await page.getByRole("button", { name: "File", exact: true }).click();
  await page.getByRole("menuitem", { name: "Appearance", exact: true }).click();
  await page.getByLabel("Keep document page white in dark mode").uncheck();
  await page.getByRole("button", { name: "Close dialog" }).click();
  await expect(page.locator(".paper")).toHaveCSS(
    "background-color",
    "rgb(34, 43, 57)",
  );
  await save(page);
  await page.reload();
  await expect(page.locator("html")).toHaveAttribute(
    "data-white-page",
    "false",
  );
  await expect(page.locator(".word-content")).toContainText(
    "Quarterly planning",
  );
  await page.screenshot({ path: info.outputPath("dark-write.png") });
  await page
    .getByRole("button", { name: "Print preview", exact: true })
    .click();
  await expect(page.locator(".print-document")).toHaveCSS(
    "background-color",
    "rgb(255, 255, 255)",
  );
});

test("slide shapes retain geometry, fill, outline and rotation in PPTX", async ({
  page,
}, info) => {
  await page.locator(".create-card.presentation").click();
  await tab(page, "Insert");
  await page
    .getByLabel("Insert shape", { exact: true })
    .selectOption("hexagon");
  await page.getByLabel("Object outline", { exact: true }).fill("#c43e1c");
  await page.getByLabel("Object outline width", { exact: true }).fill("3");
  await page.getByLabel("Object rotation", { exact: true }).fill("30");
  const downloaded = page.waitForEvent("download");
  await page.getByRole("button", { name: "Export PPTX", exact: true }).click();
  const path = info.outputPath("shapes.pptx");
  await (await downloaded).saveAs(path);
  const xml = new TextDecoder().decode(
    (await readZip(new Uint8Array(await readFile(path))))[
      "ppt/slides/slide1.xml"
    ],
  );
  expect(xml).toContain('prst="hexagon"');
  expect(xml).toContain('rot="1800000"');
  expect(xml).toContain('w="28575"');
  await save(page);
  await page.getByRole("button", { name: "Back to home" }).click();
  await page.locator("input[type=file]").first().setInputFiles(path);
  await expect(page.locator(".object-handle")).toHaveCount(1);
  await page.locator(".object-handle").click();
  await expect(page.getByLabel("Object rotation", { exact: true })).toHaveValue(
    "30",
  );
});

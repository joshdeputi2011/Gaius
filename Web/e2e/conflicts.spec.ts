import { test, expect } from "@playwright/test";
test("a second window cannot silently overwrite newer work", async ({
  page,
  context,
}) => {
  await page.goto("/");
  await page.locator(".create-card.document").click();
  await page
    .getByRole("textbox", { name: "Document content" })
    .fill("First draft");
  await page.keyboard.press("Control+s");
  await expect(page.getByText("Saved on this device")).toBeVisible();
  const second = await context.newPage();
  await second.goto(page.url());
  await expect(
    second.getByRole("textbox", { name: "Document content" }),
  ).toContainText("First draft");
  await page
    .getByRole("textbox", { name: "Document content" })
    .fill("Newer draft");
  await page.keyboard.press("Control+s");
  await expect(page.getByText("Saved on this device")).toBeVisible();
  await second
    .getByRole("textbox", { name: "Document content" })
    .fill("Conflicting draft");
  await expect(second.getByRole("alert")).toContainText(
    "changed in another window",
  );
  await page.keyboard.press("Control+s");
  await page.reload();
  await expect(
    page.getByRole("textbox", { name: "Document content" }),
  ).toContainText("Newer draft");
});

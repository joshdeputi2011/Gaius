import { useEffect, useState } from "react";

export type Appearance = {
  theme: "system" | "light" | "dark";
  whitePage: boolean;
};
const defaults: Appearance = { theme: "system", whitePage: true };
function read(): Appearance {
  try {
    return {
      ...defaults,
      ...JSON.parse(localStorage.getItem("gaius.appearance") || "{}"),
    };
  } catch {
    return defaults;
  }
}
export function useAppearance() {
  const [appearance, setAppearance] = useState(read);
  useEffect(() => {
    const media = matchMedia("(prefers-color-scheme: dark)");
    const apply = () => {
      document.documentElement.dataset.theme =
        appearance.theme === "system"
          ? media.matches
            ? "dark"
            : "light"
          : appearance.theme;
      document.documentElement.dataset.whitePage = String(appearance.whitePage);
    };
    apply();
    media.addEventListener("change", apply);
    try {
      localStorage.setItem("gaius.appearance", JSON.stringify(appearance));
    } catch {
      /* Private storage can be unavailable. */
    }
    return () => media.removeEventListener("change", apply);
  }, [appearance]);
  return { appearance, setAppearance };
}

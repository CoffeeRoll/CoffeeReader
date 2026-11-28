import { readStored, writeStored } from "../hooks/useLocalStorage";

export type AppTheme = "system" | "light" | "dark";
const key = "cr.appTheme";

export function getAppTheme(): AppTheme {
  const stored = readStored<{ theme: AppTheme }>(key, { theme: "system" });
  return stored.theme;
}

export function applyAppTheme(theme: AppTheme = getAppTheme()) {
  if (theme === "system") {
    delete document.documentElement.dataset.theme;
  } else {
    document.documentElement.dataset.theme = theme;
  }
}

export function setAppTheme(theme: AppTheme) {
  writeStored(key, { theme });
  applyAppTheme(theme);
}

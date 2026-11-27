import { useLocalStorage } from "../../hooks/useLocalStorage";

export type ReaderTheme = "paper" | "sepia" | "dusk" | "night";
export type ReaderFont = "publisher" | "literata" | "atkinson" | "georgia" | "sans";
export type TextAlign = "publisher" | "left" | "justify";
export type PageLayout = "auto" | "single";

export interface ReaderSettings {
  theme: ReaderTheme;
  font: ReaderFont;
  fontSize: number;
  lineHeight: number;
  margin: 0 | 1 | 2 | 3;
  align: TextAlign;
  layout: PageLayout;
  tocPinned: boolean;
  animate: boolean;
}

export const themes: Record<ReaderTheme, { label: string; page: string; desk: string; text: string; muted: string; link: string; highlight: string; dark: boolean }> = {
  paper: { label: "Paper", page: "#fffdf8", desk: "#ece6dc", text: "#1f1a16", muted: "#8b8079", link: "#8c4f24", highlight: "rgba(255, 196, 0, 0.45)", dark: false },
  sepia: { label: "Sepia", page: "#f4ead7", desk: "#e3d6bd", text: "#3b2d1e", muted: "#8f7c64", link: "#8a4a1c", highlight: "rgba(230, 160, 0, 0.42)", dark: false },
  dusk: { label: "Dusk", page: "#2b2724", desk: "#1d1a18", text: "#e6dccf", muted: "#9c9184", link: "#e3a873", highlight: "rgba(227, 168, 115, 0.38)", dark: true },
  night: { label: "Night", page: "#000000", desk: "#000000", text: "#c9c2b8", muted: "#7c766e", link: "#d89b66", highlight: "rgba(216, 155, 102, 0.4)", dark: true },
};

export const fonts: Record<ReaderFont, { label: string; stack: string | null; sample: string }> = {
  publisher: { label: "Book's font", stack: null, sample: "inherit" },
  literata: { label: "Literata", stack: "'CR Literata', Georgia, serif", sample: "'Literata Variable', Georgia, serif" },
  atkinson: { label: "Atkinson", stack: "'CR Atkinson', system-ui, sans-serif", sample: "'Atkinson Hyperlegible', system-ui, sans-serif" },
  georgia: { label: "Georgia", stack: "Georgia, 'Iowan Old Style', 'Times New Roman', serif", sample: "Georgia, serif" },
  sans: { label: "System sans", stack: "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif", sample: "system-ui, sans-serif" },
};

export const marginLabels = ["Narrow", "Normal", "Wide", "Extra wide"] as const;

const isPhone = typeof window !== "undefined" && window.matchMedia("(max-width: 599px)").matches;

export const defaultSettings: ReaderSettings = {
  theme: "paper",
  font: "literata",
  fontSize: isPhone ? 18 : 20,
  lineHeight: 1.6,
  margin: 1,
  align: "justify",
  layout: "auto",
  tocPinned: true,
  animate: true,
};

export function useReaderSettings() {
  // Per device on purpose: a phone and a desktop want different sizes.
  return useLocalStorage<ReaderSettings>("cr.readerSettings", defaultSettings);
}

/** Horizontal page margin in pixels for the given page width. */
export function marginPx(level: ReaderSettings["margin"], width: number): number {
  const fractions = [0.035, 0.06, 0.09, 0.12];
  return Math.round(Math.max(14, Math.min(96, width * fractions[level])));
}

/** The longest comfortable single page: about 66 characters per line. */
export function maxPageWidth(settings: ReaderSettings): number {
  return Math.round(settings.fontSize * 34);
}

/** Typography and theme rules injected into each chapter. */
export function typographyCss(settings: ReaderSettings): string {
  const theme = themes[settings.theme];
  const font = fonts[settings.font].stack;
  const rules: string[] = [
    `html { color-scheme: ${theme.dark ? "dark" : "light"}; }`,
    `body {
      color: ${theme.text} !important;
      font-size: ${settings.fontSize}px !important;
      line-height: ${settings.lineHeight} !important;
      -webkit-hyphens: auto; hyphens: auto;
      overflow-wrap: break-word;
      text-rendering: optimizeLegibility;
      font-kerning: normal;
      -webkit-font-smoothing: antialiased;
    }`,
    `p, li, blockquote, dd, div { line-height: ${settings.lineHeight} !important; }`,
    `p { orphans: 2; widows: 2; }`,
    `h1, h2, h3, h4, h5, h6 { break-after: avoid; break-inside: avoid; line-height: 1.25 !important; -webkit-hyphens: manual; hyphens: manual; }`,
    `a { color: ${theme.link} !important; text-decoration-thickness: 1px; text-underline-offset: 2px; }`,
    `::selection { background: ${theme.highlight}; }`,
    `::highlight(cr-search) { background-color: ${theme.highlight}; color: inherit; }`,
    `mark.cr-search { background-color: ${theme.highlight}; color: inherit; border-radius: 2px; }`,
    `pre, code, kbd, samp { white-space: pre-wrap; font-size: 0.9em; }`,
    `table { max-width: 100%; border-collapse: collapse; }`,
  ];

  if (font) {
    rules.push(`body, body p, body div, body span, body li, body blockquote, body td, body th, body dd, body dt, body a, body em, body i, body strong, body b, body small, body h1, body h2, body h3, body h4, body h5, body h6 { font-family: ${font} !important; }`);
    rules.push(`body { font-family: ${font} !important; }`);
  }

  if (settings.align !== "publisher") {
    rules.push(`body p, body li, body blockquote, body div { text-align: ${settings.align} !important; }`);
    rules.push(`body h1, body h2, body h3, body h4, body h5, body h6, body [style*="center"], body .center, body .centered { text-align: center !important; }`);
  }

  if (theme.dark || settings.theme === "sepia") {
    // Book CSS often hard-codes black text on white; let the theme win.
    rules.push(`body *:not(a):not(a *) { color: inherit !important; background-color: transparent !important; border-color: ${theme.muted} !important; }`);
  }

  if (theme.dark) {
    rules.push(`img { filter: brightness(0.88); }`);
  }

  return rules.join("\n");
}

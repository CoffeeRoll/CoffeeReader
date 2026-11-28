import type { AuthorRef, BookFormat, BookSummary } from "../api/types";

export const formatLabels: Record<BookFormat, string> = { epub: "EPUB", pdf: "PDF", mobi: "MOBI" };

// Names can contain "&" ("Black & Decker"), so different people are joined with "and", never "&".
const authorList = new Intl.ListFormat("en", { style: "long", type: "conjunction" });

const namesOf = (authors: AuthorRef[] | string[]) => authors.map((a) => (typeof a === "string" ? a : a.name));

/**
 * How authors are written everywhere: "A", "A and B", "A, B, and C". Lists longer than `max`
 * shorten to "A and 2 others".
 */
export function authorsText(authors: AuthorRef[] | string[], { fallback = "Unknown author", max = 2 }: { fallback?: string; max?: number } = {}): string {
  const names = namesOf(authors);
  if (names.length === 0) return fallback;
  if (names.length > max) return `${names[0]} and ${names.length - 1} ${names.length === 2 ? "other" : "others"}`;
  return authorList.format(names);
}

/** The same wording as `authorsText` (with every name), split so each name can be rendered as a link. */
export function authorsParts(names: string[]): { name: boolean; value: string }[] {
  return authorList.formatToParts(names).map((part) => ({ name: part.type === "element", value: part.value }));
}

/** The authors field in the edit form: only ";" separates people, since a name can contain "&" or ",". */
export const authorsFieldSeparator = "; ";

export function splitAuthorsField(value: string): string[] {
  return value.split(";").map((name) => name.replace(/\s+/g, " ").trim()).filter(Boolean);
}

export function percent(fraction: number): string {
  const value = Math.round(fraction * 100);
  if (value === 0 && fraction > 0) return "<1%";
  return `${Math.min(100, value)}%`;
}

export function fileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

const relative = new Intl.RelativeTimeFormat(undefined, { numeric: "auto" });

export function timeAgo(iso: string | null | undefined): string {
  if (!iso) return "never";
  const seconds = (new Date(iso).getTime() - Date.now()) / 1000;
  const steps: [Intl.RelativeTimeFormatUnit, number][] = [
    ["year", 31536000],
    ["month", 2592000],
    ["week", 604800],
    ["day", 86400],
    ["hour", 3600],
    ["minute", 60],
  ];
  for (const [unit, size] of steps) {
    if (Math.abs(seconds) >= size) return relative.format(Math.round(seconds / size), unit);
  }

  return "just now";
}

export function seriesLabel(book: Pick<BookSummary, "series" | "seriesIndex">): string | null {
  if (!book.series) return null;
  return book.seriesIndex === null ? book.series.name : `${book.series.name} #${formatIndex(book.seriesIndex)}`;
}

export function formatIndex(index: number): string {
  return Number.isInteger(index) ? String(index) : index.toFixed(1).replace(/\.0$/, "");
}

export type ReadingState = "unread" | "reading" | "finished";

export function readingState(book: BookSummary): ReadingState {
  if (!book.progress) return "unread";
  if (book.progress.isFinished) return "finished";
  return "reading";
}

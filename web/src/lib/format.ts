import type { AuthorRef, BookFormat, BookSummary } from "../api/types";

export const formatLabels: Record<BookFormat, string> = { epub: "EPUB", pdf: "PDF", mobi: "MOBI" };

export function authorsText(authors: AuthorRef[] | string[], fallback = "Unknown author"): string {
  const names = authors.map((a) => (typeof a === "string" ? a : a.name));
  if (names.length === 0) return fallback;
  if (names.length <= 2) return names.join(" & ");
  return `${names[0]} & ${names.length - 1} others`;
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

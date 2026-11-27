import type { ReaderManifest, TocEntry } from "../../api/types";

/** Whole-book progress for a position, weighted by chapter size (matches the server). */
export function overallFraction(manifest: ReaderManifest, chapterIndex: number, fraction: number): number {
  const chapters = manifest.chapters;
  if (chapters.length === 0) return 0;
  const total = manifest.totalLength;
  if (total <= 0) return (chapterIndex + fraction) / chapters.length;
  let before = 0;
  for (const chapter of chapters) {
    if (chapter.index >= chapterIndex) break;
    before += chapter.length;
  }

  const current = chapters[chapterIndex]?.length ?? 0;
  return Math.min(1, Math.max(0, (before + current * fraction) / total));
}

/** The chapter and position within it for a whole-book fraction. */
export function locate(manifest: ReaderManifest, overall: number): { chapterIndex: number; fraction: number } {
  const chapters = manifest.chapters;
  const total = manifest.totalLength;
  if (total <= 0) {
    const position = overall * chapters.length;
    const chapterIndex = Math.min(chapters.length - 1, Math.floor(position));
    return { chapterIndex, fraction: position - chapterIndex };
  }

  const target = overall * total;
  let before = 0;
  for (const chapter of chapters) {
    if (before + chapter.length >= target || chapter.index === chapters.length - 1) {
      return { chapterIndex: chapter.index, fraction: chapter.length > 0 ? Math.min(1, Math.max(0, (target - before) / chapter.length)) : 0 };
    }

    before += chapter.length;
  }

  return { chapterIndex: 0, fraction: 0 };
}

/** Index of the table-of-contents entry that covers the current page. */
export function currentTocIndex(entries: TocEntry[], chapterIndex: number, page: number, pageOfFragment: (id: string) => number | null): number {
  let best = -1;
  for (let i = 0; i < entries.length; i++) {
    const entry = entries[i];
    if (entry.chapterIndex < chapterIndex) {
      best = i;
    } else if (entry.chapterIndex === chapterIndex) {
      const entryPage = entry.fragment ? (pageOfFragment(entry.fragment) ?? 0) : 0;
      if (entryPage <= page || best === -1 || entries[best].chapterIndex < chapterIndex) {
        if (entryPage <= page) best = i;
        else if (best === -1) best = i;
      }
    }
  }

  return best;
}

import { useQueryClient } from "@tanstack/react-query";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useNavigate } from "react-router";
import { api } from "../../api/client";
import { queryKeys } from "../../api/queries";
import type { Chapter, ReaderManifest, SearchHit, TocEntry } from "../../api/types";
import { useToast } from "../../components/Toaster";
import { useHasFinePointer, useIsDesktop, useMediaQuery } from "../../hooks/useMediaQuery";
import { percent } from "../../lib/format";
import { currentTocIndex, locate, overallFraction } from "./location";
import { Paginator, type LinkTarget, type PageLayout, type PageTarget, type Relocation } from "./Paginator";
import { useElementSize, useProgressSaver, useReaderDocument, useReaderKeys, useWheelTurns, type Panel } from "./readerHooks";
import { ReaderShell } from "./ReaderShell";
import { marginPx, maxPageWidth, themes, typographyCss, useReaderSettings } from "./readerSettings";
import type { SearchState } from "./SearchPanel";
import styles from "./ReaderPage.module.css";

/** The reader for reflowable books (EPUB and MOBI): chapters paginated into real pages. */
export function EpubReader({ manifest }: { manifest: ReaderManifest }) {
  const bookId = manifest.bookId;
  const navigate = useNavigate();
  const client = useQueryClient();
  const toast = useToast();
  const isDesktop = useIsDesktop();
  const isWide = useMediaQuery("(min-width: 1100px)");
  const finePointer = useHasFinePointer();
  const [settings, setSettings] = useReaderSettings();
  const theme = themes[settings.theme];

  const stageRef = useRef<HTMLDivElement>(null);
  const viewportRef = useRef<HTMLDivElement>(null);
  const iframeRef = useRef<HTMLIFrameElement>(null);
  const paginatorRef = useRef<Paginator | null>(null);
  const stage = useElementSize(stageRef);
  const viewport = useElementSize(viewportRef);
  const [chapterIndex, setChapterIndex] = useState<number | null>(null);
  const [where, setWhere] = useState<Relocation | null>(null);
  const [loading, setLoading] = useState(true);
  const [chromeVisible, setChromeVisible] = useState(true);
  const [panel, setPanel] = useState<Panel>(null);
  const [search, setSearch] = useState<SearchState>({ query: "", response: null, active: -1 });
  const [jumps, setJumps] = useState<{ chapterIndex: number; fraction: number }[]>([]);
  const [atBookEnd, setAtBookEnd] = useState(false);

  const chapterRef = useRef<number | null>(null);
  const whereRef = useRef<Relocation | null>(null);
  const navToken = useRef(0);
  const userNavigated = useRef(false);
  const tocPinned = isWide && settings.tocPinned;
  const progress = useProgressSaver(bookId);
  useReaderDocument(manifest.title, theme.page);

  // ---- Geometry: a comfortable page (or spread) that fits the screen --------------------------------

  const geometry = useMemo(() => {
    if (!stage) return null;
    const framed = stage.width >= 700 && stage.height >= 480;
    const arrows = framed && finePointer ? 112 : 0;
    const availableWidth = stage.width - (framed ? 48 : 0) - arrows;
    const availableHeight = stage.height - (framed ? 36 : 0);
    const columns: 1 | 2 = settings.layout === "auto" && availableWidth >= 1000 && availableWidth > availableHeight * 1.2 ? 2 : 1;
    const textWidth = maxPageWidth(settings);
    const margin = marginPx(settings.margin, Math.min(availableWidth / columns, textWidth + 120));
    let width = Math.floor(Math.min(availableWidth, columns * (textWidth + margin * 2)));
    if (columns === 2) width -= width % 2;
    return { framed, columns, margin, width, height: Math.floor(availableHeight) };
  }, [stage, settings, finePointer]);

  const layout = useMemo<PageLayout | null>(() => {
    if (!geometry || !viewport || viewport.width < 50 || viewport.height < 50) return null;
    // Wait for the page frame to take its computed size before laying anything out.
    if (Math.abs(viewport.width - geometry.width) > 1) return null;
    return {
      width: Math.floor(viewport.width),
      height: Math.floor(viewport.height),
      columns: geometry.columns,
      margin: geometry.margin,
      padding: 4,
      css: typographyCss(settings),
      animate: settings.animate,
    };
  }, [geometry, viewport, settings]);
  const layoutRef = useRef(layout);
  layoutRef.current = layout;

  // ---- Navigation -----------------------------------------------------------------------------------

  const fetchChapter = useCallback(
    (index: number) =>
      client.fetchQuery({
        queryKey: queryKeys.chapter(bookId, index),
        queryFn: () => api.get<Chapter>(`/api/books/${bookId}/chapters/${index}`),
        staleTime: Infinity,
      }),
    [bookId, client],
  );

  const openChapter = useCallback(
    async (index: number, target: PageTarget) => {
      const paginator = paginatorRef.current;
      if (!paginator || !layoutRef.current) return;
      const token = ++navToken.current;
      setAtBookEnd(false);
      const spinner = window.setTimeout(() => setLoading(true), 150);
      try {
        const chapter = await fetchChapter(index);
        if (token !== navToken.current) return;
        chapterRef.current = index;
        setChapterIndex(index);
        await paginator.load(chapter, layoutRef.current!, target);
      } catch (error) {
        toast.error(error);
      } finally {
        window.clearTimeout(spinner);
        if (token === navToken.current) setLoading(false);
      }

      for (const neighbour of [index + 1, index - 1]) {
        if (neighbour >= 0 && neighbour < manifest.chapters.length) {
          void client.prefetchQuery({
            queryKey: queryKeys.chapter(bookId, neighbour),
            queryFn: () => api.get<Chapter>(`/api/books/${bookId}/chapters/${neighbour}`),
            staleTime: Infinity,
          });
        }
      }
    },
    [manifest, fetchChapter, client, bookId, toast],
  );

  const neighbourChapter = useCallback(
    (from: number, direction: 1 | -1): number | null => {
      for (let i = from + direction; i >= 0 && i < manifest.chapters.length; i += direction) {
        if (manifest.chapters[i].isLinear) return i;
      }

      return null;
    },
    [manifest],
  );

  const goTo = useCallback(
    (index: number, target: PageTarget) => {
      userNavigated.current = true;
      if (index === chapterRef.current) paginatorRef.current?.goToTarget(target);
      else void openChapter(index, target);
    },
    [openChapter],
  );

  const turn = useCallback(
    (direction: 1 | -1) => {
      const paginator = paginatorRef.current;
      const current = chapterRef.current;
      if (!paginator || current === null) return;
      userNavigated.current = true;
      if (!isDesktop) setChromeVisible(false);
      if (direction === 1 ? paginator.next() : paginator.prev()) return;

      const target = neighbourChapter(current, direction);
      if (target === null) {
        paginator.settle();
        if (direction === 1) setAtBookEnd(true);
        return;
      }

      void openChapter(target, direction === 1 ? { kind: "fraction", value: 0 } : { kind: "end" });
    },
    [isDesktop, neighbourChapter, openChapter],
  );

  const onWheel = useWheelTurns(turn);
  const onKey = useReaderKeys(turn, setPanel, () => paginatorRef.current?.direction === "rtl");

  const onLink = useCallback(
    (link: LinkTarget) => {
      const index = manifest.chapters.findIndex((c) => c.href.toLowerCase() === link.path.toLowerCase());
      if (index < 0) return;
      const current = chapterRef.current;
      if (current !== null) {
        setJumps((stack) => [...stack.slice(-9), { chapterIndex: current, fraction: whereRef.current?.fraction ?? 0 }]);
      }

      goTo(index, link.fragment ? { kind: "fragment", id: link.fragment } : { kind: "fraction", value: 0 });
    },
    [manifest, goTo],
  );

  const onRelocate = useCallback(
    (location: Relocation) => {
      whereRef.current = location;
      setWhere(location);
      const current = chapterRef.current;
      if (!location.settled || current === null) return;
      if (!userNavigated.current && !manifest.progress) return;

      const lastChapter = neighbourChapter(current, 1) === null;
      const atEnd = lastChapter && location.page === location.pageCount - 1;
      progress.queue(current, atEnd ? 1 : location.fraction);
    },
    [manifest, neighbourChapter, progress],
  );

  // The paginator lives as long as the page; its callbacks always see the latest handlers.
  const handlers = useRef({ onRelocate, onKey, onWheel, onLink, turn, panel, finePointer, tocPinned, neighbourChapter });
  handlers.current = { onRelocate, onKey, onWheel, onLink, turn, panel, finePointer, tocPinned, neighbourChapter };

  useEffect(() => {
    const iframe = iframeRef.current;
    if (!iframe) return;
    const paginator = new Paginator(iframe, bookId, {
      onRelocate: (location) => handlers.current.onRelocate(location),
      onKey: (event) => handlers.current.onKey(event),
      onWheel: (event) => handlers.current.onWheel(event),
      onLink: (link) => handlers.current.onLink(link),
      onSwipe: (direction) => handlers.current.turn(direction),
      canLeave: (direction) => chapterRef.current !== null && handlers.current.neighbourChapter(chapterRef.current, direction) !== null,
      onTap: (zone) => {
        const { panel: open, finePointer: mouse, tocPinned: pinned, turn: doTurn } = handlers.current;
        if (open && !(open === "toc" && pinned)) {
          setPanel(null);
          return;
        }

        if (mouse) {
          doTurn(zone === "prev" ? -1 : 1);
        } else if (zone === "middle") {
          setChromeVisible((visible) => !visible);
        } else {
          doTurn(zone === "next" ? 1 : -1);
        }
      },
    });
    paginatorRef.current = paginator;
    return () => {
      paginator.destroy();
      paginatorRef.current = null;
    };
  }, [bookId]);

  // Open the book where the reader left off.
  useEffect(() => {
    if (!layout || chapterRef.current !== null || !paginatorRef.current) return;
    const saved = manifest.progress;
    const first = manifest.chapters.find((c) => c.isLinear)?.index ?? 0;
    if (saved && !saved.isFinished && saved.chapterIndex < manifest.chapters.length) {
      void openChapter(saved.chapterIndex, { kind: "fraction", value: saved.chapterProgress });
    } else {
      void openChapter(first, { kind: "fraction", value: 0 });
    }
  }, [manifest, layout, openChapter]);

  // Re-flow when the window or typography changes.
  useEffect(() => {
    if (!layout || chapterRef.current === null) return;
    const timer = window.setTimeout(() => void paginatorRef.current?.relayout(layout), 60);
    return () => window.clearTimeout(timer);
  }, [layout]);

  useEffect(() => {
    const element = stageRef.current;
    if (!element) return;
    element.addEventListener("wheel", onWheel, { passive: false });
    return () => element.removeEventListener("wheel", onWheel);
  }, [onWheel]);

  // ---- Derived state --------------------------------------------------------------------------------

  const chapter = chapterIndex !== null ? manifest.chapters[chapterIndex] : null;
  const overall = chapterIndex !== null && where ? overallFraction(manifest, chapterIndex, where.fraction) : (manifest.progress?.percentage ?? 0);
  const tocIndex = useMemo(
    () => (chapterIndex !== null && where ? currentTocIndex(manifest.tableOfContents, chapterIndex, where.page, (id) => paginatorRef.current?.pageOfFragment(id) ?? null) : -1),
    [manifest, chapterIndex, where],
  );
  const chapterTitle = (tocIndex >= 0 ? manifest.tableOfContents[tocIndex]?.title : null) ?? chapter?.title ?? manifest.title;
  const pagesLeft = where ? where.pageCount - where.page - 1 : 0;

  const selectToc = (entry: TocEntry) => {
    if (!tocPinned) setPanel(null);
    setJumps([]);
    goTo(entry.chapterIndex, entry.fragment ? { kind: "fragment", id: entry.fragment } : { kind: "fraction", value: 0 });
  };

  const selectHit = (hit: SearchHit, index: number) => {
    setSearch((s) => ({ ...s, active: index }));
    if (!isDesktop) setPanel(null);
    goTo(hit.chapterIndex, { kind: "search", query: search.query, occurrence: hit.occurrence });
  };

  return (
    <ReaderShell
      manifest={manifest}
      settings={settings}
      onSettingsChange={setSettings}
      kind="reflowable"
      framed={!!geometry?.framed}
      chromeHidden={!(isDesktop || chromeVisible || panel !== null)}
      panel={panel}
      onPanelChange={setPanel}
      tocIndex={tocIndex}
      onSelectToc={selectToc}
      search={search}
      onSearchChange={setSearch}
      onSelectHit={selectHit}
      onClearSearch={() => {
        setSearch({ query: "", response: null, active: -1 });
        paginatorRef.current?.clearHighlight();
      }}
      canJumpBack={jumps.length > 0}
      onJumpBack={() => {
        const last = jumps[jumps.length - 1];
        if (!last) return;
        setJumps((stack) => stack.slice(0, -1));
        goTo(last.chapterIndex, { kind: "fraction", value: last.fraction });
      }}
      onDismissJumps={() => setJumps([])}
      overall={overall}
      positionLabel={chapterIndex !== null ? `${chapterIndex + 1}/${manifest.chapters.length}` : ""}
      describePosition={(value) => manifest.chapters[locate(manifest, value).chapterIndex]?.title ?? percent(value)}
      onSeek={(value) => {
        const target = locate(manifest, value);
        goTo(target.chapterIndex, { kind: "fraction", value: target.fraction });
      }}
      atBookEnd={atBookEnd}
      onDismissEnd={() => setAtBookEnd(false)}
      onBack={() => {
        progress.flush();
        if (window.history.length > 1) navigate(-1);
        else navigate(`/books/${bookId}`);
      }}
      stageRef={stageRef}
    >
      {geometry?.framed && finePointer && (
        <button type="button" className={styles.turnButton} style={{ left: 12 }} onClick={() => turn(-1)} aria-label="Previous page">
          <ChevronLeft />
        </button>
      )}

      <div
        className={`${styles.pageFrame} ${geometry?.framed ? styles.framed : ""} ${theme.dark ? styles.dark : ""} ${loading ? styles.loading : ""}`}
        style={
          geometry
            ? {
                width: geometry.width,
                height: geometry.framed ? geometry.height : "100%",
                paddingTop: geometry.framed ? 0 : "var(--safe-top)",
                paddingBottom: geometry.framed ? 0 : "var(--safe-bottom)",
              }
            : { width: "100%", height: "100%" }
        }
      >
        {geometry?.columns === 2 && <div className={styles.spine} />}
        <div className={styles.runningHead} style={{ height: geometry?.framed ? 44 : 36, padding: `0 ${geometry?.margin ?? 16}px` }}>
          {geometry?.columns === 2 ? (
            <>
              <span>{manifest.title}</span>
              <span>{chapterTitle}</span>
            </>
          ) : (
            <span style={{ margin: "0 auto" }}>{chapterTitle}</span>
          )}
        </div>
        <div className={styles.viewport} ref={viewportRef}>
          <iframe ref={iframeRef} title={manifest.title} />
          {loading && (
            <div className={styles.spinnerWrap}>
              <div className="spinner" />
            </div>
          )}
        </div>
        <div className={styles.pageFoot} style={{ height: geometry?.framed ? 44 : 36, padding: `0 ${geometry?.margin ?? 16}px` }}>
          {where && (
            <>
              <span>{pagesLeft === 0 ? "Last page in chapter" : `${pagesLeft} ${pagesLeft === 1 ? "page" : "pages"} left in chapter`}</span>
              <span>{isDesktop ? `Page ${where.page + 1} of ${where.pageCount} · ${percent(overall)}` : percent(overall)}</span>
            </>
          )}
        </div>
      </div>

      {geometry?.framed && finePointer && (
        <button type="button" className={styles.turnButton} style={{ right: 12 }} onClick={() => turn(1)} aria-label="Next page">
          <ChevronRight />
        </button>
      )}
    </ReaderShell>
  );
}

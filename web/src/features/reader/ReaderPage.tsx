import { useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, ChevronLeft, ChevronRight, List, Search, Type, Undo2, X } from "lucide-react";
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { api, request } from "../../api/client";
import { queryKeys } from "../../api/queries";
import type { Chapter, ReaderManifest, SearchHit, TocEntry } from "../../api/types";
import { useToast } from "../../components/Toaster";
import { useHasFinePointer, useIsDesktop, useMediaQuery } from "../../hooks/useMediaQuery";
import { percent } from "../../lib/format";
import { currentTocIndex, locate, overallFraction } from "./location";
import { Paginator, type LinkTarget, type PageLayout, type PageTarget, type Relocation } from "./Paginator";
import { marginPx, maxPageWidth, themes, typographyCss, useReaderSettings } from "./readerSettings";
import { SearchPanel, type SearchState } from "./SearchPanel";
import { SettingsPanel } from "./SettingsPanel";
import { TocPanel } from "./TocPanel";
import styles from "./ReaderPage.module.css";

type Panel = "toc" | "search" | "settings" | null;

export default function ReaderPage() {
  const bookId = Number(useParams().bookId);
  const navigate = useNavigate();
  const client = useQueryClient();
  const toast = useToast();
  const isDesktop = useIsDesktop();
  const isWide = useMediaQuery("(min-width: 1100px)");
  const finePointer = useHasFinePointer();
  const [settings, setSettings] = useReaderSettings();
  const theme = themes[settings.theme];

  const manifestQuery = useQuery({
    queryKey: queryKeys.manifest(bookId),
    queryFn: () => api.get<ReaderManifest>(`/api/books/${bookId}/manifest`),
    staleTime: Infinity,
    refetchOnWindowFocus: false,
  });
  const manifest = manifestQuery.data;

  const stageRef = useRef<HTMLDivElement>(null);
  const viewportRef = useRef<HTMLDivElement>(null);
  const iframeRef = useRef<HTMLIFrameElement>(null);
  const paginatorRef = useRef<Paginator | null>(null);
  const [stage, setStage] = useState<{ width: number; height: number } | null>(null);
  const [viewport, setViewport] = useState<{ width: number; height: number } | null>(null);
  const [chapterIndex, setChapterIndex] = useState<number | null>(null);
  const [where, setWhere] = useState<Relocation | null>(null);
  const [loading, setLoading] = useState(true);
  const [chromeVisible, setChromeVisible] = useState(true);
  const [panel, setPanel] = useState<Panel>(null);
  const [search, setSearch] = useState<SearchState>({ query: "", response: null, active: -1 });
  const [jumps, setJumps] = useState<{ chapterIndex: number; fraction: number }[]>([]);
  const [atBookEnd, setAtBookEnd] = useState(false);
  const [scrub, setScrub] = useState<number | null>(null);

  const chapterRef = useRef<number | null>(null);
  const whereRef = useRef<Relocation | null>(null);
  const navToken = useRef(0);
  const userNavigated = useRef(false);
  const tocPinned = isWide && settings.tocPinned;

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

  useLayoutEffect(() => {
    const observe = (element: HTMLElement | null, set: (size: { width: number; height: number }) => void) => {
      if (!element) return () => {};
      const observer = new ResizeObserver(([entry]) => set({ width: entry.contentRect.width, height: entry.contentRect.height }));
      observer.observe(element);
      return () => observer.disconnect();
    };
    const stopStage = observe(stageRef.current, setStage);
    const stopViewport = observe(viewportRef.current, setViewport);
    return () => {
      stopStage();
      stopViewport();
    };
  }, []);

  // ---- Progress -------------------------------------------------------------------------------------

  const pendingSave = useRef<{ chapterIndex: number; chapterProgress: number } | null>(null);
  const saveTimer = useRef(0);

  const flushProgress = useCallback(() => {
    window.clearTimeout(saveTimer.current);
    const body = pendingSave.current;
    if (!body) return;
    pendingSave.current = null;
    request("PUT", `/api/books/${bookId}/progress`, body, { keepalive: true }).catch(() => {
      // Offline or signed out; the next page turn will try again.
    });
  }, [bookId]);

  useEffect(() => {
    const onHide = () => document.visibilityState === "hidden" && flushProgress();
    document.addEventListener("visibilitychange", onHide);
    window.addEventListener("pagehide", flushProgress);
    return () => {
      document.removeEventListener("visibilitychange", onHide);
      window.removeEventListener("pagehide", flushProgress);
      flushProgress();
      client.invalidateQueries({ queryKey: queryKeys.books });
    };
  }, [flushProgress, client]);

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
      if (!paginator || !layoutRef.current || !manifest) return;
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
      if (!manifest) return null;
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

  // Scrolling turns pages: one flick or one wheel notch is one page, ignoring trackpad momentum.
  const wheel = useRef({ total: 0, lastTime: 0, lastSize: 0, locked: false, turnedAt: 0 });
  const onWheel = useCallback(
    (event: WheelEvent) => {
      event.preventDefault();
      const state = wheel.current;
      const now = performance.now();
      const raw = Math.abs(event.deltaY) >= Math.abs(event.deltaX) ? event.deltaY : event.deltaX;
      const delta = raw * (event.deltaMode === 1 ? 32 : event.deltaMode === 2 ? 600 : 1);
      const size = Math.abs(delta);
      const gap = now - state.lastTime;
      state.lastTime = now;

      if (state.locked) {
        const newGesture = gap > 180 || size > state.lastSize * 1.5 + 4 || (size >= 50 && now - state.turnedAt > 380);
        state.lastSize = size;
        if (!newGesture) return;
        state.locked = false;
        state.total = 0;
      }

      state.lastSize = size;
      if (gap > 300) state.total = 0;
      state.total += delta;
      if (Math.abs(state.total) >= 40) {
        turn(state.total > 0 ? 1 : -1);
        state.total = 0;
        state.locked = true;
        state.turnedAt = now;
      }
    },
    [turn],
  );

  const onKey = useCallback(
    (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null;
      if (target?.closest?.("input, textarea, select") || event.metaKey || event.ctrlKey || event.altKey) return;
      const rtl = paginatorRef.current?.direction === "rtl";
      const forward = () => (event.preventDefault(), turn(1));
      const back = () => (event.preventDefault(), turn(-1));
      switch (event.key) {
        case "ArrowRight":
          return rtl ? back() : forward();
        case "ArrowLeft":
          return rtl ? forward() : back();
        case "ArrowDown":
        case "PageDown":
          return forward();
        case "ArrowUp":
        case "PageUp":
          return back();
        case " ":
          return event.shiftKey ? back() : forward();
        case "Escape":
          setPanel(null);
          return;
        case "t":
          setPanel((p) => (p === "toc" ? null : "toc"));
          return;
        case "/":
          event.preventDefault();
          setPanel("search");
          return;
      }
    },
    [turn],
  );

  const onLink = useCallback(
    (link: LinkTarget) => {
      if (!manifest) return;
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
      if (!location.settled || current === null || !manifest) return;
      if (!userNavigated.current && !manifest.progress) return;

      const lastChapter = neighbourChapter(current, 1) === null;
      const atEnd = lastChapter && location.page === location.pageCount - 1;
      pendingSave.current = { chapterIndex: current, chapterProgress: atEnd ? 1 : location.fraction };
      window.clearTimeout(saveTimer.current);
      saveTimer.current = window.setTimeout(flushProgress, 1200);
    },
    [manifest, neighbourChapter, flushProgress],
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
    if (!manifest || !layout || chapterRef.current !== null || !paginatorRef.current) return;
    const progress = manifest.progress;
    const first = manifest.chapters.find((c) => c.isLinear)?.index ?? 0;
    if (progress && !progress.isFinished && progress.chapterIndex < manifest.chapters.length) {
      void openChapter(progress.chapterIndex, { kind: "fraction", value: progress.chapterProgress });
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
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onKey]);

  useEffect(() => {
    const element = stageRef.current;
    if (!element) return;
    element.addEventListener("wheel", onWheel, { passive: false });
    return () => element.removeEventListener("wheel", onWheel);
  }, [onWheel]);

  // Match the browser chrome to the page colour, and keep the page from scrolling behind the reader.
  useEffect(() => {
    const meta = document.querySelector<HTMLMetaElement>('meta[name="theme-color"]:not([media])') ?? document.head.appendChild(Object.assign(document.createElement("meta"), { name: "theme-color" }));
    const previousOverflow = document.body.style.overflow;
    meta.content = theme.page;
    document.body.style.overflow = "hidden";
    return () => {
      meta.remove();
      document.body.style.overflow = previousOverflow;
    };
  }, [theme.page]);

  useEffect(() => {
    if (manifest) document.title = `${manifest.title} · CoffeeReader`;
    return () => {
      document.title = "CoffeeReader";
    };
  }, [manifest]);

  // ---- Derived state --------------------------------------------------------------------------------

  const chapter = manifest && chapterIndex !== null ? manifest.chapters[chapterIndex] : null;
  const overall = manifest && chapterIndex !== null && where ? overallFraction(manifest, chapterIndex, where.fraction) : (manifest?.progress?.percentage ?? 0);
  const tocIndex = useMemo(
    () =>
      manifest && chapterIndex !== null && where
        ? currentTocIndex(manifest.tableOfContents, chapterIndex, where.page, (id) => paginatorRef.current?.pageOfFragment(id) ?? null)
        : -1,
    [manifest, chapterIndex, where],
  );
  const chapterTitle = (tocIndex >= 0 ? manifest?.tableOfContents[tocIndex]?.title : null) ?? chapter?.title ?? manifest?.title ?? "";
  const pagesLeft = where ? where.pageCount - where.page - 1 : 0;
  const hits = search.response?.hits ?? [];
  const chromeShown = isDesktop || chromeVisible || panel !== null;

  const selectToc = (entry: TocEntry) => {
    if (!tocPinned || !isWide) setPanel(null);
    setJumps([]);
    goTo(entry.chapterIndex, entry.fragment ? { kind: "fragment", id: entry.fragment } : { kind: "fraction", value: 0 });
  };

  const selectHit = (hit: SearchHit, index: number) => {
    setSearch((s) => ({ ...s, active: index }));
    if (!isDesktop) setPanel(null);
    goTo(hit.chapterIndex, { kind: "search", query: search.query, occurrence: hit.occurrence });
  };

  const stepHit = (delta: number) => {
    const next = search.active + delta;
    if (next >= 0 && next < hits.length) selectHit(hits[next], next);
  };

  const clearSearch = () => {
    setSearch({ query: "", response: null, active: -1 });
    paginatorRef.current?.clearHighlight();
  };

  const jumpBack = () => {
    const last = jumps[jumps.length - 1];
    if (!last) return;
    setJumps((stack) => stack.slice(0, -1));
    goTo(last.chapterIndex, { kind: "fraction", value: last.fraction });
  };

  const commitScrub = (value: number) => {
    setScrub(null);
    if (!manifest) return;
    const target = locate(manifest, value);
    goTo(target.chapterIndex, { kind: "fraction", value: target.fraction });
  };

  const back = () => {
    flushProgress();
    if (window.history.length > 1) navigate(-1);
    else navigate(`/books/${bookId}`);
  };

  const scrubLabel = scrub !== null && manifest ? (manifest.chapters[locate(manifest, scrub).chapterIndex]?.title ?? `${percent(scrub)}`) : null;

  if (manifestQuery.isError) {
    return (
      <div className="center-fill" style={{ minHeight: "100dvh", padding: 24, textAlign: "center" }}>
        <div style={{ display: "grid", gap: 12, justifyItems: "center" }}>
          <p>This book couldn't be opened. {manifestQuery.error.message}</p>
          <Link to="/" className="btn">
            Back to library
          </Link>
        </div>
      </div>
    );
  }

  return (
    <div
      className={`${styles.reader} ${chromeShown ? "" : styles.chromeHidden}`}
      style={
        {
          "--page": theme.page,
          "--desk": geometry?.framed ? theme.desk : theme.page,
          "--ink": theme.text,
          "--ink-muted": theme.muted,
          "--accent-ink": theme.link,
          colorScheme: theme.dark ? "dark" : "light",
        } as React.CSSProperties
      }
    >
      <header className={styles.topbar}>
        <button type="button" className={styles.tool} onClick={back} aria-label="Back to library">
          <ArrowLeft />
        </button>
        <div className={styles.titleBlock}>
          <span className={styles.bookTitle}>{manifest?.title ?? "Loading…"}</span>
          <span className={styles.chapterTitle}>{manifest?.authors.join(", ")}</span>
        </div>
        <button
          type="button"
          className={styles.tool}
          aria-label="Contents"
          aria-pressed={panel === "toc" || tocPinned}
          onClick={() => {
            if (tocPinned) setSettings((s) => ({ ...s, tocPinned: false }));
            else setPanel((p) => (p === "toc" ? null : "toc"));
          }}
        >
          <List />
        </button>
        <button type="button" className={styles.tool} aria-label="Search" aria-pressed={panel === "search"} onClick={() => setPanel((p) => (p === "search" ? null : "search"))}>
          <Search />
        </button>
        <button type="button" className={styles.tool} aria-label="Reading settings" aria-pressed={panel === "settings"} onClick={() => setPanel((p) => (p === "settings" ? null : "settings"))}>
          <Type />
        </button>
      </header>

      <div className={styles.body}>
        {tocPinned && manifest && (
          <aside className={styles.sidebar}>
            <TocPanel
              entries={manifest.tableOfContents}
              currentIndex={tocIndex}
              pinned
              canPin
              onSelect={selectToc}
              onTogglePin={() => setSettings((s) => ({ ...s, tocPinned: false }))}
              onClose={() => undefined}
            />
          </aside>
        )}

        <main className={styles.stage} ref={stageRef} onPointerDown={() => panel === "settings" && setPanel(null)}>
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
                  <span>{manifest?.title}</span>
                  <span>{chapterTitle}</span>
                </>
              ) : (
                <span style={{ margin: "0 auto" }}>{chapterTitle}</span>
              )}
            </div>
            <div className={styles.viewport} ref={viewportRef}>
              <iframe ref={iframeRef} title={manifest?.title ?? "Book"} />
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

          {search.response && search.active >= 0 ? (
            <div className={styles.pill}>
              <button type="button" onClick={() => stepHit(-1)} disabled={search.active <= 0} aria-label="Previous match">
                <ChevronLeft />
              </button>
              <button type="button" onClick={() => setPanel("search")}>
                {search.active + 1} of {hits.length}
              </button>
              <button type="button" onClick={() => stepHit(1)} disabled={search.active >= hits.length - 1} aria-label="Next match">
                <ChevronRight />
              </button>
              <button type="button" onClick={clearSearch} aria-label="Clear search">
                <X />
              </button>
            </div>
          ) : (
            jumps.length > 0 && (
              <div className={styles.pill}>
                <button type="button" onClick={jumpBack}>
                  <Undo2 /> Back to where you were
                </button>
                <button type="button" onClick={() => setJumps([])} aria-label="Dismiss">
                  <X />
                </button>
              </div>
            )
          )}
        </main>

        {panel === "toc" && !tocPinned && manifest && (
          <>
            <div className={styles.scrim} onClick={() => setPanel(null)} />
            <aside className={`${styles.drawer} ${styles.drawerLeft}`}>
              <TocPanel
                entries={manifest.tableOfContents}
                currentIndex={tocIndex}
                pinned={false}
                canPin={isWide}
                onSelect={selectToc}
                onTogglePin={() => {
                  setSettings((s) => ({ ...s, tocPinned: true }));
                  setPanel(null);
                }}
                onClose={() => setPanel(null)}
              />
            </aside>
          </>
        )}

        {panel === "search" && (
          <>
            {!isDesktop && <div className={styles.scrim} onClick={() => setPanel(null)} />}
            <aside className={`${styles.drawer} ${styles.drawerRight}`}>
              <SearchPanel bookId={bookId} state={search} onChange={setSearch} onSelect={selectHit} onClose={() => setPanel(null)} />
            </aside>
          </>
        )}

        {panel === "settings" && (
          <>
            {!isDesktop && <div className={styles.scrim} onClick={() => setPanel(null)} />}
            <SettingsPanel settings={settings} onChange={setSettings} wide={isWide} />
          </>
        )}
      </div>

      <footer className={styles.bottombar}>
        <span className={styles.percent} style={{ textAlign: "left" }}>
          {chapterIndex !== null && manifest ? `${chapterIndex + 1}/${manifest.chapters.length}` : ""}
        </span>
        <div className={styles.scrubber}>
          {scrub !== null && scrubLabel && (
            <span className={styles.scrubLabel} style={{ ["--x" as string]: `${scrub * 100}%` }}>
              {scrubLabel}
            </span>
          )}
          <input
            type="range"
            min={0}
            max={1000}
            value={Math.round((scrub ?? overall) * 1000)}
            onChange={(e) => setScrub(Number(e.target.value) / 1000)}
            onPointerUp={(e) => commitScrub(Number((e.target as HTMLInputElement).value) / 1000)}
            onKeyUp={(e) => commitScrub(Number((e.target as HTMLInputElement).value) / 1000)}
            aria-label="Position in book"
          />
        </div>
        <span className={styles.percent}>{percent(scrub ?? overall)}</span>
      </footer>

      {atBookEnd && manifest && (
        <div className={styles.endCard} onClick={() => setAtBookEnd(false)}>
          <div className={styles.endInner} onClick={(e) => e.stopPropagation()}>
            <span style={{ fontSize: 40 }} aria-hidden>
              ☕
            </span>
            <h2>The end</h2>
            <p style={{ color: "var(--ink-muted)" }}>You've finished {manifest.title}. It's been marked as read.</p>
            <div className={styles.endActions}>
              <button type="button" className="btn" onClick={() => setAtBookEnd(false)}>
                Stay here
              </button>
              <Link to="/" className="btn btn-primary">
                Back to library
              </Link>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

import { ChevronLeft, ChevronRight } from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useNavigate } from "react-router";
import type { ReaderManifest, SearchHit, TocEntry } from "../../api/types";
import { useHasFinePointer, useIsDesktop, useMediaQuery } from "../../hooks/useMediaQuery";
import { percent } from "../../lib/format";
import { currentTocIndex } from "./location";
import { describePdfError, openPdf, type PDFDocumentProxy } from "./pdf";
import { PdfPageView } from "./PdfPageView";
import { useElementSize, useProgressSaver, useReaderDocument, useReaderKeys, useWheelTurns, type Panel } from "./readerHooks";
import { ReaderShell } from "./ReaderShell";
import { pdfPageFilter, themes, useReaderSettings } from "./readerSettings";
import type { SearchState } from "./SearchPanel";
import styles from "./ReaderPage.module.css";

/** Pages shown together: one at a time, or book-style spreads (cover alone, then pairs). */
function buildSpreads(pageCount: number, columns: 1 | 2): number[][] {
  if (columns === 1) return Array.from({ length: pageCount }, (_, i) => [i]);
  const spreads: number[][] = [[0]];
  for (let page = 1; page < pageCount; page += 2) spreads.push(page + 1 < pageCount ? [page, page + 1] : [page]);
  return spreads;
}

/** The reader for fixed-layout PDFs, drawn with PDF.js. */
export default function PdfReader({ manifest }: { manifest: ReaderManifest }) {
  const bookId = manifest.bookId;
  const navigate = useNavigate();
  const isDesktop = useIsDesktop();
  const isWide = useMediaQuery("(min-width: 1100px)");
  const finePointer = useHasFinePointer();
  const [settings, setSettings] = useReaderSettings();
  const theme = themes[settings.theme];
  const tocPinned = isWide && settings.tocPinned;
  const progress = useProgressSaver(bookId);
  useReaderDocument(manifest.title, theme.page);

  const [doc, setDoc] = useState<PDFDocumentProxy | null>(null);
  const [error, setError] = useState<string | null>(null);
  const pageCount = doc?.numPages ?? manifest.chapters.length;
  const saved = manifest.progress;
  const [page, setPage] = useState(() => (saved && !saved.isFinished ? Math.min(saved.chapterIndex, manifest.chapters.length - 1) : 0));
  const [panel, setPanel] = useState<Panel>(null);
  const [chromeVisible, setChromeVisible] = useState(true);
  const [search, setSearch] = useState<SearchState>({ query: "", response: null, active: -1 });
  const [highlight, setHighlight] = useState<{ page: number; query: string } | null>(null);
  const [atBookEnd, setAtBookEnd] = useState(false);
  const userNavigated = useRef(false);

  const stageRef = useRef<HTMLDivElement>(null);
  const areaRef = useRef<HTMLDivElement>(null);
  const stage = useElementSize(stageRef);
  const area = useElementSize(areaRef);

  useEffect(() => {
    const pdf = openPdf(bookId, manifest.fileVersion);
    pdf.promise.then(setDoc, (e) => setError(describePdfError(e)));
    return pdf.destroy;
  }, [bookId, manifest.fileVersion]);

  // ---- Layout ---------------------------------------------------------------------------------------

  const fitWidth = settings.pdfFit === "width";
  const zoom = fitWidth ? Math.min(3, Math.max(1, settings.pdfZoom || 1)) : 1;
  const framed = !!stage && stage.width >= 700 && stage.height >= 480;
  const columns: 1 | 2 = !fitWidth && settings.layout === "auto" && !!stage && stage.width >= 1000 && stage.width > stage.height * 1.15 ? 2 : 1;
  const spreads = useMemo(() => buildSpreads(pageCount, columns), [pageCount, columns]);
  const spreadIndex = Math.max(0, spreads.findIndex((s) => s.includes(page)));
  const visible = spreads[spreadIndex] ?? [0];
  const box = useMemo(
    () => (area ? { width: Math.floor(((area.width - (fitWidth ? 0 : 16)) / columns) * zoom), height: Math.floor(area.height - (fitWidth ? 0 : 16)) } : null),
    [area, columns, fitWidth, zoom],
  );

  // Keep neighbouring spreads rendered (hidden) so turning a page is instant.
  const mounted = useMemo(() => {
    const pages = new Set<number>();
    for (const i of [spreadIndex - 1, spreadIndex, spreadIndex + 1]) spreads[i]?.forEach((p) => pages.add(p));
    return [...pages].sort((a, b) => a - b);
  }, [spreads, spreadIndex]);

  // ---- Navigation -----------------------------------------------------------------------------------

  const goToPage = useCallback(
    (target: number) => {
      userNavigated.current = true;
      setAtBookEnd(false);
      setPage(Math.max(0, Math.min(pageCount - 1, target)));
      areaRef.current?.scrollTo({ top: 0 });
    },
    [pageCount],
  );

  const turn = useCallback(
    (direction: 1 | -1) => {
      if (!isDesktop) setChromeVisible(false);
      const next = spreads[spreadIndex + direction];
      if (!next) {
        if (direction === 1) setAtBookEnd(true);
        return;
      }

      goToPage(next[0]);
    },
    [isDesktop, spreads, spreadIndex, goToPage],
  );

  const wheelTurn = useWheelTurns(turn);
  useReaderKeys(turn, setPanel, () => false);

  // In "fit width" the page scrolls first; the wheel only turns pages at the top or bottom edge.
  useEffect(() => {
    const element = stageRef.current;
    if (!element) return;
    const onWheel = (event: WheelEvent) => {
      const scroller = areaRef.current;
      if (fitWidth && scroller) {
        const down = event.deltaY > 0;
        const atTop = scroller.scrollTop <= 0;
        const atBottom = scroller.scrollTop + scroller.clientHeight >= scroller.scrollHeight - 1;
        if ((down && !atBottom) || (!down && !atTop)) return;
      }

      wheelTurn(event);
    };
    element.addEventListener("wheel", onWheel, { passive: false });
    return () => element.removeEventListener("wheel", onWheel);
  }, [wheelTurn, fitWidth]);

  // Save the position once a spread is on screen.
  const isLast = spreadIndex === spreads.length - 1;
  useEffect(() => {
    if (!doc || (!userNavigated.current && !manifest.progress)) return;
    progress.queue(visible[0], isLast ? 1 : 0);
  }, [doc, visible, isLast, manifest.progress, progress]);

  // Taps and swipes on the page.
  // Taps are recognised from touch events directly: browsers don't always synthesise a click after a swipe.
  const touch = useRef<{ x: number; y: number; time: number } | null>(null);
  const touchHandledAt = useRef(0);
  const tap = (clientX: number, element: HTMLElement, isTouch: boolean) => {
    const selection = window.getSelection();
    if (selection && !selection.isCollapsed) return;
    if (panel && !(panel === "toc" && tocPinned)) {
      setPanel(null);
      return;
    }

    const rect = element.getBoundingClientRect();
    const x = (clientX - rect.left) / rect.width;
    if (!isTouch && finePointer) turn(x < 0.3 ? -1 : 1);
    else if (x < 0.3) turn(-1);
    else if (x > 0.7) turn(1);
    else setChromeVisible((v) => !v);
  };
  const onClick = (event: React.MouseEvent<HTMLDivElement>) => {
    if (performance.now() - touchHandledAt.current < 600) return;
    tap(event.clientX, event.currentTarget, false);
  };

  const overall = pageCount > 1 ? (isLast ? 1 : visible[0] / (pageCount - 1)) : 1;
  const tocIndex = currentTocIndex(manifest.tableOfContents, visible[visible.length - 1], 0, () => null);
  const sectionTitle = tocIndex >= 0 ? manifest.tableOfContents[tocIndex].title : manifest.title;
  const pageLabel = visible.length > 1 ? `Pages ${visible[0] + 1}–${visible[1] + 1} of ${pageCount}` : `Page ${visible[0] + 1} of ${pageCount}`;
  const filter = pdfPageFilter(settings);
  const onHighlighted = useCallback((mark: HTMLElement) => mark.scrollIntoView({ block: "center" }), []);

  return (
    <ReaderShell
      manifest={manifest}
      settings={settings}
      onSettingsChange={setSettings}
      kind="fixed"
      framed={framed}
      chromeHidden={!(isDesktop || chromeVisible || panel !== null)}
      panel={panel}
      onPanelChange={setPanel}
      tocIndex={tocIndex}
      onSelectToc={(entry: TocEntry) => {
        if (!tocPinned) setPanel(null);
        goToPage(entry.chapterIndex);
      }}
      search={search}
      onSearchChange={setSearch}
      onSelectHit={(hit: SearchHit, index: number) => {
        setSearch((s) => ({ ...s, active: index }));
        setHighlight({ page: hit.chapterIndex, query: search.query });
        if (!isDesktop) setPanel(null);
        goToPage(hit.chapterIndex);
      }}
      onClearSearch={() => {
        setSearch({ query: "", response: null, active: -1 });
        setHighlight(null);
      }}
      canJumpBack={false}
      onJumpBack={() => undefined}
      onDismissJumps={() => undefined}
      overall={overall}
      positionLabel={`${visible[0] + 1}/${pageCount}`}
      describePosition={(value) => `Page ${Math.round(value * (pageCount - 1)) + 1}`}
      onSeek={(value) => goToPage(Math.round(value * (pageCount - 1)))}
      atBookEnd={atBookEnd}
      onDismissEnd={() => setAtBookEnd(false)}
      onBack={() => {
        progress.flush();
        if (window.history.length > 1) navigate(-1);
        else navigate(`/books/${bookId}`);
      }}
      stageRef={stageRef}
      stageClassName={styles.pdfStage}
    >
      {framed && finePointer && (
        <button type="button" className={styles.turnButton} style={{ left: 12 }} onClick={() => turn(-1)} disabled={spreadIndex === 0} aria-label="Previous page">
          <ChevronLeft />
        </button>
      )}

      <div className={styles.pdfFrame} style={{ paddingInline: framed ? (finePointer ? 64 : 24) : 0 }}>
        <div className={styles.runningHead} style={{ height: framed ? 40 : 32, padding: "0 4px", paddingTop: framed ? 0 : "var(--safe-top)" }}>
          {framed ? (
            <>
              <span>{manifest.title}</span>
              <span>{sectionTitle}</span>
            </>
          ) : (
            <span style={{ margin: "0 auto" }}>{sectionTitle}</span>
          )}
        </div>

        <div
          ref={areaRef}
          className={`${styles.pdfArea} ${fitWidth ? styles.pdfScroll : ""} ${zoom > 1 ? styles.pdfZoomed : ""}`}
          onClick={onClick}
          onTouchStart={(e) => (touch.current = e.touches.length === 1 ? { x: e.touches[0].clientX, y: e.touches[0].clientY, time: performance.now() } : null)}
          onTouchEnd={(e) => {
            const start = touch.current;
            touch.current = null;
            if (!start || e.changedTouches.length !== 1) return;
            const end = e.changedTouches[0];
            const dx = end.clientX - start.x;
            const dy = end.clientY - start.y;
            // When zoomed in, horizontal drags pan the page instead of turning it.
            if (zoom === 1 && Math.abs(dx) > 50 && Math.abs(dx) > Math.abs(dy) * 1.5) {
              touchHandledAt.current = performance.now();
              turn(dx < 0 ? 1 : -1);
            } else if (Math.hypot(dx, dy) < 10 && performance.now() - start.time < 350) {
              touchHandledAt.current = performance.now();
              tap(end.clientX, e.currentTarget, true);
            }
          }}
        >
          {error ? (
            <div className={styles.spinnerWrap} style={{ padding: 24, textAlign: "center" }}>
              {error}
            </div>
          ) : !doc || !box ? (
            <div className={styles.spinnerWrap}>
              <div className="spinner" />
            </div>
          ) : (
            <div className={styles.pdfSpread} style={{ gridTemplateColumns: `repeat(${visible.length}, auto)` }}>
              {mounted.map((index) => {
                const position = visible.indexOf(index);
                return (
                  <PdfPageView
                    key={index}
                    doc={doc}
                    index={index}
                    box={box}
                    fit={settings.pdfFit}
                    hidden={position < 0}
                    align={visible.length === 1 ? "center" : position === 0 ? "end" : "start"}
                    highlight={highlight && highlight.page === index ? highlight.query : null}
                    onHighlighted={fitWidth ? onHighlighted : undefined}
                    filter={filter}
                  />
                );
              })}
            </div>
          )}
        </div>

        <div className={styles.pageFoot} style={{ height: framed ? 40 : 32, padding: "0 4px", paddingBottom: framed ? 0 : "var(--safe-bottom)" }}>
          <span>{pageLabel}</span>
          <span>{percent(overall)}</span>
        </div>
      </div>

      {framed && finePointer && (
        <button type="button" className={styles.turnButton} style={{ right: 12 }} onClick={() => turn(1)} disabled={isLast} aria-label="Next page">
          <ChevronRight />
        </button>
      )}
    </ReaderShell>
  );
}

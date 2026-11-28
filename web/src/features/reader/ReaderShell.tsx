import { ArrowLeft, ChevronLeft, ChevronRight, List, Search, Type, Undo2, X } from "lucide-react";
import { useState, type ReactNode, type RefObject } from "react";
import { Link } from "react-router";
import type { ReaderManifest, SearchHit, TocEntry } from "../../api/types";
import { useIsDesktop, useMediaQuery } from "../../hooks/useMediaQuery";
import { authorsText, percent } from "../../lib/format";
import type { Panel } from "./readerHooks";
import { themes, type ReaderSettings } from "./readerSettings";
import { SearchPanel, type SearchState } from "./SearchPanel";
import { SettingsPanel } from "./SettingsPanel";
import { TocPanel } from "./TocPanel";
import styles from "./ReaderPage.module.css";

interface Props {
  manifest: ReaderManifest;
  settings: ReaderSettings;
  onSettingsChange: (settings: ReaderSettings) => void;
  /** Reflowable books (EPUB, MOBI) get typography settings; fixed-layout ones (PDF) get zoom and colour. */
  kind: "reflowable" | "fixed";
  framed: boolean;
  chromeHidden: boolean;
  panel: Panel;
  onPanelChange: (panel: Panel) => void;
  tocIndex: number;
  onSelectToc: (entry: TocEntry) => void;
  search: SearchState;
  onSearchChange: (state: SearchState) => void;
  onSelectHit: (hit: SearchHit, index: number) => void;
  onClearSearch: () => void;
  canJumpBack: boolean;
  onJumpBack: () => void;
  onDismissJumps: () => void;
  overall: number;
  positionLabel: string;
  describePosition: (fraction: number) => string | null;
  onSeek: (fraction: number) => void;
  atBookEnd: boolean;
  onDismissEnd: () => void;
  onBack: () => void;
  stageRef: RefObject<HTMLDivElement | null>;
  stageClassName?: string;
  children: ReactNode;
}

/**
 * Everything around the page: toolbars, contents, search, settings, the position scrubber and the
 * little helpers (search stepper, "back to where you were", end of book). Shared by every reader.
 */
export function ReaderShell(props: Props) {
  const { manifest, settings, panel, onPanelChange, search } = props;
  const isDesktop = useIsDesktop();
  const isWide = useMediaQuery("(min-width: 1100px)");
  const tocPinned = isWide && settings.tocPinned;
  const theme = themes[settings.theme];
  const [scrub, setScrub] = useState<number | null>(null);
  const hits = search.response?.hits ?? [];
  const scrubLabel = scrub === null ? null : props.describePosition(scrub);

  const commitScrub = (value: number) => {
    setScrub(null);
    props.onSeek(value);
  };

  const toc = (pinned: boolean) => (
    <TocPanel
      entries={manifest.tableOfContents}
      currentIndex={props.tocIndex}
      pinned={pinned}
      canPin={isWide}
      onSelect={props.onSelectToc}
      onTogglePin={() => {
        props.onSettingsChange({ ...settings, tocPinned: !pinned });
        onPanelChange(null);
      }}
      onClose={() => onPanelChange(null)}
    />
  );

  return (
    <div
      className={`${styles.reader} ${props.chromeHidden ? styles.chromeHidden : ""}`}
      style={
        {
          "--page": theme.page,
          "--desk": props.framed ? theme.desk : theme.page,
          "--ink": theme.text,
          "--ink-muted": theme.muted,
          "--accent-ink": theme.link,
          colorScheme: theme.dark ? "dark" : "light",
        } as React.CSSProperties
      }
    >
      <header className={styles.topbar}>
        <button type="button" className={styles.tool} onClick={props.onBack} aria-label="Back to library">
          <ArrowLeft />
        </button>
        <div className={styles.titleBlock}>
          <span className={styles.bookTitle}>{manifest.title}</span>
          <span className={styles.chapterTitle}>{authorsText(manifest.authors, { fallback: "" })}</span>
        </div>
        <button
          type="button"
          className={styles.tool}
          aria-label="Contents"
          aria-pressed={panel === "toc" || tocPinned}
          onClick={() => {
            if (tocPinned) props.onSettingsChange({ ...settings, tocPinned: false });
            else onPanelChange(panel === "toc" ? null : "toc");
          }}
        >
          <List />
        </button>
        <button type="button" className={styles.tool} aria-label="Search" aria-pressed={panel === "search"} onClick={() => onPanelChange(panel === "search" ? null : "search")}>
          <Search />
        </button>
        <button type="button" className={styles.tool} aria-label="Reading settings" aria-pressed={panel === "settings"} onClick={() => onPanelChange(panel === "settings" ? null : "settings")}>
          <Type />
        </button>
      </header>

      <div className={styles.body}>
        {tocPinned && <aside className={styles.sidebar}>{toc(true)}</aside>}

        <main className={`${styles.stage} ${props.stageClassName ?? ""}`} ref={props.stageRef} onPointerDown={() => panel === "settings" && onPanelChange(null)}>
          {props.children}

          {search.response && search.active >= 0 ? (
            <div className={styles.pill}>
              <button type="button" onClick={() => search.active > 0 && props.onSelectHit(hits[search.active - 1], search.active - 1)} disabled={search.active <= 0} aria-label="Previous match">
                <ChevronLeft />
              </button>
              <button type="button" onClick={() => onPanelChange("search")}>
                {search.active + 1} of {hits.length}
              </button>
              <button
                type="button"
                onClick={() => search.active < hits.length - 1 && props.onSelectHit(hits[search.active + 1], search.active + 1)}
                disabled={search.active >= hits.length - 1}
                aria-label="Next match"
              >
                <ChevronRight />
              </button>
              <button type="button" onClick={props.onClearSearch} aria-label="Clear search">
                <X />
              </button>
            </div>
          ) : (
            props.canJumpBack && (
              <div className={styles.pill}>
                <button type="button" onClick={props.onJumpBack}>
                  <Undo2 /> Back to where you were
                </button>
                <button type="button" onClick={props.onDismissJumps} aria-label="Dismiss">
                  <X />
                </button>
              </div>
            )
          )}
        </main>

        {panel === "toc" && !tocPinned && (
          <>
            <div className={styles.scrim} onClick={() => onPanelChange(null)} />
            <aside className={`${styles.drawer} ${styles.drawerLeft}`}>{toc(false)}</aside>
          </>
        )}

        {panel === "search" && (
          <>
            {!isDesktop && <div className={styles.scrim} onClick={() => onPanelChange(null)} />}
            <aside className={`${styles.drawer} ${styles.drawerRight}`}>
              <SearchPanel bookId={manifest.bookId} state={search} onChange={props.onSearchChange} onSelect={props.onSelectHit} onClose={() => onPanelChange(null)} />
            </aside>
          </>
        )}

        {panel === "settings" && (
          <>
            {!isDesktop && <div className={styles.scrim} onClick={() => onPanelChange(null)} />}
            <SettingsPanel settings={settings} onChange={props.onSettingsChange} wide={isWide} kind={props.kind} />
          </>
        )}
      </div>

      <footer className={styles.bottombar}>
        <span className={styles.percent} style={{ textAlign: "left", minWidth: 52 }}>
          {props.positionLabel}
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
            value={Math.round((scrub ?? props.overall) * 1000)}
            onChange={(e) => setScrub(Number(e.target.value) / 1000)}
            onPointerUp={(e) => commitScrub(Number((e.target as HTMLInputElement).value) / 1000)}
            onKeyUp={(e) => commitScrub(Number((e.target as HTMLInputElement).value) / 1000)}
            aria-label="Position in book"
          />
        </div>
        <span className={styles.percent}>{percent(scrub ?? props.overall)}</span>
      </footer>

      {props.atBookEnd && (
        <div className={styles.endCard} onClick={props.onDismissEnd}>
          <div className={styles.endInner} onClick={(e) => e.stopPropagation()}>
            <span style={{ fontSize: 40 }} aria-hidden>
              ☕
            </span>
            <h2>The end</h2>
            <p style={{ color: "var(--ink-muted)" }}>You've finished {manifest.title}. It's been marked as read.</p>
            <div className={styles.endActions}>
              <button type="button" className="btn" onClick={props.onDismissEnd}>
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

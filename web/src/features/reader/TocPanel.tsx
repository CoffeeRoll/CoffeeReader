import { Pin, PinOff, X } from "lucide-react";
import { useEffect, useRef } from "react";
import type { TocEntry } from "../../api/types";
import styles from "./ReaderPage.module.css";

interface Props {
  entries: TocEntry[];
  currentIndex: number;
  pinned: boolean;
  canPin: boolean;
  onSelect: (entry: TocEntry) => void;
  onTogglePin: () => void;
  onClose: () => void;
}

export function TocPanel({ entries, currentIndex, pinned, canPin, onSelect, onTogglePin, onClose }: Props) {
  const listRef = useRef<HTMLOListElement>(null);

  // Keep the current chapter in view.
  useEffect(() => {
    listRef.current?.querySelector('[aria-current="true"]')?.scrollIntoView({ block: "nearest" });
  }, [currentIndex]);

  return (
    <>
      <div className={styles.panelHeader}>
        <h2 className={styles.panelTitle}>Contents</h2>
        {canPin && (
          <button type="button" className={styles.tool} onClick={onTogglePin} aria-pressed={pinned} title={pinned ? "Unpin contents" : "Keep contents open"}>
            {pinned ? <PinOff /> : <Pin />}
          </button>
        )}
        {!pinned && (
          <button type="button" className={styles.tool} onClick={onClose} aria-label="Close contents">
            <X />
          </button>
        )}
      </div>
      <nav className={styles.panelBody} aria-label="Table of contents">
        <ol className={styles.tocList} ref={listRef}>
          {entries.map((entry, index) => (
            <li key={index}>
              <button
                type="button"
                className={`${styles.tocItem} ${entry.depth === 0 ? styles.tocDepth0 : ""}`}
                style={{ ["--depth" as string]: Math.min(entry.depth, 4) }}
                aria-current={index === currentIndex}
                onClick={() => onSelect(entry)}
              >
                {entry.title}
              </button>
            </li>
          ))}
        </ol>
      </nav>
    </>
  );
}

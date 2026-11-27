import { Check } from "lucide-react";
import type { ReactNode } from "react";
import { Link } from "react-router";
import type { BookSummary } from "../api/types";
import { authorsText, formatIndex } from "../lib/format";
import { BookCover } from "./BookCover";
import styles from "./BookCard.module.css";

interface Props {
  book: BookSummary;
  to?: string;
  showSeriesIndex?: boolean;
  menu?: ReactNode;
  eager?: boolean;
  /** Suppress navigation (e.g. while the card is being dragged). */
  inert?: boolean;
}

export function BookCard({ book, to, showSeriesIndex, menu, eager, inert }: Props) {
  const progress = book.progress;
  const reading = progress && !progress.isFinished && progress.percentage > 0;
  const content = (
    <>
      <div className={styles.coverWrap}>
        <BookCover book={book} eager={eager} />
        {progress?.isFinished && (
          <span className={styles.badge} title="Finished">
            <Check strokeWidth={3} />
          </span>
        )}
        {showSeriesIndex && book.seriesIndex !== null && !reading && <span className={styles.seriesTag}>#{formatIndex(book.seriesIndex)}</span>}
        {reading && (
          <div className={styles.progress} title={`${Math.round(progress.percentage * 100)}% read`}>
            <div className={styles.progressFill} style={{ width: `${Math.max(3, progress.percentage * 100)}%` }} />
          </div>
        )}
      </div>
      <div className={styles.meta}>
        <span className={styles.title}>{book.title}</span>
        <span className={styles.author}>{authorsText(book.authors)}</span>
      </div>
    </>
  );

  return (
    <div className={styles.wrapper}>
      {inert ? (
        <div className={styles.card}>{content}</div>
      ) : (
        <Link to={to ?? `/books/${book.id}`} className={styles.card} draggable={false}>
          {content}
        </Link>
      )}
      {menu}
    </div>
  );
}

export { styles as bookCardStyles };

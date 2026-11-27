import { BookPlus, FolderOpen } from "lucide-react";
import { useMemo } from "react";
import { Link } from "react-router";
import { useBooks, useCurrentUser, useShelves } from "../../api/queries";
import type { BookSummary } from "../../api/types";
import { BookCover } from "../../components/BookCover";
import { EmptyState } from "../../components/EmptyState";
import { authorsText, percent, timeAgo } from "../../lib/format";
import { useUploads } from "../upload/UploadProvider";
import { ShelfBoard } from "./ShelfBoard";
import styles from "./ShelfBoard.module.css";

export function HomePage() {
  const books = useBooks();
  const shelves = useShelves();
  const user = useCurrentUser();
  const uploads = useUploads();

  const continueReading = useMemo(
    () =>
      (books.data ?? [])
        .filter((b) => b.progress && !b.progress.isFinished)
        .sort((a, b) => b.progress!.updatedAt.localeCompare(a.progress!.updatedAt))
        .slice(0, 12),
    [books.data],
  );

  if (books.isLoading || shelves.isLoading) {
    return (
      <div className="center-fill">
        <div className="spinner" />
      </div>
    );
  }

  if (books.isError || shelves.isError) {
    return (
      <div className="page">
        <div className="form-error">Couldn't load your library. {(books.error ?? shelves.error)?.message}</div>
      </div>
    );
  }

  if ((books.data ?? []).length === 0) {
    return (
      <div className="page">
        <EmptyState
          icon={<BookPlus />}
          title="Your library is empty"
          actions={
            <button type="button" className="btn btn-primary btn-lg" onClick={uploads.openPicker}>
              <BookPlus /> Add books
            </button>
          }
        >
          Drop EPUB files anywhere on this page, or copy them into your books folder — CoffeeReader picks them up automatically.
        </EmptyState>
        <p className="muted" style={{ display: "flex", gap: 8, justifyContent: "center", alignItems: "center", fontSize: 13 }}>
          <FolderOpen size={16} /> Library folder scans run on start-up and whenever files change.
        </p>
      </div>
    );
  }

  return (
    <div className="page">
      <h1 className="page-title">{greeting()}, {user.displayName}</h1>
      {continueReading.length > 0 && (
        <section aria-labelledby="continue-heading" style={{ marginTop: 20 }}>
          <h2 id="continue-heading" className={styles.heading} style={{ marginBottom: 12 }}>
            Continue reading
          </h2>
          <div className={styles.continueRow}>
            {continueReading.map((book) => (
              <ContinueCard key={book.id} book={book} />
            ))}
          </div>
        </section>
      )}
      <ShelfBoard books={books.data!} shelves={shelves.data!} />
    </div>
  );
}

function ContinueCard({ book }: { book: BookSummary }) {
  const progress = book.progress!;
  return (
    <Link to={`/read/${book.id}`} className={styles.continueCard}>
      <BookCover book={book} eager />
      <div className={styles.continueMeta}>
        <span className={styles.continueTitle}>{book.title}</span>
        <span className={styles.continueAuthor}>{authorsText(book.authors)}</span>
        <div className={styles.bar}>
          <div className={styles.barFill} style={{ width: `${Math.max(2, progress.percentage * 100)}%` }} />
        </div>
        <div className={styles.continueFoot}>
          <span>{percent(progress.percentage)}</span>
          <span>{timeAgo(progress.updatedAt)}</span>
        </div>
      </div>
    </Link>
  );
}

function greeting() {
  const hour = new Date().getHours();
  if (hour < 5) return "Reading late";
  if (hour < 12) return "Good morning";
  if (hour < 18) return "Good afternoon";
  return "Good evening";
}

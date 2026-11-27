import { BookOpen, Check, CheckCheck, Download, Pencil, Plus, RotateCcw, Trash2 } from "lucide-react";
import { useEffect, useState, type FormEvent } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { ApiError } from "../../api/client";
import { useBook, useCurrentUser, useDeleteBook, useSetFinished, useShelves, useUpdateBook } from "../../api/queries";
import type { BookDetails } from "../../api/types";
import { BookCover } from "../../components/BookCover";
import { Dialog } from "../../components/Dialog";
import { useToast } from "../../components/Toaster";
import { fileSize, formatIndex, percent, timeAgo } from "../../lib/format";
import { useShelfActions } from "./shelfActions";
import { ShelfNameDialog } from "./ShelfNameDialog";
import styles from "./BookPage.module.css";

export function BookPage() {
  const bookId = Number(useParams().bookId);
  const { data: book, isLoading, error } = useBook(bookId);
  const shelves = useShelves().data ?? [];
  const user = useCurrentUser();
  const actions = useShelfActions();
  const setFinished = useSetFinished(bookId);
  const deleteBook = useDeleteBook();
  const navigate = useNavigate();
  const toast = useToast();
  const [editing, setEditing] = useState(false);
  const [creatingShelf, setCreatingShelf] = useState(false);

  if (isLoading) {
    return (
      <div className="center-fill">
        <div className="spinner" />
      </div>
    );
  }

  if (!book) {
    return (
      <div className="page">
        <div className="form-error">{error instanceof ApiError && error.status === 404 ? "This book isn't in the library any more." : "Couldn't load this book."}</div>
      </div>
    );
  }

  const progress = book.progress;
  const started = progress && progress.percentage > 0 && !progress.isFinished;
  const onShelves = new Set(shelves.filter((s) => s.bookIds.includes(book.id)).map((s) => s.id));

  const remove = async () => {
    if (!window.confirm(`Delete “${book.title}”? This removes the file from the library folder and everyone's progress.`)) return;
    try {
      await deleteBook.mutateAsync(book.id);
      toast.show(`Deleted “${book.title}”`);
      navigate("/library");
    } catch (e) {
      toast.error(e);
    }
  };

  return (
    <div className="page">
      <div className={styles.layout}>
        <div className={styles.coverColumn}>
          <BookCover book={book} eager className={styles.cover} />
        </div>

        <div className={styles.info}>
          <div style={{ display: "grid", gap: 8 }}>
            {book.series && (
              <Link className={styles.series} to={`/library?q=${encodeURIComponent(book.series.name)}`}>
                {book.series.name}
                {book.seriesIndex !== null && ` · Book ${formatIndex(book.seriesIndex)}`}
              </Link>
            )}
            <h1 className={styles.title}>{book.title}</h1>
            <p className={styles.authors}>
              {book.authors.length === 0
                ? "Unknown author"
                : book.authors.map((author, i) => (
                    <span key={author.id}>
                      {i > 0 && " & "}
                      <Link to={`/library?q=${encodeURIComponent(author.name)}`}>{author.name}</Link>
                    </span>
                  ))}
            </p>
          </div>

          <div className={styles.actions}>
            <Link to={`/read/${book.id}`} className="btn btn-primary btn-lg">
              <BookOpen /> {started ? "Continue reading" : progress?.isFinished ? "Read again" : "Start reading"}
            </Link>
            <a href={`/api/books/${book.id}/download`} className="btn btn-lg" download>
              <Download /> Download
            </a>
          </div>

          {progress && (
            <div className={styles.progressCard}>
              <div className={styles.progressTop}>
                <strong>{progress.isFinished ? "Finished" : `${percent(progress.percentage)} read`}</strong>
                <span className="muted">{timeAgo(progress.updatedAt)}</span>
              </div>
              <div className={styles.bar}>
                <div className={styles.barFill} style={{ width: `${progress.isFinished ? 100 : progress.percentage * 100}%` }} />
              </div>
              <div style={{ display: "flex", gap: 8, flexWrap: "wrap", marginTop: 4 }}>
                {!progress.isFinished && (
                  <button className="btn btn-sm" onClick={() => setFinished.mutate(true)}>
                    <CheckCheck /> Mark as finished
                  </button>
                )}
                <button className="btn btn-sm btn-ghost" onClick={() => setFinished.mutate(false)}>
                  <RotateCcw /> Mark as unread
                </button>
              </div>
            </div>
          )}
          {!progress && (
            <div>
              <button className="btn btn-sm btn-ghost" onClick={() => setFinished.mutate(true)}>
                <CheckCheck /> I've already read this
              </button>
            </div>
          )}

          <div style={{ display: "grid", gap: 10 }}>
            <h2 className={styles.sectionTitle}>Shelves</h2>
            <div className={styles.shelves}>
              {shelves.map((shelf) => {
                const on = onShelves.has(shelf.id);
                return (
                  <button key={shelf.id} type="button" className={styles.shelfChip} aria-pressed={on} onClick={() => actions.toggle(shelf.id, book.id, !on)}>
                    {on && <Check />}
                    {shelf.name}
                  </button>
                );
              })}
              <button type="button" className={styles.shelfChip} onClick={() => setCreatingShelf(true)}>
                <Plus /> New shelf
              </button>
            </div>
          </div>

          {book.description && (
            <div style={{ display: "grid", gap: 10 }}>
              <h2 className={styles.sectionTitle}>About this book</h2>
              <p className={styles.description}>{book.description}</p>
            </div>
          )}

          <div style={{ display: "grid", gap: 10 }}>
            <h2 className={styles.sectionTitle}>Details</h2>
            <dl className={styles.details}>
              {book.publisher && (
                <>
                  <dt>Publisher</dt>
                  <dd>{book.publisher}</dd>
                </>
              )}
              {book.publishedDate && (
                <>
                  <dt>Published</dt>
                  <dd>{book.publishedDate}</dd>
                </>
              )}
              {book.language && (
                <>
                  <dt>Language</dt>
                  <dd>{languageName(book.language)}</dd>
                </>
              )}
              {book.isbn && (
                <>
                  <dt>ISBN</dt>
                  <dd>{book.isbn}</dd>
                </>
              )}
              <dt>File</dt>
              <dd>
                {book.fileName} · {fileSize(book.fileSize)}
              </dd>
              <dt>Added</dt>
              <dd>{new Date(book.addedAt).toLocaleDateString()}</dd>
            </dl>
          </div>

          {user.role === "admin" && (
            <div className={styles.actions} style={{ paddingTop: 8, borderTop: "1px solid var(--border)" }}>
              <button className="btn btn-sm" onClick={() => setEditing(true)}>
                <Pencil /> Edit details
              </button>
              <button className="btn btn-sm btn-danger" onClick={remove} disabled={deleteBook.isPending}>
                <Trash2 /> Delete book
              </button>
            </div>
          )}
        </div>
      </div>

      <EditBookDialog book={book} open={editing} onClose={() => setEditing(false)} />
      <ShelfNameDialog open={creatingShelf} title="New shelf" submitLabel="Create shelf" onClose={() => setCreatingShelf(false)} onSubmit={(name) => actions.create(name, [book.id])} />
    </div>
  );
}

function languageName(code: string) {
  try {
    return new Intl.DisplayNames(undefined, { type: "language" }).of(code) ?? code;
  } catch {
    return code;
  }
}

function EditBookDialog({ book, open, onClose }: { book: BookDetails; open: boolean; onClose: () => void }) {
  const update = useUpdateBook(book.id);
  const toast = useToast();
  const [form, setForm] = useState(() => toForm(book));
  useEffect(() => {
    if (open) setForm(toForm(book));
  }, [open, book]);

  const set = (key: keyof ReturnType<typeof toForm>) => (event: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) =>
    setForm((f) => ({ ...f, [key]: event.target.value }));

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    const blank = (value: string) => (value.trim() ? value.trim() : null);
    try {
      await update.mutateAsync({
        title: form.title.trim(),
        authors: form.authors.split(/[;&]/).map((a) => a.trim()).filter(Boolean),
        series: blank(form.series),
        seriesIndex: form.seriesIndex.trim() ? Number(form.seriesIndex) : null,
        description: blank(form.description),
        publisher: blank(form.publisher),
        language: blank(form.language),
        publishedDate: blank(form.publishedDate),
        isbn: blank(form.isbn),
      });
      toast.show("Saved");
      onClose();
    } catch (e) {
      toast.error(e);
    }
  };

  return (
    <Dialog open={open} title="Edit details" onClose={onClose} wide>
      <form onSubmit={submit} className={styles.formGrid}>
        <label className={`field ${styles.span2}`}>
          <span>Title</span>
          <input className="input" value={form.title} onChange={set("title")} required />
        </label>
        <label className={`field ${styles.span2}`}>
          <span>Authors (separate with “;”)</span>
          <input className="input" value={form.authors} onChange={set("authors")} />
        </label>
        <label className="field">
          <span>Series</span>
          <input className="input" value={form.series} onChange={set("series")} />
        </label>
        <label className="field">
          <span>Number in series</span>
          <input className="input" type="number" step="0.1" min="0" value={form.seriesIndex} onChange={set("seriesIndex")} />
        </label>
        <label className="field">
          <span>Publisher</span>
          <input className="input" value={form.publisher} onChange={set("publisher")} />
        </label>
        <label className="field">
          <span>Published</span>
          <input className="input" value={form.publishedDate} onChange={set("publishedDate")} placeholder="YYYY-MM-DD" />
        </label>
        <label className="field">
          <span>Language</span>
          <input className="input" value={form.language} onChange={set("language")} placeholder="en" />
        </label>
        <label className="field">
          <span>ISBN</span>
          <input className="input" value={form.isbn} onChange={set("isbn")} />
        </label>
        <label className={`field ${styles.span2}`}>
          <span>Description</span>
          <textarea className="textarea" value={form.description} onChange={set("description")} rows={6} />
        </label>
        <p className={`muted ${styles.span2}`} style={{ fontSize: 13 }}>
          Edited details are kept even if the file is rescanned.
        </p>
        <div className={styles.span2} style={{ display: "flex", justifyContent: "flex-end", gap: 10 }}>
          <button type="button" className="btn" onClick={onClose}>
            Cancel
          </button>
          <button className="btn btn-primary" disabled={update.isPending}>
            Save changes
          </button>
        </div>
      </form>
    </Dialog>
  );
}

function toForm(book: BookDetails) {
  return {
    title: book.title,
    authors: book.authors.map((a) => a.name).join("; "),
    series: book.series?.name ?? "",
    seriesIndex: book.seriesIndex === null ? "" : String(book.seriesIndex),
    description: book.description ?? "",
    publisher: book.publisher ?? "",
    language: book.language ?? "",
    publishedDate: book.publishedDate ?? "",
    isbn: book.isbn ?? "",
  };
}

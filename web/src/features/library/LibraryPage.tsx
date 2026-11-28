import { SearchX, Search } from "lucide-react";
import { useEffect, useMemo, useRef } from "react";
import { useSearchParams } from "react-router";
import { useBooks } from "../../api/queries";
import type { BookFormat, BookSummary } from "../../api/types";
import { BookCard } from "../../components/BookCard";
import { EmptyState } from "../../components/EmptyState";
import { useLocalStorage } from "../../hooks/useLocalStorage";
import { formatLabels, readingState, type ReadingState } from "../../lib/format";
import { BookMenu } from "./BookMenu";
import styles from "./LibraryPage.module.css";

type Sort = "title" | "author" | "added" | "read" | "series";
type Group = "none" | "series" | "author";
type Filter = "all" | ReadingState;

interface View {
  sort: Sort;
  group: Group;
  filter: Filter;
  format: "all" | BookFormat;
}

const collator = new Intl.Collator(undefined, { sensitivity: "base", numeric: true });
const fold = (value: string) => value.normalize("NFD").replace(/\p{Mn}/gu, "").toLowerCase();
const firstAuthor = (book: BookSummary) => book.authors[0]?.sortName ?? "￿";

const sorters: Record<Sort, (a: BookSummary, b: BookSummary) => number> = {
  title: (a, b) => collator.compare(a.sortTitle, b.sortTitle),
  author: (a, b) => collator.compare(firstAuthor(a), firstAuthor(b)) || bySeries(a, b),
  added: (a, b) => b.addedAt.localeCompare(a.addedAt),
  read: (a, b) => (b.progress?.updatedAt ?? "").localeCompare(a.progress?.updatedAt ?? "") || collator.compare(a.sortTitle, b.sortTitle),
  series: (a, b) => bySeries(a, b),
};

function bySeries(a: BookSummary, b: BookSummary) {
  const seriesA = a.series?.name ?? "￿";
  const seriesB = b.series?.name ?? "￿";
  return collator.compare(seriesA, seriesB) || (a.seriesIndex ?? 1e9) - (b.seriesIndex ?? 1e9) || collator.compare(a.sortTitle, b.sortTitle);
}

export function LibraryPage() {
  const { data: books = [], isLoading } = useBooks();
  const [params, setParams] = useSearchParams();
  const query = params.get("q") ?? "";
  const searchRef = useRef<HTMLInputElement>(null);
  const [view, setView] = useLocalStorage<View>("cr.libraryView", { sort: "title", group: "none", filter: "all", format: "all" });
  const formatsInLibrary = useMemo(() => [...new Set(books.map((b) => b.format))].sort(), [books]);
  // A remembered filter for a format the library no longer has would hide everything.
  const formatFilter = view.format !== "all" && formatsInLibrary.includes(view.format) ? view.format : "all";

  useEffect(() => {
    if (params.get("focus") === "search") searchRef.current?.focus();
  }, [params]);

  const filtered = useMemo(() => {
    const words = fold(query).split(/\s+/).filter(Boolean);
    return books
      .filter((book) => view.filter === "all" || readingState(book) === view.filter)
      .filter((book) => formatFilter === "all" || book.format === formatFilter)
      .filter((book) => {
        if (words.length === 0) return true;
        const haystack = fold([book.title, book.series?.name ?? "", ...book.authors.map((a) => a.name)].join(" "));
        return words.every((word) => haystack.includes(word));
      })
      .sort(sorters[view.group === "series" ? "series" : view.sort]);
  }, [books, query, view, formatFilter]);

  const groups = useMemo(() => {
    if (view.group === "none") return [{ key: "all", title: null as string | null, books: filtered }];
    const map = new Map<string, { key: string; title: string; sortKey: string; books: BookSummary[] }>();
    for (const book of filtered) {
      const key = view.group === "series" ? (book.series ? `s${book.series.id}` : "none") : book.authors[0] ? `a${book.authors[0].id}` : "none";
      const title = view.group === "series" ? (book.series?.name ?? "Standalone books") : (book.authors[0]?.name ?? "Unknown author");
      const sortKey = key === "none" ? "￿" : view.group === "series" ? title : firstAuthor(book);
      if (!map.has(key)) map.set(key, { key, title, sortKey, books: [] });
      map.get(key)!.books.push(book);
    }

    return [...map.values()].sort((a, b) => collator.compare(a.sortKey, b.sortKey));
  }, [filtered, view.group]);

  const setQuery = (value: string) => {
    const next = new URLSearchParams(params);
    next.delete("focus");
    if (value) next.set("q", value);
    else next.delete("q");
    setParams(next, { replace: true });
  };

  return (
    <div className="page">
      <div className={styles.header}>
        <h1 className="page-title">Library</h1>
        <span className="muted">
          {filtered.length === books.length ? `${books.length} books` : `${filtered.length} of ${books.length} books`}
        </span>
      </div>

      <div className={styles.toolbar}>
        <label className={styles.search}>
          <Search />
          <span className="visually-hidden">Search</span>
          <input ref={searchRef} className="input" type="search" placeholder="Search titles, authors, series…" value={query} onChange={(e) => setQuery(e.target.value)} />
        </label>
        <div className={styles.segmented} role="group" aria-label="Show">
          {(["all", "unread", "reading", "finished"] as Filter[]).map((filter) => (
            <button key={filter} type="button" aria-pressed={view.filter === filter} onClick={() => setView((v) => ({ ...v, filter }))}>
              {filter === "all" ? "All" : filter[0].toUpperCase() + filter.slice(1)}
            </button>
          ))}
        </div>
        <div className={styles.selects}>
          <label>
            Sort
            <select value={view.sort} onChange={(e) => setView((v) => ({ ...v, sort: e.target.value as Sort }))} disabled={view.group === "series"}>
              <option value="title">Title</option>
              <option value="author">Author</option>
              <option value="series">Series</option>
              <option value="added">Recently added</option>
              <option value="read">Recently read</option>
            </select>
          </label>
          {formatsInLibrary.length > 1 && (
            <label>
              Format
              <select value={formatFilter} onChange={(e) => setView((v) => ({ ...v, format: e.target.value as View["format"] }))}>
                <option value="all">All</option>
                {formatsInLibrary.map((format) => (
                  <option key={format} value={format}>
                    {formatLabels[format]}
                  </option>
                ))}
              </select>
            </label>
          )}
          <label>
            Group
            <select value={view.group} onChange={(e) => setView((v) => ({ ...v, group: e.target.value as Group }))}>
              <option value="none">None</option>
              <option value="series">Series</option>
              <option value="author">Author</option>
            </select>
          </label>
        </div>
      </div>

      {isLoading ? (
        <div className="center-fill">
          <div className="spinner" />
        </div>
      ) : filtered.length === 0 ? (
        <EmptyState icon={<SearchX />} title="No books match">
          {query ? `Nothing in your library matches “${query}”.` : "Try a different filter."}
        </EmptyState>
      ) : (
        <div className={styles.groups}>
          {groups.map((group) => (
            <section key={group.key} aria-label={group.title ?? "Books"}>
              {group.title && (
                <header className={styles.groupHeader}>
                  <h2 className={styles.groupTitle}>{group.title}</h2>
                  <span className="muted">{group.books.length}</span>
                </header>
              )}
              <div className={styles.grid}>
                {group.books.map((book) => (
                  <BookCard key={book.id} book={book} showSeriesIndex={view.group === "series"} menu={<BookMenu book={book} />} />
                ))}
              </div>
            </section>
          ))}
        </div>
      )}
    </div>
  );
}

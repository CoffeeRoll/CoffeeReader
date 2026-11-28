import { Search, X } from "lucide-react";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { api, ApiError } from "../../api/client";
import type { SearchHit, SearchResponse } from "../../api/types";
import { percent } from "../../lib/format";
import styles from "./ReaderPage.module.css";

export interface SearchState {
  query: string;
  response: SearchResponse | null;
  active: number;
}

interface Props {
  bookId: number;
  state: SearchState;
  onChange: (state: SearchState) => void;
  onSelect: (hit: SearchHit, index: number) => void;
  onClose: () => void;
}

export function SearchPanel({ bookId, state, onChange, onSelect, onClose }: Props) {
  const [draft, setDraft] = useState(state.query);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    inputRef.current?.focus();
  }, []);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    const query = draft.trim();
    if (query.length < 2) {
      setError("Type at least two characters.");
      return;
    }

    setBusy(true);
    setError(null);
    try {
      const response = await api.get<SearchResponse>(`/api/books/${bookId}/search?q=${encodeURIComponent(query)}`);
      onChange({ query, response, active: -1 });
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Search failed.");
    } finally {
      setBusy(false);
    }
  };

  const hits = state.response?.hits ?? [];

  return (
    <>
      <div className={styles.panelHeader}>
        <h2 className={styles.panelTitle}>Search this book</h2>
        <button type="button" className={styles.tool} onClick={onClose} aria-label="Close search">
          <X />
        </button>
      </div>
      <form className={styles.searchForm} onSubmit={submit} role="search">
        <input ref={inputRef} type="search" enterKeyHint="search" placeholder="Words or a phrase" value={draft} onChange={(e) => setDraft(e.target.value)} aria-label="Search text" />
        <button type="submit" className={styles.tool} aria-label="Search" disabled={busy}>
          <Search />
        </button>
      </form>
      {error && <p className={styles.searchSummary} style={{ color: "var(--danger)" }}>{error}</p>}
      {busy && <p className={styles.searchSummary}>Searching…</p>}
      {state.response && !busy && (
        <p className={styles.searchSummary}>
          {state.response.totalMatches === 0
            ? `No matches for “${state.response.query}”.`
            : `${state.response.totalMatches} ${state.response.totalMatches === 1 ? "match" : "matches"}${state.response.truncated ? ` (showing the first ${hits.length})` : ""}`}
        </p>
      )}
      <div className={styles.panelBody}>
        {hits.map((hit, index) => {
          const newGroup = index === 0 || hits[index - 1].chapterIndex !== hit.chapterIndex;
          return (
            <div key={`${hit.chapterIndex}-${hit.occurrence}`}>
              {newGroup && (
                <div className={styles.hitGroup}>
                  {hit.chapterTitle ?? `Section ${hit.chapterIndex + 1}`} · {percent(hit.percentage)}
                </div>
              )}
              <button type="button" className={styles.hit} aria-current={index === state.active} onClick={() => onSelect(hit, index)}>
                {hit.before}
                <mark>{hit.match}</mark>
                {hit.after}
              </button>
            </div>
          );
        })}
      </div>
    </>
  );
}

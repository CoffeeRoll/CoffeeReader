import { BookOpen, Info, MoreHorizontal, Plus, Bookmark } from "lucide-react";
import { useState } from "react";
import { useNavigate } from "react-router";
import { useShelves } from "../../api/queries";
import type { BookSummary } from "../../api/types";
import { bookCardStyles } from "../../components/BookCard";
import { Menu, MenuItem, MenuLabel, MenuSeparator } from "../../components/Menu";
import { useShelfActions } from "./shelfActions";
import { ShelfNameDialog } from "./ShelfNameDialog";

/** The "⋯" menu on a book card: open it, or put it on shelves. */
export function BookMenu({ book }: { book: BookSummary }) {
  const navigate = useNavigate();
  const shelves = useShelves().data ?? [];
  const actions = useShelfActions();
  const [creating, setCreating] = useState(false);
  const started = book.progress && !book.progress.isFinished;

  return (
    <>
      <Menu
        align="start"
        trigger={(props) => (
          <button
            type="button"
            className={bookCardStyles.menuButton}
            aria-label={`Options for ${book.title}`}
            onPointerDown={(e) => e.stopPropagation()}
            onMouseDown={(e) => e.stopPropagation()}
            onTouchStart={(e) => e.stopPropagation()}
            {...props}
          >
            <MoreHorizontal />
          </button>
        )}
      >
        {(close) => (
          <>
            <MenuItem icon={<BookOpen />} onSelect={() => (close(), navigate(`/read/${book.id}`))}>
              {started ? "Continue reading" : "Start reading"}
            </MenuItem>
            <MenuItem icon={<Info />} onSelect={() => (close(), navigate(`/books/${book.id}`))}>
              Details
            </MenuItem>
            <MenuSeparator />
            <MenuLabel>Shelves</MenuLabel>
            {shelves.map((shelf) => {
              const on = shelf.bookIds.includes(book.id);
              return (
                <MenuItem key={shelf.id} icon={<Bookmark />} checked={on} onSelect={() => actions.toggle(shelf.id, book.id, !on)}>
                  {shelf.name}
                </MenuItem>
              );
            })}
            <MenuItem icon={<Plus />} onSelect={() => (close(), setCreating(true))}>
              New shelf…
            </MenuItem>
          </>
        )}
      </Menu>
      <ShelfNameDialog open={creating} title="New shelf" submitLabel="Create shelf" onClose={() => setCreating(false)} onSubmit={(name) => actions.create(name, [book.id])} />
    </>
  );
}

import {
  closestCenter,
  DndContext,
  DragOverlay,
  KeyboardSensor,
  MeasuringStrategy,
  MouseSensor,
  pointerWithin,
  rectIntersection,
  TouchSensor,
  useDroppable,
  useSensor,
  useSensors,
  type CollisionDetection,
  type DragEndEvent,
  type DragOverEvent,
  type DragStartEvent,
  type UniqueIdentifier,
} from "@dnd-kit/core";
import { arrayMove, rectSortingStrategy, SortableContext, sortableKeyboardCoordinates, useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { ChevronDown, ListOrdered, MoreHorizontal, Pencil, Plus, Trash2 } from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { BookSummary, Shelf } from "../../api/types";
import { BookCard } from "../../components/BookCard";
import { Menu, MenuItem } from "../../components/Menu";
import { useLocalStorage } from "../../hooks/useLocalStorage";
import { ArrangeShelvesDialog } from "./ArrangeShelvesDialog";
import { BookMenu } from "./BookMenu";
import { useShelfActions } from "./shelfActions";
import { ShelfNameDialog } from "./ShelfNameDialog";
import styles from "./ShelfBoard.module.css";

const UNSHELVED = "unshelved";
const UNSHELVED_PREVIEW = 30;

type ContainerId = string;
type Board = Record<ContainerId, string[]>;

const containerFor = (shelfId: number): ContainerId => `shelf:${shelfId}`;
const shelfIdOf = (container: ContainerId): number | null => (container === UNSHELVED ? null : Number(container.slice(6)));
const itemId = (container: ContainerId, bookId: number) => `${container}|${bookId}`;
const bookIdOf = (id: UniqueIdentifier) => Number(String(id).split("|")[1]);

function buildBoard(shelves: Shelf[], unshelved: BookSummary[]): Board {
  const board: Board = {};
  for (const shelf of shelves) {
    const container = containerFor(shelf.id);
    board[container] = shelf.bookIds.map((id) => itemId(container, id));
  }

  board[UNSHELVED] = unshelved.map((book) => itemId(UNSHELVED, book.id));
  return board;
}

interface Props {
  books: BookSummary[];
  shelves: Shelf[];
}

/**
 * Every shelf as a section of covers. Drag books to reorder them, move them between shelves,
 * or drop them on "Not on a shelf" to take them off.
 */
export function ShelfBoard({ books, shelves }: Props) {
  const actions = useShelfActions();
  const byId = useMemo(() => new Map(books.map((b) => [b.id, b])), [books]);
  const unshelved = useMemo(() => {
    const shelved = new Set(shelves.flatMap((s) => s.bookIds));
    return books.filter((b) => !shelved.has(b.id));
  }, [books, shelves]);

  const serverBoard = useMemo(() => buildBoard(shelves, unshelved), [shelves, unshelved]);
  const [draft, setDraft] = useState<Board | null>(null);
  const [active, setActive] = useState<{ id: string; origin: ContainerId; width: number } | null>(null);
  const [overContainer, setOverContainer] = useState<ContainerId | null>(null);
  const [collapsed, setCollapsed] = useLocalStorage<Record<string, boolean>>("cr.collapsedShelves", {});
  const [showAllUnshelved, setShowAllUnshelved] = useState(false);
  const [creating, setCreating] = useState(false);
  const [arranging, setArranging] = useState(false);
  const [renaming, setRenaming] = useState<Shelf | null>(null);
  const suppressClick = useRef(false);
  const board = draft ?? serverBoard;
  const boardRef = useRef(board);
  boardRef.current = board;
  const lastOverId = useRef<UniqueIdentifier | null>(null);
  const recentlyMovedContainer = useRef(false);

  // Moving a book into another shelf reflows the page; give the layout a frame to settle before
  // trusting collisions again, otherwise the book can ping-pong between shelves.
  useEffect(() => {
    const frame = requestAnimationFrame(() => (recentlyMovedContainer.current = false));
    return () => cancelAnimationFrame(frame);
  }, [draft]);

  /** The book under the pointer, or the nearest book in the shelf the pointer is over. */
  const collisionDetection = useCallback<CollisionDetection>((args) => {
    const within = pointerWithin(args);
    const hits = within.length > 0 ? within : rectIntersection(args);
    let overId = hits.find((hit) => hit.data?.droppableContainer?.data?.current?.type === "book")?.id ?? hits[0]?.id ?? null;

    if (overId !== null) {
      const items = boardRef.current[String(overId)];
      if (items && items.length > 0 && hits.every((hit) => hit.data?.droppableContainer?.data?.current?.type !== "book")) {
        const nearest = closestCenter({
          ...args,
          droppableContainers: args.droppableContainers.filter((c) => c.id !== overId && items.includes(String(c.id))),
        });
        overId = nearest[0]?.id ?? overId;
      }

      lastOverId.current = overId;
      return [{ id: overId }];
    }

    if (recentlyMovedContainer.current) lastOverId.current = args.active.id;
    return lastOverId.current !== null ? [{ id: lastOverId.current }] : [];
  }, []);

  const sensors = useSensors(
    useSensor(MouseSensor, { activationConstraint: { distance: 6 } }),
    // Long-press to pick up a book on touch screens, so scrolling still works.
    useSensor(TouchSensor, { activationConstraint: { delay: 280, tolerance: 8 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );

  const findContainer = (id: UniqueIdentifier, state: Board): ContainerId | undefined => {
    if (String(id) in state) return String(id);
    return Object.keys(state).find((key) => state[key].includes(String(id)));
  };

  const onDragStart = ({ active: dragged }: DragStartEvent) => {
    const origin = findContainer(dragged.id, serverBoard);
    if (!origin) return;
    setDraft(serverBoard);
    setActive({ id: String(dragged.id), origin, width: dragged.rect.current.initial?.width ?? 140 });
    navigator.vibrate?.(10);
  };

  const onDragOver = ({ active: dragged, over }: DragOverEvent) => {
    if (!over || !draft) return;
    const from = findContainer(dragged.id, draft);
    const to = findContainer(over.id, draft);
    setOverContainer(to ?? null);
    if (!from || !to || from === to) return;

    // A shelf can hold a book only once.
    const bookId = bookIdOf(dragged.id);
    if (to !== UNSHELVED && draft[to].some((id) => bookIdOf(id) === bookId)) return;

    const translated = dragged.rect.current.translated;
    const afterOver = translated ? translated.left + translated.width / 2 > over.rect.left + over.rect.width / 2 : false;
    recentlyMovedContainer.current = true;
    setDraft((current) => {
      if (!current) return current;
      const source = current[from].filter((id) => id !== dragged.id);
      const target = current[to].filter((id) => id !== dragged.id);
      const overIndex = target.indexOf(String(over.id));
      const insertAt = overIndex >= 0 ? overIndex + (afterOver ? 1 : 0) : target.length;
      target.splice(insertAt, 0, String(dragged.id));
      return { ...current, [from]: source, [to]: target };
    });
  };

  const onDragEnd = ({ active: dragged, over }: DragEndEvent) => {
    const state = draft;
    const origin = active?.origin;
    lastOverId.current = null;
    setActive(null);
    setOverContainer(null);
    suppressClick.current = true;
    window.setTimeout(() => (suppressClick.current = false), 50);

    if (!state || !origin || !over) {
      setDraft(null);
      return;
    }

    const final = findContainer(dragged.id, state);
    if (!final) {
      setDraft(null);
      return;
    }

    let items = state[final];
    const overIndex = items.indexOf(String(over.id));
    const activeIndex = items.indexOf(String(dragged.id));
    if (overIndex >= 0 && activeIndex >= 0 && overIndex !== activeIndex) {
      items = arrayMove(items, activeIndex, overIndex);
    }

    const bookId = bookIdOf(dragged.id);
    const position = items.indexOf(String(dragged.id));
    if (final === origin) {
      const shelfId = shelfIdOf(origin);
      const changed = items.join() !== serverBoard[origin].join();
      if (shelfId !== null && changed) {
        actions.arrange(shelfId, items.map(bookIdOf));
      }
    } else {
      actions.move({
        bookId,
        fromShelfId: shelfIdOf(origin),
        toShelfId: shelfIdOf(final),
        position: final === UNSHELVED ? null : position,
      });
    }

    setDraft(null);
  };

  const onDragCancel = () => {
    lastOverId.current = null;
    setDraft(null);
    setActive(null);
    setOverContainer(null);
  };

  const activeBook = active ? byId.get(bookIdOf(active.id)) : undefined;
  const sections: { container: ContainerId; shelf: Shelf | null }[] = [
    ...shelves.map((shelf) => ({ container: containerFor(shelf.id), shelf })),
    { container: UNSHELVED, shelf: null },
  ];

  return (
    <>
      <div className={styles.toolbar}>
        <h2 className={styles.heading}>Shelves</h2>
        <div className={styles.toolbarActions}>
          {shelves.length > 1 && (
            <button type="button" className="btn btn-sm btn-ghost" onClick={() => setArranging(true)}>
              <ListOrdered /> Arrange
            </button>
          )}
          <button type="button" className="btn btn-sm" onClick={() => setCreating(true)}>
            <Plus /> New shelf
          </button>
        </div>
      </div>

      {shelves.length === 0 && (
        <div className={styles.hint}>
          <p>Group books however you like — genres, a reading list, a book club. Create a shelf, then drag covers onto it. Order within a shelf is yours to set too.</p>
          <button type="button" className="btn btn-primary btn-sm" onClick={() => setCreating(true)}>
            <Plus /> Create your first shelf
          </button>
        </div>
      )}

      <DndContext
        sensors={sensors}
        collisionDetection={collisionDetection}
        measuring={{ droppable: { strategy: MeasuringStrategy.Always } }}
        onDragStart={onDragStart}
        onDragOver={onDragOver}
        onDragEnd={onDragEnd}
        onDragCancel={onDragCancel}
      >
        <div
          className={styles.sections}
          onClickCapture={(event) => {
            if (suppressClick.current) {
              event.preventDefault();
              event.stopPropagation();
            }
          }}
        >
          {sections.map(({ container, shelf }) => {
            const ids = board[container] ?? [];
            const isUnshelved = shelf === null;
            if (isUnshelved && ids.length === 0 && !active) return null;
            const visible = isUnshelved && !showAllUnshelved && !active ? ids.slice(0, UNSHELVED_PREVIEW) : ids;
            return (
              <ShelfSection
                key={container}
                container={container}
                name={shelf?.name ?? "Not on a shelf"}
                count={ids.length}
                collapsed={!!collapsed[container] && !(active && overContainer === container)}
                highlighted={!!active && overContainer === container && active.origin !== container}
                onToggle={() => setCollapsed((c) => ({ ...c, [container]: !c[container] }))}
                menu={
                  shelf && (
                    <Menu
                      trigger={(props) => (
                        <button type="button" className="icon-btn" aria-label={`Options for ${shelf.name}`} {...props}>
                          <MoreHorizontal />
                        </button>
                      )}
                    >
                      {(close) => (
                        <>
                          <MenuItem icon={<Pencil />} onSelect={() => (close(), setRenaming(shelf))}>
                            Rename
                          </MenuItem>
                          <MenuItem icon={<ListOrdered />} onSelect={() => (close(), setArranging(true))}>
                            Arrange shelves
                          </MenuItem>
                          <MenuItem
                            icon={<Trash2 />}
                            danger
                            onSelect={() => {
                              close();
                              if (window.confirm(`Delete the shelf “${shelf.name}”? The books stay in your library.`)) actions.remove(shelf.id);
                            }}
                          >
                            Delete shelf
                          </MenuItem>
                        </>
                      )}
                    </Menu>
                  )
                }
              >
                <SortableContext items={visible} strategy={rectSortingStrategy}>
                  {visible.length > 0 ? (
                    <div className={styles.grid}>
                      {visible.map((id) => {
                        const book = byId.get(bookIdOf(id));
                        return book ? <SortableBook key={id} id={id} container={container} book={book} /> : null;
                      })}
                    </div>
                  ) : (
                    <div className={styles.emptyDrop}>{isUnshelved ? "Drop here to take a book off its shelf" : "Drag books here"}</div>
                  )}
                </SortableContext>
                {isUnshelved && !showAllUnshelved && !active && ids.length > UNSHELVED_PREVIEW && (
                  <div className={styles.more}>
                    <button type="button" className="btn btn-sm" onClick={() => setShowAllUnshelved(true)}>
                      Show all {ids.length}
                    </button>
                  </div>
                )}
              </ShelfSection>
            );
          })}
        </div>

        <DragOverlay dropAnimation={{ duration: 180, easing: "cubic-bezier(0.2, 0.8, 0.2, 1)" }}>
          {activeBook && active ? (
            <div className={styles.overlay} style={{ ["--overlay-width" as string]: `${active.width}px` }}>
              <BookCard book={activeBook} inert />
            </div>
          ) : null}
        </DragOverlay>
      </DndContext>

      <ShelfNameDialog open={creating} title="New shelf" submitLabel="Create shelf" onClose={() => setCreating(false)} onSubmit={(name) => actions.create(name)} />
      <ShelfNameDialog
        open={renaming !== null}
        title="Rename shelf"
        submitLabel="Save"
        initialName={renaming?.name}
        onClose={() => setRenaming(null)}
        onSubmit={(name) => renaming && actions.rename(renaming.id, name)}
      />
      <ArrangeShelvesDialog open={arranging} shelves={shelves} onClose={() => setArranging(false)} onSave={actions.reorder} />
    </>
  );
}

interface SectionProps {
  container: ContainerId;
  name: string;
  count: number;
  collapsed: boolean;
  highlighted: boolean;
  onToggle: () => void;
  menu?: React.ReactNode;
  children: React.ReactNode;
}

function ShelfSection({ container, name, count, collapsed, highlighted, onToggle, menu, children }: SectionProps) {
  const { setNodeRef } = useDroppable({ id: container, data: { type: "container" } });
  return (
    <section ref={setNodeRef} className={`${styles.section} ${collapsed ? styles.collapsed : ""} ${highlighted ? styles.sectionOver : ""}`} aria-label={name}>
      <header className={styles.sectionHeader}>
        <button type="button" className={styles.collapse} onClick={onToggle} aria-expanded={!collapsed}>
          <ChevronDown className={styles.chevron} />
          <span className={styles.sectionName}>{name}</span>
          <span className={styles.count}>{count}</span>
        </button>
        {menu}
      </header>
      {children}
    </section>
  );
}

function SortableBook({ id, container, book }: { id: string; container: ContainerId; book: BookSummary }) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id, data: { type: "book", container } });
  return (
    <div
      ref={setNodeRef}
      className={`${styles.sortable} ${isDragging ? styles.dragging : ""}`}
      style={{ transform: CSS.Translate.toString(transform), transition }}
      {...attributes}
      {...listeners}
      aria-roledescription="draggable book"
    >
      <BookCard book={book} menu={<BookMenu book={book} />} />
    </div>
  );
}

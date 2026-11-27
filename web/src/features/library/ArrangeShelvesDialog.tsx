import { closestCenter, DndContext, KeyboardSensor, PointerSensor, useSensor, useSensors, type DragEndEvent } from "@dnd-kit/core";
import { arrayMove, SortableContext, sortableKeyboardCoordinates, useSortable, verticalListSortingStrategy } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { ArrowDown, ArrowUp, GripVertical } from "lucide-react";
import { useEffect, useState } from "react";
import type { Shelf } from "../../api/types";
import { Dialog } from "../../components/Dialog";
import styles from "./ShelfBoard.module.css";

interface Props {
  open: boolean;
  shelves: Shelf[];
  onClose: () => void;
  onSave: (shelfIds: number[]) => void;
}

/** Reorder shelves as a compact list — easier than dragging whole sections around. */
export function ArrangeShelvesDialog({ open, shelves, onClose, onSave }: Props) {
  const [order, setOrder] = useState<Shelf[]>(shelves);
  useEffect(() => {
    if (open) setOrder(shelves);
  }, [open, shelves]);

  const sensors = useSensors(useSensor(PointerSensor), useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }));

  const onDragEnd = ({ active, over }: DragEndEvent) => {
    if (!over || active.id === over.id) return;
    setOrder((current) => {
      const from = current.findIndex((s) => s.id === active.id);
      const to = current.findIndex((s) => s.id === over.id);
      return arrayMove(current, from, to);
    });
  };

  const nudge = (index: number, delta: number) =>
    setOrder((current) => {
      const target = index + delta;
      return target < 0 || target >= current.length ? current : arrayMove(current, index, target);
    });

  return (
    <Dialog
      open={open}
      title="Arrange shelves"
      onClose={onClose}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose}>
            Cancel
          </button>
          <button
            type="button"
            className="btn btn-primary"
            onClick={() => {
              onSave(order.map((s) => s.id));
              onClose();
            }}
          >
            Save order
          </button>
        </>
      }
    >
      <p className="muted" style={{ marginBottom: 14, fontSize: 14 }}>
        Drag the handles, or use the arrows. Shelves appear on your home page in this order.
      </p>
      <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={onDragEnd}>
        <SortableContext items={order.map((s) => s.id)} strategy={verticalListSortingStrategy}>
          <ul className={styles.arrangeList}>
            {order.map((shelf, index) => (
              <ArrangeRow key={shelf.id} shelf={shelf} first={index === 0} last={index === order.length - 1} onUp={() => nudge(index, -1)} onDown={() => nudge(index, 1)} />
            ))}
          </ul>
        </SortableContext>
      </DndContext>
    </Dialog>
  );
}

function ArrangeRow({ shelf, first, last, onUp, onDown }: { shelf: Shelf; first: boolean; last: boolean; onUp: () => void; onDown: () => void }) {
  const { attributes, listeners, setNodeRef, setActivatorNodeRef, transform, transition, isDragging } = useSortable({ id: shelf.id });
  return (
    <li ref={setNodeRef} className={styles.arrangeItem} style={{ transform: CSS.Translate.toString(transform), transition, zIndex: isDragging ? 2 : undefined, boxShadow: isDragging ? "var(--shadow-md)" : undefined }}>
      <button type="button" ref={setActivatorNodeRef} className={styles.handle} aria-label={`Drag ${shelf.name}`} {...attributes} {...listeners}>
        <GripVertical />
      </button>
      <span>{shelf.name}</span>
      <span className="chip" style={{ flex: "none" }}>
        {shelf.bookIds.length}
      </span>
      <button type="button" className="icon-btn" onClick={onUp} disabled={first} aria-label={`Move ${shelf.name} up`}>
        <ArrowUp />
      </button>
      <button type="button" className="icon-btn" onClick={onDown} disabled={last} aria-label={`Move ${shelf.name} down`}>
        <ArrowDown />
      </button>
    </li>
  );
}

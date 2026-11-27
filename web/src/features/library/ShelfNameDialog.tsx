import { useEffect, useState, type FormEvent } from "react";
import { Dialog } from "../../components/Dialog";

interface Props {
  open: boolean;
  title: string;
  initialName?: string;
  submitLabel: string;
  onClose: () => void;
  onSubmit: (name: string) => void;
}

export function ShelfNameDialog({ open, title, initialName = "", submitLabel, onClose, onSubmit }: Props) {
  const [name, setName] = useState(initialName);
  useEffect(() => {
    if (open) setName(initialName);
  }, [open, initialName]);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (name.trim()) {
      onSubmit(name.trim());
      onClose();
    }
  };

  return (
    <Dialog open={open} title={title} onClose={onClose}>
      <form onSubmit={submit} style={{ display: "grid", gap: 16 }}>
        <label className="field">
          <span>Shelf name</span>
          <input className="input" value={name} maxLength={100} onChange={(e) => setName(e.target.value)} placeholder="e.g. Fantasy, To read, Book club" autoFocus />
        </label>
        <div style={{ display: "flex", justifyContent: "flex-end", gap: 10 }}>
          <button type="button" className="btn" onClick={onClose}>
            Cancel
          </button>
          <button className="btn btn-primary" disabled={!name.trim()}>
            {submitLabel}
          </button>
        </div>
      </form>
    </Dialog>
  );
}

import { Check } from "lucide-react";
import { useCallback, useEffect, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";
import styles from "./Menu.module.css";

interface MenuProps {
  trigger: (props: { onClick: (event: React.MouseEvent) => void; "aria-expanded": boolean; "aria-haspopup": "menu" }) => ReactNode;
  children: (close: () => void) => ReactNode;
  align?: "start" | "end";
}

/** A small popover menu anchored to its trigger. */
export function Menu({ trigger, children, align = "end" }: MenuProps) {
  const [anchor, setAnchor] = useState<DOMRect | null>(null);
  const [position, setPosition] = useState<{ top: number; left: number } | null>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const close = useCallback(() => setAnchor(null), []);

  useLayoutEffect(() => {
    if (!anchor || !menuRef.current) {
      setPosition(null);
      return;
    }

    const menu = menuRef.current.getBoundingClientRect();
    const margin = 12;
    let left = align === "end" ? anchor.right - menu.width : anchor.left;
    left = Math.max(margin, Math.min(left, window.innerWidth - menu.width - margin));
    let top = anchor.bottom + 6;
    if (top + menu.height > window.innerHeight - margin) {
      top = Math.max(margin, anchor.top - menu.height - 6);
    }

    setPosition({ top, left });
  }, [anchor, align]);

  useEffect(() => {
    if (!anchor) return;
    const onPointer = (event: PointerEvent) => {
      if (!menuRef.current?.contains(event.target as Node)) close();
    };
    const onKey = (event: KeyboardEvent) => event.key === "Escape" && close();
    const onScroll = () => close();
    window.addEventListener("pointerdown", onPointer, true);
    window.addEventListener("keydown", onKey);
    window.addEventListener("resize", onScroll);
    return () => {
      window.removeEventListener("pointerdown", onPointer, true);
      window.removeEventListener("keydown", onKey);
      window.removeEventListener("resize", onScroll);
    };
  }, [anchor, close]);

  useEffect(() => {
    if (position) menuRef.current?.querySelector<HTMLElement>("button, a")?.focus();
  }, [position]);

  return (
    <>
      {trigger({
        onClick: (event) => {
          event.preventDefault();
          event.stopPropagation();
          setAnchor((current) => (current ? null : (event.currentTarget as HTMLElement).getBoundingClientRect()));
        },
        "aria-expanded": anchor !== null,
        "aria-haspopup": "menu",
      })}
      {anchor &&
        createPortal(
          <div
            ref={menuRef}
            role="menu"
            className={styles.menu}
            style={position ? { top: position.top, left: position.left } : { visibility: "hidden", top: 0, left: 0 }}
            onClick={(event) => event.stopPropagation()}
          >
            {children(close)}
          </div>,
          document.body,
        )}
    </>
  );
}

interface ItemProps {
  icon?: ReactNode;
  children: ReactNode;
  onSelect: () => void;
  danger?: boolean;
  checked?: boolean;
}

export function MenuItem({ icon, children, onSelect, danger, checked }: ItemProps) {
  return (
    <button type="button" role={checked === undefined ? "menuitem" : "menuitemcheckbox"} aria-checked={checked} className={`${styles.item} ${danger ? styles.danger : ""}`} onClick={onSelect}>
      {icon}
      <span>{children}</span>
      {checked && <Check className={styles.check} />}
    </button>
  );
}

export const MenuLabel = ({ children }: { children: ReactNode }) => <div className={styles.label}>{children}</div>;
export const MenuSeparator = () => <div className={styles.separator} role="separator" />;

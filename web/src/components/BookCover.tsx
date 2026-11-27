import { useState } from "react";
import { coverUrl } from "../api/client";
import type { BookSummary } from "../api/types";
import { placeholderPalette } from "../lib/color";
import { authorsText } from "../lib/format";
import styles from "./BookCover.module.css";

interface Props {
  book: Pick<BookSummary, "id" | "title" | "authors" | "hasCover" | "coverVersion">;
  eager?: boolean;
  className?: string;
}

export function BookCover({ book, eager, className }: Props) {
  const [loaded, setLoaded] = useState(false);
  const [failed, setFailed] = useState(false);
  const showImage = book.hasCover && !failed;
  const [from, to] = placeholderPalette(book.title);

  return (
    <div className={`${styles.cover} ${className ?? ""}`}>
      {(!showImage || !loaded) && (
        <div className={styles.placeholder} style={{ background: `linear-gradient(155deg, ${to}, ${from})` }} aria-hidden="true">
          <div>
            <div className={styles.placeholderTitle}>{book.title}</div>
            <div className={styles.rule} />
          </div>
          <div className={styles.placeholderAuthor}>{authorsText(book.authors, "")}</div>
        </div>
      )}
      {showImage && (
        <img
          className={`${styles.image} ${loaded ? styles.loaded : ""}`}
          src={coverUrl(book)}
          alt=""
          loading={eager ? "eager" : "lazy"}
          decoding="async"
          draggable={false}
          onLoad={() => setLoaded(true)}
          onError={() => setFailed(true)}
        />
      )}
    </div>
  );
}

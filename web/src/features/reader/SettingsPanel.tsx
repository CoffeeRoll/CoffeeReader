import { fonts, marginLabels, themes, type ReaderSettings } from "./readerSettings";
import styles from "./ReaderPage.module.css";

interface Props {
  settings: ReaderSettings;
  onChange: (settings: ReaderSettings) => void;
  wide: boolean;
}

export function SettingsPanel({ settings, onChange, wide }: Props) {
  const set = <K extends keyof ReaderSettings>(key: K, value: ReaderSettings[K]) => onChange({ ...settings, [key]: value });

  return (
    <div className={styles.settings} role="dialog" aria-label="Reading settings" onPointerDown={(e) => e.stopPropagation()}>
      <div className={styles.settingGroup}>
        <span className={styles.settingLabel}>Theme</span>
        <div className={styles.swatches}>
          {(Object.keys(themes) as (keyof typeof themes)[]).map((key) => (
            <button
              key={key}
              type="button"
              className={styles.swatch}
              style={{ background: themes[key].page, color: themes[key].text }}
              aria-pressed={settings.theme === key}
              onClick={() => set("theme", key)}
            >
              <strong>Aa</strong>
              {themes[key].label}
            </button>
          ))}
        </div>
      </div>

      <div className={styles.settingGroup}>
        <span className={styles.settingLabel}>Font</span>
        <div className={styles.fontGrid}>
          {(Object.keys(fonts) as (keyof typeof fonts)[]).map((key) => (
            <button key={key} type="button" className={styles.option} style={{ fontFamily: fonts[key].sample }} aria-pressed={settings.font === key} onClick={() => set("font", key)}>
              {fonts[key].label}
            </button>
          ))}
        </div>
      </div>

      <div className={styles.settingGroup}>
        <span className={styles.settingLabel}>Text size</span>
        <div className={styles.stepper}>
          <button type="button" onClick={() => set("fontSize", Math.max(12, settings.fontSize - 1))} aria-label="Smaller text" style={{ fontSize: 15 }}>
            A
          </button>
          <output>{settings.fontSize}px</output>
          <button type="button" onClick={() => set("fontSize", Math.min(36, settings.fontSize + 1))} aria-label="Larger text" style={{ fontSize: 22 }}>
            A
          </button>
        </div>
      </div>

      <div className={styles.settingGroup}>
        <span className={styles.settingLabel}>
          Line spacing <span>{settings.lineHeight.toFixed(2)}</span>
        </span>
        <input className={styles.range} type="range" min={1.2} max={2.2} step={0.05} value={settings.lineHeight} onChange={(e) => set("lineHeight", Number(e.target.value))} aria-label="Line spacing" />
      </div>

      <div className={styles.settingGroup}>
        <span className={styles.settingLabel}>Margins</span>
        <div className={styles.segmented}>
          {marginLabels.map((label, index) => (
            <button key={label} type="button" aria-pressed={settings.margin === index} onClick={() => set("margin", index as ReaderSettings["margin"])}>
              {label}
            </button>
          ))}
        </div>
      </div>

      <div className={styles.settingGroup}>
        <span className={styles.settingLabel}>Alignment</span>
        <div className={styles.segmented}>
          {([
            ["justify", "Justified"],
            ["left", "Left"],
            ["publisher", "Book's"],
          ] as const).map(([value, label]) => (
            <button key={value} type="button" aria-pressed={settings.align === value} onClick={() => set("align", value)}>
              {label}
            </button>
          ))}
        </div>
      </div>

      {wide && (
        <div className={styles.settingGroup}>
          <span className={styles.settingLabel}>Pages</span>
          <div className={styles.segmented}>
            <button type="button" aria-pressed={settings.layout === "auto"} onClick={() => set("layout", "auto")}>
              Two pages when wide
            </button>
            <button type="button" aria-pressed={settings.layout === "single"} onClick={() => set("layout", "single")}>
              Single page
            </button>
          </div>
        </div>
      )}

      <div className={styles.settingGroup}>
        {wide && (
          <label className={styles.toggleRow}>
            Keep contents open beside the page
            <input type="checkbox" checked={settings.tocPinned} onChange={(e) => set("tocPinned", e.target.checked)} />
          </label>
        )}
        <label className={styles.toggleRow}>
          Animate page turns
          <input type="checkbox" checked={settings.animate} onChange={(e) => set("animate", e.target.checked)} />
        </label>
      </div>
    </div>
  );
}

import { useQueryClient } from "@tanstack/react-query";
import { House, LibraryBig, LogOut, Moon, Plus, Search, Settings, Sun, SunMoon } from "lucide-react";
import { useEffect, useState } from "react";
import { Link, NavLink, Outlet, useLocation, useNavigate, useSearchParams } from "react-router";
import { api } from "../api/client";
import { queryKeys, useCurrentUser } from "../api/queries";
import { useUploads } from "../features/upload/UploadProvider";
import { getAppTheme, setAppTheme, type AppTheme } from "../lib/theme";
import { Menu, MenuItem, MenuLabel, MenuSeparator } from "./Menu";
import styles from "./AppShell.module.css";

export function AppShell() {
  const user = useCurrentUser();
  const uploads = useUploads();
  const navigate = useNavigate();
  const location = useLocation();
  const [params] = useSearchParams();
  const client = useQueryClient();
  const [query, setQuery] = useState(params.get("q") ?? "");
  const [theme, setTheme] = useState<AppTheme>(getAppTheme);

  // Keep the header search in step with the library page's URL.
  useEffect(() => {
    if (location.pathname === "/library") setQuery(params.get("q") ?? "");
  }, [location.pathname, params]);

  const search = (value: string) => {
    setQuery(value);
    const next = new URLSearchParams(location.pathname === "/library" ? params : undefined);
    if (value) next.set("q", value);
    else next.delete("q");
    navigate({ pathname: "/library", search: next.toString() }, { replace: location.pathname === "/library" });
  };

  const signOut = async () => {
    await api.post("/api/auth/logout");
    client.clear();
    await client.invalidateQueries({ queryKey: queryKeys.auth });
    navigate("/login");
  };

  const chooseTheme = (value: AppTheme) => {
    setAppTheme(value);
    setTheme(value);
  };

  return (
    <div className={styles.shell}>
      <header className={styles.header}>
        <div className={styles.headerInner}>
          <Link to="/" className={styles.brand}>
            <img src="/favicon.svg" alt="" />
            <span>CoffeeReader</span>
          </Link>

          <nav className={styles.nav} aria-label="Main">
            <NavLink to="/" end className={styles.navLink}>
              Home
            </NavLink>
            <NavLink to="/library" className={styles.navLink}>
              Library
            </NavLink>
          </nav>

          <label className={styles.search}>
            <Search />
            <span className="visually-hidden">Search your library</span>
            <input type="search" placeholder="Search titles, authors, series…" value={query} onChange={(e) => search(e.target.value)} />
          </label>

          <div className={styles.actions}>
            <Link to="/library?focus=search" className={`icon-btn ${styles.mobileOnly}`} aria-label="Search">
              <Search />
            </Link>
            <button type="button" className={`btn btn-primary ${styles.uploadButton}`} onClick={uploads.openPicker} aria-label="Add books">
              <Plus />
              <span className={styles.uploadLabel}>Add books</span>
            </button>
            <Menu
              trigger={(props) => (
                <button type="button" className={styles.avatar} aria-label="Account menu" {...props}>
                  {(user.displayName || user.username).slice(0, 1).toUpperCase()}
                </button>
              )}
            >
              {(close) => (
                <>
                  <MenuLabel>{user.displayName}</MenuLabel>
                  <MenuItem icon={<Settings />} onSelect={() => (close(), navigate("/settings"))}>
                    Settings{user.role === "admin" ? " & admin" : ""}
                  </MenuItem>
                  <MenuSeparator />
                  <MenuLabel>Appearance</MenuLabel>
                  <MenuItem icon={<SunMoon />} checked={theme === "system"} onSelect={() => chooseTheme("system")}>
                    Match system
                  </MenuItem>
                  <MenuItem icon={<Sun />} checked={theme === "light"} onSelect={() => chooseTheme("light")}>
                    Light
                  </MenuItem>
                  <MenuItem icon={<Moon />} checked={theme === "dark"} onSelect={() => chooseTheme("dark")}>
                    Dark
                  </MenuItem>
                  <MenuSeparator />
                  <MenuItem icon={<LogOut />} onSelect={signOut}>
                    Sign out
                  </MenuItem>
                </>
              )}
            </Menu>
          </div>
        </div>
      </header>

      <main>
        <Outlet />
      </main>

      <nav className={styles.tabbar} aria-label="Main">
        <NavLink to="/" end className={styles.tab}>
          <House />
          Home
        </NavLink>
        <NavLink to="/library" className={styles.tab}>
          <LibraryBig />
          Library
        </NavLink>
        <NavLink to="/settings" className={styles.tab}>
          <Settings />
          Settings
        </NavLink>
      </nav>
    </div>
  );
}


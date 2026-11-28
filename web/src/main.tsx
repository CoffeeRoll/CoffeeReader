import "@fontsource-variable/inter";
import "@fontsource-variable/literata";
import "@fontsource-variable/literata/wght-italic.css";
import "./styles/tokens.css";
import "./styles/global.css";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import { applyAppTheme } from "./lib/theme";

applyAppTheme();

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);

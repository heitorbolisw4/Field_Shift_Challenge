import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { criarApi } from "./api";
import { App } from "./App";
import { ApiProvider } from "./contexto";
import "./estilo.css";

const api = criarApi();

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <ApiProvider value={api}>
      <App />
    </ApiProvider>
  </StrictMode>,
);

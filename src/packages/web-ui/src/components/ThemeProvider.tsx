"use client";

import { ThemeProvider as NextThemesProvider } from "next-themes";
import type { ReactNode } from "react";

/**
 * Wraps the app with `next-themes`. Uses `data-theme` attribute so the
 * `globals.css` `[data-theme="dark"]` selector takes effect. `enableSystem`
 * lets the OS preference drive the default; `disableTransitionOnChange`
 * avoids a one-frame flash when toggling.
 */
export function ThemeProvider({ children }: { children: ReactNode }) {
  return (
    <NextThemesProvider
      attribute="data-theme"
      defaultTheme="system"
      enableSystem
      disableTransitionOnChange
      themes={["light", "dark"]}
    >
      {children}
    </NextThemesProvider>
  );
}

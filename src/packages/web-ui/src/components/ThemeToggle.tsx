"use client";

import { Monitor, Moon, Sun } from "lucide-react";
import { useTheme } from "next-themes";
import { useSyncExternalStore } from "react";
import { cn } from "@/lib/cn";

type ThemeChoice = "light" | "dark" | "system";

const OPTIONS: Array<{ value: ThemeChoice; label: string; Icon: typeof Sun }> = [
  { value: "light", label: "ライト", Icon: Sun },
  { value: "dark", label: "ダーク", Icon: Moon },
  { value: "system", label: "システム", Icon: Monitor },
];

const subscribeNoop = () => () => {};
const getMountedSnapshot = () => true;
const getMountedServerSnapshot = () => false;

/**
 * Segmented control for light / dark / system theme.
 * Hidden until mounted to avoid SSR/CSR mismatch (next-themes pattern).
 */
export function ThemeToggle() {
  const { theme, setTheme } = useTheme();
  // useSyncExternalStore returns false during SSR / first render, true after mount.
  const mounted = useSyncExternalStore(
    subscribeNoop,
    getMountedSnapshot,
    getMountedServerSnapshot,
  );

  // Render a placeholder of identical size to avoid layout shift.
  if (!mounted) {
    return (
      <div
        aria-hidden="true"
        className="h-9 w-[120px] rounded-[var(--radius-md)] border border-[var(--border)] bg-[var(--card)]"
      />
    );
  }

  const current = (theme as ThemeChoice) ?? "system";

  return (
    <div
      role="radiogroup"
      aria-label="テーマ切替"
      className="inline-flex items-center gap-0.5 rounded-[var(--radius-md)] border border-[var(--border)] bg-[var(--card)] p-0.5"
    >
      {OPTIONS.map(({ value, label, Icon }) => {
        const active = current === value;
        return (
          <button
            key={value}
            type="button"
            role="radio"
            aria-checked={active}
            aria-label={`テーマ: ${label}`}
            onClick={() => setTheme(value)}
            className={cn(
              "inline-flex h-8 min-w-[44px] items-center justify-center gap-1 rounded-[var(--radius-sm)] px-2 text-xs",
              "transition-colors duration-[var(--motion-base)] ease-[var(--motion-ease-out)]",
              "focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--primary)]",
              active
                ? "bg-[var(--primary)] text-white"
                : "text-[var(--muted)] hover:bg-[var(--surface)] hover:text-[var(--foreground)]",
            )}
          >
            <Icon size={14} aria-hidden="true" />
            <span>{label}</span>
          </button>
        );
      })}
    </div>
  );
}

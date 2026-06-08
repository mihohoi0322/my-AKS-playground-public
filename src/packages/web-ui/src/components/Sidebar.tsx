"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

const navItems = [
  { href: "/", label: "ダッシュボード", icon: "⌂" },
  { href: "/employees", label: "従業員", icon: "○" },
  { href: "/attendance", label: "勤怠", icon: "◷" },
  { href: "/organizations", label: "組織", icon: "△" },
];

export function Sidebar() {
  const pathname = usePathname();

  return (
    <aside className="w-60 bg-[var(--card)] border-r border-[var(--border)] min-h-screen px-5 py-6 flex flex-col">
      <div className="mb-10">
        <h1 className="text-lg font-semibold tracking-wide text-[var(--foreground)]">
          HR System
        </h1>
        <p className="text-xs text-[var(--muted)] mt-0.5 tracking-wider uppercase">
          AKS Playground
        </p>
      </div>
      <nav className="space-y-0.5 flex-1">
        {navItems.map((item) => {
          const isActive =
            item.href === "/"
              ? pathname === "/"
              : pathname.startsWith(item.href);
          return (
            <Link
              key={item.href}
              href={item.href}
              className={`flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm transition-all duration-200 ${
                isActive
                  ? "bg-[var(--primary-light)] text-[var(--primary)] font-medium"
                  : "text-[var(--muted)] hover:text-[var(--foreground)] hover:bg-[var(--surface)]"
              }`}
            >
              <span className="text-base">{item.icon}</span>
              {item.label}
            </Link>
          );
        })}
      </nav>
      <div className="pt-4 border-t border-[var(--border)]">
        <p className="text-[10px] text-[var(--muted)] tracking-wider uppercase">
          Chaos Engineering Lab
        </p>
      </div>
    </aside>
  );
}

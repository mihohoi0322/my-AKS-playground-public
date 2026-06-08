"use client";

import {
  AlertTriangle,
  BarChart3,
  CheckCircle2,
  ClipboardList,
  Clock,
  FileCheck2,
  Inbox,
  UserPlus,
} from "lucide-react";
import Link from "next/link";
import { useCallback, useMemo, useState, useSyncExternalStore, type ReactNode } from "react";
import { Button } from "@/components/ui/Button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/Card";
import { ThemeToggle } from "@/components/ThemeToggle";
import { cn } from "@/lib/cn";

/**
 * Phase 1 Dashboard (Issue #8) — Nordic design system applied.
 *
 * - 異常値ハイライト: 濃い danger 色 + AlertTriangle (山本要望)
 * - 密度切替: localStorage["dashboard-density"], enum 検証 (佐藤要望)
 * - モバイル優先: 1col → 2col → 4col (田中要望)
 * - 全データはモック。実 API 接続は Phase 1 後半 / 別タスク
 *
 * TODO(Issue #12 M-CSP): CSP middleware は次タスクで対応。
 */

type Density = "comfortable" | "compact";
const DENSITY_KEY = "dashboard-density";

function isDensity(value: unknown): value is Density {
  return value === "comfortable" || value === "compact";
}

function readDensityFromStorage(): Density {
  if (typeof window === "undefined") return "comfortable";
  try {
    const raw = window.localStorage.getItem(DENSITY_KEY);
    if (isDensity(raw)) return raw;
    // Discard invalid value
    if (raw !== null) window.localStorage.removeItem(DENSITY_KEY);
  } catch {
    // localStorage unavailable (private mode etc.) — fall through
  }
  return "comfortable";
}

const subscribeStorage = (cb: () => void) => {
  if (typeof window === "undefined") return () => {};
  window.addEventListener("storage", cb);
  return () => window.removeEventListener("storage", cb);
};
const getDensityServerSnapshot = (): Density => "comfortable";

// --- Mock data ----------------------------------------------------------

interface ActivityItem {
  id: string;
  who: string;
  what: string;
  when: string;
}

const MOCK_ACTIVITIES: ActivityItem[] = [
  { id: "a1", who: "佐藤美咲", what: "申請を承認しました", when: "10 分前" },
  { id: "a2", who: "田中涼太", what: "勤怠を打刻しました", when: "32 分前" },
  { id: "a3", who: "山本健一", what: "残業申請を差戻ししました", when: "1 時間前" },
  { id: "a4", who: "鈴木一郎", what: "従業員を追加しました", when: "2 時間前" },
  { id: "a5", who: "高橋花子", what: "組織を更新しました", when: "本日" },
];

// --- Page ---------------------------------------------------------------

export default function DashboardPage() {
  // Density is sourced from localStorage on the client; SSR snapshot is the
  // default ("comfortable"). useSyncExternalStore avoids hydration mismatch
  // and the react-hooks/set-state-in-effect rule violation.
  const storedDensity = useSyncExternalStore(
    subscribeStorage,
    readDensityFromStorage,
    getDensityServerSnapshot,
  );
  // Local override so toggling re-renders synchronously without waiting for
  // a `storage` event (which only fires for cross-tab changes).
  const [override, setOverride] = useState<Density | null>(null);
  const density: Density = override ?? storedDensity;

  const setDensity = useCallback((next: Density) => {
    setOverride(next);
    try {
      window.localStorage.setItem(DENSITY_KEY, next);
    } catch {
      // ignore
    }
  }, []);

  // Mock KPI values. Replace with real fetch when available.
  const kpis = useMemo(
    () => ({
      pendingRequests: 12,
      anomalousOvertime: 3,
      awaitingApproval: 5,
      punchCompletionRate: 0.94,
    }),
    [],
  );

  return (
    <div className="space-y-8">
      {/* Header */}
      <header className="flex flex-col gap-4 md:flex-row md:items-end md:justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">ダッシュボード</h1>
          <p className="mt-1 text-sm text-[var(--muted)]">
            人事システムの今日の状況
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <DensityToggle value={density} onChange={setDensity} />
          <ThemeToggle />
        </div>
      </header>

      {/* KPI cards */}
      <section aria-labelledby="kpi-heading">
        <h2 id="kpi-heading" className="sr-only">
          主要指標
        </h2>
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-4">
          <KpiCard
            title="未対応申請"
            value={kpis.pendingRequests}
            unit="件"
            icon={<Inbox size={20} aria-hidden="true" />}
            description="あなたの対応待ち"
            href="/requests"
          />
          <KpiCard
            title="異常残業 (90h+)"
            value={kpis.anomalousOvertime}
            unit="件"
            icon={<AlertTriangle size={20} aria-hidden="true" />}
            description="今月 90 時間を超過"
            href="/attendance?filter=overtime"
            tone="danger"
          />
          <KpiCard
            title="承認待ち"
            value={kpis.awaitingApproval}
            unit="件"
            icon={<FileCheck2 size={20} aria-hidden="true" />}
            description="部長承認が必要"
            href="/approvals"
          />
          <KpiCard
            title="今月の打刻完了率"
            value={`${Math.round(kpis.punchCompletionRate * 100)}`}
            unit="%"
            icon={<BarChart3 size={20} aria-hidden="true" />}
            description="全社平均"
            href="/attendance"
          />
        </div>
      </section>

      {/* Today's primary actions */}
      <section aria-labelledby="actions-heading">
        <Card>
          <CardHeader>
            <CardTitle id="actions-heading">今日の主要アクション</CardTitle>
            <CardDescription>よく使う操作にすばやくアクセス</CardDescription>
          </CardHeader>
          <CardContent>
            <div className="flex flex-wrap gap-3">
              <Button
                variant="primary"
                size="md"
                leftIcon={<ClipboardList size={16} aria-hidden="true" />}
                onClick={() => {
                  window.location.href = "/attendance/new";
                }}
              >
                勤怠を打刻する
              </Button>
              <Button
                variant="secondary"
                size="md"
                leftIcon={<FileCheck2 size={16} aria-hidden="true" />}
                onClick={() => {
                  window.location.href = "/approvals";
                }}
              >
                承認申請を確認
              </Button>
              <Button
                variant="ghost"
                size="md"
                leftIcon={<UserPlus size={16} aria-hidden="true" />}
                onClick={() => {
                  window.location.href = "/employees/new";
                }}
              >
                従業員を追加
              </Button>
            </div>
          </CardContent>
        </Card>
      </section>

      {/* Recent activity */}
      <section aria-labelledby="activity-heading">
        <Card>
          <CardHeader>
            <CardTitle id="activity-heading">最近のアクティビティ</CardTitle>
            <CardDescription>直近の操作 5 件</CardDescription>
          </CardHeader>
          <CardContent className="p-0">
            {MOCK_ACTIVITIES.length === 0 ? (
              <EmptyState
                icon={<CheckCircle2 size={28} aria-hidden="true" />}
                title="アクティビティはまだありません"
                description="操作が行われるとここに表示されます。"
              />
            ) : (
              <ul role="list" className="divide-y divide-[var(--border)]">
                {MOCK_ACTIVITIES.map((item) => (
                  <li
                    key={item.id}
                    className={cn(
                      "flex items-center justify-between gap-3 px-6",
                      density === "compact" ? "h-10 py-1" : "h-14 py-2",
                    )}
                  >
                    <div className="flex min-w-0 items-center gap-3">
                      <Clock
                        size={16}
                        aria-hidden="true"
                        className="shrink-0 text-[var(--muted)]"
                      />
                      <div className="min-w-0">
                        <p className="truncate text-sm">
                          <span className="font-medium">{item.who}</span>
                          <span className="text-[var(--muted)]">
                            {" "}
                            が{item.what}
                          </span>
                        </p>
                      </div>
                    </div>
                    <span className="shrink-0 text-xs text-[var(--muted)]">
                      {item.when}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </CardContent>
        </Card>
      </section>
    </div>
  );
}

// --- Sub-components -----------------------------------------------------

interface KpiCardProps {
  title: string;
  value: number | string;
  unit: string;
  icon: ReactNode;
  description: string;
  href: string;
  tone?: "default" | "danger";
}

function KpiCard({
  title,
  value,
  unit,
  icon,
  description,
  href,
  tone = "default",
}: KpiCardProps) {
  const isDanger = tone === "danger";
  return (
    <Link
      href={href}
      className={cn(
        "block rounded-[var(--radius-lg)]",
        "focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2",
        isDanger
          ? "focus-visible:outline-[var(--danger)]"
          : "focus-visible:outline-[var(--primary)]",
      )}
      aria-label={`${title} ${value}${unit} — 詳細へ`}
    >
      <Card
        className={cn(
          "h-full",
          isDanger &&
            "border-l-4 border-l-[var(--danger)] bg-[var(--danger-light)]",
        )}
      >
        <CardContent className="flex flex-col gap-3 p-6">
          <div className="flex items-center justify-between">
            <span className="text-sm font-medium text-[var(--muted)]">
              {title}
            </span>
            <span
              className={cn(
                "inline-flex h-9 w-9 items-center justify-center rounded-[var(--radius-md)]",
                isDanger
                  ? "bg-[var(--danger)] text-white"
                  : "bg-[var(--surface)] text-[var(--foreground)]",
              )}
            >
              {icon}
            </span>
          </div>
          <div className="flex items-baseline gap-1">
            <span
              className={cn(
                "text-3xl font-semibold tracking-tight",
                isDanger && "text-[var(--danger)]",
              )}
            >
              {value}
            </span>
            <span className="text-sm text-[var(--muted)]">{unit}</span>
          </div>
          <p className="text-xs text-[var(--muted)]">{description}</p>
        </CardContent>
      </Card>
    </Link>
  );
}

interface DensityToggleProps {
  value: Density;
  onChange: (value: Density) => void;
}

function DensityToggle({ value, onChange }: DensityToggleProps) {
  const options: Array<{ key: Density; label: string }> = [
    { key: "comfortable", label: "ゆったり" },
    { key: "compact", label: "コンパクト" },
  ];
  return (
    <div
      role="radiogroup"
      aria-label="表示密度"
      className="inline-flex items-center gap-0.5 rounded-[var(--radius-md)] border border-[var(--border)] bg-[var(--card)] p-0.5"
    >
      {options.map((opt) => {
        const active = value === opt.key;
        return (
          <button
            key={opt.key}
            type="button"
            role="radio"
            aria-checked={active}
            onClick={() => onChange(opt.key)}
            className={cn(
              "inline-flex h-8 min-w-[44px] items-center justify-center rounded-[var(--radius-sm)] px-3 text-xs",
              "transition-colors duration-[var(--motion-base)] ease-[var(--motion-ease-out)]",
              "focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--primary)]",
              active
                ? "bg-[var(--primary)] text-white"
                : "text-[var(--muted)] hover:bg-[var(--surface)] hover:text-[var(--foreground)]",
            )}
          >
            {opt.label}
          </button>
        );
      })}
    </div>
  );
}

interface EmptyStateProps {
  icon: ReactNode;
  title: string;
  description: string;
}

function EmptyState({ icon, title, description }: EmptyStateProps) {
  return (
    <div className="flex flex-col items-center justify-center gap-2 px-6 py-12 text-center">
      <span className="text-[var(--muted)]">{icon}</span>
      <p className="text-sm font-medium">{title}</p>
      <p className="text-xs text-[var(--muted)]">{description}</p>
    </div>
  );
}


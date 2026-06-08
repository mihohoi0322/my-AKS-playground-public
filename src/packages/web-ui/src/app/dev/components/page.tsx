import { notFound } from "next/navigation";
import { Button } from "@/components/ui/Button";
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/components/ui/Card";

export const metadata = {
  title: "Components Preview — Dev Only",
};

const variants = ["primary", "secondary", "tertiary", "ghost", "danger", "link"] as const;
const sizes = ["sm", "md", "lg"] as const;

function CheckIcon() {
  return (
    <svg
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2.5"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <polyline points="20 6 9 17 4 12" />
    </svg>
  );
}

function ArrowRightIcon() {
  return (
    <svg
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2.5"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <line x1="5" y1="12" x2="19" y2="12" />
      <polyline points="12 5 19 12 12 19" />
    </svg>
  );
}

/**
 * /dev/components — Storybook 代替プレビュー (本番では 404)
 */
export default function ComponentsPreviewPage() {
  if (process.env.NODE_ENV === "production") {
    notFound();
  }

  return (
    <div className="flex flex-col gap-10">
      <header className="flex flex-col gap-2">
        <h1 className="text-2xl font-semibold">Nordic Components Preview</h1>
        <p className="text-sm text-[var(--muted)]">
          Dev-only page for visual review of UI primitives. Returns 404 in production.
        </p>
      </header>

      {/* Buttons grid: variant × size */}
      <section className="flex flex-col gap-4">
        <h2 className="text-lg font-semibold">Button — variant × size</h2>
        <div className="overflow-x-auto">
          <table className="border-separate border-spacing-3 text-sm">
            <thead>
              <tr>
                <th className="text-left text-[var(--muted)] font-medium">variant \\ size</th>
                {sizes.map((s) => (
                  <th key={s} className="text-left text-[var(--muted)] font-medium">
                    {s}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {variants.map((v) => (
                <tr key={v}>
                  <td className="text-[var(--muted)] pr-4 align-middle">{v}</td>
                  {sizes.map((s) => (
                    <td key={s} className="align-middle">
                      <Button variant={v} size={s}>
                        Button
                      </Button>
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      {/* Button states */}
      <section className="flex flex-col gap-4">
        <h2 className="text-lg font-semibold">Button — states & icons</h2>
        <div className="flex flex-wrap gap-3">
          <Button leftIcon={<CheckIcon />}>承認する</Button>
          <Button variant="secondary" rightIcon={<ArrowRightIcon />}>
            次へ
          </Button>
          <Button variant="danger">削除</Button>
          <Button loading>送信中</Button>
          <Button disabled>無効</Button>
          <Button variant="ghost" aria-label="check icon only">
            <CheckIcon />
          </Button>
        </div>
      </section>

      {/* Cards */}
      <section className="flex flex-col gap-4">
        <h2 className="text-lg font-semibold">Card</h2>
        <div className="grid gap-4 grid-cols-1 md:grid-cols-2 lg:grid-cols-3">
          <Card>
            <CardHeader>
              <CardTitle>従業員数</CardTitle>
              <CardDescription>本日時点</CardDescription>
            </CardHeader>
            <CardContent>
              <p className="text-3xl font-semibold">1,284</p>
            </CardContent>
            <CardFooter>
              <Button variant="link">詳細を見る</Button>
            </CardFooter>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>勤怠未提出</CardTitle>
              <CardDescription>過去 7 日</CardDescription>
            </CardHeader>
            <CardContent>
              <p className="text-3xl font-semibold text-[var(--danger)]">12</p>
            </CardContent>
            <CardFooter>
              <Button variant="secondary" size="sm">
                対象者を見る
              </Button>
            </CardFooter>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>承認待ち</CardTitle>
              <CardDescription>あなた宛て</CardDescription>
            </CardHeader>
            <CardContent>
              <p className="text-3xl font-semibold text-[var(--color-warning)]">4</p>
            </CardContent>
            <CardFooter>
              <Button size="sm">承認画面へ</Button>
            </CardFooter>
          </Card>
        </div>
      </section>
    </div>
  );
}

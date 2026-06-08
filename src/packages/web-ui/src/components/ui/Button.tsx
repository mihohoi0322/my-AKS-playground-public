import { cva, type VariantProps } from "class-variance-authority";
import { forwardRef, type ButtonHTMLAttributes, type ReactNode } from "react";
import { cn } from "@/lib/cn";

/**
 * Button — Nordic design system primitive.
 *
 * Variants:
 *  - primary   : 主要アクション（緑塗り）
 *  - secondary : 副次アクション（枠付き）
 *  - tertiary  : 三次アクション（surface 背景）
 *  - ghost     : 控えめ（透過、hover で surface）
 *  - danger    : 破壊的アクション（赤塗り）
 *  - link      : テキストリンク風
 *
 * Sizes:
 *  - sm: 32px height
 *  - md: 40px height (default)
 *  - lg: 48px height
 *
 * Accessibility:
 *  - focus-visible で 2px のアウトライン (--primary)
 *  - loading 時は aria-busy + disabled
 *  - icon-only 用途では呼出側で aria-label を必須に
 */

const buttonVariants = cva(
  [
    "inline-flex items-center justify-center gap-2",
    "font-medium whitespace-nowrap select-none",
    "rounded-[var(--radius-md)]",
    "transition-[background-color,color,border-color,box-shadow,opacity]",
    "duration-[var(--motion-base)] ease-[var(--motion-ease-out)]",
    "focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2",
    "focus-visible:outline-[var(--primary)]",
    "disabled:cursor-not-allowed disabled:opacity-60",
  ].join(" "),
  {
    variants: {
      variant: {
        primary: [
          "bg-[var(--primary)] text-white border border-transparent",
          "hover:bg-[var(--primary-hover)]",
          "shadow-[var(--shadow-sm)]",
        ].join(" "),
        secondary: [
          "bg-[var(--card)] text-[var(--foreground)]",
          "border border-[var(--border)]",
          "hover:bg-[var(--card-hover)] hover:border-[var(--primary)]",
        ].join(" "),
        tertiary: [
          "bg-[var(--surface)] text-[var(--foreground)] border border-transparent",
          "hover:bg-[var(--card-hover)]",
        ].join(" "),
        ghost: [
          "bg-transparent text-[var(--foreground)] border border-transparent",
          "hover:bg-[var(--surface)]",
        ].join(" "),
        danger: [
          "bg-[var(--danger)] text-white border border-transparent",
          "hover:opacity-90",
          "focus-visible:outline-[var(--danger)]",
          "shadow-[var(--shadow-sm)]",
        ].join(" "),
        link: [
          "bg-transparent border border-transparent",
          "text-[var(--primary)] underline-offset-4 hover:underline",
          "h-auto px-0 py-0",
        ].join(" "),
      },
      size: {
        sm: "h-8 px-3 text-sm",
        md: "h-10 px-4 text-sm",
        lg: "h-12 px-6 text-base",
      },
    },
    compoundVariants: [
      // link variant ignores size paddings/heights
      { variant: "link", size: "sm", class: "h-auto px-0 text-sm" },
      { variant: "link", size: "md", class: "h-auto px-0 text-sm" },
      { variant: "link", size: "lg", class: "h-auto px-0 text-base" },
    ],
    defaultVariants: {
      variant: "primary",
      size: "md",
    },
  },
);

export type ButtonVariantProps = VariantProps<typeof buttonVariants>;

export interface ButtonProps
  extends ButtonHTMLAttributes<HTMLButtonElement>,
    ButtonVariantProps {
  /** 左側に表示するアイコン */
  leftIcon?: ReactNode;
  /** 右側に表示するアイコン */
  rightIcon?: ReactNode;
  /** ローディング表示。true の間はクリック不可 + 左に Spinner を表示 */
  loading?: boolean;
}

function Spinner({ className }: { className?: string }) {
  return (
    <svg
      className={cn("animate-spin", className)}
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="none"
      aria-hidden="true"
    >
      <circle
        cx="12"
        cy="12"
        r="10"
        stroke="currentColor"
        strokeWidth="3"
        opacity="0.25"
      />
      <path
        d="M22 12a10 10 0 0 1-10 10"
        stroke="currentColor"
        strokeWidth="3"
        strokeLinecap="round"
      />
    </svg>
  );
}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(
  function Button(
    {
      className,
      variant,
      size,
      leftIcon,
      rightIcon,
      loading = false,
      disabled,
      type = "button",
      children,
      ...props
    },
    ref,
  ) {
    const isDisabled = disabled || loading;
    return (
      <button
        ref={ref}
        type={type}
        className={cn(buttonVariants({ variant, size }), className)}
        disabled={isDisabled}
        aria-busy={loading || undefined}
        data-loading={loading || undefined}
        {...props}
      >
        {loading ? <Spinner /> : leftIcon}
        {children}
        {!loading && rightIcon}
      </button>
    );
  },
);

export { buttonVariants };

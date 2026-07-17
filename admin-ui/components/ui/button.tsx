import type { ButtonHTMLAttributes, ReactNode } from "react";
import { cva, type VariantProps } from "class-variance-authority";
import { cn } from "@/lib/utils";

// Wireless button conventions (.claude/skills/design-aesthetic/SKILL.md):
// primary = solid --accent, --accent-ink text; secondary = 1px --line border
// on --surface; destructive = solid --danger, --danger-ink text, confirm
// dialogs only. On-accent/on-danger text always uses the dedicated ink
// tokens (never --surface/--ink — their meaning flips per theme). 6px
// radius, 40px min touch target. Colors resolve from semantic tokens only —
// no raw hex, no Tailwind stock palette classes.
const buttonVariants = cva(
  "inline-flex h-10 min-w-10 items-center justify-center gap-1.5 rounded-[6px] px-4 text-[0.85rem] font-semibold transition-colors duration-[120ms] ease-out focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent disabled:pointer-events-none disabled:opacity-50",
  {
    variants: {
      variant: {
        primary: "bg-accent text-accent-ink hover:bg-accent/90",
        secondary: "border border-line bg-surface text-ink hover:bg-surface-2",
        destructive: "bg-danger text-danger-ink hover:bg-danger/90",
      },
    },
    defaultVariants: {
      variant: "primary",
    },
  }
);

interface ButtonProps
  extends ButtonHTMLAttributes<HTMLButtonElement>,
    VariantProps<typeof buttonVariants> {
  children?: ReactNode;
}

export function Button({
  className,
  variant,
  type = "button",
  ...props
}: ButtonProps): ReactNode {
  return (
    <button
      type={type}
      className={cn(buttonVariants({ variant }), className)}
      {...props}
    />
  );
}

export { buttonVariants };

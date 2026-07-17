"use client";

import { useState, type FormEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/button";

interface LoginFormProps {
  returnTo: string;
}

export default function LoginForm({ returnTo }: LoginFormProps): ReactNode {
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  // Client-side fetch so the browser itself receives the api's Set-Cookie (via the Next rewrite),
  // then a full-page navigation to the (validated) return target — defaults to /dashboard.
  async function onSubmit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault();
    setPending(true);
    setError(null);

    const password = (
      event.currentTarget.elements.namedItem("password") as HTMLInputElement
    ).value;

    try {
      const response = await fetch("/api/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ password }),
      });
      if (response.status === 204) {
        window.location.assign(returnTo);
        return;
      }
    } catch {
      // fall through to the generic error below
    }

    setError("Invalid credentials");
    setPending(false);
  }

  return (
    <form onSubmit={onSubmit} className="flex flex-col gap-4">
      {error !== null && (
        <p role="alert" aria-live="polite" className="text-[0.85rem] text-danger">
          {error}
        </p>
      )}
      <div className="flex flex-col gap-1.5">
        <label htmlFor="password" className="text-[0.82rem] font-semibold text-mute">
          Password
        </label>
        <input
          id="password"
          name="password"
          type="password"
          autoComplete="current-password"
          required
          disabled={pending}
          className="h-10 rounded-[6px] border border-line bg-surface px-3 text-[0.85rem] text-ink outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent"
        />
      </div>
      <Button type="submit" disabled={pending} className="w-full">
        {pending ? "Signing in…" : "Sign in"}
      </Button>
    </form>
  );
}

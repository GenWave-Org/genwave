import type { ReactNode } from "react";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { safeReturnTo } from "@/lib/safe-return";
import LoginForm from "./LoginForm";

const SESSION_COOKIE = "genwave-auth";

interface LoginPageProps {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}

export default async function LoginPage({
  searchParams,
}: LoginPageProps): Promise<ReactNode> {
  // Redirect already-authenticated users to the dashboard
  const cookieStore = await cookies();
  if (cookieStore.get(SESSION_COOKIE)) {
    redirect("/dashboard");
  }

  const params = await searchParams;
  const returnTo = safeReturnTo(params["return"]);

  return (
    <main className="flex min-h-screen items-center justify-center bg-bg px-4">
      <div className="w-full max-w-sm rounded-[6px] border border-line bg-surface p-8">
        <p className="mb-1 font-display text-2xl italic text-ink">GenWave</p>
        <h1 className="mb-6 text-[1.35rem] font-semibold text-ink">Sign in</h1>
        <LoginForm returnTo={returnTo} />
      </div>
    </main>
  );
}

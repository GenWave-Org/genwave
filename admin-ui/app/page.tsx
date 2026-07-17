import { cookies } from "next/headers";
import { redirect } from "next/navigation";

const SESSION_COOKIE = "genwave-auth";

export default async function RootPage(): Promise<never> {
  const cookieStore = await cookies();
  const session = cookieStore.get(SESSION_COOKIE);

  if (session) {
    redirect("/dashboard");
  } else {
    redirect("/login");
  }
}

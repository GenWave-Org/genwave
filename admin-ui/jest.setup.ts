// sonner ≥2.0.8 replays every still-active toast to a newly mounted <Toaster>
// (Observer.subscribe now hydrates from getActiveToasts(); 2.0.7 started empty).
// The toast store is module-global, so it outlives a test's render: a toast fired
// in one test resurfaces in the next test's mount and turns any getByText on the
// same copy into "Found multiple elements". Dismissing between tests keeps each
// test's Toaster starting empty, which is the isolation every spec was written
// against (gh-#516).
import { afterEach } from "@jest/globals";
import { toast } from "sonner";
import { resetNavGroupOpenStateForTests } from "./app/(authed)/_components/useNavGroupOpenState";

afterEach(() => {
  toast.dismiss();
  // Same module-global-leak shape as the toast store above (PLAN T559 fix round): the shared nav
  // open-state store outlives a test's render, so a toggle in one test's Sidebar/MobileNav would
  // otherwise leak into the next test's mount.
  resetNavGroupOpenStateForTests();
});

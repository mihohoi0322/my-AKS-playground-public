import { RealtimeProvider } from "@/lib/realtime";
import type { ReactNode } from "react";

/**
 * Wraps authenticated UI in <RealtimeProvider> so child Client Components can subscribe
 * via `useRealtimeEvents(topic, handler)`. The login page lives outside this layout, so
 * unauthenticated routes never open an SSE stream.
 *
 * See docs/frontend-design.md §1.5 and ADR-016.
 */
export default function AuthenticatedLayout({ children }: { children: ReactNode }): ReactNode {
  return <RealtimeProvider>{children}</RealtimeProvider>;
}

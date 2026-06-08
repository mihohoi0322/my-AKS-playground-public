import type { Metadata } from "next";
import "./globals.css";
import { Sidebar } from "@/components/Sidebar";
import AuthenticatedLayout from "@/components/AuthenticatedLayout";
import { ThemeProvider } from "@/components/ThemeProvider";

export const metadata: Metadata = {
  title: "HR System — AKS Playground",
  description: "HR System management dashboard",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  // NOTE: When /login (and other unauthenticated routes) are introduced, move <AuthenticatedLayout>
  // into a route-group layout (e.g. app/(authenticated)/layout.tsx) so /login does not open an SSE
  // stream. Today every page in this app requires session, so wrapping at the root is correct.
  //
  // TODO(Issue #12 M-CSP): Add Content-Security-Policy via middleware in a follow-up PR.
  return (
    <html lang="ja" className="h-full antialiased" suppressHydrationWarning>
      <body className="min-h-full flex bg-[var(--background)]">
        <ThemeProvider>
          <AuthenticatedLayout>
            <Sidebar />
            <main className="flex-1 px-6 py-6 md:px-10 md:py-8 max-w-6xl">
              {children}
            </main>
          </AuthenticatedLayout>
        </ThemeProvider>
      </body>
    </html>
  );
}

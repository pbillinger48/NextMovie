import type { Metadata } from "next";
import Link from "next/link";
import "./globals.css";

export const metadata: Metadata = {
  title: "NextMovie",
  description:
    "Find the best movie you haven't seen that you can stream right now.",
};

export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body className="min-h-dvh bg-neutral-50 text-neutral-900 antialiased dark:bg-neutral-950 dark:text-neutral-100">
        <div className="flex min-h-dvh flex-col">
          <header className="border-b border-neutral-200 dark:border-neutral-800">
            <div className="mx-auto w-full max-w-5xl px-4 py-4">
              <Link
                href="/"
                className="text-lg font-semibold tracking-tight focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-blue-600"
              >
                NextMovie
              </Link>
            </div>
          </header>

          <main className="mx-auto w-full max-w-5xl flex-1 px-4 py-8">
            {children}
          </main>

          {/*
            Required by TMDb's terms of use, not decoration. Any product built on
            their API must display this attribution.
          */}
          <footer className="border-t border-neutral-200 px-4 py-6 dark:border-neutral-800">
            <p className="mx-auto w-full max-w-5xl text-xs text-neutral-600 dark:text-neutral-400">
              This product uses the TMDB API but is not endorsed or certified by{" "}
              <a
                href="https://www.themoviedb.org/"
                className="underline underline-offset-2 hover:text-neutral-900 dark:hover:text-neutral-100"
                target="_blank"
                rel="noopener noreferrer"
              >
                TMDB
              </a>
              .
            </p>
          </footer>
        </div>
      </body>
    </html>
  );
}

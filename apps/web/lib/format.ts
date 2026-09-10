/**
 * Display formatting shared across pages.
 *
 * Pure and dependency-free, so it can be unit tested without rendering anything.
 */

/**
 * Renders a runtime in minutes as hours and minutes: 148 → "2h 28m".
 *
 * Minutes alone are hard to read at a glance — "148 minutes" makes most people
 * do arithmetic before they know whether they have time to watch it.
 */
export function formatRuntime(minutes: number): string {
  const hours = Math.floor(minutes / 60);
  const remainder = minutes % 60;

  if (hours === 0) {
    return `${remainder}m`;
  }

  // "2h" rather than "2h 0m": the zero adds nothing and reads oddly.
  return remainder === 0 ? `${hours}h` : `${hours}h ${remainder}m`;
}

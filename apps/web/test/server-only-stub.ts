/**
 * Stands in for the `server-only` package under vitest.
 *
 * `server-only` throws unless it is resolved under React's `react-server`
 * condition, which the test runner does not use. That marker exists to make a
 * bad import a build error in the app — it is not a runtime guarantee, and it has
 * nothing to say about a module being exercised directly by a test.
 *
 * Aliased in vitest.config.mts. Deliberately empty.
 */
export {};

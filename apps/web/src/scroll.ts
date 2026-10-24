/**
 * Scrolls to the top on navigation. Returns nothing on purpose: newer Chrome
 * resolves `window.scrollTo` to a Promise, and an effect that returns it makes
 * React call that Promise as the cleanup, which tears the tree down and leaves
 * a blank page (#40).
 */
export function scrollToTop() {
  window.scrollTo({ top: 0 })
}

import { useEffect, useRef, useState } from 'react'

export const prefersReducedMotion = () =>
  typeof window !== 'undefined' && typeof window.matchMedia === 'function' && window.matchMedia('(prefers-reduced-motion: reduce)').matches

/** A number that counts up once when it first appears; later changes are shown at once. */
export function useCountUp(target: number | null, duration = 900): number | null {
  const [value, setValue] = useState<number | null>(() => (target == null || prefersReducedMotion() ? target : 0))
  const played = useRef(false)
  useEffect(() => {
    if (target == null || played.current || prefersReducedMotion()) {
      setValue(target)
      return
    }
    played.current = true
    const start = performance.now()
    let frame = 0
    const tick = (now: number) => {
      const t = Math.min(1, (now - start) / duration)
      const eased = 1 - Math.pow(2, -10 * t)
      setValue(t >= 1 ? target : target * eased)
      if (t < 1) frame = requestAnimationFrame(tick)
    }
    frame = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(frame)
  }, [target, duration])
  return value
}

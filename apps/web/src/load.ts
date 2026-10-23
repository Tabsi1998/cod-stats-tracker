import { useEffect, useState } from 'react'
import { NotFoundError } from './api'

export type Load<T> = { state: 'loading' } | { state: 'error'; notFound: boolean } | { state: 'ready'; data: T }

/** Loads data whenever a dependency changes; a newer request replaces an older one. */
export function useLoad<T>(load: () => Promise<T>, deps: unknown[]): Load<T> {
  const [result, setResult] = useState<Load<T>>({ state: 'loading' })
  useEffect(() => {
    let current = true
    setResult({ state: 'loading' })
    load()
      .then((data) => current && setResult({ state: 'ready', data }))
      .catch((error: unknown) => current && setResult({ state: 'error', notFound: error instanceof NotFoundError }))
    return () => {
      current = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps)
  return result
}

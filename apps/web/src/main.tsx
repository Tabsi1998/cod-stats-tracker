import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router'
import { App } from './App'
import './index.css'

// After an update the files of the old version are gone; a page still open asks for them when it opens a profile.
// Reload once to get the new version instead of failing (Vite announces this as vite:preloadError).
window.addEventListener('vite:preloadError', (event) => {
  event.preventDefault()
  try {
    if (sessionStorage.getItem('reloaded-for-update') === '1') return
    sessionStorage.setItem('reloaded-for-update', '1')
  } catch {
    // Without storage, reload anyway: a loop is impossible because the new page has the new files.
  }
  window.location.reload()
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <App />
    </BrowserRouter>
  </StrictMode>,
)

import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './styles.css'
import { App } from './App'
import { createApi, type SupportApi } from './api'

async function start() {
  let api: SupportApi | null = null
  if (__MOCK__ && new URLSearchParams(location.search).get('mock') === '1') {
    api = (await import('./mock')).createMockApi()
  } else {
    api = createApi()
  }
  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <App api={api} />
    </StrictMode>,
  )
}

void start()

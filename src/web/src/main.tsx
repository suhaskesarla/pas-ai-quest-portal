import { StrictMode, useEffect, useState } from 'react'
import { createRoot } from 'react-dom/client'
import './styles.css'

type HealthState = 'checking' | 'healthy' | 'unavailable'

function App() {
  const [health, setHealth] = useState<HealthState>('checking')

  useEffect(() => {
    fetch('/api/health')
      .then((response) => {
        if (!response.ok) throw new Error(`Health check failed: ${response.status}`)
        setHealth('healthy')
      })
      .catch(() => setHealth('unavailable'))
  }, [])

  return (
    <main>
      <section className="card">
        <p className="eyebrow">PAS AI QUEST</p>
        <h1>Local development environment</h1>
        <p>Solution scaffolding is ready. Domain workflows begin in later playbook steps.</p>
        <p className={`status status--${health}`}>
          API: {health === 'checking' ? 'checking…' : health}
        </p>
      </section>
    </main>
  )
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)

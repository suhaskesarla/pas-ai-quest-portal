import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { App } from './App'
import { AuthProvider } from './auth/AuthContext'
import './styles.css'

const demoModeAvailable = import.meta.env.VITE_DEMO_AUTH_ENABLED === 'true'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AuthProvider demoModeAvailable={demoModeAvailable}>
      <App />
    </AuthProvider>
  </StrictMode>,
)

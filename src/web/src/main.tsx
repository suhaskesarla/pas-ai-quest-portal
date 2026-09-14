import { StrictMode, type ReactNode } from 'react'
import { createRoot } from 'react-dom/client'
import { MsalProvider } from '@azure/msal-react'
import { App } from './App'
import { AuthProvider } from './auth/AuthContext'
import { readAuthRuntimeConfig } from './auth/authConfig'
import { BrandedAuthLoading } from './auth/LoginPage'
import { getMsalInstance } from './auth/msalInstance'
import './styles.css'

const root = createRoot(document.getElementById('root')!)
const strict = (content: ReactNode) => <StrictMode>{content}</StrictMode>

root.render(strict(<BrandedAuthLoading />))

async function start() {
  const runtime = readAuthRuntimeConfig(import.meta.env)
  if (runtime.mode === 'demo') {
    root.render(strict(<AuthProvider mode="demo" demoModeAvailable><App /></AuthProvider>))
    return
  }
  if (runtime.error || !runtime.entra) {
    root.render(strict(<AuthProvider mode="entra" demoModeAvailable={false} configurationError={runtime.error}><App /></AuthProvider>))
    return
  }
  try {
    const msal = getMsalInstance(runtime.entra)
    await msal.initialize()
    const redirect = await msal.handleRedirectPromise()
    const account = redirect?.account ?? msal.getActiveAccount() ?? msal.getAllAccounts()[0] ?? null
    if (account) msal.setActiveAccount(account)
    root.render(strict(<MsalProvider instance={msal}><AuthProvider mode="entra" demoModeAvailable={false} msal={msal} entraConfig={runtime.entra}><App /></AuthProvider></MsalProvider>))
  } catch {
    root.render(strict(<AuthProvider mode="entra" demoModeAvailable={false} configurationError="Microsoft sign-in could not be initialized. Please try again or contact IT."><App /></AuthProvider>))
  }
}

void start()

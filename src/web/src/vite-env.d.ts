/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_APP_ENVIRONMENT?: string
  readonly VITE_DEMO_AUTH_ENABLED?: string
  readonly VITE_ENTRA_TENANT_ID?: string
  readonly VITE_ENTRA_CLIENT_ID?: string
  readonly VITE_ENTRA_REDIRECT_URI?: string
  readonly VITE_ENTRA_API_SCOPE?: string
}

const recoveryMarkerKey = 'pas-ai-quest.auth-recovery-attempted'

let recoveryInFlight: Promise<void> | null = null
let recoveryStartedThisPage = false

function markerPresent() {
  try { return window.sessionStorage.getItem(recoveryMarkerKey) === 'true' }
  catch { return false }
}

function setMarker() {
  try { window.sessionStorage.setItem(recoveryMarkerKey, 'true') }
  catch { /* MSAL redirect remains the recovery path when storage is unavailable. */ }
}

export class AuthenticationRecoveryAlreadyAttempted extends Error {}

export function hasAuthenticationRecoveryMarker() { return markerPresent() }
export function authenticationRecoveryStartedOnThisPage() { return recoveryStartedThisPage }

export function clearAuthenticationRecovery() {
  try { window.sessionStorage.removeItem(recoveryMarkerKey) }
  catch { /* Nothing sensitive is retained when storage is unavailable. */ }
  recoveryInFlight = null
  recoveryStartedThisPage = false
}

export async function recoverAuthentication(startRedirect: () => Promise<void>) {
  if (recoveryInFlight) return recoveryInFlight
  if (markerPresent()) {
    if (recoveryStartedThisPage) return
    throw new AuthenticationRecoveryAlreadyAttempted('Interactive session recovery was already attempted.')
  }
  recoveryStartedThisPage = true
  setMarker()
  recoveryInFlight = Promise.resolve().then(startRedirect)
  try { await recoveryInFlight }
  catch (error) { clearAuthenticationRecovery(); throw error }
  finally { recoveryInFlight = null }
}

export function resetAuthenticationRecoveryRuntimeForTests() {
  recoveryInFlight = null
  recoveryStartedThisPage = false
}

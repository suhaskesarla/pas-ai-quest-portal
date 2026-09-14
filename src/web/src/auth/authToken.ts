import { InteractionRequiredAuthError, type IPublicClientApplication } from '@azure/msal-browser'

export class AuthenticationRedirectStarted extends Error {}

export function createAccessTokenProvider(msal: IPublicClientApplication, apiScope: string, recoverAuthentication: () => Promise<void>) {
  return async () => {
    const account = msal.getActiveAccount() ?? msal.getAllAccounts()[0]
    if (!account) return null
    try { return (await msal.acquireTokenSilent({ account, scopes: [apiScope] })).accessToken }
    catch (error) {
      if (error instanceof InteractionRequiredAuthError || (error instanceof Error && error.name === 'InteractionRequiredAuthError')) {
        await recoverAuthentication()
        throw new AuthenticationRedirectStarted('Interactive authentication started.')
      }
      throw error
    }
  }
}

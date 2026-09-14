import { PublicClientApplication, type IPublicClientApplication } from '@azure/msal-browser'
import { msalConfiguration, type EntraFrontendConfig } from './authConfig'

let singleton: IPublicClientApplication | null = null
export function getMsalInstance(config: EntraFrontendConfig) { singleton ??= new PublicClientApplication(msalConfiguration(config)); return singleton }

targetScope = 'subscription'

@description('Azure region for all POC resources.')
param location string = 'australiaeast'

@allowed([
  'poc'
  'dev'
  'uat'
])
param environmentName string = 'poc'

@minLength(3)
@maxLength(12)
@description('Short globally unique suffix used for public Azure resource names.')
param nameSuffix string

@description('App Service Plan SKU. F1 is intended for POC/testing only.')
param appServiceSku string = 'F1'

@description('Use the Azure SQL Database free-offer flag and auto-pause when the monthly free limit is exhausted.')
param useFreeSql bool = true

param sqlAdminLogin string = 'pasadmin'

@secure()
param sqlAdminPassword string

var resourceGroupName = 'rg-pas-ai-quest-${environmentName}'
var apiAppName = 'app-pas-ai-quest-api-${environmentName}-${nameSuffix}'
var webAppName = 'app-pas-ai-quest-web-${environmentName}-${nameSuffix}'
var sqlServerName = 'sql-pas-ai-quest-${environmentName}-${nameSuffix}'
var sqlDatabaseName = 'pasaiquest'

// Storage names must be lowercase alphanumeric, 3-24 chars.
var storageRaw = toLower(replace('stpasq${environmentName}${nameSuffix}', '-', ''))
var storageAccountName = take(storageRaw, 24)

// Key Vault names are 3-24 chars.
var keyVaultName = take('kv-pasq-${environmentName}-${nameSuffix}', 24)
var appServicePlanName = 'asp-pas-ai-quest-${environmentName}-${nameSuffix}'
var logAnalyticsName = 'log-pas-ai-quest-${environmentName}-${nameSuffix}'
var appInsightsName = 'appi-pas-ai-quest-${environmentName}-${nameSuffix}'

var commonTags = {
  application: 'PAS AI Quest'
  environment: environmentName
  managedBy: 'Bicep'
  purpose: 'POC'
}

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: commonTags
}

module monitoring './modules/monitoring.bicep' = {
  name: 'monitoring'
  scope: rg
  params: {
    location: location
    logAnalyticsName: logAnalyticsName
    appInsightsName: appInsightsName
    tags: commonTags
  }
}

module storage './modules/storage.bicep' = {
  name: 'storage'
  scope: rg
  params: {
    location: location
    storageAccountName: storageAccountName
    blobContainerName: 'evidence'
    tags: commonTags
  }
}

module keyVault './modules/keyvault.bicep' = {
  name: 'keyVault'
  scope: rg
  params: {
    location: location
    keyVaultName: keyVaultName
    tags: commonTags
  }
}

module sql './modules/sql.bicep' = {
  name: 'sql'
  scope: rg
  params: {
    location: location
    sqlServerName: sqlServerName
    sqlDatabaseName: sqlDatabaseName
    sqlAdminLogin: sqlAdminLogin
    sqlAdminPassword: sqlAdminPassword
    useFreeSql: useFreeSql
    tags: commonTags
  }
}

module appService './modules/appservice.bicep' = {
  name: 'appService'
  scope: rg
  params: {
    location: location
    appServicePlanName: appServicePlanName
    appServiceSku: appServiceSku
    apiAppName: apiAppName
    webAppName: webAppName
    applicationInsightsConnectionString: monitoring.outputs.applicationInsightsConnectionString
    tags: commonTags
  }
}

output resourceGroupName string = resourceGroupName
output webAppName string = appService.outputs.webAppName
output webUrl string = appService.outputs.webUrl
output apiAppName string = appService.outputs.apiAppName
output apiUrl string = appService.outputs.apiUrl
output sqlServerName string = sql.outputs.sqlServerName
output sqlDatabaseName string = sql.outputs.sqlDatabaseName
output storageAccountName string = storage.outputs.storageAccountName
output blobContainerName string = storage.outputs.blobContainerName
output keyVaultName string = keyVault.outputs.keyVaultName
output applicationInsightsName string = monitoring.outputs.applicationInsightsName

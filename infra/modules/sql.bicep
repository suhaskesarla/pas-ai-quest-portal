param location string
param sqlServerName string
param sqlDatabaseName string
param sqlAdminLogin string

@secure()
param sqlAdminPassword string

param useFreeSql bool = true
param tags object = {}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  tags: tags
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// POC convenience: permits connections from Azure-hosted services.
// Production should move toward private networking / explicit firewall controls.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  tags: tags
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    autoPauseDelay: 60
    maxSizeBytes: 34359738368
    requestedBackupStorageRedundancy: 'Local'
    useFreeLimit: useFreeSql
    freeLimitExhaustionBehavior: useFreeSql ? 'AutoPause' : 'BillOverUsage'
  }
}

output sqlServerName string = sqlServer.name
output sqlDatabaseName string = database.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName

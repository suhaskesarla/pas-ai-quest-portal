FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source
COPY src/api/PAS.AIQuestPortal.Api.csproj src/api/
RUN dotnet restore src/api/PAS.AIQuestPortal.Api.csproj
COPY src/api/ src/api/
RUN dotnet publish src/api/PAS.AIQuestPortal.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "PAS.AIQuestPortal.Api.dll"]

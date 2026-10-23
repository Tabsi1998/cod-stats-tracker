# Build context: repository root
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .editorconfig ./
COPY shared/contracts/CodStats.Contracts/ shared/contracts/CodStats.Contracts/
COPY apps/api/CodStats.Api/ apps/api/CodStats.Api/
RUN dotnet publish apps/api/CodStats.Api/CodStats.Api.csproj -c Release -o /app -p:RestoreLockedMode=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "CodStats.Api.dll"]

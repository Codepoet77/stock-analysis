FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY StockAnalysis.Api.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:5123
EXPOSE 5123

HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
  CMD curl -f http://localhost:5123/health || exit 1

ENTRYPOINT ["dotnet", "StockAnalysis.Api.dll"]

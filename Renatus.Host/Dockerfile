FROM node:24-bookworm-slim AS frontend
WORKDIR /front
RUN npm install --global pnpm@11.7.0
COPY Renatus.Front/package.json Renatus.Front/pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile
COPY Renatus.Front/ ./
RUN pnpm build

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY Renatus.Host/Renatus.Host.csproj Renatus.Host/
COPY Renatus.Host/Frontend.targets Renatus.Host/
COPY Renatus.Data/Renatus.Data.csproj Renatus.Data/
RUN dotnet restore Renatus.Host/Renatus.Host.csproj
COPY . .
COPY --from=frontend /front/dist/ Renatus.Host/wwwroot/
RUN dotnet publish Renatus.Host/Renatus.Host.csproj -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:BuildFrontend=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Renatus.Host.dll"]

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish LibreKO.Login/LibreKO.Login.csproj -c Release -o /app/login
RUN dotnet publish LibreKO.Game/LibreKO.Game.csproj -c Release -o /app/game \
    && cp -r LibreKO.Game/Map /app/game/Map

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS login
WORKDIR /app
COPY --from=build /app/login .
EXPOSE 15100
ENTRYPOINT ["dotnet", "LibreKO.Login.dll"]

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS game
WORKDIR /app
COPY --from=build /app/game .
EXPOSE 15001
ENTRYPOINT ["dotnet", "LibreKO.Game.dll"]

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY TakeTaikoSimulator/Updates/GitHubReleaseSource.cs TakeTaikoSimulator/Updates/InviteReleaseSource.cs TakeTaikoSimulator/Updates/UpdateFeed.cs ./TakeTaikoSimulator/Updates/
COPY tools/installer/InviteRelay/InviteRelay.csproj tools/installer/InviteRelay/Program.cs ./tools/installer/InviteRelay/
RUN dotnet publish tools/installer/InviteRelay/InviteRelay.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out/ ./
ENV ASPNETCORE_HTTP_PORTS=10000
EXPOSE 10000
USER $APP_UID
ENTRYPOINT ["dotnet", "InviteRelay.dll"]

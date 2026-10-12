# Build from the repository root:
#   docker build -f src/DutyHuddle.Relay/Dockerfile -t dutyhuddle-relay .
#   docker run -p 8080:8080 dutyhuddle-relay
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .
COPY src/DutyHuddle.Relay/ src/DutyHuddle.Relay/
RUN dotnet publish src/DutyHuddle.Relay/DutyHuddle.Relay.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "DutyHuddle.Relay.dll"]

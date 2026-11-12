FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY TrackDesk.sln ./
COPY src/TrackDesk.Api/TrackDesk.Api.csproj src/TrackDesk.Api/
COPY tests/TrackDesk.Tests/TrackDesk.Tests.csproj tests/TrackDesk.Tests/
RUN dotnet restore
COPY . .
RUN dotnet publish src/TrackDesk.Api -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir /data && chown $APP_UID /data
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__Default="Data Source=/data/trackdesk.db"
# Jwt__Key (en az 32 karakter) çalıştırırken verilmelidir.
EXPOSE 8080
VOLUME /data
ENTRYPOINT ["dotnet", "TrackDesk.Api.dll"]

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY FacilityOps.sln ./
COPY src/FacilityOps.Api/FacilityOps.Api.csproj src/FacilityOps.Api/
COPY tests/FacilityOps.Tests/FacilityOps.Tests.csproj tests/FacilityOps.Tests/
RUN dotnet restore
COPY . .
RUN dotnet publish src/FacilityOps.Api -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir /data && chown $APP_UID /data
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__Default="Data Source=/data/facilityops.db"
# Jwt__Key (en az 32 karakter) çalıştırırken verilmelidir.
EXPOSE 8080
VOLUME /data
ENTRYPOINT ["dotnet", "FacilityOps.Api.dll"]

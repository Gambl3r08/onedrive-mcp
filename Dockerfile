FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/OneDriveMcp/OneDriveMcp.csproj OneDriveMcp/
RUN dotnet restore OneDriveMcp/OneDriveMcp.csproj
COPY src/OneDriveMcp/ OneDriveMcp/
RUN dotnet publish OneDriveMcp/OneDriveMcp.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
# /data holds the MSAL token cache (refresh token); owned by the non-root app user.
RUN mkdir /data && chown $APP_UID /data
COPY --from=build /app .
USER $APP_UID
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "OneDriveMcp.dll"]

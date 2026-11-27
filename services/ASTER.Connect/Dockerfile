FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ASTER.Connect.csproj ./
RUN dotnet restore ASTER.Connect.csproj
COPY . ./
RUN dotnet publish ASTER.Connect.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "ASTER.Connect.dll"]

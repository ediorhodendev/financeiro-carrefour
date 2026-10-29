FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
WORKDIR /src
COPY . .
RUN dotnet restore Financial.slnx
RUN dotnet publish src/Financial.Api/Financial.Api.csproj -c Release --no-restore -o /out
FROM build AS tests
RUN dotnet build Financial.slnx -c Release --no-restore
ENTRYPOINT ["dotnet", "test", "Financial.slnx", "-c", "Release", "--no-build", "--logger", "trx", "--collect:XPlat Code Coverage"]
FROM mcr.microsoft.com/dotnet/aspnet:10.0.14
WORKDIR /app
COPY --from=build /out .
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Financial.Api.dll"]

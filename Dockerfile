# syntax=docker/dockerfile:1.7

# ---------------------------
# Build
# ---------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj first for better layer caching
COPY CalAssistant.csproj ./
RUN dotnet restore ./CalAssistant.csproj

COPY . ./
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# ---------------------------
# Runtime
# ---------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# HTTP endpoint for container
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "CalAssistant.dll"]


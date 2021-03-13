#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:5.0-buster-slim AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:5.0-buster-slim AS build
WORKDIR /src
COPY ["PrawkoChecker.csproj", "./"]
RUN dotnet restore "PrawkoChecker.csproj"
COPY . .
RUN dotnet build "PrawkoChecker.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PrawkoChecker.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
RUN sed -i "s|TLSv1.2|TLSv1.0|g" /etc/ssl/openssl.cnf
ENTRYPOINT ["dotnet", "PrawkoChecker.dll"]
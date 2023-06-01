#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["Esw.SwaggerEditor/Esw.SwaggerEditor.csproj", "Esw.SwaggerEditor/"]
RUN dotnet restore "Esw.SwaggerEditor/Esw.SwaggerEditor.csproj"
COPY . .
WORKDIR "/src/Esw.SwaggerEditor"
RUN dotnet build "Esw.SwaggerEditor.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Esw.SwaggerEditor.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Esw.SwaggerEditor.dll"]
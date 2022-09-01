FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["BondPrototype/BondPrototype.csproj", "BondPrototype/"]
COPY ["Translator/Translator.csproj", "Translator/"]
RUN dotnet restore "BondPrototype/BondPrototype.csproj"
COPY . .
WORKDIR "/src/BondPrototype"
RUN dotnet build "BondPrototype.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "BondPrototype.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BondPrototype.dll"]

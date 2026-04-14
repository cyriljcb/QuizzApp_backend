# ── Étape 1 : build ──────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copie et restore des dépendances en premier
# (optimise le cache Docker — si les .csproj ne changent pas,
#  cette couche est réutilisée)
COPY *.csproj .
RUN dotnet restore

# Copie du reste du code et build
COPY . .
RUN dotnet publish -c Release -o /app/publish

# ── Étape 2 : runtime ────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Copie uniquement le binaire publié
COPY --from=build /app/publish .

# Crée le dossier data pour SQLite et les questions
RUN mkdir -p data/questions

# Port exposé
EXPOSE 5000

# Variable d'environnement pour ASP.NET Core
ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "QuizzBackend.dll"]
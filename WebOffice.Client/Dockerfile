# Стадия 1: Сборка (publish)
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Копируем csproj и восстанавливаем зависимости (кэширование)
COPY ["WebOffice.Client/WebOffice.Client.csproj", "WebOffice.Client/"]
RUN dotnet restore "WebOffice.Client/WebOffice.Client.csproj"

# Копируем весь код
COPY . .
WORKDIR "/src/WebOffice.Client"

# Публикуем в /app/publish (Blazor WASM → статические файлы)
RUN dotnet publish "WebOffice.Client.csproj" -c Release -o /app/publish /p:BlazorEnableCompressionDuringPublish=false

# Стадия 2: Финальный образ — просто nginx + скопированные файлы
FROM nginx:alpine AS final
WORKDIR /usr/share/nginx/html

# Удаляем дефолтный nginx-контент
RUN rm -rf ./*

# Копируем опубликованные статические файлы Blazor WASM
COPY --from=build /app/publish/wwwroot .

# Копируем кастомный nginx.conf для Blazor (если нужно SPA-роутинг)
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80

# Nginx уже запускается по умолчанию
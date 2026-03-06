**Список основных компонентов системы (сервисы)**


**Frontend** (клиентская часть): Blazor WebAssembly (WASM)  

**Backend:** Web API на ASP.NET Core Аутентификация → ASP.NET Identity (JWT или Cookie)  

**Хранилище файлов:** MinIO (S3-совместимое объектное хранилище)  

**Онлайн-редактор:** OnlyOffice Document Server (Docker) Поддержка протокола WOPI (включён обязательно)  

**Прокси / обратный прокси** (рекомендуется): Nginx или Traefik (для удобного https и маршрутизации)  

**Браузер** → обычный веб-доступ через браузер


## Быстрый старт для разработки

1. Установи Docker
2. Клонируй репозиторий
3. Запусти сервисы: `docker compose up` (MinIO, OnlyOffice и Nginx запустятся автоматически).
4. Запусти backend: `cd WebOffice.Api && dotnet run`.
5. Запусти frontend: `cd ../WebOffice.Client && dotnet run`.
6. Доступ:
   - MinIO console: http://localhost:9001 (admin/admin123).
   - OnlyOffice: http://localhost:8080.
   - Приложение: http://localhost (через Nginx).
7. Остановка: `docker compose down`.

## Версии образов 

1. MinIO: RELEASE.2024-02-17T01-15-57Z
2. OnlyOffie: 8.0.1
3. Nginx: 1.27.0

# WebOffice
<img width="1734" height="718" alt="image" src="https://github.com/user-attachments/assets/83f19271-dbcd-4ab8-897b-7e691bd24509" />

WOPI арқылы OnlyOffice интеграциясымен онлайн кеңсе құжаттарын өңдеу жүйесі (Система онлайн-редактирования офисных документов с интеграцией OnlyOffice через WOPI)

# Іске қосу нұсқаулары (Инструкции по запуску)

<details>

## Дамытуға арналған жылдам бастау (Быстрый старт для разработки)
1. Репозиторийді клондаңыз (Склонируйте репозиторий): `git clone https://github.com/sisharppravi/WebOffice.git`

2. `.env` файлын көшіріңіз (Скопируйте файл `.env`)

```env
# MinIO
MINIO_ROOT_USER=admin
MINIO_ROOT_PASSWORD=admin123
MINIO_BUCKET=weboffice-files

# OnlyOffice
ONLYOFFICE_JWT_SECRET=78YsTwvZAo646cK0BRZn2yJYps26Wx4M7sfnvzTd0nY=

# Nginx
NGINX_PORT=80
```

3. Бэкендті іске қосыңыз (Запустите backend): `cd WebOffice.Api && dotnet run`

4. Frontend-ті іске қосыңыз (Запустите frontend): `cd ../WebOffice.Client && dotnet run`

5. Инфрақұрылымды іске қосыңыз (Запустите инфраструктуру) `(MinIO + OnlyOffice + Nginx)`: `docker compose up`

Шолғышта `http://localhost` мекенжайын ашыңыз (Откройте в браузере `http://localhost`)

Жаңа пайдаланушыны тіркеп, жүйеге кіріңіз (Зарегистрируйте нового пользователя и войдите в систему)
![img.png](img.png)
![img_1.png](img_1.png)

Жаңа құжат жасап, OnlyOffice-пен интеграцияның дұрыс жұмыс істеп тұрғанына көз жеткізу үшін оны өңдеңіз (Создайте новый документ и отредактируйте его, чтобы убедиться, что интеграция с OnlyOffice работает корректно)
![img_2.png](img_2.png)
![img_3.png](img_3.png)

</details>

# Қолданылған технологиялар (Использованные технологии)

<details>

## Backend (WebOffice.Api)

| Технология (Технология) | Нұсқасы (Версия) |
|---|---|
| .NET / ASP.NET Core | 9.0 |
| Entity Framework Core | 9.0.14 |
| Microsoft.EntityFrameworkCore.Sqlite | 9.0.14 |
| Microsoft.AspNetCore.Identity | 2.3.9 |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 9.0.14 |
| Microsoft.AspNetCore.Authentication.JwtBearer | 9.0.3 |
| Microsoft.IdentityModel.Tokens | 8.16.0 |
| System.IdentityModel.Tokens.Jwt | 8.16.0 |
| Microsoft.AspNetCore.OpenApi | 9.0.8 |
| Swashbuckle.AspNetCore (Swagger) | 9.0.6 |
| Minio (.NET SDK) | 7.0.0 |

## Frontend (WebOffice.Client)

| Технология (Технология) | Нұсқасы (Версия) |
|---|---|
| .NET / Blazor WebAssembly | 9.0 |
| Microsoft.AspNetCore.Components.WebAssembly | 9.0.8 |

## Инфрақұрылым (Docker инфраструктура)

| Образ (Образ) | Нұсқасы (Версия) |
|---|---|
| MinIO | RELEASE.2024-02-17T01-15-57Z |
| OnlyOffice Document Server | 8.0.1 |
| Nginx | 1.27.0 |

Жұмысты бастамас бұрын `dotnet ef database update` командасын іске қосыңыз (Перед началом работы выполните команду `dotnet ef database update`)

Swagger-пен жұмыс істеу үшін шолғышта `https://localhost:7130/swagger` мекенжайына өтіңіз (Для работы со Swagger перейдите в браузере по адресу `https://localhost:7130/swagger`)

</details>

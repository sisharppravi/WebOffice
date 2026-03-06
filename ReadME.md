# Docker init

<details>
   
## Быстрый старт для разработки
   
1. Установить Docker
2. Клонировать репозиторий
3. Запустить сервисы: `docker compose up` (MinIO, OnlyOffice и Nginx запустятся автоматически).
4. Запустить backend: `cd WebOffice.Api && dotnet run`.
5. Запустить frontend: `cd ../WebOffice.Client && dotnet run`.
6. Доступ:
   - MinIO console: http://localhost:9001 (admin/admin123).
   - OnlyOffice: http://localhost:8080.
   - Приложение: http://localhost (через Nginx).
7. Остановка: `docker compose down`.

## Версии образов ( Прописаны в docker-compose.yaml ) 

1. MinIO: **RELEASE.2024-02-17T01-15-57Z**
2. OnlyOffie: **8.0.1**
3. Nginx: **1.27.0**

## Содержание .env файла

      # MinIO
      MINIO_ROOT_USER=admin
      MINIO_ROOT_PASSWORD=admin123
      MINIO_BUCKET=weboffice-files
      
      # OnlyOffice
      ONLYOFFICE_JWT_SECRET=78YsTwvZAo646cK0BRZn2yJYps26Wx4M7sfnvzTd0nY=
      
      # Nginx 
      NGINX_PORT=80

</details>

#.NET

<details>

   Test
   
</details>

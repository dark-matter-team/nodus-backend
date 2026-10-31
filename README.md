# Nodus Backend (.NET 10 Web API)

## Вимоги
- **.NET 10 SDK**
- **PostgreSQL 16** (локально або через Docker)

## Швидкий старт (Локальний запуск)

1. **Запустити базу даних PostgreSQL** (якщо використовується Docker):
   ```bash
   docker compose up -d
   ```
   *(Або використовувати локально встановлений PostgreSQL 16 на порту `5432`, налаштування підключення — у `Nodus.Api/appsettings.Development.json`).*

2. **Застосувати міграції БД (створити таблиці):**
   ```bash
   cd Nodus.Api
   dotnet ef database update
   ```

3. **Запустити сервер та відкрити Swagger UI:**
   ```bash
   dotnet run
   ```
   Після запуску сторінка з усіма 20 ендпоінтами доступна в браузері за адресою:
   `http://localhost:5033/swagger`

## Структура проєкту (`Nodus.Api`)
- `Enums/` — перерахування (`KnowledgeLevel`, `RoadmapStatus`, `NodeStatus`, `NodeCategory`)
- `Entities/` — моделі таблиць БД PostgreSQL (`User`, `Roadmap`, `RoadmapNode`, `NodeChecklistItem`)
- `Data/` — `AppDbContext` та налаштування зв'язків БД
- `DTOs/` — контракти запитів та відповідей JSON (`AuthDtos`, `RoadmapDtos`, `NodeDtos`, `CatalogDtos`)
- `Controllers/` — 4 контролери API (20 ендпоінтів)
- `Services/` — бізнес-логіка (`AuthService`, `RoadmapService`, `CatalogService`, `AiGenerationService`)
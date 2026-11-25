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

## Структура проєкту

Чотири проєкти-шари. Межі між ними перевіряє компілятор: шар не може використати код шару, на який у нього немає посилання.

| Проєкт | Що містить | Залежить від |
|---|---|---|
| `Nodus.Domain` | Сутності (`User`, `Roadmap`, `RoadmapNode`, `NodeChecklistItem`, `NodeResource`), enum'и, `FieldLimits` | нічого |
| `Nodus.Application` | Сценарії (сервіси Roadmaps, Nodes, Catalog, Auth, Generation) та їхні DTO; інтерфейси до всього зовнішнього (`IAppDbContext`, `IEmailSender`, `IAiGenerationClient`, `ITokenService`, `IPasswordHasher`, `IGoogleTokenValidator`, `IClock`); винятки; options | `Domain` |
| `Nodus.Infrastructure` | Реалізації інтерфейсів: `AppDbContext` + конфігурації сутностей + міграції, MailKit, клієнт AI-сервісу, JWT / BCrypt / Google, фонові воркери | `Application`, `Domain` |
| `Nodus.Api` | Контролери, middleware (обробка помилок, rate limits, перевірка версії токена), `Program.cs` — збирає все через DI | `Application`, `Infrastructure` |

**Правило залежностей:** посилання йдуть лише всередину: `Api → Infrastructure → Application → Domain`. `Domain` і `Application` нічого не знають про HTTP, PostgreSQL, SMTP чи AI-провайдера — лише про свої інтерфейси.

**Куди класти новий код:**
- нова таблиця / поле → сутність у `Domain`, конфігурація й міграція в `Infrastructure`;
- нова бізнес-логіка → сервіс у `Application`;
- виклик зовнішньої системи → інтерфейс в `Application`, реалізація в `Infrastructure`;
- новий ендпоінт → тонкий контролер в `Api`, який лише викликає сервіс з `Application`.

**Окремого репозиторію на кожну сутність немає:** сервіси працюють через `IAppDbContext`, бо EF Core вже реалізує repository і unit of work.

**Стан переходу:** зараз у `Domain` перенесено сутності, enum'и та `FieldLimits`; решта коду поки лежить у `Nodus.Api` і переїжджає невеликими PR без зміни поведінки. Новий код одразу пишемо у правильному шарі.

`Nodus.Agent` — тимчасова in-process копія AI-генерації. Її приберуть, коли бекенд перейде на окремий AI-сервіс.
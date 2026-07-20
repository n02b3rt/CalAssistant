# AGENTS.md — CalAssistant

Canonical guide for AI agents (Cursor, Claude Code, Copilot, etc.) and humans working in this repo.
`CLAUDE.md` points here. **Keep this file up to date whenever architecture changes.**

---

## What this project is

A web-based **Google Calendar chat assistant** — users talk to it in English.

| User says… | Result |
|---|---|
| "what do I have today?" | Fetches events → day timeline (`DayTimeline`) + summary |
| "schedule a meeting with Kate tomorrow at 3pm" | Creates a Google Calendar event |
| "remind me about meds at 9pm" | Creates an event with a popup reminder |

- **LLM:** local `qwen3:4b` via [Ollama](https://ollama.com) — conversations never leave your machine.
- **LLM framework:** [MaIN.NET](https://github.com/mobitouchOS/MaIN.NET) 10.1.0 (NuGet) with native **tool calling**.
- **UI:** Blazor Server (.NET 10), Interactive Server mode (SignalR).
- **Calendar:** Google Calendar API v3, Desktop app OAuth.
- **License:** MIT — see `LICENSE`.

---

## Stack

| Layer | Technology | Version / notes |
|---|---|---|
| Runtime | .NET | 10.0 (`net10.0`) |
| UI | Blazor Web App | Interactive Server |
| LLM framework | MaIN.NET | 10.1.0 |
| Model | `qwen3:4b` | Ollama, `http://localhost:11434` |
| Calendar | Google.Apis.Calendar.v3 | Desktop OAuth |
| Container | Docker + Compose | 3 services: gateway (:8080), app, ollama |

### External dependencies (not in repo)

Run the setup script — it handles everything automatically:

```powershell
# Windows
.\setup.ps1

# Linux / macOS
chmod +x setup.sh && ./setup.sh
```

Or manually:

```bash
ollama pull qwen3:4b
ollama list          # must show qwen3:4b
```

`credentials.json` from Google Cloud Console — see `README.md`.

---

## Architecture

`qwen3:4b` supports **tool calling** in Ollama. There is no manual intent router or JSON parsing from the model — MaIN.NET manages the tool-call loop.

```
User (Chat.razor)
    │
    ▼
AssistantService.SendAsync()
    │
    ▼
AssistantService.RunWithToolsAsync()
    │
    ├─► IMaINHub.Chat()
    │       .WithModel("qwen3:4b")
    │       .WithMessage(userText)
    │       .WithSystemPrompt(...)
    │       .WithTools(list_day, create_event)
    │       .CompleteAsync()
    │
    ├─► [tool: list_day]      → CalendarService.GetDayAsync()
    ├─► [tool: create_event]  → CalendarService.CreateEventAsync()
    │
    ▼
Model response (English text)
    │
    ▼
Chat.razor renders DayTimeline when message carries a DayPlan
```

### Responsibility split

| Component | Responsibility |
|---|---|
| `AssistantService` | Chat orchestration, MaIN tool registration, conversation state |
| `CalendarService` | **Only** place with Google Calendar operations (OAuth, read, write) |
| `DayTimeline.razor` | Day visualization from real API data — not model hallucinations |
| LLM model | Language understanding, tool selection, response wording |

The model **never** touches Google API directly — only through C#-registered tools.

---

## File map

```
CalAssistant/
├── setup.ps1 / setup.sh          # automated environment setup
├── Program.cs                    # DI, ASP.NET pipeline, AddMaIN(Ollama), model registration
├── CalAssistant.csproj           # NuGet dependencies
├── Dockerfile                    # multi-stage build (SDK → aspnet runtime)
├── docker-compose.yml            # port 5136:8080, OAuth volumes, Ollama host
├── .dockerignore / .gitignore
├── LICENSE                       # MIT
├── README.md                     # user documentation
├── CLAUDE.md                     # shortcuts for Claude Code → points here
│
├── Models/
│   └── AssistantModels.cs        # ChatMessage, CalEvent, DayPlan
│
├── Services/
│   ├── AssistantService.cs       # orchestrator + tool calling (Scoped)
│   └── CalendarService.cs        # Google Calendar API (Singleton)
│
├── Components/
│   ├── Pages/
│   │   ├── Chat.razor(.css)      # main page "/"
│   │   ├── Error.razor
│   │   └── NotFound.razor
│   ├── DayTimeline.razor(.css)   # day timeline view
│   ├── Layout/                   # MainLayout, ReconnectModal
│   ├── App.razor, Routes.razor
│   └── _Imports.razor
│
├── wwwroot/app.css               # dark theme, CSS variables
├── appsettings.json
├── Properties/launchSettings.json
│
├── credentials.json              # LOCAL ONLY — never commit
└── token-store/                  # OAuth token cache — never commit
```

---

## Key types (`Models/AssistantModels.cs`)

| Type | Role |
|---|---|
| `ChatMessage` | Single chat bubble; optionally carries `Day` (DayPlan) or `CreatedEvent` |
| `DayPlan` | Day plan: date + list of `CalEvent` |
| `CalEvent` | Simplified event (title, start, end, location) |
| `ChatRole` | `User` / `Assistant` |

---

## MaIN tools (`AssistantService.RunWithToolsAsync`)

| Tool | Parameters | Calls |
|---|---|---|
| `list_day` | `date` (YYYY-MM-DD) | `CalendarService.GetDayAsync(date)` |
| `create_event` | `title`, `start`, `end?`, `location?`, `reminder_minutes?` | `CalendarService.CreateEventAsync(...)` |

Config: `WithToolChoice("auto")`, `WithMaxIterations(5)`, temperature `0.4f`.

After `list_day` runs, the resulting `DayPlan` is attached to `ChatMessage.Day` — UI renders `DayTimeline`.

---

## DI and lifetimes

| Service | Registration | Why |
|---|---|---|
| `CalendarService` | `Singleton` | Single user, single OAuth token |
| `AssistantService` | `Scoped` | Conversation state per Blazor circuit (SignalR) |
| `IMaINHub` | via `AddMaIN()` | MaIN.NET hub |

---

## Configuration (env / appsettings)

| Key | Default | Description |
|---|---|---|
| `MaIN__OllamaBaseUrl` | `http://localhost:11434` | Ollama endpoint; in Docker Compose: `http://ollama:11434` |
| `Google__CredentialsPath` | `credentials.json` | Path to OAuth credentials |
| `EnableHttpsRedirection` | `false` | `true` only behind a TLS reverse proxy |
| `ASPNETCORE_URLS` | `http://+:8080` (Docker) | Bind address |

---

## Running

### Automated setup (recommended)

```powershell
# Windows
.\setup.ps1

# Linux / macOS
chmod +x setup.sh && ./setup.sh
```

The script checks/installs .NET 10, Ollama, pulls `qwen3:4b`, creates folders, and builds the project.

### Local dev

```powershell
dotnet run
# → http://localhost:5136
```

### Docker (full stack)

```powershell
docker compose up --build
# → http://localhost:8080  (nginx gateway)
```

Stack:
- **gateway** — nginx, public port 8080, WebSocket proxy for Blazor SignalR
- **calassistant** — app container (internal port 8080, not exposed to host)
- **ollama** — LLM with persistent volume `ollama_data`
- **ollama-init** — one-shot `ollama pull qwen3:4b`

Requirements:
- `./credentials.json` and `./token-store` mounted as volumes
- First run downloads the model (~2.5 GB) — be patient

---

## MaIN.NET gotchas (read before editing `AssistantService`)

### 1. Call chain order

`WithSystemPrompt` is on `IChatConfigurationBuilder` — **only after** `WithMessage`:

```csharp
// correct
_hub.Chat()
    .WithModel(ModelName)
    .WithMessage(userText)
    .WithSystemPrompt(system)
    .WithTools(tools)
    .CompleteAsync();

// wrong — CS1061: WithSystemPrompt doesn't exist on IChatMessageBuilder
_hub.Chat()
    .WithModel(ModelName)
    .WithSystemPrompt(system)   // compile error
    .WithMessage(userText)
```

### 2. Model registration in ModelRegistry

`WithModel()` requires the model to be registered in `ModelRegistry`.
MaIN.NET 10.1.0 has no built-in constant for `qwen3:4b` (only `qwen3:8b`, `qwen3:14b`, etc.).

**Already handled in `Program.cs`:**

```csharp
ModelRegistry.RegisterOrReplace(
    new GenericCloudModel(AssistantService.ModelName, BackendType.Ollama, "Qwen3 4B (Ollama)"));
```

### 3. `qwen3:4b` vs `gemma3:4b`

The project originally used `gemma3:4b` with a manual JSON intent router (gemma doesn't support tool calling in Ollama).
**We now use only `qwen3:4b` + tool calling.** Do not revert to the intent router without an explicit decision.

---

## Google Calendar OAuth

Summary — full instructions in `README.md`:

1. Google Cloud Console → project → enable **Calendar API**.
2. OAuth consent screen (External) → add yourself as **Test user**.
3. Credentials → **OAuth client ID → Desktop app** → download JSON.
4. Save as `credentials.json` in the project root.
5. Run app → click **Connect calendar** → token saved in `token-store/`.

**Never commit:** `credentials.json`, `token-store/`.

---

## Code conventions

- UI language, model responses, and code comments: **English**.
- Calendar operations only in `CalendarService` — not in Blazor components or tool lambdas beyond delegation.
- Model name: constant `AssistantService.ModelName` — single source of truth.
- Don't add tests or abstractions "just in case" — only when requested or they cover real behavior.
- Don't commit generated files (`bin/`, `obj/`) or secrets.

---

## Verification after changes

1. `dotnet build` — zero errors (NU190x from MaIN are warnings, not blockers).
2. `dotnet run` → send `what can you do?` → response without Google (tests MaIN→Ollama).
3. With calendar connected: `what do I have today?` → `DayTimeline` appears.
4. Docker: `docker compose up --build` → `http://localhost:8080`.

---

## Troubleshooting

| Problem | Cause / fix |
|---|---|
| `ModelNotRegisteredException` | Check `Program.cs` model registration |
| `CS1061 WithSystemPrompt` | Wrong chain order — `WithMessage` before `WithSystemPrompt` |
| Model doesn't respond | `ollama list`, `ollama serve`, check `MaIN__OllamaBaseUrl` |
| Docker: no Ollama | Run full stack: `docker compose up --build` (Ollama is a container now) |
| Missing credentials.json | Place file in project root or mount in Docker volume |
| 403 on Google login | Add Gmail as Test user in OAuth consent screen |
| `rpc error ... EOF` on docker build | Restart Docker Desktop, `docker builder prune -af` |
| Setup script fails on .NET | Install .NET 10 SDK manually |

---

## TODO

- [ ] Tools `update_event` / `delete_event`.
- [ ] Streaming responses (`CompleteAsync(changeOfValue: …)`).
- [ ] Week view / date range.
- [ ] Multi-user support (today singleton = one user).
- [ ] Conflict detection on `create_event`.

---

## Links

- MaIN.NET: https://github.com/mobitouchOS/MaIN.NET
- MaIN docs: https://www.usemain.net
- Ollama: https://ollama.com

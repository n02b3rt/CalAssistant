# AGENTS.md — CalAssistant

Canonical guide for AI agents (Cursor, Claude Code, Copilot, etc.) and humans working in this repo.
`CLAUDE.md` points here. **Keep this file up to date whenever architecture changes.**

---

## What this project is

A web-based **Google Calendar chat assistant**. Users talk to it in **Polish or English** and it
replies in the same language.

| User says… | Result |
|---|---|
| "co mam dziś do zrobienia?" / "what do I have today?" | Fetches events → visual day timeline (`DayTimeline`) + summary |
| "umów spotkanie z Kasią jutro o 15" | Checks the slot is free, then creates the event (asks first if details are missing) |
| "przypomnij mi o lekach o 21" | Creates an event with a popup reminder |

- **LLM:** local **`qwen3:1.7b`** via [Ollama](https://ollama.com) — conversations never leave your machine.
- **LLM framework:** [MaIN.NET](https://github.com/mobitouchOS/MaIN.NET) 10.1.0 (NuGet) with native **tool calling**.
- **UI:** Blazor Server (.NET 10), Interactive Server mode (SignalR).
- **Calendar:** Google Calendar API v3, Desktop-app OAuth.

### Why `qwen3:1.7b` (not `qwen3:4b` or `gemma3:4b`)

- **`gemma3:4b`** does **not** support tool calling in Ollama (`does not support tools`) — ruled out.
- **`qwen3:4b`** supports tools but is 2.5 GB; on a 4 GB-VRAM GPU it runs **~33% CPU / 67% GPU** (spills, slow).
- **`qwen3:1.7b`** supports tools and fits **100% on GPU** in 4 GB → fast. This is the default.

The model is **configurable** — see [Configuration](#configuration). Anything that supports Ollama tool
calling works (e.g. set `Assistant:Model` to `qwen3:4b` for higher quality if you have the VRAM).

---

## Stack

| Layer | Technology | Version / notes |
|---|---|---|
| Runtime | .NET | 10.0 (`net10.0`), SDK at `C:\Program Files\dotnet\dotnet.exe` |
| UI | Blazor Web App | Interactive Server |
| LLM framework | MaIN.NET | 10.1.0 |
| Model | `qwen3:1.7b` (default) | Ollama, `http://localhost:11434` |
| Calendar | Google.Apis.Calendar.v3 | Desktop OAuth |
| Container | Docker + Compose | gateway (:8080), app, optional bundled ollama |

External deps: `ollama pull qwen3:1.7b` (or run `setup.ps1` / `setup.sh`), and `credentials.json` from
Google Cloud (see `README.md`).

---

## Architecture

`qwen3:1.7b` supports **native tool calling**. There is no manual intent router / JSON parsing — MaIN.NET
runs the tool-call loop. Deterministic, sensitive operations (reading/writing the calendar, conflict
detection, confirmations) live in **C#**; the model only understands intent, picks tools, and phrases replies.

```
User (Chat.razor)
   │
   ▼
AssistantService.SendAsync(userText)          // appends user msg, raises Busy
   │
   ▼
AssistantService.RunWithToolsAsync()
   ├─ detect language (PL/EN)                  // deterministic, forces reply language
   ├─ build conversation history               // full multi-turn context (see gotcha #1)
   ├─ IMaINHub.Chat()
   │     .WithModel("qwen3:1.7b")
   │     .WithMessages(history)                // history LAST item = current user turn
   │     .WithSystemPrompt(rules)
   │     .WithInferenceParams(temp 0.2, max_tokens 500, think=false)
   │     .WithTools(list_day, check_availability, create_event)
   │     .CompleteAsync(cancellationToken: 60s timeout)
   │
   ├─ [tool] list_day          → CalendarService.GetDayAsync()      → attaches DayPlan to the message
   ├─ [tool] check_availability→ CalendarService.GetConflictsAsync()
   └─ [tool] create_event      → conflict check, then CalendarService.CreateEventAsync()
   │
   ▼
Reply (in the user's language). Created-event confirmations are deterministic (not model prose).
Chat.razor renders DayTimeline when the message carries a DayPlan.
```

### Responsibility split

| Component | Responsibility |
|---|---|
| `AssistantService` | Orchestration, tool registration, conversation state, generation limits, live status (Scoped) |
| `CalendarService` | **Only** place with Google Calendar operations — OAuth, read, create, conflict lookup (Singleton) |
| `Localizer` | PL/EN UI strings + selected language; drives both chrome and the assistant's reply language (Scoped) |
| `DayTimeline.razor` | Day visualization from real API data — never from model output |
| LLM model | Language understanding, tool selection, wording |

### UI / UX

- **Light theme** (`wwwroot/app.css`): warm off-white + subtle dot texture, hairline borders, one calm green
  accent, ink primary buttons. No gradients. CSS variables drive everything.
- **Bilingual (PL/EN)** via `Localizer` — a `PL | EN` segmented toggle in the chat header switches the whole UI
  *and* the assistant's reply language live (`Localizer.LanguageName` → system prompt). Add UI text as keys in
  `Localizer.Pl` / `Localizer.En`, never hard-code strings in components.
- **Live status** (not just a spinner): `AssistantService.StatusKey` updates as work happens. MaIN's
  `CompleteAsync(toolCallback: OnToolInvoked)` fires per tool call → mapped to a plain-language status
  (`status.reading` / `status.checking` / `status.creating`). The busy bubble shows `Loc[StatusKey]`.
- **DayTimeline**: header (weekday + localized date) · stat tiles · a horizontal "day rail" busy strip with a
  now-marker · "next up" · event cards with soft per-title colors (`color-mix`). Culture from `Localizer.Culture`.
- Auto-scroll via a tiny JS helper (`calAssist.scrollToBottom` in `App.razor`).

---

## File map

```
CalAssistant/
├── setup.ps1 / setup.sh          # automated environment setup
├── Program.cs                    # DI, ASP.NET pipeline, AddMaIN(Ollama), model registration from config
├── CalAssistant.csproj
├── Dockerfile                    # multi-stage (SDK → aspnet runtime)
├── docker-compose.yml            # gateway(:8080) + app + optional bundled ollama (profile)
├── docker-compose.gpu.yml        # NVIDIA override for bundled ollama
├── docker/nginx.conf             # reverse proxy (WebSocket for Blazor SignalR)
├── .dockerignore / .gitignore
├── README.md                     # user documentation (incl. Google OAuth)
├── CLAUDE.md                     # shortcuts for Claude Code → points here
│
├── Models/AssistantModels.cs     # ChatMessage, CalEvent, DayPlan
├── Services/
│   ├── AssistantService.cs       # orchestrator + tool calling (Scoped)
│   └── CalendarService.cs        # Google Calendar API (Singleton)
├── Components/
│   ├── Pages/Chat.razor(.css)    # main page "/"
│   ├── DayTimeline.razor(.css)   # day timeline view
│   └── Layout/, App.razor, Routes.razor, _Imports.razor
├── wwwroot/app.css               # dark theme, CSS variables
├── appsettings.json              # Assistant:Model = qwen3:1.7b
│
├── credentials.json              # LOCAL ONLY — never commit
└── token-store/                  # OAuth token cache — never commit
```

---

## MaIN tools (`AssistantService.BuildTools`)

| Tool | Parameters | Calls | Notes |
|---|---|---|---|
| `list_day` | `date` (YYYY-MM-DD) | `CalendarService.GetDayAsync` | Attaches `DayPlan` → UI renders `DayTimeline` |
| `check_availability` | `start`, `end` (ISO) | `CalendarService.GetConflictsAsync` | Returns FREE / BUSY |
| `create_event` | `title`, `start`, `end?`, `location?`, `reminder_minutes?`, `force?` | conflict check → `CalendarService.CreateEventAsync` | Returns `CONFLICT` (and does NOT create) unless `force=true` |

Config: `WithToolChoice("auto")`, `WithMaxIterations(5)`, temperature `0.2`, `max_tokens 500`, `think=false`.

---

## ⚠️ MaIN.NET / small-model gotchas (read before editing `AssistantService`)

### 1. Conversation MUST start with a user message  *(most important)*
`WithSystemPrompt` inserts a `System` message at index 0. If the next message is an assistant turn
(e.g. the UI greeting), the conversation becomes `system → assistant → …`, and **qwen3:1.7b stops
emitting tool calls** — it just replies with text (events silently don't get created).
`BuildConversation()` therefore **drops the greeting and any leading assistant turns** so the history
starts on a user turn. Do not re-add the greeting to the model context.

### 2. Disable Qwen3 "thinking" for speed
Qwen3 reasons by default. Left on (no token cap) a single turn took **30–75 s** and could run away.
We send `think=false` via `OllamaInferenceParams.AdditionalParams` (Ollama's `ApplyBackendParams` only
maps Temperature/MaxTokens/TopP/TopK/NumCtx/NumGpu; extra keys go through `AdditionalParams`). Result: ~3–10 s/call.

### 3. Bound generation
`MaxTokens = 500` **and** a `CancellationTokenSource(60s)` on `CompleteAsync`. A small model can otherwise
loop and hit the 100 s HttpClient timeout. The cancellation is caught in `SendAsync` → friendly retry message.

### 4. Small models misstate details in prose
qwen3:1.7b once confirmed a July event as "21 **maja**". So **created-event confirmations are generated
deterministically in C#** (`FallbackText`) from the real `CalEvent`, not from the model's words.

### 5. Reply language
A tiny model ignores "reply in the user's language". We **detect PL/EN in C#** (`DetectLanguage`) and put an
explicit "reply ONLY in {language}" line in the system prompt.

### 6. Chain order
`WithSystemPrompt` is on `IChatConfigurationBuilder` — call it after `WithModel(...).WithMessages(...)`,
not before, or you get `CS1061`.

### 7. Model registration
`WithModel(id)` needs the id in `ModelRegistry`. MaIN 10.1.0 has no constant for `qwen3:1.7b`, so
`Program.cs` registers it: `ModelRegistry.RegisterOrReplace(new GenericCloudModel(modelName, BackendType.Ollama, ...))`.

### 8. `Models` name clash
`MaIN.Domain.Models.Models` vs our `CalAssistant.Models`. In `AssistantService` we reference MaIN types
fully or via `MaIN.Domain.Entities` (`Message`, `MessageType`).

---

## Configuration

| Key (appsettings) | Env var | Default | Description |
|---|---|---|---|
| `Assistant:Model` | `Assistant__Model` | `qwen3:1.7b` | Ollama model (must support tool calling) |
| `MaIN__OllamaBaseUrl` | `MaIN__OllamaBaseUrl` | `http://localhost:11434` | Ollama endpoint; Docker bundled: `http://ollama:11434` |
| `EnableHttpsRedirection` | same | `false` | `true` only behind a TLS reverse proxy |
| `ASPNETCORE_URLS` | same | `http://+:8080` (Docker) | Bind address |

---

## Running

```powershell
# Local dev
dotnet run                       # → http://localhost:5136, click "Connect calendar"

# Docker (app in container, Ollama on host = GPU)
docker compose up --build        # → http://localhost:8080

# Docker full stack (Ollama in a container too; add gpu.yml for NVIDIA)
docker compose --profile bundled-ollama up --build
docker compose -f docker-compose.yml -f docker-compose.gpu.yml --profile bundled-ollama up --build
```

GPU check: `ollama ps` should show the model at `100% GPU`. On the host (Windows) Ollama uses the GPU
automatically; in a bundled container you need the NVIDIA container toolkit + `docker-compose.gpu.yml`.

---

## Google Calendar OAuth
Summary — full steps in `README.md`:
1. Google Cloud Console → project → enable **Calendar API**.
2. OAuth consent screen (External) → add yourself as **Test user**.
3. Credentials → **OAuth client ID → Desktop app** → download JSON.
4. Save as `credentials.json` in the project root (watch out for `credentials.json.json` if Windows hides extensions).
5. Run app → **Connect calendar** → token cached in `token-store/`. **Never commit** `credentials.json` or `token-store/`.

---

## Verification after changes
1. `dotnet build` — zero errors (NU190x from MaIN are warnings).
2. `dotnet run` → send `co potrafisz?` → replies in Polish (tests MaIN→Ollama, no Google needed).
3. Connect calendar → `co mam jutro?` → `DayTimeline` renders; `umów spotkanie z X jutro o 15` creates it
   (and warns on a conflicting slot); `zaplanuj spotkanie` alone asks for missing details.

---

## Roadmap / TODO

- [ ] `update_event` / `delete_event` tools ("przełóż spotkanie na 17", "odwołaj call").
- [ ] Streaming the final reply token-by-token (`CompleteAsync(changeOfValue: …)`) on top of the phase status.
- [ ] Week / date-range view (reuse `DayTimeline` per day).
- [ ] Smarter relative-date parsing fallback in C# (belt-and-suspenders for the model).
- [ ] Multi-user support (today `CalendarService` is a singleton = one user/token).
- [ ] Recurring events, time-zone awareness, natural-language reminders ("na godzinę przed").
- [ ] Persist the selected language (localStorage) across reloads.

Done recently: light redesign, PL/EN UI + localized DayTimeline, live phase status, multi-turn memory,
slot-filling, conflict detection, GPU-fit model, latency bounds.

---

## Links
- MaIN.NET: https://github.com/mobitouchOS/MaIN.NET · docs: https://www.usemain.net
- Ollama: https://ollama.com

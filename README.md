# CalAssistant

> A Google Calendar chat assistant powered by a **local LLM** — your conversations never leave your machine.

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![MaIN.NET](https://img.shields.io/badge/MaIN.NET-10.1.0-blueviolet)](https://github.com/mobitouchOS/MaIN.NET)
[![Ollama](https://img.shields.io/badge/Ollama-qwen3%3A4b-orange)](https://ollama.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

Built with **Blazor Server (.NET 10)** and **[MaIN.NET](https://github.com/mobitouchOS/MaIN.NET)** — a local `qwen3:4b` model running through Ollama handles all natural language understanding via native **tool calling**. Google Calendar API handles the actual calendar operations.

---

## What it does

Talk to your Google Calendar in plain English:

| You say… | What happens |
|---|---|
| `what do I have today?` | Fetches today's events → day timeline + summary |
| `what's my schedule tomorrow?` | Fetches tomorrow's events → day timeline |
| `schedule a meeting with Kate tomorrow at 3pm` | Creates event 3:00–4:00 PM |
| `block 2 hours for the project on Friday from 10am` | Creates event 10:00 AM–12:00 PM |
| `remind me about meds today at 9pm` | Creates event with popup reminder |

> All LLM inference runs **locally**. Only Google Calendar API calls go to the internet.

---

## How it works

```
You → Blazor chat UI
       └─ AssistantService
            └─ qwen3:4b via Ollama  ←── tool calling (MaIN.NET manages the loop)
                 ├─ list_day tool   →  CalendarService.GetDayAsync()
                 └─ create_event tool → CalendarService.CreateEventAsync()
       └─ DayTimeline.razor renders the visual day view
```

`qwen3:4b` supports native function/tool calling in Ollama — no JSON parsing hacks, no intent routers. The model decides when to call a tool and MaIN.NET handles the iteration loop automatically.

---

## Prerequisites

| Requirement | Notes |
|---|---|
| **.NET SDK 10** | `dotnet --version` |
| **Ollama** | [ollama.com](https://ollama.com) |
| **qwen3:4b model** | `ollama pull qwen3:4b` |
| **Google account** | For Calendar API access |

---

## Quick Start

### Option A — automated setup (recommended)

**Windows:**
```powershell
git clone <repo-url>
cd CalAssistant
.\setup.ps1
dotnet run
```

**Linux / macOS:**
```bash
git clone <repo-url>
cd CalAssistant
chmod +x setup.sh && ./setup.sh
dotnet run
```

The setup script will:
1. Check / install **.NET SDK 10**
2. Check / install **Ollama**
3. Start the Ollama server if it's not running
4. Pull the **`qwen3:4b`** model if missing
5. Create `token-store/` and warn if `credentials.json` is absent
6. `dotnet restore` + `dotnet build`

Open **http://localhost:5136**, click **Connect calendar** and sign in to Google.

---

### Option B — manual setup

**1. Clone and enter the project:**
```bash
git clone <repo-url>
cd CalAssistant
```

**2. Pull the model:**
```bash
ollama pull qwen3:4b
```

**3. Set up Google Calendar credentials** (one-time, ~10 min) — see [Google OAuth Setup](#-google-oauth-setup) below.

**4. Run:**
```powershell
dotnet run
```

Open **http://localhost:5136**, click **Connect calendar** and sign in to Google.

---

### Option C — Docker (recommended: host Ollama)

**Fast setup** — Ollama runs on your Windows host (uses GPU natively), only the app runs in Docker:

```bash
# 1. Make sure Ollama is running on the host with the model
ollama pull qwen3:4b
ollama serve

# 2. Start app + gateway
docker compose up --build
```

Open **http://localhost:8080**.

| Container | Role |
|---|---|
| `gateway` (nginx) | Public entry point — port **8080** |
| `calassistant` | Blazor Server app |
| Ollama | **On your host** at `localhost:11434` (not in Docker) |

This is much faster than running Ollama inside Docker on CPU.

---

### Option D — Docker full stack (Ollama in container)

Everything in containers — convenient but **slow without GPU** (CPU inference):

```bash
docker compose --profile bundled-ollama up --build
```

**NVIDIA GPU** (optional, much faster):

```bash
docker compose -f docker-compose.yml -f docker-compose.gpu.yml --profile bundled-ollama up --build
```

Verify GPU is visible inside the container:

```bash
docker exec calassistant-ollama nvidia-smi
```

If `nvidia-smi` fails, Ollama falls back to CPU and will feel terrible.

---

## Google OAuth Setup

The app uses **Desktop app OAuth** — you authenticate in your browser, the app never sees your password, and the token is cached locally in `token-store/`.

1. Go to [Google Cloud Console](https://console.cloud.google.com/) and create a project (e.g. `CalAssistant`).
2. Enable **Google Calendar API**: *APIs & Services → Library → Google Calendar API → Enable*.
3. Configure consent screen: *APIs & Services → OAuth consent screen*:
   - User type: **External**
   - Add your Gmail address as a **Test user**
4. Create credentials: *Credentials → Create Credentials → OAuth client ID*:
   - Application type: **Desktop app**
   - Download the JSON file
5. Save the downloaded file as **`credentials.json`** in the project root:
   ```
   CalAssistant/
   └── credentials.json   ← here
   ```

> `credentials.json` and `token-store/` are in `.gitignore` and will never be committed.

---

## Project Structure

```
CalAssistant/
├── setup.ps1 / setup.sh          # automated environment setup
├── Program.cs                    # DI setup, ASP.NET pipeline
├── CalAssistant.csproj           # NuGet dependencies
│
├── Models/
│   └── AssistantModels.cs        # ChatMessage, CalEvent, DayPlan
│
├── Services/
│   ├── AssistantService.cs       # LLM orchestration — tool calling via MaIN.NET
│   └── CalendarService.cs        # Google Calendar API wrapper (OAuth, read/write)
│
├── Components/
│   ├── Pages/
│   │   └── Chat.razor            # Main chat page with sidebar
│   ├── DayTimeline.razor         # Visual day timeline component
│   └── Layout/
│       └── MainLayout.razor
│
├── wwwroot/
│   └── app.css                   # Dark theme, CSS variables
│
├── Dockerfile                    # Multi-stage build (SDK → ASP.NET runtime)
├── docker-compose.yml            # Compose with Ollama host config + volume mounts
├── .dockerignore
├── .gitignore
└── LICENSE
```

---

## Configuration

All configuration can be overridden via environment variables:

| Variable | Default | Description |
|---|---|---|
| `MaIN__OllamaBaseUrl` | `http://localhost:11434` | Ollama API endpoint; in Docker Compose: `http://ollama:11434` |
| `Google__CredentialsPath` | `credentials.json` | Path to Google OAuth credentials |
| `EnableHttpsRedirection` | `false` | Enable HTTPS redirect (set `true` in production behind a reverse proxy) |
| `ASPNETCORE_URLS` | `http://+:8080` (app container) | Bind address; public URL is `http://localhost:8080` via nginx |

---

## Tech Stack

| Layer | Technology |
|---|---|
| Frontend | Blazor Server, .NET 10, SignalR |
| LLM framework | [MaIN.NET](https://github.com/mobitouchOS/MaIN.NET) 10.1.0 |
| Local LLM | qwen3:4b via [Ollama](https://ollama.com) |
| Calendar | Google Calendar API v3 (`Google.Apis.Calendar.v3`) |
| Containerisation | Docker, Docker Compose |

---

## Troubleshooting

| Problem | Fix |
|---|---|
| **Missing credentials.json** in the sidebar | Place `credentials.json` in the project root (see OAuth setup) |
| **403 / access_denied** on Google login | Add your Gmail as a *Test user* in OAuth consent screen |
| **Model doesn't respond** | Run `ollama list` — make sure `qwen3:4b` is listed; `ollama serve` must be running |
| **Docker: can't reach Ollama** | Use `docker compose up` — Ollama runs in its own container now |
| **NU1902/NU1903/NU1904 warnings** on build | Transitive vulnerability warnings from MaIN.NET dependencies; safe to ignore in dev |

---

## License

[MIT](LICENSE)

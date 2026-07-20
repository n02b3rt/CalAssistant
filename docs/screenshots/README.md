# Screenshots — scheduling flow (English)

A short screen-by-screen walkthrough of the assistant handling a scheduling request:
vague ask → clarifying question → conflict detection → day overview. Captured against a
local `qwen3:1.7b` model driving a real Google Calendar.

| # | File | What it shows |
|---|------|---------------|
| 1 | [01-start.png](01-start.png) | Fresh chat, light theme, English, calendar connected |
| 2 | [02-clarify.png](02-clarify.png) | Vague "Schedule a meeting" → assistant asks for date/time (no invented details) |
| 3 | [03-conflict.png](03-conflict.png) | Proposed 3pm slot overlaps an existing event → conflict warning + options |
| 4 | [04-day-overview.png](04-day-overview.png) | "What's my schedule tomorrow?" → day timeline visualization |

Regenerate with the Playwright helper (headless Chrome; the app must be running on
`http://localhost:5136` and the calendar connected). See the scratch `shot.js` used to produce these.

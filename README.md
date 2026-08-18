# TaskLens

TaskLens is a local-first WinUI 3 task manager that turns pasted meeting
transcripts and brain dumps into reviewed, explainable tasks.

## MVP

- My Day, Inbox, Upcoming, Completed, and area views
- User-created areas, with drag-and-drop task movement between them
- Task editing for title, area, completion date, estimate, and priority
- Local SQLite storage under `%LOCALAPPDATA%\TaskLens`
- Quick task creation, completion, and deletion
- Transcript and brain-dump action extraction
- Review inbox showing source excerpt, rationale, area, priority, duration, and
  confidence before any task is created
- Optional OpenAI-compatible cloud extraction

## Run locally

```powershell
dotnet run --project .\src\TaskLens.App\TaskLens.App.csproj `
  -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```

Without AI configuration, TaskLens uses an explicitly labelled rules-based
offline extractor. It recognizes bullets, checklists, commitment phrases,
relative dates, estimates, priorities, and area-name matches, but it does not
understand context like a language model. To use an
OpenAI-compatible or Azure OpenAI chat-completions endpoint, set these
environment variables before launch:

```powershell
$env:TASKLENS_AI_ENDPOINT = "https://<full-chat-completions-endpoint>"
$env:TASKLENS_AI_API_KEY = "<key>"
$env:TASKLENS_AI_MODEL = "<model-or-deployment>"
```

Secrets are never written to the TaskLens database.

## Planning views

- **Inbox**: incomplete tasks with no completion date.
- **My Day**: incomplete tasks due today or overdue.
- **Upcoming**: incomplete tasks with a future completion date.
- **Completed**: finished tasks.

Areas describe the responsibility or project a task belongs to. Planning views
describe when the task needs attention, so a task belongs to one area while
also appearing in a planning view.

## Test

```powershell
dotnet test .\tests\TaskLens.Core.Tests\TaskLens.Core.Tests.csproj
```

## Build both distribution artifacts

Run PowerShell as Administrator because the Win32 MSI is per-machine:

```powershell
.\scripts\Build-Packages.ps1
```

The script creates:

- `artifacts\win32\installer\TaskLens.Installer.msi`
- a signed sideload MSIX under `artifacts\msix\sideload`
- an unsigned Store upload under `artifacts\msix\store`
- `artifacts\certificate\TaskLens-Development.cer` for local installation

The script generates an ephemeral self-signed certificate, signs every Win32 PE
file plus the MSI, signs the MSIX, exports only the public certificate, and
deletes the private key. The certificate is suitable for local testing only.
Microsoft's published Store policy requires public Win32 installers and their
contained PE files to chain to a certificate authority in the Microsoft Trusted
Root Program. Partner Center may therefore reject the experimental self-signed
MSI; the MSIX is the supported Store fallback after replacing its identity with
the reserved Store identity.

## AI and Microsoft 365 roadmap

The core app works for personal and enterprise users without sign-in. Microsoft
sign-in is only needed when Outlook, Teams, and calendar connectors are added.
Mail integration can support eligible personal and organizational Microsoft
accounts, while several Teams chat and transcript permissions are restricted to
organizational tenants and may require administrator consent.

The extraction interface is provider-neutral. A future Windows AI provider can
use Phi Silica or Foundry Local on supported hardware without changing the task
workflow. It is not the default because Windows local model availability varies
by OS build, GPU/NPU capability, region, and model download state.

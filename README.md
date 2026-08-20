# TaskLens

TaskLens is a local-first WinUI 3 task manager that turns pasted meeting
transcripts and brain dumps into reviewed, explainable tasks.

## MVP

- My Day, Inbox, Upcoming, Completed, and area views
- User-created areas, with drag-and-drop task movement between them
- Task editing for title, area, due date, and priority
- Safe area deletion that moves existing tasks instead of deleting them
- Windows reminders at 9:00 AM on the due date
- Local SQLite storage under `%LOCALAPPDATA%\TaskLens`
- Quick task creation, completion, and deletion
- Transcript and brain-dump action extraction
- Review inbox showing source excerpt, rationale, area, priority, and
  confidence before any task is created
- Automatic Windows local AI extraction on supported Copilot+ PCs

## Run locally

```powershell
dotnet run --project .\src\TaskLens.App\TaskLens.App.csproj `
  -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```

TaskLens automatically uses the Windows system `LanguageModel` when Windows
reports it as available. The model runs locally and returns structured JSON for
reviewed task creation. No API key or environment-variable configuration is
required. On unsupported systems, TaskLens clearly falls back to its rules-based
offline extractor.

## Planning views

- **Inbox**: incomplete tasks with no completion date.
- **My Day**: incomplete tasks due today or overdue.
- **Upcoming**: incomplete tasks with a future completion date.
- **Completed**: finished tasks.

Areas describe the responsibility or project a task belongs to. Planning views
describe when the task needs attention, so a task belongs to one area while
also appearing in a planning view.

Priority only affects ordering: for tasks with the same due date, High appears
before Normal, which appears before Low. Priority does not change reminder time.

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

- `artifacts\win32\TaskLens-win-x64.zip`
- `artifacts\win32\installer\TaskLens.Installer.msi`
- a signed sideload MSIX under a timestamped `artifacts\msix\sideload` folder
- an unsigned Store upload under a timestamped `artifacts\msix\store` folder

For portable testing, extract `TaskLens-win-x64.zip` into a new folder and run
`TaskLens.App.exe`. Do not run an executable left in an older build directory;
the application depends on the matching DLLs and resources beside it.
- `artifacts\certificate\TaskLens-Development.cer` for local installation

The script creates or reuses a persistent self-signed development certificate,
signs every Win32 PE file plus the MSI, signs the MSIX, and exports the public
certificate. It reuses one development certificate from the current user's
certificate store so later MSIX builds update cleanly after the certificate is
trusted once. The certificate is suitable for local testing only.
Microsoft's published Store policy requires public Win32 installers and their
contained PE files to chain to a certificate authority in the Microsoft Trusted
Root Program. Partner Center may therefore reject the experimental self-signed
MSI; the MSIX is the supported Store fallback after replacing its identity with
the reserved Store identity.

## Automated Win32 Store submission

`.github\workflows\publish-win32-store.yml` builds and tests the app on every
push to `main`, signs the MSI, publishes it at an immutable public Azure Blob
URL, updates product `f8333337-5231-467d-976e-a8784e31ad07`, and creates the
Partner Center submission through the MSI/EXE submission API. Azure Blob is
used because this enterprise-managed GitHub account cannot create public
repositories, and Partner Center must download the installer anonymously.

Configure these repository secrets:

- `AZURE_AD_TENANT_ID`
- `AZURE_AD_APPLICATION_CLIENT_ID`
- `AZURE_AD_APPLICATION_SECRET`
- `SELLER_ID`
- `WINDOWS_SIGNING_CERTIFICATE_BASE64`
- `WINDOWS_SIGNING_CERTIFICATE_PASSWORD`
- `AZURE_STORAGE_CONNECTION_STRING`

Repository variables:

- `STORE_PRODUCT_ID=f8333337-5231-467d-976e-a8784e31ad07`
- `AZURE_STORAGE_ACCOUNT=tasklensstoref8333337`
- `AZURE_STORAGE_CONTAINER=releases`

Store copy and assets live under `store-listing`. Microsoft documents that
self-signed certificates are not eligible for public MSI/EXE submissions, so
the current experimental certificate may still be rejected by certification.

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

# TaskLens MVP product requirements

## Product promise

TaskLens helps Microsoft 365 professionals turn fragmented work context into a
small, trusted list of next actions. AI proposes; the user approves.

## Target user

Knowledge workers who manage multiple responsibilities across meetings, email,
Teams, certifications, and people management, and who do not trust silent
automation to create or schedule work.

## MVP requirements

1. Users can create, complete, reopen, delete, and locally persist tasks.
2. Users can organize tasks into Project Blue Badge, AI Certification, Manager,
   and Personal areas.
3. Users can see My Day, Inbox, Upcoming, Completed, and area-filtered views.
4. Users can paste unstructured notes or transcripts and extract candidate
   action items.
5. No extracted item becomes a task without explicit review.
6. Each suggestion displays its source excerpt, rationale, inferred area,
   priority, estimate, and confidence.
7. The app works offline without authentication.
8. Cloud AI is optional and secrets are not persisted in the app database.
9. Builds produce both a self-contained Win32 installer and an MSIX package.

## Post-MVP

- Editable task details, recurrence, reminders, and search
- Explainable My Day planning based on deadlines, priority, and available time
- Microsoft identity and Outlook mail suggestions
- Calendar-aware planning
- Teams chat and transcript suggestions for eligible organizational tenants
- Area-specific assistant instructions
- Windows AI provider using supported local models

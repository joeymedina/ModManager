# <Subsystem> Architecture

<!-- Keep the sections that apply. Delete the rest rather than filling them with filler. -->

## Context

What this is, what it replaced and why, and links to related or superseded docs.

## Layering / Clean Architecture

- Which interfaces live in **Application**, which implementations in **Infrastructure**.
- Where the models live, and why there.
- Any layering rule a future change could break without the compiler noticing.

## What Changed

Only on a rework page. Bullets, past tense.

## Architecture

```text
┌──────────────────────────────┐
│ Caller                        │
└──────────────┬───────────────┘
               │ interface
┌──────────────▼───────────────┐
│ Implementation                │
└──────────────────────────────┘
```

## Design decisions

| Decision | Reason |
| --- | --- |
|  |  |

## Dependency Injection

| Registration | Implementation | Lifetime | Note |
| --- | --- | --- | --- |
|  |  |  |  |

Registered in `<AddApplicationServices / AddInfrastructureServices / AddUiServices>`.

## Operational Flow

Numbered steps for each public entry point. Say what it does *not* do when that's load-bearing
(e.g. "reads create nothing").

## Error and Conflict Behavior

| Condition | Behavior |
| --- | --- |
|  |  |

State whether failures throw or come back as a result/failure list, and whether a partial batch
rolls back.

## Test Strategy

Which boundary is mocked, which asserts against real IO, and what the tests actually cover.

## Gotchas

Things that cost debugging time and aren't visible in the code. One bold sentence naming the
symptom, then the cause and the fix.

## Known Gaps / Deferred

What isn't built, why it was acceptable to skip, and the upgrade path. Cross-reference any
matching `ponytail:` comment in the code.

## Out of Scope

Explicitly not doing this, so it doesn't get re-proposed.

## Verification

- `dotnet build ModManager.slnx`
- `dotnet test ModManager.slnx`
- Manual steps that actually exercised the change.

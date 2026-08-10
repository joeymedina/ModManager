---
name: arch-doc
description: Write or update a docs/architecture page in this repo's house style
disable-model-invocation: true
---

# arch-doc

Write a new `docs/architecture/<feature>.md`, or update an existing one, matching the style the
seven existing pages already share.

## Steps

1. **Read the nearest existing doc first.** `mods-folder-service.md` is the model for a subsystem
   page; `mods-page-redesign.md` is the model for a rework page. Match whichever fits.
2. **Read the code you're documenting**, not just the diff. These docs explain *why* a shape was
   chosen — that reasoning isn't in the diff.
3. Start from [template.md](template.md). Drop sections that don't apply; don't invent content to
   fill one.
4. **If this supersedes an older doc, edit the older doc too** — add the banner (below) at its top.
   This is the step that gets forgotten, and a stale doc with no banner is worse than no doc.
5. Cross-link related docs by relative path: `[mods-folder-service.md](./mods-folder-service.md)`.

## What these docs are for

Decisions and their reasons. Not an API listing, not a file tour — someone can read the code for
that. Every table row that says *what* should have a neighbour saying *why*.

Specifically worth capturing, because they cost real time to rediscover:

- Why a thing lives in the layer it lives in (especially Application vs Infrastructure).
- Trade-offs accepted knowingly, and what the mitigation would be if it ever matters.
- Gotchas that cost debugging time and aren't obvious from the code — the redesign doc's
  "Gotchas found while building this" section is the format.
- Known gaps, so the next reader doesn't file them as bugs. Cross-reference any `ponytail:` comment
  in the code that names the same ceiling.

## Superseded banner

Put this at the top of a doc that a newer one replaces, and say precisely what still holds — the
navigation-shell banner is the example: the shell control changed, the navigation *model* didn't.

```markdown
> **Partly superseded.** <what changed and where> — see [newer-doc.md](./newer-doc.md).
> <what in this document is still accurate>
```

## Conventions

- Filename is kebab-case, named for the subsystem or the rework, not the ticket.
- Prose wraps at ~95 columns, matching the existing files.
- Code and type names in backticks; ASCII box diagrams in a ```text fence.
- Past tense for what changed, present tense for how it works now.

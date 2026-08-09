# Copilot Instructions

## Project Guidelines
- Preference/team standard: always use dependency injection where possible.
- Team/project coding standard: always keep one class per file and avoid defining model types inside service classes.
- Team/project coding standard: never use `var`; always use explicit types.
- Architecture preference: avoid placing core/domain models in Infrastructure; keep Infrastructure limited to implementation details like adapters/persistence DTOs.

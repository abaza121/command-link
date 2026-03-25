# AGENTS.md

## Purpose
This folder is being prepared to live as a standalone Unity package.

Treat everything under `Assets/CommandLink` as package-owned code and optimize changes for extraction, reuse, and low coupling.

## Package Boundaries
- Keep runtime code inside the `CrossCut.CommandLink` namespace.
- Do not introduce dependencies on `Assets/Scripts` project gameplay code.
- Do not reference project-only MonoBehaviours, ScriptableObjects, scenes, prefabs, or authoring components from this package.
- Keep the current dependency direction:
  - `CommandLink` may depend on `LockstepFoundations`
  - project gameplay may depend on `CommandLink`
  - `CommandLink` should not depend back on project gameplay

## Folder Intent
- `Runtime/`: shipping package runtime code
- `Diagnostics/Runtime/`: optional runtime diagnostics helpers
- `Diagnostics/Editor/`: editor-only tooling
- `Docs/`: package-specific design notes, investigations, and extraction guidance

## Change Guidance
- Prefer generic abstractions over Project Crosscut-specific assumptions.
- If a behavior is currently project-specific, isolate it behind an interface, config object, delegate, or bridge.
- Avoid hardcoding scene names, game rules, building/unit concepts, or presentation assumptions unless they are truly part of CommandLink itself.
- Keep transport, session orchestration, deterministic input flow, and lockstep bridging reusable.
- When adding diagnostics, ensure they are non-authoritative and do not change lockstep behavior.

## Extraction Readiness
- Keep package metadata up to date in `package.json`.
- Favor self-contained docs inside `Assets/CommandLink/Docs`.
- If new code requires another assembly, place it under the package and wire it through asmdefs here rather than relying on external project assemblies.
- Call out any new dependency that would make standalone extraction harder.

## Testing Expectations
- Prefer compile verification with:
  - `dotnet build "Assembly-CSharp.csproj" -nologo`
- For multiplayer fixes, verify both:
  - lockstep progression
  - diagnostics output from host and client

## Documentation
- Add or update a short Markdown note in `Assets/CommandLink/Docs` when fixing subtle networking, lockstep, ack, resend, or session-state issues.
- Keep docs practical: what failed, what changed, how to validate it.

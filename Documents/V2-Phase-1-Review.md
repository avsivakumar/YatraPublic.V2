# Phase 1 review — protocol and host foundation

Only Phase 1 is implemented. Phase 2 has not started.

## Changed files

- `.gitignore`: .NET/React generated files, local settings and artifact exclusions.
- `src/Yatra.Contracts/Messages/YatraMessageTypes.cs`: new request/response constants.
- `src/Yatra.Contracts/Messages/YatraMessageValidator.cs`: generic payload validation, required response correlation and actionable request reply expectation.
- `src/Yatra.Contracts/Components/ComponentRequestPayload.cs`: request record.
- `src/Yatra.Contracts/Components/ComponentResponsePayload.cs`: response record.
- `src/Yatra.Contracts/Components/ComponentPayloadValidator.cs`: non-throwing JSON shape and identifier validation.
- `tests/Yatra.Contracts.Tests/ComponentContractTests.cs`: 24 new backend test cases.
- `yatra-ui/src/types.ts`: message union and component type exports.
- `yatra-ui/src/componentProtocol.ts`: TypeScript contracts, lifecycle vocabulary and runtime parsing guards.
- `yatra-ui/src/componentRegistry.ts`: trusted in-process registry, exact ID/version resolution, duplicate protection and controlled hosted-component interface.
- `yatra-ui/src/components/ComponentHost.tsx`: validation, closed failure rendering and React error boundary.
- `yatra-ui/src/components/MessageList.tsx`: generic host delegation and plain component-response status.
- `yatra-ui/src/componentProtocol.test.tsx`: 32 new frontend test cases.
- `Documents/Phase-1-Review.md`: this review note.

## Validation results

- `dotnet test Yatra.slnx --nologo`: 236 passed, zero failed or skipped (Contracts 64, Orchestration 96, Engine 69, AppHost 7).
- `dotnet build Yatra.slnx --no-restore --nologo`: passed, zero warnings/errors.
- `npm test --prefix yatra-ui`: 83 passed across 8 files, including all existing V1 tests.
- `npm run build --prefix yatra-ui`: TypeScript checking and production build passed.
- Added tests cover payload round trips, missing/malformed fields, invalid identity/version/instance/action formats, required correlation, exact version/case resolution, duplicate registration, unknown/unsupported rendering, invalid properties, throwing validators/renderers, and timeline isolation.

## Decisions and limitations

- The production registry is deliberately empty. No customer component, sample command, component response HTTP submission, pending reply metadata, lifecycle persistence, recovery, or capability discovery is implemented. Those belong to later phases.
- Trusted test registrations exercise the rendering boundary. Until Phase 2, hosted views receive an unsupported lifecycle, no available actions and an inert callback so the foundation cannot imply working submissions.
- Versions use explicit major.minor decimal strings (for example `1.0`), with no leading zeroes or implicit fallback. IDs/actions are lowercase kebab-case. Generic props/data must be JSON objects.
- Shared generic limits: ID 100 characters, version 32, action 64, title 200, description 2000, 1–16 unique actions. Whole-payload/depth limits remain later-phase work.
- All requested actions must be supported by the registration; mixed supported/unsupported action sets fail closed.
- Authoritative domain validation and checking responses against original pending component metadata remain Phase 2 work. The generic validator does not assert domain truth or registration availability.
- Return-path handling and the V1 form, engine, transport and persistence implementations were not refactored.
- No agent-supplied source, markup, remote modules, dynamic imports, eval, or other executable remote-content mechanism was introduced. Payload text is rendered through React escaping; failure cards expose no internal exceptions.
- No manual browser acceptance check was performed; automated jsdom tests cover the Phase 1 rendering boundary.
- `npm audit` reports one existing transitive high-severity advisory for `source-map-js` (<1.2.2), GHSA-68fv-2mgg-jv7q. Dependency versions and lockfile were not upgraded in this phase.
- The supplied workspace has no Git metadata; changed files are enumerated above against the inspected extracted source.

## Complete source delivery

`artifacts/YatraV2-Phase1.zip` contains the complete repository source, documentation, specification and tests, excluding generated dependencies/build outputs, runtime storage and the archive itself. It is a Phase 1 review package, not a V2 release candidate. Restore dependencies with `dotnet restore Yatra.slnx` and `npm ci --prefix yatra-ui`.

Stop here for review before Phase 2.

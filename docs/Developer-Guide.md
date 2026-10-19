# Developer guide — Yatra V2

Use .NET SDK 10 and Node.js 24. Restore with `dotnet restore Yatra.slnx` and `npm ci --prefix yatra-ui`. Run the complete checks in [Testing](Testing.md).

## Local startup

The default Vite `/api` proxy targets `https://localhost:7144`. Start AppHost with its explicit HTTPS profile:

```powershell
dotnet run --project .\src\Yatra.AppHost --launch-profile https
npm run dev --prefix .\yatra-ui
```

Run the second command in a separate terminal. Open `http://localhost:5173`. If the local HTTPS certificate is unavailable, use the [HTTP-only alternative](../README.md#http-only-local-alternative); never bypass a browser security interstitial as a release check.

`VITE_YATRA_API_BASE_URL` is public build-time configuration. Set it in the terminal before starting/building the UI when using a separate API origin. Configure the exact UI origin in `Yatra:AllowedCorsOrigins`. A built bundle does not read new environment values at runtime; rebuild after changing its base URL.

## Sample prompts

`hello`, `show contact form`, `request application access`, `confirm an action`, `run sample skill`, `compare customers`, and `show status summary`. Fake is the distributable model provider; no credential is needed for these tests.

## Configuration and storage

AppHost reads `appsettings.json`, environment and command-line configuration for engine options. Storage defaults to `library` beneath its executable directory; use `Yatra:StorageFolder` for a stable directory across build/configuration changes. One process owns that directory. Pending lifetime defaults to 30 minutes, cleanup to 60 seconds and heartbeat to 15 seconds. SSE subscriber capacity is bounded; old inbound/outbound capacity options do not bound the durable file queues. Completed IDs and terminal records are retained, not pruned by the retention option in the file store.

The optional OpenAI sample uses private `AiProvider`, `AiModel`, `AiApiKey` and `AiTimeoutSeconds` constants in `SampleOrchestrationAgentAdapter.cs`. That mechanism embeds a populated key in binaries; keep Fake and the placeholder in anything shared. A real integration should replace it with an appropriate server-side secret provider. No authentication or authorization is added by enabling a model.

## Extension boundaries

Keep shared protocol in Contracts, neutral interfaces in AgentIntegration, generic transport/state in Engine, domain factories/validators in Orchestration and composition in AppHost. The engine must not reference concrete orchestration, and the message list must not branch on concrete component IDs.

For forms, retain the V1 `formValidation.ts`/DynamicForm contract and authoritative sample form validators. Do not loosen existing tests or change form retry/replacement semantics while adding components.

For trusted React components, follow [Component authoring](Component-Authoring.md). Register exact versions, define strict bounded JSON, implement an authoritative `IComponentResponseValidator`, and add rendering/correlation/recovery tests. No remote import, runtime installation, JSX/source payload, executable expression or `dangerouslySetInnerHTML` is supported.

## Source packaging

Package only repository source, lockfiles, public configuration, fixtures, documentation and license. Exclude `node_modules`, `bin`, `obj`, `dist`, TestResults, coverage, runtime `library`/conversation files, local settings/secrets, credentials, screenshots, prior archives and staging artifacts. Include `.gitignore` and preserve historical records. Re-run audits and archive checks after the final source edits; a documentation or lockfile change is still part of the review package.

The UI's private npm package version is metadata; V2 denotes the architectural iteration. Component contract versions remain explicit `1.0`, independent of package/release naming.

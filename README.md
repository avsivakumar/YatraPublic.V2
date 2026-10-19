# Yatra V2

Yatra V2 is an architectural sample for asynchronous agent-to-human interaction: chat, V1 dynamic forms, and explicitly registered React components in one conversation timeline. It is not a production agent platform.

The browser sends JSON through HTTP; the .NET engine persists inbox/outbox work and private reply routing, and delivers complete messages through SSE. Agents select approved component IDs and versions and supply data. They cannot send executable UI source or remote modules. Forms remain supported with their original contracts.

## Run the sample

Requires .NET SDK 10, Node.js 24 and npm. The default Fake LLM needs no API key.

From the source root:

```powershell
dotnet restore .\Yatra.slnx
npm ci --prefix .\yatra-ui
dotnet run --project .\src\Yatra.AppHost --launch-profile https
```

In a second terminal:

```powershell
npm run dev --prefix .\yatra-ui
```

Open [http://localhost:5173](http://localhost:5173). The existing Vite proxy targets `https://localhost:7144`; use the AppHost **https** profile. If that local certificate is unavailable, use the HTTP-only alternative below. Wait for **connected** before sending text.

| Command | Result |
| --- | --- |
| `compare customers` | Compare customer cards; select and confirm, or cancel |
| `show status summary` | Review a status and acknowledge it |
| `show contact form` | V1 contact form |
| `request application access` | V1 access-request form |
| `confirm an action` | V1 confirmation form |
| `run sample skill` | V1 sample-skill form and outcome |
| Other text | Fake LLM response by default |

Commands ignore leading/trailing spaces and case. Replies correlate to their original request, so simultaneous forms/components can be answered in any order. HTTP acceptance locks an interaction; the correlated agent outcome completes or cancels it. A transport retry reuses the original response identity and JSON.

### HTTP-only local alternative

Backend:

```powershell
dotnet run --project .\src\Yatra.AppHost --launch-profile http
```

UI, in a separate terminal:

```powershell
$env:VITE_YATRA_API_BASE_URL = 'http://localhost:5130'
npm run dev --prefix .\yatra-ui
```

The UI origin must match `Yatra:AllowedCorsOrigins` (default `http://localhost:5173`). `VITE_*` values are public build-time browser configuration; never put secrets there. Production hosting must provide an appropriate API origin/proxy, TLS and access controls.

## Verify

```powershell
dotnet test .\Yatra.slnx -c Release
dotnet build .\Yatra.slnx -c Release --no-restore
npm test --prefix .\yatra-ui
npm run build --prefix .\yatra-ui
npm audit --prefix .\yatra-ui
```

The UI build includes TypeScript checking. Release evidence and manual checks are in [Phase 5 review](Documents/V2-Phase-5-Review.md).

## Architecture and boundaries

- `Yatra.Contracts`: messages, forms, components, capabilities, limits and ULIDs.
- `Yatra.AgentIntegration`: neutral adapter and authoritative validator interfaces.
- `Yatra.Engine`: HTTP, queues, SSE, correlation, private paths and atomic JSON state. It references no concrete orchestration or React component.
- `Yatra.Orchestration`: trusted sample factories, validators, commands, domain outcomes and Fake/optional OpenAI client.
- `Yatra.AppHost`: composition root.
- `yatra-ui`: registry, generic host, trusted components, dynamic forms and browser lifecycle storage.

The registry is a compile-time allowlist. Resolution uses an exact case-sensitive ID/version pair, never a latest-version fallback. New user text advertises only registered IDs, versions and actions; capabilities are advisory, not authorization. Built-ins remain `customer-comparison/1.0` and `status-summary/1.0`.

## Data and configuration

`Yatra:StorageFolder` selects engine storage; the default is `library` under the host executable directory. One process owns a storage folder. Preserve it across restarts; it contains private routing metadata and potentially sensitive conversation data. Browser state is origin/profile-scoped local storage. **Clear all messages** hides messages locally; it does not delete server data or cancel pending work.

The distributable uses `AiProvider = "Fake"` and a placeholder key in `SampleOrchestrationAgentAdapter.cs`. The optional OpenAI sample is configured through its private constants and embeds any populated key in binaries. Do not distribute a populated key. A production integration needs server-side secret management; this sample does not implement that configuration path.

## Documentation

Start with the [documentation index](docs/README.md): [user guide](docs/User-Guide.md), [protocol](docs/Message-Protocol.md), [architecture](docs/Architecture.md), [component authoring](docs/Component-Authoring.md), [adapter guide](docs/Agent-Adapter-Guide.md), [persistence](docs/Persistence.md), [security](docs/Security.md), [testing](docs/Testing.md), [troubleshooting](docs/Troubleshooting.md), and [V2 scope](docs/Yatra-Version-2-Scope.md).

The [original V1 documentation snapshot](docs/V1-History.md), historical design/review documents, and [changelog](CHANGELOG.md) preserve the earlier release record. V2 does not retroactively redefine the V1 phases.

## Known limits

No authentication, authorization, tenants, distributed engine coordination, marketplace, remote UI loading, payments, uploads or token streaming. Conversation ULIDs are identifiers, not access credentials. Unfinished adapter work may execute again after a crash; external effects must deduplicate by input message ID. JSON history and terminal identities grow without automatic retention pruning. See [scope](docs/Yatra-Version-2-Scope.md) for exact payload/rendering limits and remaining operational limits.

Yatra was conceived, designed and created by **Sivakumar Angarai**. Released under the [MIT License](LICENSE).

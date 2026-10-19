# Security and trust boundaries — Yatra V2

Yatra V2 is an architectural sample, not a production security or agent platform. It has no authentication, authorization, roles, tenant isolation or conversation ownership checks. A conversation ULID is an identifier, not a credential. Exact component validation does not supply those missing controls.

## Executable UI allowlist

Only compiled, explicitly registered React components can render. Agents supply exact IDs/versions and bounded JSON data. There is no remote import, JSX/HTML execution, style/expression evaluation, runtime npm installation, iframe plugin or marketplace path. Unknown identities, versions and invalid properties fail closed. Text is escaped through React; do not introduce `dangerouslySetInnerHTML`.

The component receives no credentials, private return path, transport client, message constructor or lifecycle store. The host owns response identity, correlation, network submission and retry. Capability declarations describe available built-ins and do not authorize a business action or install code. Client and hidden fields remain untrusted.

The engine binds a response to original pending conversation/request/component/version/instance/actions. A registered requester validator authoritatively checks domain data against the original properties. Customer IDs repeated by a browser are not business truth. Expired and terminal requests reject later responses.

## Secrets and model configuration

Keep Fake as the distributable default and the sample API key as a placeholder. A source constant is not a secret and is recoverable from binaries. The optional OpenAI sample currently uses private constants; production code needs an appropriate server-side secret/configuration provider. Never place a key in React, `VITE_*`, fixtures, logs, screenshots or archives. If a real credential has been shared, revoke it; deleting a file is insufficient.

Release scans cover current source/package patterns and dependency advisories, not a guarantee that no unknown vulnerability exists. This extracted workspace has no Git history to audit. Historical V1 documents are frozen records, not instructions to populate credentials in a release.

## Transport, storage and resource limits

Internal return paths and validator metadata remain server-side. Browser-controlled source/destination/timestamp fields do not determine trusted routing. Use HTTPS beyond local development and exact CORS origins. The development proxy's local-certificate setting is not a production TLS policy.

Messages, properties and responses can be sensitive; protect the JSON storage folder, browser profile, backups and logs. The sample does not encrypt them or implement retention, per-user access checks or production telemetry. Do not log complete business data by default.

[Payload/rendering limits](Yatra-Version-2-Scope.md) bound component data and active views. They do not replace host-level body/rate limits, CSP, authentication, abuse protection or a sandbox for compiled trusted code. Durable history/identity records still grow. One process owns each storage folder; the file store is not a distributed or tenant-isolation boundary.

## External effects

Restart recovery may execute unfinished input again. Idempotency must be enforced at external business boundaries using the input message ULID. Local file transactions cannot atomically commit another service's operation.

## Source-release checks

Use the commands in [Testing](Testing.md), inspect only public source/configuration/fixtures/documentation/license in the package, and exclude credentials, user files, binaries, runtime `library`/conversation data, dependency folders and generated output. Inspect advisory results and placeholders; record limitations in the release review. Public publication and production deployment require the maintainer's own review and security design.

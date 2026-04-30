# RimSearcher Streamable HTTP Transport Design

## Context

RimSearcher is currently a local MCP server launched as an executable over stdio. Each Agent that configures the executable starts its own process, which means each Agent loads or builds its own RimWorld C# and XML indexes. The source data is effectively static in the target workflow: a single decompiled RimWorld source folder and XML data folder that do not change during normal use.

The goal is to let users run one shared local MCP server process and connect multiple compatible clients to it, while preserving the current stdio behavior for clients that still launch the executable directly.

## Scope

This design adds an explicit transport selector to the existing executable.

- Default behavior remains stdio.
- A new Streamable HTTP mode can be started manually.
- Existing tools and index behavior remain unchanged.
- The design does not include stdio-to-HTTP proxying, automatic background launch, service discovery, config fingerprinting, lock files, or multi-instance management.

## User-Facing Behavior

Default stdio mode stays compatible with the current README examples:

```powershell
RimSearcher.Server.exe
```

Shared local HTTP mode is started manually:

```powershell
RimSearcher.Server.exe --transport streamable-http --host 127.0.0.1 --port 51234 --mount-path /mcp
```

Clients that support URL-based MCP servers can connect to:

```text
http://127.0.0.1:51234/mcp
```

The executable continues to read `RIMSEARCHER_CONFIG` first and falls back to `config.json` beside the executable when the environment variable is not set.

## CLI Options

Add these command-line options:

- `--transport`: one of `stdio` or `streamable-http`; default `stdio`.
- `--host`: HTTP bind host; default `127.0.0.1`.
- `--port`: HTTP bind port; default `51234`.
- `--mount-path`: MCP HTTP endpoint path; default `/mcp`.

HTTP options are only used when `--transport streamable-http` is selected.

## Architecture

The existing startup sequence should remain the single source of truth:

1. Set console encoding.
2. Load `AppConfig`.
3. Initialize `PathSecurity`.
4. Load index cache or scan source paths.
5. Freeze indexes.
6. Register the six existing MCP tools.
7. Start the selected transport.

To avoid duplicating startup logic, the current program flow can be factored into small pieces only where needed:

- A runtime object owns tool registration, JSON-RPC request handling, concurrency limiting, cancellation, and logging notification behavior.
- The stdio transport reads newline-delimited JSON-RPC from stdin and writes JSON-RPC messages to stdout, matching current behavior.
- The HTTP transport exposes a local endpoint and reuses the same request handling path for JSON-RPC requests.

The existing `Tools/*` and `RimSearcher.Core/*` behavior should not be rewritten for this feature.

## HTTP Protocol Behavior

The initial Streamable HTTP implementation is request-response focused:

- `POST /mcp` accepts one JSON-RPC request, notification, or response body.
- Requests with an `id` return one JSON-RPC response with `Content-Type: application/json`.
- Notifications without an `id` return `202 Accepted` with no response body.
- `GET /mcp` returns `405 Method Not Allowed` in the initial implementation.
- `initialize`, `notifications/initialized`, `tools/list`, `list_tools`, `tools/call`, and `call_tool` keep the same behavior as stdio where the transport permits it.

Server-to-client logging notifications are straightforward over stdio because stdout is the protocol stream. In the initial HTTP request-response mode, unsolicited outgoing notifications are not delivered over a separate SSE stream. HTTP-mode diagnostics that cannot be returned as JSON-RPC responses should be written to stderr or the normal process logs.

## Security

The default HTTP bind address is `127.0.0.1`. Localhost-only binding is the supported target for this feature.

When an `Origin` header is present on HTTP requests, the server should reject non-localhost origins with `403 Forbidden`. This follows the MCP Streamable HTTP guidance for local servers and reduces DNS rebinding risk.

Binding to `0.0.0.0` is not the default and is not the recommended setup. Remote or LAN exposure, authentication, and authorization are outside the scope of this design.

## Error Handling

Startup errors keep the current behavior where possible:

- Missing or invalid config is logged.
- Missing source paths are logged.
- Cache load failure falls back to rebuild.
- Cache save failure is logged without failing the server.

HTTP-specific errors should be explicit:

- Unsupported HTTP methods return `405`.
- Invalid JSON returns a JSON-RPC parse error when possible.
- Invalid Origin returns `403`.
- Port bind failure is reported by the host startup failure and should be visible in stderr.

## Testing

Automated tests should focus on transport selection and request handling without requiring a real RimWorld source tree:

- CLI parsing covers default stdio values and explicit Streamable HTTP values.
- JSON-RPC handling covers `initialize` and `tools/list`.
- HTTP smoke test posts `initialize` to the configured mount path and verifies `serverInfo`.
- Notification handling verifies that an HTTP notification returns `202 Accepted`.
- `GET /mcp` returns `405 Method Not Allowed`.

Manual verification should cover:

- Existing stdio configuration still lists all six tools.
- Manual HTTP startup reaches `http://127.0.0.1:51234/mcp`.
- A URL-configured MCP client can call `locate`.
- Two clients connected to the same HTTP URL share one server process.

## Documentation

Update README with:

- Existing stdio usage preserved as the default.
- New shared local HTTP command.
- URL-based MCP client example.
- A short note that stdio clients still create one process per client.
- A short warning that HTTP mode is intended for localhost use.


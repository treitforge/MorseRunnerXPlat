# Security Instructions

- Never commit or log secrets, credentials, tokens, private keys, or local
  environment values.
- Bind the optional host to loopback unless remote access is explicitly enabled.
- Treat remote access as a separate authenticated TLS feature.
- Authenticate unrelated local clients when the host is externally reachable.
- Sanitize paths and error details returned through transport contracts.
- Bound message sizes, queues, retained event history, and session counts.
- Require explicit intent for reset, overwrite, delete, and forced takeover.

# gRPC Contract Instructions

- Protobuf defines only the optional external transport.
- Map generated messages to project-owned client and domain models.
- Keep the initial contract cohesive and small.
- Prefer additive evolution.
- Preserve field numbers and enum values.
- Reserve removed field names and numbers.
- Give mutating requests a request ID and session ID.
- Include engine epoch, sequence, revision, and simulation block in update
  streams where applicable.
- Bound subscriber queues and require resync after retained history is lost.
- Reuse long-lived channels.
- Bind standalone hosts to loopback by default.
- Never stream real-time PCM.
- Run `buf lint` and `buf breaking` when the contract exists.

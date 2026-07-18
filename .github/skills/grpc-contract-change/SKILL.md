---
name: grpc-contract-change
description: Design, implement, or review MorseRunnerXPlat Protobuf and gRPC contracts, transport mappings, standalone EngineHost behavior, reconnect, resync, capabilities, or control leases. Use for files under proto, MorseRunner.Contracts, MorseRunner.Grpc, or MorseRunner.EngineHost.
---

# gRPC Contract Change

1. Confirm that the capability requires an external client. Do not add transport
   types to solve an in-process domain design problem.
2. Define semantic client behavior before defining Protobuf messages.
3. Map generated messages explicitly at the transport edge.
4. Prefer additive schema changes. Preserve and reserve field numbers, names,
   and enum values.
5. Add request IDs to mutations and sequence, revision, epoch, session, and
   block metadata to streamed state as specified.
6. Bound message sizes, subscriber queues, retained history, and session counts.
7. Test cancellation, duplicate requests, disconnect, reconnect, slow
   subscribers, resync, lease expiry, and forced takeover.
8. Run `buf lint`, `buf breaking`, direct engine tests, and shared in-process
   versus gRPC scenario vectors.
9. Update the engineering specification in the same change.

Bind to loopback by default. Never stream PCM or expose generated messages from
`IMorseRunnerClient`.

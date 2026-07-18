# Audio and DSP Instructions

## Hot-path rules

- Render fixed-size blocks into caller-owned or pooled memory.
- Avoid per-sample allocation, LINQ, boxing, reflection, locks, exceptions,
  logging, file I/O, RPC, and UI work.
- Keep filter and oscillator state owned by the session renderer.
- Make sample rate, block size, and queue depth explicit configuration.
- Apply operator commands only at block boundaries.
- Isolate physical audio, WAV recording, and null output behind `IAudioSink`.

## Evidence

- Add golden numeric vectors for filters, modulation, envelopes, and AGC.
- Add seeded audio hashes or bounded-error comparisons for complete scenarios.
- Benchmark render time, allocation, queue depth, and underrun recovery.
- Test device loss, default-device changes, stop, restart, and disposal.

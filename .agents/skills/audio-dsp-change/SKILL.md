---
name: audio-dsp-change
description: Implement or review MorseRunnerXPlat audio rendering, Morse envelopes, filters, modulation, AGC, propagation effects, audio queues, device adapters, WAV output, or performance. Use whenever DSP output, block timing, native audio lifetime, or real-time behavior changes.
---

# Audio and DSP Change

1. Identify the sample rate, block size, queue depth, stateful DSP components,
   and affected sinks.
2. Capture a golden numeric vector, audio hash, or benchmark before editing.
3. Keep the render path free from per-sample allocation, LINQ, boxing, locks,
   exceptions, RPC, UI dispatch, logging sinks, and file I/O.
4. Preserve filter, oscillator, random, and envelope state across blocks.
5. Keep physical device, WAV, and null output behind `IAudioSink`.
6. Test start, steady state, stop, restart, device loss, underrun, and disposal
   where applicable.
7. Measure render duration and allocation with representative station counts.
8. Document intentional numeric or fidelity tradeoffs.

Never stream PCM through gRPC. The process hosting the engine owns the renderer
and audio device.

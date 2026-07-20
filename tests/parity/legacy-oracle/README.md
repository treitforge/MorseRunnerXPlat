# Legacy oracle sources

Active schema-v3 parity cases bind an immutable adapter version under a
versioned directory such as `v1/`. The live runner resolves that source and its
build recipe through the case descriptor and content-addressed build registry.
Source hashes cover the exact checked-out bytes. Version-specific line-ending
attributes therefore remain part of an immutable adapter's source contract.
The v16 source is pinned to LF because that is the byte sequence certified by
its retained descriptor and evidence. New Pascal oracle versions use CRLF.

The unversioned `LegacyOracle.lpr` retains its original Git-normalized content
as the generator named by the 25 pre-schema-v3 fixtures. Those fixtures and
their schema-v1 evidence are noncertifying historical provenance. The active
runner must never build, select, or fall back to the unversioned source.

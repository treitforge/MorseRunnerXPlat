# Legacy oracle sources

Active schema-v3 parity cases bind an immutable adapter version under a
versioned directory such as `v1/`. The live runner resolves that source and its
build recipe through the case descriptor and content-addressed build registry.

The unversioned `LegacyOracle.lpr` retains its original Git-normalized content
as the generator named by the 25 pre-schema-v3 fixtures. Those fixtures and
their schema-v1 evidence are noncertifying historical provenance. The active
runner must never build, select, or fall back to the unversioned source.

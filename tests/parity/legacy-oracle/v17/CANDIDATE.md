# QRM retry lifecycle candidate

This adapter pins the seed-1843 CE lifecycle for the first QRM station:

- 104 initial QRL message blocks
- a 72-block Gaussian retry timeout
- 191 long-CQ retry blocks
- removal after relative block 366
- all selected and aggregate audio hashes
- the complete QRM trigger sequence and terminal random checkpoint

The reviewed Windows CE observation has eight rows with
`observedValuesSha256`
`59095edce62b44e0a9d3f2a06347aef4890de0875dda9b1251f74e5752c9a465`.
The source SHA-256 is
`7972e95eebc377642d83788b7dfd4b3588c11fc1aceeda006b5292dfc44d258d`,
and the build-recipe SHA-256 is
`1bc0fd2928b2bb4ad1ff3e9b4348889575276a117cb960e41bc7e1443a8d98bb`.
The compiled candidate executable had SHA-256
`967a688613af91017d497eb9997b944f7bcc7e1d0f22c96edd67a2b66c0b1a71`.

An unchanged production-backed XPlat probe matched all eight rows at branch
checkpoint `4c540607b52d697d3751e4b83de1e6635d056ae1`. The probe was exploratory
and ran from a dirty authoring tree, so it is not retained green evidence.
The adapter is not active in the parity manifest because no CE-green/XPlat-red
boundary exists for this behavior after `bf98f85`. Activating it as a new
manifest-red case would violate the mandatory test-first parity sequence.

# Current state

Shared authoring and strict judge rules are discovered through `.skills/index.md`.
The compilation adapter uses `local` mode; its explicit eligible commands
and allocation boundary live in [.skills/compilation-count.json](../.skills/compilation-count.json).
Existing product versions and version tools retain their current milestones.
This routing change does not adopt a release ledger or compile a product.

Tracking: [#36](https://github.com/PinedaTec-EU/SphereIntegrationHub/issues/36).

## Node package runtime

The npm wrapper requires Node.js `>=26.12.1 <27`; `.nvmrc` selects 26.12.1
for repository work and the cross-platform npm CI workflow. Package-local
`.npmrc` enforces engines during repository installation. Consumers must enforce
engines in their own npm configuration if they require installation rejection.
No Node image or custom runtime cache exists here. Native releases are self-contained
.NET executables and do not embed Node. Version bump and publication remain part
of the established release workflow; npm tests/packing do not compile .NET.
Tracking: [#38](https://github.com/PinedaTec-EU/SphereIntegrationHub/issues/38).

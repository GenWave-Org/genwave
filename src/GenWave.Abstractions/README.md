# GenWave.Abstractions

The contract surface for [GenWave](https://github.com/GenWave-Org/genwave) modules — the seams a
module implements or consumes to extend a running GenWave station:

- **Selection** — `INextItemProvider` (the station's "what plays next" brain), `IMediaCatalog` +
  query records, `IMediaRating`
- **Events** — `IStationEventSink` + the `StationEvent` records published at the station's choke
  points (track aired, segment generated, enrichment completed, setting changed, media/library
  mutated)
- **AI DJ** — `ITtsSynthesizer`, `ITtsVoiceLister`, `ITtsSegmentSource`, `ISegmentCopyWriter`,
  `IPersonaPreviewWriter`, `IActivePersonaAccessor`
- **Live configuration** — the provider seams (`IStationScopeProvider`, `ICadenceProvider`,
  `IRotationSettingsProvider`, `IStationIdentityProvider`, `IRenderBudgetProvider`) that read
  fresh per call, so settings edits apply without restarts

A module registers its implementation over the host's default binding — see the GenWave
repository for the composition model.

## Versioning

Semantic versioning from the first publish: additions are minor, breaking contract changes are
major. The surface is deliberately lean — implementation-flavored contracts stay in the host.

## License

This contracts package is **MIT** so any module — open or commercial — can link it freely.
The GenWave Home implementation it describes is licensed AGPL-3.0-only, and GenWave Business
is licensed commercially; see the repository for the full story.

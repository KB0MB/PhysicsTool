# PhysicsTool

A tool for editing and previewing cloth, helper-bone, ragdoll and collision data from **The Legend of Zelda: Breath of the Wild** and **Tears of the Kingdom**, with experimental **Ocarina of Time (OoT)** support.

Available for Windows, Linux and macOS with an Avalonia interface.

Download **2.2.1** from [Releases](https://github.com/KB0MB/PhysicsTool/releases/tag/PT2.2.1). Extract the whole ZIP before running the app; the .NET runtime is included. See the [release notes](docs/RELEASE_2.2.1.md) for platform requirements and known limitations.

## Features

- Cloth editing and conversion between BotW HKCL and TotK BPHCL, plus experimental OoT cloth editing and cross-game conversion.
- Pose-based BotW support-bone and TotK helper-bone editing, with live previews, captured poses and advanced response curves. BPHYSSB/BPHHB conversion remains available.
- HKRG ragdoll editing and collision generation, including TotK polytopes.
- BotW/TotK actor viewing with BFRES models, textures, animations, physics export and equipment previews for Link in both games.
- Experimental OoT actor viewing, animation playback, all-cloth simulation and helper-bone editing.
- BotW rendering using Killz's reverse-engineered shaders, including native material parameters, UV selection and texture color-space handling.
- BPHYSICS editing and mass-aware wind settings for converted cloths.
- Map Editing for TotK shrines and MainField scenes, including terrain, water, caves, wells and actor links. Add Static/Dynamic actors or edit placements with undo/redo; existing GroundControl and supported collision editing remain available.
- English, French, Spanish and Japanese interfaces, with translated in-app documentation.

Open **Documentation** in the tool for editing guides, constraints, colliders and shortcuts.

Actor/model viewing requires your own game resources. No game files are included.

## Preview Limits

- OoT simulation and conversion are experimental; Malon Adult's sitting skirt is a known remaining mismatch. See the [OoT guide](docs/OOT.md) for supported editing operations.
- Placement-only map edits do not change baked collision. Cave and well geometry is preview-only.
- BotW uses neutral preview lighting, not the complete in-game weather, shadows, reflections or post-processing.
- Windows x64 has local regression coverage. ARM64 and macOS cross-builds still need native UI/GPU validation; macOS builds are not notarized.

See the [changelog](CHANGELOG.md) for major changes.

## Build from Source

Install the **.NET 10 SDK**, then run:

```sh
dotnet build PhysicsTool.slnx -c Release
dotnet run --project src/PhysicsTool.Desktop -c Release
```

Build, test and native-dependency details are in the [build guide](docs/RELEASING.md).

## Credits

- [VelouriasMoon / HKCLTool](https://github.com/VelouriasMoon/HKCLTool) for the original project PhysicsTool grew from.
- [banan039 / totkbits](https://github.com/banan039/totkbits) for foundational BPHCL reverse-engineering work.
- [Switch Toolbox](https://github.com/KillzXGaming/Switch-Toolbox), KillzXGaming, Syroot and its contributors for model, texture and material support.
- **Killz** for reverse-engineering and sharing the BotW shaders, and approving their inclusion in the repository and release ZIPs. See the [shader credit and provenance](Licenses/Killz-BotW-Shaders.txt).
- [Max Bebok (HailToDodongo) / Ice-Hail](https://gitlab.com/ice-hail/ice-hail) for the collision-generation foundation.
- [HKX2](https://github.com/krenyy/HKX2) and [Json.NET](https://www.newtonsoft.com/json) for supporting libraries.

Thanks to the other open-source projects behind PhysicsTool, including Avalonia. Full dependency credits and licenses are in [third-party notices](THIRD_PARTY_NOTICES.txt).

## License

See [LICENSE](LICENSE) and [third-party notices](THIRD_PARTY_NOTICES.txt) for the project and dependency terms. Retain the notices and corresponding source archives when distributing the application.

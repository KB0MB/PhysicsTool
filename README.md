# PhysicsTool

A tool for editing and previewing cloth, helper-bone, ragdoll and collision data from **The Legend of Zelda: Breath of the Wild** and **Tears of the Kingdom**.

Version 2.0 brings an updated Avalonia interface, improved simulation and conversion, and builds for Windows, Linux and macOS.

## Features

- Cloth editing and conversion between BotW HKCL and TotK BPHCL.
- Support/helper-bone editing, response curves and BPHYSSB/BPHHB conversion.
- HKRG ragdoll editing and collision generation, including TotK polytopes.
- BotW/TotK actor viewing with BFRES models, textures, animations and physics previews.
- English, French, Spanish and Japanese interfaces, with translated in-app documentation.

Open **Documentation** in the tool for editing guides, constraints, colliders and shortcuts. Keep an original copy of your files as a backup!

You need your own game resources for actor/model viewing. No game files are included. Simulation and material previews can still differ from the games, and some editing operations remain unsupported.

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
- [Max Bebok (HailToDodongo) / Ice-Hail](https://gitlab.com/ice-hail/ice-hail) for the collision-generation foundation.
- [HKX2](https://github.com/krenyy/HKX2) and [Json.NET](https://www.newtonsoft.com/json) for supporting libraries.

Thanks to the other open-source projects behind PhysicsTool, including Avalonia. Full dependency credits and licenses are in [third-party notices](THIRD_PARTY_NOTICES.txt).

## License

See [LICENSE](LICENSE) and [third-party notices](THIRD_PARTY_NOTICES.txt) for the project and dependency terms. Retain the notices and corresponding source archives when distributing the application.

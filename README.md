# Canopy Studio

A desktop app for Windows and Linux that installs and updates [Canopy](https://github.com/Zorkats/Canopy), the Village in the Shade mod loader, and [VillageQoL](https://github.com/Zorkats/VillageQoL), turns mods off and on, makes new mods, edits their Lua with completion of the Canopy API, and browses the game's files.

It finds the game in your Steam libraries and changes the game folder only when you ask it to.

## Install

| System | Download | Start |
|---|---|---|
| Windows 10 or 11, 64-bit | `CanopyStudio-<version>-windows-x64.zip` | Extract anywhere, run `CanopyStudio.exe`. |
| Linux, 64-bit (x86-64) | `CanopyStudio-<version>-linux-x64.tar.gz` | Extract anywhere, run `CanopyStudio`. |

Both are on the [releases page](https://github.com/Zorkats/CanopyStudio/releases/latest) and carry everything they need: no .NET or other runtime to install. Canopy Studio keeps its own files in `%APPDATA%\CanopyStudio` on Windows and `~/.config/CanopyStudio` on Linux.

On Linux the game runs through Proton, and Canopy loads only with the Steam launch option `WINEDLLOVERRIDES="dinput8=n,b" %command%`. Canopy Studio shows it with a **Copy** button, but never changes Steam's files itself. It finds Steam installed natively or as a Flatpak.

## Installing Canopy and VillageQoL

Canopy Studio installs Canopy from the latest release of [Zorkats/Canopy](https://github.com/Zorkats/Canopy/releases/latest) and VillageQoL from the latest release of [Zorkats/VillageQoL](https://github.com/Zorkats/VillageQoL/releases/latest). Each has its own version, and each download is checked against the SHA-256 GitHub lists for it before any of it reaches the game folder. When a newer release is out, the setup page offers the update. Nightly builds are prereleases and are never installed.

## Building from source

You need the .NET 10 SDK, CMake, Ninja and a C++ compiler: Visual Studio 2022 with the **Desktop development with C++** workload on Windows, GCC 12 or newer on Linux. vcpkg is not needed.

Canopy comes in as a git submodule in `external/Canopy`. Canopy Studio reads the game through Canopy's `canopy_core` library (`canopy_core.dll`, `libcanopy_core.so`), and its tests read Canopy's Lua docs. Clone with `--recursive`, or run `git submodule update --init` in an existing clone.

Build `canopy_core` into `build/canopy`, where the project looks for it. On Windows, run this from an **x64 Native Tools Command Prompt for VS 2022**:

```sh
cmake -S external/Canopy -B build/canopy -G Ninja -DCMAKE_BUILD_TYPE=Release -DCANOPY_BUILD_LOADER=OFF -DCANOPY_BUILD_TESTS=OFF -DCANOPY_BUILD_EXAMPLES=OFF
cmake --build build/canopy --target canopy_core
```

Then build, test and publish Canopy Studio:

```sh
dotnet build CanopyStudio.sln -c Release
dotnet test tests/CanopyStudio.Tests -c Release
dotnet publish src/CanopyStudio/CanopyStudio.csproj -c Release -r win-x64 --self-contained -o build/studio-win
```

On Linux, publish with `-r linux-x64`. To take `canopy_core` from another folder, such as a Canopy checkout of your own, add `-p:CanopyCoreDir=<folder>` with the folder's full path.

The Lua editor's completion list is `src/CanopyStudio.Core/Lua/lua_api_catalog.json`, generated from Canopy's `docs/LUA.md` and `src/Canopy/lua_commands.h`. After moving the submodule to a Canopy with Lua API changes, regenerate it with `python -I scripts/lua_api_catalog.py`; a test fails while it is behind.

## Releases

GitHub Actions builds and tests every push and pull request on Windows and Linux. Pushing a tag such as `v1.2.0` publishes `CanopyStudio-1.2.0-windows-x64.zip` and `CanopyStudio-1.2.0-linux-x64.tar.gz`.

| Folder | Contents |
|---|---|
| `src\CanopyStudio\` | The app: views, view models and services. |
| `src\CanopyStudio.Core\` | Everything without a window: `canopy_core`'s calls, finding the game, the installer and the settings file. |
| `tests\CanopyStudio.Tests\` | The tests. |
| `scripts\` | The Lua catalog generator. |
| `external\Canopy\` | Canopy, as a git submodule. |

## License

MIT. See [LICENSE](LICENSE). The packages also carry the .NET runtime and the libraries Canopy Studio uses; their licenses are in [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt).

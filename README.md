# AssetRipper (CloudKickOff fork)

A hard fork of [AssetRipper](https://github.com/AssetRipper/AssetRipper), the tool that reads Unity game files and exports
them as a Unity project. This fork focuses on exports of recent Unity 6 games that open and compile in the Unity editor with
as little hand-fixing as possible.

AssetRipper was created by **[ds5678](https://github.com/ds5678)** (Jeremy Pritts) and is developed by the
[AssetRipper contributors](https://github.com/AssetRipper/AssetRipper/graphs/contributors). All of the original work is
theirs; this fork adds the changes listed below. If AssetRipper is useful to you, consider supporting the original author
through the links on the [upstream repository](https://github.com/AssetRipper/AssetRipper#tips).

## What this fork adds

Exports to a Unity project:

- **Unity packages instead of copies.** Game assemblies that come from Unity packages (URP, Input System, XR Interaction
  Toolkit and so on) are replaced with references to those packages, and `Packages/manifest.json` lists them. Components
  keep working because their scripts are remapped to the package's script files. This reads the packages bundled with an
  installed Unity editor, so it works offline.
- **Package shaders instead of placeholders.** Shaders compiled from a referenced package, such as URP's Lit, point at the
  package's shader file, so materials using them render properly.
- **Native plugins** from the game's `Data/Plugins` folder are copied into `Assets/Plugins`, except Burst output and
  libraries a referenced package already contains.
- **Chosen assemblies as source.** The `DecompiledAssemblies` setting decompiles extra assemblies to scripts, besides the
  usual `Assembly-CSharp`.
- **A dependency report.** Every export writes `Dependencies.md` next to `Assets`: each assembly kept as a plugin, with its
  version, product and company, how many components and assets use it, what references it, and whether anything uses it
  at all.
- **Photon Fusion weaving is reversed** in decompiled code, so games built with Fusion compile again.
- **Texture arrays and 3D textures** import with their layers.
- Fixes for exports that wouldn't compile or open: `[SerializeField]` on non-fields, `[SerializeReference]` on
  `UnityEngine.Object` fields, `RenderingLayerMask`, Unity's generated job registration file, `Color` and `Integer` shader
  properties, and game assemblies whose types moved to other package assemblies.
- The export continues when a single asset fails, and serialized files with trailing data are detected.

See the [commit history](https://github.com/CloudKickOff/AssetRipper/commits/master) for details.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build.
- For package references (on by default): a Unity editor installed with Unity Hub, ideally the game's exact version.
  Otherwise the newest installed editor of the same major version that is at least as new as the game is used, since
  Unity upgrades projects but doesn't downgrade them. `UnityEditorPath` can point at one explicitly.

## Building

```sh
dotnet build                                   # everything, Debug
dotnet test                                    # the tests
dotnet publish Source/AssetRipper.GUI.Free -c Release -r win-x64   # a self-contained app
```

`dotnet publish` takes any runtime identifier: `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`.
Output goes to `Source/0Bins/AssetRipper.GUI.Free/Release/<runtime>/publish/`.

## Using it

Run `AssetRipper.GUI.Free`. It starts a local web server and opens the interface in your browser.

1. **File > Open Folder** and choose the game's folder: the one containing `<Game>_Data`, so streaming assets and
   Addressables bundles are found too.
2. **Export > Export All Files**, choose a new empty folder and **Export Unity Project**. The result has `ExportedProject`
   (open this in Unity) and `AuxiliaryFiles`.
3. Open `ExportedProject` with the Unity editor version the export reports in its log.

### Without the browser

```sh
AssetRipper.GUI.Free --headless --port 5000 --log-path export.log
curl --data-urlencode "Path=C:/Games/MyGame" http://127.0.0.1:5000/LoadFolder
curl --data-urlencode "Path=C:/Exports/MyGame" http://127.0.0.1:5000/Export/UnityProject
```

In headless mode a non-empty export folder is deleted first, so always export to a new folder.

Other options: `--log false` to turn logging off, and `--help` for the full list.

### Settings

Settings are read from `AssetRipper.Settings.json` next to the executable, which the settings page writes when **Save
settings to disk** is ticked. Keys left out keep their defaults. The settings this fork adds:

```json
{
  "Import": {},
  "Processing": {
    "ReverseFusionWeaving": true
  },
  "Export": {
    "ReferenceUnityPackages": true,
    "UnityEditorPath": null,
    "DecompiledAssemblies": [ "MyStudio.Networking", "MyStudio.Gameplay" ]
  }
}
```

- `ReferenceUnityPackages`: replace package assemblies and shaders with package references.
- `UnityEditorPath`: the editor installation whose packages are used; `null` finds one.
- `DecompiledAssemblies`: extra assemblies to decompile to scripts in Hybrid script mode.
- `ReverseFusionWeaving`: undo Photon Fusion's weaving in decompiled assemblies.

## Branches and releases

- `dev` is where work happens. Every push and pull request is built and tested.
- `master` holds tested changes, merged from `dev`.
- Releases are made by hand: **Actions > Release > Run workflow**, choose the branch (normally `master`) and enter a
  version. The workflow builds all six platforms, tags the commit `v<version>` and publishes a GitHub release with the
  downloads.

## Legal

- AssetRipper is licensed under the [GNU General Public License v3.0](LICENSE.md), and so is this fork. Its source,
  including every change made here, is available in this repository.
- AssetRipper is not sponsored by or affiliated with Unity Technologies or its affiliates. "Unity" is a registered
  trademark of Unity Technologies or its affiliates in the U.S. and elsewhere.
- The upstream [Credits](https://assetripper.github.io/AssetRipper/articles/Credits.html) page lists the libraries and
  people AssetRipper builds on.
- Only export games you have the right to, and respect the licenses of their content.

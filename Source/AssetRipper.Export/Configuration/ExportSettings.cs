using AssetRipper.Import.Logging;

namespace AssetRipper.Export.Configuration;

public sealed record class ExportSettings
{
	/// <summary>
	/// The file format that audio clips get exported in. Recommended: Ogg
	/// </summary>
	public AudioExportFormat AudioExportFormat { get; set; } = AudioExportFormat.Default;

	/// <summary>
	/// The file format that images (like textures) get exported in.
	/// </summary>
	public ImageExportFormat ImageExportFormat { get; set; } = ImageExportFormat.Png;

	/// <summary>
	/// The file format that images (like textures) get exported in.
	/// </summary>
	public LightmapTextureExportFormat LightmapTextureExportFormat { get; set; } = LightmapTextureExportFormat.Yaml;

	/// <summary>
	/// How are MonoScripts exported? Recommended: Decompiled
	/// </summary>
	public ScriptExportMode ScriptExportMode { get; set; } = ScriptExportMode.Hybrid;

	/// <summary>
	/// The C# language version of decompiled scripts.
	/// </summary>
	public ScriptLanguageVersion ScriptLanguageVersion { get; set; } = ScriptLanguageVersion.AutoSafe;

	/// <summary>
	/// If true, type references in scripts are fully qualified.
	/// </summary>
	public bool ScriptTypesFullyQualified { get; set; } = false;

	/// <summary>
	/// How to export shaders?
	/// </summary>
	public ShaderExportMode ShaderExportMode { get; set; } = ShaderExportMode.Dummy;

	/// <summary>
	/// Should sprites be exported as a texture? Recommended: Native
	/// </summary>
	public SpriteExportMode SpriteExportMode { get; set; } = SpriteExportMode.Yaml;

	/// <summary>
	/// How are text assets exported?
	/// </summary>
	public TextExportMode TextExportMode { get; set; } = TextExportMode.Parse;

	public bool ExportUnreadableAssets { get; set; } = false;

	/// <summary>
	/// If true, the original texture extension (when available) will be preferred over the selected <see cref="ImageExportFormat"/>.
	/// </summary>
	public bool PreferOriginalTextureExtension { get; set; } = true;

	/// <summary>
	/// If true, assemblies that come from Unity packages are replaced with references to those packages,
	/// using the packages bundled with an installed Unity editor.
	/// </summary>
	public bool ReferenceUnityPackages { get; set; } = true;

	/// <summary>
	/// The Unity editor installation whose bundled packages are referenced, or null to find one automatically.
	/// </summary>
	public string? UnityEditorPath { get; set; }

	/// <summary>
	/// The names of extra assemblies that are decompiled to scripts in <see cref="ScriptExportMode.Hybrid"/> mode,
	/// in addition to predefined assemblies like Assembly-CSharp.
	/// </summary>
	public string[] DecompiledAssemblies { get; set; } = [];

	/// <summary>
	/// If true, assets listed in the game's Addressables catalog keep their original guids,
	/// so that the references the game uses to load them still resolve and the content can be rebuilt.
	/// </summary>
	public bool RestoreAddressablesGuids { get; set; } = true;

	/// <summary>
	/// If true, exported files get guids derived from where their assets are in the game files instead of random ones,
	/// so that exporting the same game again gives the same guids and Unity can reuse its import results.
	/// </summary>
	public bool StableGuids { get; set; } = true;

	/// <summary>
	/// Packages to reference in addition to the ones bundled with the Unity editor, such as platform SDKs from a scoped registry:
	/// package archives (.tgz), package directories, or directories containing either.
	/// Game assemblies built from them are replaced with references to the packages, like <see cref="ReferenceUnityPackages"/>.
	/// </summary>
	public string[] ExtraPackages { get; set; } = [];

	/// <summary>
	/// Scoped registries to add to the project's package manifest, so that Unity can download the <see cref="ExtraPackages"/>.
	/// </summary>
	public ScopedRegistry[] PackageRegistries { get; set; } = [];

	public bool SaveSettingsToDisk { get; set; }

	public string? LanguageCode { get; set; }

	public void Log()
	{
		Logger.Info(LogCategory.General, $"{nameof(AudioExportFormat)}: {AudioExportFormat}");
		Logger.Info(LogCategory.General, $"{nameof(ImageExportFormat)}: {ImageExportFormat}");
		Logger.Info(LogCategory.General, $"{nameof(LightmapTextureExportFormat)}: {LightmapTextureExportFormat}");
		Logger.Info(LogCategory.General, $"{nameof(ScriptExportMode)}: {ScriptExportMode}");
		Logger.Info(LogCategory.General, $"{nameof(ScriptLanguageVersion)}: {ScriptLanguageVersion}");
		Logger.Info(LogCategory.General, $"{nameof(ShaderExportMode)}: {ShaderExportMode}");
		Logger.Info(LogCategory.General, $"{nameof(SpriteExportMode)}: {SpriteExportMode}");
		Logger.Info(LogCategory.General, $"{nameof(TextExportMode)}: {TextExportMode}");
		Logger.Info(LogCategory.General, $"{nameof(ExportUnreadableAssets)}: {ExportUnreadableAssets}");
		Logger.Info(LogCategory.General, $"{nameof(PreferOriginalTextureExtension)}: {PreferOriginalTextureExtension}");
		Logger.Info(LogCategory.General, $"{nameof(ReferenceUnityPackages)}: {ReferenceUnityPackages}");
		Logger.Info(LogCategory.General, $"{nameof(UnityEditorPath)}: {UnityEditorPath ?? "(automatic)"}");
		Logger.Info(LogCategory.General, $"{nameof(DecompiledAssemblies)}: {(DecompiledAssemblies.Length > 0 ? string.Join(", ", DecompiledAssemblies) : "(none)")}");
	}
}

using AssetRipper.Assets;
using AssetRipper.Export.UnityProjects.Scripts.UnityPackages;
using AssetRipper.Processing.Scenes;

namespace AssetRipper.Export.UnityProjects.Project;

/// <summary>
/// Replaces game assets that are copies of files in a Unity package's Resources folders with references to those files.
/// </summary>
/// <remarks>
/// A game built with a package contains the package's resources, such as settings and style assets.
/// When the project references the package, exporting them as well would give each resource name two files,
/// and Resources.Load could return either. Only packages that the game's assemblies came from are used,
/// and only standalone assets: objects that belong to a scene or prefab hierarchy are exported normally.
/// </remarks>
public sealed class PackageResourceExporter : IAssetExporter
{
	private readonly UnityPackageIndex index;
	private readonly IReadOnlySet<string> referencedPackages;

	public PackageResourceExporter(UnityPackageIndex index, IReadOnlySet<string> referencedPackages)
	{
		this.index = index;
		this.referencedPackages = referencedPackages;
	}

	public bool TryCreateCollection(IUnityObjectBase asset, [NotNullWhen(true)] out IExportCollection? exportCollection)
	{
		if (!SceneHelpers.IsSceneCompatible(asset)
			&& asset.OriginalPath is { } originalPath
			&& GetResourceName(originalPath) is { } resourceName
			&& index.TryGetResource(resourceName, out PackageResource? resource)
			&& referencedPackages.Contains(resource.PackageName))
		{
			exportCollection = new SingleRedirectExportCollection(asset, ExportIdHandler.GetMainExportID(asset), resource.Guid, AssetType.Serialized);
			return true;
		}
		exportCollection = null;
		return false;
	}

	/// <summary>
	/// The path inside the innermost Resources folder, without extension if it has one.
	/// </summary>
	private static string? GetResourceName(string originalPath)
	{
		string path = originalPath.Replace('\\', '/');
		int folder = path.LastIndexOf("Resources/", StringComparison.OrdinalIgnoreCase);
		if (folder < 0 || folder > 0 && path[folder - 1] != '/')
		{
			return null;
		}
		string relative = path[(folder + "Resources/".Length)..];
		int dot = relative.LastIndexOf('.');
		return dot > relative.LastIndexOf('/') ? relative[..dot] : relative;
	}

	public AssetType ToExportType(IUnityObjectBase asset) => AssetType.Serialized;

	public bool ToUnknownExportType(Type type, out AssetType assetType)
	{
		assetType = AssetType.Serialized;
		return false;
	}
}

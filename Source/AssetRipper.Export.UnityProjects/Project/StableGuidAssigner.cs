using AssetRipper.Assets;
using AssetRipper.Import.Logging;

namespace AssetRipper.Export.UnityProjects.Project;

/// <summary>
/// Gives exported files guids derived from where their assets are in the game files, instead of random ones.
/// </summary>
/// <remarks>
/// Exporting the same game again then gives every file the same guid. Unity can keep the import results of a previous
/// export (its Library folder) instead of importing every file again, and changes that refer to files by guid,
/// such as a patch set, still apply.
/// <br/>
/// The guid is a hash of the asset's bundle, file and path ID. A prefab uses its root game object, which is read from
/// the game files, rather than the prefab asset that processing created. If two files would get the same guid,
/// the second keeps its random one.
/// </remarks>
internal static class StableGuidAssigner
{
	public static void Assign(IEnumerable<IExportCollection> collections)
	{
		HashSet<UnityGuid> used = [];
		int assigned = 0;
		int duplicates = 0;
		foreach (IExportCollection collection in collections)
		{
			if (collection is not IOriginalGuidTarget target)
			{
				continue;
			}
			IUnityObjectBase asset = (collection as PrefabExportCollection)?.RootGameObject ?? target.MainAsset;
			UnityGuid guid = UnityGuid.Md5Hash($"{asset.Collection.Bundle.Name}/{asset.Collection.Name}/{asset.PathID}");
			if (used.Add(guid))
			{
				target.Guid = guid;
				assigned++;
			}
			else
			{
				duplicates++;
			}
		}
		Logger.Info(LogCategory.Export, $"Assigned stable guids to {assigned} exported files");
		if (duplicates > 0)
		{
			Logger.Warning(LogCategory.Export, $"{duplicates} exported files kept random guids, because their stable guids were already taken");
		}
	}
}

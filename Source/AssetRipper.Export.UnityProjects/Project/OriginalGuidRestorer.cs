using AssetRipper.Assets;
using AssetRipper.Import.Logging;

namespace AssetRipper.Export.UnityProjects.Project;

/// <summary>
/// An export collection whose guid can be replaced with the asset's original guid.
/// </summary>
internal interface IOriginalGuidTarget
{
	IUnityObjectBase MainAsset { get; }
	UnityGuid Guid { get; set; }
}

/// <summary>
/// Gives exported files their original guids, where those are known by original path.
/// </summary>
/// <remarks>
/// Exported files normally get random guids. Content that a game finds by guid at runtime, such as Addressables assets
/// behind an AssetReference, then can't be found in the editor or rebuilt. With the original guid, the references
/// serialized in the game's assets resolve again.
/// <br/>
/// Paths are compared ignoring case. When no file was exported at the original path, a file in the same folder with the
/// same name and another extension is used, since some assets change format on export (texture arrays, audio, models).
/// </remarks>
internal static class OriginalGuidRestorer
{
	public static void Restore(IReadOnlyDictionary<string, string> originalPaths, IEnumerable<IExportCollection> collections)
	{
		Dictionary<string, List<IOriginalGuidTarget>> byPath = new(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, List<IOriginalGuidTarget>> byPathWithoutExtension = new(StringComparer.OrdinalIgnoreCase);
		foreach (IExportCollection collection in collections)
		{
			if (collection is not IOriginalGuidTarget target || GetOriginalPath(collection, target) is not { } path)
			{
				continue;
			}
			Add(byPath, path, target);
			Add(byPathWithoutExtension, RemoveExtension(path), target);
		}

		HashSet<IOriginalGuidTarget> assigned = [];
		int restored = 0;
		List<string> missing = [];
		List<string> ambiguous = [];
		foreach ((string guid, string path) in originalPaths)
		{
			if (!byPath.TryGetValue(path, out List<IOriginalGuidTarget>? candidates)
				&& !byPathWithoutExtension.TryGetValue(RemoveExtension(path), out candidates))
			{
				missing.Add(path);
				continue;
			}
			if (Choose(candidates) is not { } target || !assigned.Add(target))
			{
				ambiguous.Add(path);
				continue;
			}
			target.Guid = UnityGuid.Parse(guid);
			restored++;
		}

		Logger.Info(LogCategory.Export, $"Restored the original guids of {restored} of {originalPaths.Count} assets");
		foreach (string path in missing)
		{
			Logger.Warning(LogCategory.Export, $"No exported asset for the original path {path}, so its guid was not restored");
		}
		foreach (string path in ambiguous)
		{
			Logger.Warning(LogCategory.Export, $"Several exported assets could have the original path {path}, so its guid was not restored");
		}
	}

	/// <summary>
	/// The one candidate, or the prefab among several, as a model is exported as a prefab plus its meshes.
	/// </summary>
	private static IOriginalGuidTarget? Choose(List<IOriginalGuidTarget> candidates)
	{
		if (candidates.Count == 1)
		{
			return candidates[0];
		}
		List<IOriginalGuidTarget> prefabs = candidates.Where(c => c is PrefabExportCollection).ToList();
		return prefabs.Count == 1 ? prefabs[0] : null;
	}

	private static string? GetOriginalPath(IExportCollection collection, IOriginalGuidTarget target)
	{
		return target.MainAsset.OriginalPath
			?? (collection as PrefabExportCollection)?.RootGameObject.OriginalPath;
	}

	private static string RemoveExtension(string path)
	{
		int slash = path.LastIndexOf('/');
		int dot = path.LastIndexOf('.');
		return dot > slash ? path[..dot] : path;
	}

	private static void Add(Dictionary<string, List<IOriginalGuidTarget>> index, string key, IOriginalGuidTarget target)
	{
		if (!index.TryGetValue(key, out List<IOriginalGuidTarget>? list))
		{
			list = [];
			index.Add(key, list);
		}
		list.Add(target);
	}
}

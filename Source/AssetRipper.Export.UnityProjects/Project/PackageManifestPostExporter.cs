using AssetRipper.Export.Configuration;
using AssetRipper.Export.UnityProjects.Scripts.UnityPackages;
using AssetRipper.Import.Logging;
using AssetRipper.Processing;

namespace AssetRipper.Export.UnityProjects.Project;

public class PackageManifestPostExporter : IPostExporter
{
	public void DoPostExport(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		string packagesDirectory = fileSystem.Path.Join(settings.ProjectRootPath, "Packages");
		fileSystem.Directory.Create(packagesDirectory);
		string path = fileSystem.Path.Join(packagesDirectory, "manifest.json");
		PackageManifest manifest = CreateManifest(settings.Version);
		AddReferencedPackages(manifest, gameData, settings);
		if (settings.ExportSettings.PackageRegistries.Length > 0)
		{
			manifest.ScopedRegistries = settings.ExportSettings.PackageRegistries.ToList();
		}
		using Stream stream = fileSystem.File.Create(path);
		manifest.Save(stream);
	}

	protected virtual PackageManifest CreateManifest(UnityVersion version)
	{
		return PackageManifest.CreateDefault(version);
	}

	/// <summary>
	/// Add the packages whose assemblies were replaced with package references.
	/// </summary>
	private static void AddReferencedPackages(PackageManifest manifest, GameData gameData, FullConfiguration settings)
	{
		if (!settings.ExportSettings.ReferenceUnityPackages || !gameData.AssemblyManager.IsSet)
		{
			return;
		}
		UnityPackageIndex? index = UnityPackageIndex.GetForProject(settings.Version, settings.ExportSettings);
		if (index is null)
		{
			return;
		}
		foreach (AsmResolver.DotNet.AssemblyDefinition gameAssembly in gameData.AssemblyManager.GetAssemblies().OrderBy(a => a.Name?.Value))
		{
			foreach (PackageAssembly assembly in index.GetReplacements(gameAssembly))
			{
				string reference = GetReference(assembly, index, settings.ExportSettings.PackageRegistries);
				if (manifest.Dependencies.TryAdd(assembly.PackageName, reference))
				{
					Logger.Info(LogCategory.Export, $"Referencing package {assembly.PackageName} {reference}");
				}
			}
		}
		Logger.Info(LogCategory.Export, $"The package versions match Unity {index.EditorVersion}. Open the project with that version.");
	}

	/// <summary>
	/// The manifest's reference to a package: its version, which Unity looks up in its own packages or a registry, or,
	/// for an extra package that no registry provides (one only distributed as a download), its archive or directory.
	/// </summary>
	private static string GetReference(PackageAssembly assembly, UnityPackageIndex index, ScopedRegistry[] registries)
	{
		string name = assembly.PackageName;
		bool inRegistry = registries.Any(r => r.Scopes.Any(scope => name == scope || name.StartsWith(scope + ".", StringComparison.Ordinal)));
		return !inRegistry && index.TryGetExtraPackageSource(name, out string? path)
			? "file:" + path.Replace('\\', '/')
			: assembly.PackageVersion;
	}
}

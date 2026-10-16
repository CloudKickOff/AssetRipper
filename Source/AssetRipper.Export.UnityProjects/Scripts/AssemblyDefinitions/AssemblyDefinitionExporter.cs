using AsmResolver.DotNet;
using System.Text.Json;

namespace AssetRipper.Export.UnityProjects.Scripts.AssemblyDefinitions;

public static class AssemblyDefinitionExporter
{
	/// <summary>
	/// Write an assembly definition reference, which compiles the scripts in <paramref name="folder"/> into another
	/// assembly, such as one in a package.
	/// </summary>
	public static void ExportReference(string folder, string assemblyName, FileSystem fileSystem)
	{
		string assetPath = fileSystem.Path.Join(folder, $"{assemblyName}.asmref");
		// Written by hand: reflection-based serialization is disabled in trimmed builds. Assembly names need no escaping.
		fileSystem.File.WriteAllText(assetPath, $"{{\n\t\"reference\": \"{assemblyName}\"\n}}\n");
	}

	/// <param name="getReferenceNames">
	/// Gets the assembly definition names to reference for an assembly that the exported one references.
	/// Engine assemblies and plugins are referenced automatically, so they have none.
	/// </param>
	public static void Export(AssemblyDefinitionDetails details, FileSystem fileSystem, Func<string, IEnumerable<string>> getReferenceNames)
	{
		string assetPath = fileSystem.Path.Join(details.OutputFolder, $"{details.AssemblyName}.asmdef");

		AssemblyDefinitionAsset asset = new AssemblyDefinitionAsset(details.AssemblyName);
		ModuleDefinition? module = details.Assembly?.ManifestModule;
		if (module is not null)
		{
			foreach (AssemblyReference reference in module.AssemblyReferences)
			{
				if (reference.Name is null)
				{
					continue;
				}

				foreach (string name in getReferenceNames(reference.Name))
				{
					if (!asset.References.Contains(name))
					{
						asset.References.Add(name);
					}
				}
			}
		}

		string assetData = JsonSerializer.Serialize(asset, AssemblyDefinitionSerializerContext.Default.AssemblyDefinitionAsset);
		fileSystem.File.WriteAllText(assetPath, assetData);
	}
}

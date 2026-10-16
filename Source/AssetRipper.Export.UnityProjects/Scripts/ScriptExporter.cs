using AssetRipper.Assets;
using AssetRipper.Export.Configuration;
using AssetRipper.Export.UnityProjects.Scripts.UnityPackages;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly;
using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_115;
using AssetRipper.SourceGenerated.Extensions;

namespace AssetRipper.Export.UnityProjects.Scripts;

public class ScriptExporter : IAssetExporter
{
	public ScriptExporter(IAssemblyManager assemblyManager, FullConfiguration configuration)
	{
		AssemblyManager = assemblyManager;
		Decompiler = new ScriptDecompiler(AssemblyManager)
		{
			LanguageVersion = configuration.ExportSettings.ScriptLanguageVersion.ToCSharpLanguageVersion(configuration.Version),
			ScriptContentLevel = configuration.ImportSettings.ScriptContentLevel,
			FullyQualifiedTypeNames = configuration.ExportSettings.ScriptTypesFullyQualified,
		};
		ExportMode = configuration.ExportSettings.ScriptExportMode;
		DecompiledAssemblies = configuration.ExportSettings.DecompiledAssemblies.ToHashSet();
		ReferenceAssemblyDictionary = ReferenceAssemblies.GetReferenceAssemblies(AssemblyManager, configuration.Version);
		if (configuration.ExportSettings.ReferenceUnityPackages && AssemblyManager.IsSet)
		{
			PackageIndex = UnityPackageIndex.GetForProject(configuration.Version, configuration.ExportSettings);
			if (PackageIndex is null)
			{
				Logger.Warning(LogCategory.Export, $"No Unity editor installation was found for {configuration.Version}, so package assemblies will be exported as plugins.");
			}
			else
			{
				foreach (AsmResolver.DotNet.AssemblyDefinition assembly in AssemblyManager.GetAssemblies())
				{
					IReadOnlyList<PackageAssembly> replacements = PackageIndex.GetReplacements(assembly);
					if (replacements.Count > 0)
					{
						packageReplacements[assembly.Name!] = replacements;
						if (configuration.ExportSettings.KeepTypesMissingFromPackages.Contains(assembly.Name!.Value))
						{
							AddKeptTypes(assembly, replacements, PackageIndex);
						}
					}
				}
			}
		}
	}

	public IAssemblyManager AssemblyManager { get; }
	public ScriptExportMode ExportMode { get; }
	/// <summary>
	/// Extra assemblies that are decompiled in <see cref="ScriptExportMode.Hybrid"/> mode.
	/// </summary>
	public IReadOnlySet<string> DecompiledAssemblies { get; }
	/// <summary>
	/// The packages that assemblies can be replaced with, or null if packages aren't referenced.
	/// </summary>
	public UnityPackageIndex? PackageIndex { get; }
	/// <summary>
	/// The package assemblies replacing each game assembly that comes from a package.
	/// </summary>
	private readonly Dictionary<string, IReadOnlyList<PackageAssembly>> packageReplacements = new();
	private readonly HashSet<string> missingPackageScripts = new();
	/// <summary>
	/// For each game assembly in <see cref="ExportSettings.KeepTypesMissingFromPackages"/>, the package assembly its kept
	/// types are compiled into, and the full names of those top level types.
	/// </summary>
	private readonly Dictionary<string, (PackageAssembly Target, HashSet<string> Types)> keptTypes = new();
	internal ScriptDecompiler Decompiler { get; }
	internal Dictionary<string, UnityGuid> ReferenceAssemblyDictionary { get; }
	private bool HasDecompiled { get; set; } = false;
	private static long MonoScriptDecompiledFileID { get; } = ExportIdHandler.GetMainExportID((int)ClassIDType.MonoScript);

	public bool TryCreateCollection(IUnityObjectBase asset, [NotNullWhen(true)] out IExportCollection? exportCollection)
	{
		if (asset is IMonoScript script)
		{
			if (HasDecompiled)
			{
				exportCollection = new SingleRedirectExportCollection(asset, CreateExportPointer(script));
			}
			else
			{
				HasDecompiled = true;
				if (AssemblyManager.IsSet)
				{
					exportCollection = new ScriptExportCollection(this, script);
				}
				else
				{
					exportCollection = new EmptyScriptExportCollection(this, script);
				}
			}
			return true;
		}
		else
		{
			exportCollection = null;
			return false;
		}
	}

	private void AddKeptTypes(AsmResolver.DotNet.AssemblyDefinition assembly, IReadOnlyList<PackageAssembly> replacements, UnityPackageIndex index)
	{
		string name = assembly.Name!;
		// The kept types need the package assembly's internals, which only the assembly itself can see.
		PackageAssembly? target = replacements.FirstOrDefault(a => a.AssemblyName == name);
		if (target is null)
		{
			Logger.Warning(LogCategory.Export, $"{name} isn't replaced by a package assembly of the same name, so its types missing from the package can't be kept.");
			return;
		}
		HashSet<string> types = assembly.ManifestModule?.TopLevelTypes
			.Where(t => t.Name?.Value is { } n && n != "<Module>" && !n.StartsWith('<'))
			.Select(t => t.FullName)
			.Where(fullName => !index.DeclaresType(fullName))
			.ToHashSet() ?? [];
		if (types.Count > 0)
		{
			keptTypes.Add(name, (target, types));
			Logger.Info(LogCategory.Export, $"Keeping {types.Count} types of {name} that {target.PackageName} {target.PackageVersion} doesn't have: {string.Join(", ", types.Order())}");
		}
	}

	/// <summary>
	/// Whether a type of a game assembly replaced by a package is kept (see <see cref="ExportSettings.KeepTypesMissingFromPackages"/>).
	/// </summary>
	public bool IsKeptType(string assemblyName, string fullName)
	{
		return keptTypes.TryGetValue(assemblyName, out (PackageAssembly Target, HashSet<string> Types) kept) && kept.Types.Contains(fullName);
	}

	/// <summary>
	/// The package assembly that the kept types of a game assembly are compiled into, if any of its types are kept.
	/// </summary>
	public bool TryGetKeptTypesTarget(string assemblyName, [NotNullWhen(true)] out PackageAssembly? target, [NotNullWhen(true)] out IReadOnlySet<string>? types)
	{
		if (keptTypes.TryGetValue(assemblyName, out (PackageAssembly Target, HashSet<string> Types) kept))
		{
			(target, types) = (kept.Target, kept.Types);
			return true;
		}
		(target, types) = (null, null);
		return false;
	}

	/// <summary>
	/// Whether a script's class is a kept type of a game assembly replaced by a package, so it's decompiled.
	/// </summary>
	public bool IsKeptScript(IMonoScript script)
	{
		return IsKeptType(script.GetAssemblyNameFixed(), GetTopLevelFullName(script));
	}

	private static string GetTopLevelFullName(IMonoScript script)
	{
		// A nested class's MonoScript names the outer class first, separated by a slash or a dot.
		string className = script.GetNonGenericClassName();
		int nested = className.IndexOfAny(['/', '+']);
		string topLevel = nested < 0 ? className : className[..nested];
		string @namespace = script.Namespace.String;
		return @namespace.Length == 0 ? topLevel : $"{@namespace}.{topLevel}";
	}

	public AssemblyExportType GetExportType(IMonoScript script)
	{
		return GetExportType(script.GetAssemblyNameFixed());
	}

	public MetaPtr CreateExportPointer(IMonoScript script)
	{
		return GetExportType(script) switch
		{
			AssemblyExportType.Decompile => new(MonoScriptDecompiledFileID, ScriptHashing.CalculateScriptGuid(script), AssetType.Meta),
			AssemblyExportType.Skip => new(ScriptHashing.CalculateScriptFileID(script), ReferenceAssemblyDictionary[script.GetAssemblyNameFixed()], AssetType.Meta),
			AssemblyExportType.Package => CreatePackageExportPointer(script),
			_ => new(ScriptHashing.CalculateScriptFileID(script), ScriptHashing.CalculateAssemblyGuid(script), AssetType.Meta),
		};
	}

	private MetaPtr CreatePackageExportPointer(IMonoScript script)
	{
		string assemblyName = script.GetAssemblyNameFixed();
		IReadOnlyList<PackageAssembly> replacements = packageReplacements[assemblyName];
		string @namespace = script.Namespace.String;
		string className = script.GetNonGenericClassName();
		foreach (PackageAssembly assembly in replacements)
		{
			if (assembly.TryGetScriptGuid(@namespace, className, out UnityGuid guid))
			{
				// Like decompiled scripts, a script file in a package has the main object of the MonoScript class.
				return new(MonoScriptDecompiledFileID, guid, AssetType.Meta);
			}
		}
		if (IsKeptScript(script))
		{
			return new(MonoScriptDecompiledFileID, ScriptHashing.CalculateScriptGuid(script), AssetType.Meta);
		}

		lock (missingPackageScripts)
		{
			if (missingPackageScripts.Add($"{assemblyName}:{@namespace}.{className}"))
			{
				// Builds contain a MonoScript for many types that are never used as components, so this is usually harmless.
				string packages = string.Join(", ", replacements.Select(a => $"{a.PackageName} {a.PackageVersion}").Distinct());
				Logger.Info(LogCategory.Export, $"Script {@namespace}.{className} was not found in {packages}. Any component using it will have a missing script.");
			}
		}
		return new(ScriptHashing.CalculateScriptFileID(script), ScriptHashing.CalculateAssemblyGuid(script), AssetType.Meta);
	}

	public AssemblyExportType GetExportType(string assemblyName)
	{
		if (ReferenceAssemblyDictionary.ContainsKey(assemblyName))
		{
			return AssemblyExportType.Skip;
		}
		else if (packageReplacements.ContainsKey(assemblyName))
		{
			return AssemblyExportType.Package;
		}
		else if (!AssemblyManager.IsSet)
		{
			return AssemblyExportType.Decompile;
		}
		else if (ExportMode is ScriptExportMode.Decompiled)
		{
			return AssemblyExportType.Decompile;
		}
		else if (ExportMode is ScriptExportMode.Hybrid)
		{
			return ReferenceAssemblies.IsPredefinedAssembly(assemblyName) || DecompiledAssemblies.Contains(assemblyName)
				? AssemblyExportType.Decompile
				: AssemblyExportType.Save;
		}
		else
		{
			return AssemblyExportType.Save;
		}
	}

	/// <summary>
	/// Gets the package assemblies that replace the game assembly <paramref name="assemblyName"/>.
	/// </summary>
	public IReadOnlyList<PackageAssembly> GetPackageReplacements(string assemblyName)
	{
		return packageReplacements.TryGetValue(assemblyName, out IReadOnlyList<PackageAssembly>? replacements) ? replacements : [];
	}

	/// <summary>
	/// Gets the assembly definitions that a decompiled assembly references in place of the game assembly <paramref name="assemblyName"/>.
	/// </summary>
	public IEnumerable<string> GetAssemblyDefinitionReferences(string assemblyName)
	{
		return GetExportType(assemblyName) switch
		{
			// Predefined assemblies like Assembly-CSharp can't be referenced by an assembly definition.
			AssemblyExportType.Decompile when !ReferenceAssemblies.IsPredefinedAssembly(assemblyName) => [assemblyName],
			AssemblyExportType.Package => packageReplacements[assemblyName].Select(a => a.AssemblyName).Distinct(),
			// Engine assemblies and plugins are referenced automatically.
			_ => [],
		};
	}

	AssetType IAssetExporter.ToExportType(IUnityObjectBase asset) => AssetType.Meta;

	bool IAssetExporter.ToUnknownExportType(Type type, out AssetType assetType)
	{
		assetType = AssetType.Meta;
		return true;
	}
}

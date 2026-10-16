using AssetRipper.Export.Configuration;
using AssetRipper.Import.Logging;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AssetRipper.Export.UnityProjects.Scripts.UnityPackages;

/// <summary>
/// An index of the Unity packages bundled with a Unity editor installation.
/// </summary>
/// <remarks>
/// Each assembly defined by a package is mapped to the package and to the guids of its script files,
/// so that game assemblies built from a package can be replaced with a reference to that package.
/// Shader files are indexed by the shader name they declare, so that game shaders compiled from a package
/// can be replaced in the same way. Files in Resources folders are indexed by resource name, so that game assets
/// that are copies of them can be replaced too.
/// </remarks>
public sealed partial class UnityPackageIndex
{
	private readonly Dictionary<string, PackageAssembly> assemblies;
	/// <summary>
	/// The package assembly declaring each type, by full name. Nested types are included as if they were top level.
	/// </summary>
	private readonly Dictionary<string, PackageAssembly> typeOwners = new();
	/// <summary>
	/// The package shader declaring each shader name, or null if several packages declare it.
	/// </summary>
	private readonly Dictionary<string, PackageShader?> shaders;
	/// <summary>
	/// The package file for each resource name (the path inside a Resources folder, without extension, in lower case),
	/// or null if several files have it.
	/// </summary>
	private readonly Dictionary<string, PackageResource?> resources;
	/// <summary>
	/// The packages containing each plugin file name, such as a native library, ignoring case.
	/// </summary>
	private readonly Dictionary<string, HashSet<string>> pluginPackages;
	/// <summary>
	/// Where each extra package was read from (an archive or a directory), by package name.
	/// </summary>
	private readonly Dictionary<string, string> extraPackageSources;

	/// <summary>
	/// The directory of the editor installation that the packages were read from.
	/// </summary>
	public string EditorDirectory { get; }

	public UnityVersion EditorVersion { get; }

	private UnityPackageIndex(string editorDirectory, UnityVersion editorVersion, Dictionary<string, PackageAssembly> assemblies, Dictionary<string, PackageShader?> shaders, Dictionary<string, PackageResource?> resources, Dictionary<string, HashSet<string>> pluginPackages, Dictionary<string, string> extraPackageSources)
	{
		EditorDirectory = editorDirectory;
		EditorVersion = editorVersion;
		this.assemblies = assemblies;
		this.shaders = shaders;
		this.resources = resources;
		this.pluginPackages = pluginPackages;
		this.extraPackageSources = extraPackageSources;
		foreach (PackageAssembly assembly in assemblies.Values)
		{
			foreach (string typeName in assembly.DeclaredTypes)
			{
				typeOwners.TryAdd(typeName, assembly);
			}
		}
	}

	/// <summary>
	/// Whether a package declares a type, by full name (generic arity, as in "List`1", is ignored).
	/// </summary>
	public bool DeclaresType(string fullName)
	{
		int arity = fullName.IndexOf('`');
		return typeOwners.ContainsKey(arity < 0 ? fullName : fullName[..arity]);
	}

	public bool TryGetAssembly(string assemblyName, [NotNullWhen(true)] out PackageAssembly? assembly)
	{
		return assemblies.TryGetValue(assemblyName, out assembly);
	}

	/// <summary>
	/// Get the package shader file that declares a shader name, if exactly one package declares it.
	/// </summary>
	public bool TryGetShader(string shaderName, [NotNullWhen(true)] out PackageShader? shader)
	{
		shader = shaders.GetValueOrDefault(shaderName);
		return shader is not null;
	}

	/// <summary>
	/// Get the package file that has a resource name, if exactly one package file has it.
	/// </summary>
	/// <param name="resourceName">The path inside a Resources folder, without extension.</param>
	public bool TryGetResource(string resourceName, [NotNullWhen(true)] out PackageResource? resource)
	{
		resource = resources.GetValueOrDefault(resourceName.ToLowerInvariant());
		return resource is not null;
	}

	/// <summary>
	/// Get the full path of the archive or directory an extra package was read from.
	/// </summary>
	/// <remarks>
	/// The editor's own packages have none, since Unity finds them by version.
	/// </remarks>
	public bool TryGetExtraPackageSource(string packageName, [NotNullWhen(true)] out string? path)
	{
		return extraPackageSources.TryGetValue(packageName, out path);
	}

	/// <summary>
	/// Whether one of <paramref name="packageNames"/> contains a plugin file named <paramref name="fileName"/>.
	/// </summary>
	public bool HasPluginFile(string fileName, IReadOnlySet<string> packageNames)
	{
		return pluginPackages.TryGetValue(fileName, out HashSet<string>? packages) && packages.Overlaps(packageNames);
	}

	/// <summary>
	/// Get the names of the packages whose assemblies replace any of these game assemblies.
	/// </summary>
	public HashSet<string> GetReferencedPackageNames(IEnumerable<AsmResolver.DotNet.AssemblyDefinition> gameAssemblies)
	{
		return gameAssemblies.SelectMany(GetReplacements).Select(a => a.PackageName).ToHashSet();
	}

	/// <summary>
	/// Get the package assemblies that replace a game assembly, or an empty list if it doesn't come from a package.
	/// </summary>
	/// <remarks>
	/// Usually this is the package assembly with the same name. However, packages sometimes move types into differently
	/// named assemblies in newer versions. A game assembly whose types are mostly declared by packages is replaced by the
	/// package assemblies that declare them, since keeping it would define those types twice (error CS0433).
	/// </remarks>
	public IReadOnlyList<PackageAssembly> GetReplacements(AsmResolver.DotNet.AssemblyDefinition assembly)
	{
		if (assembly.Name?.Value is not { } name || ReferenceAssemblies.IsPredefinedAssembly(name))
		{
			return [];
		}
		if (assemblies.TryGetValue(name, out PackageAssembly? sameName))
		{
			return [sameName];
		}

		string[] typeNames = assembly.ManifestModule?.TopLevelTypes
			.Where(t => t.Name?.Value is { } n && n != "<Module>" && !n.StartsWith('<') && !n.StartsWith("__", StringComparison.Ordinal))
			.Select(t => GetFullName(t.Namespace?.Value ?? "", t.Name!.Value))
			.ToArray() ?? [];
		if (typeNames.Length == 0)
		{
			return [];
		}
		PackageAssembly[] owners = typeNames.Select(t => typeOwners.GetValueOrDefault(t)).OfType<PackageAssembly>().ToArray();
		const double RequiredFraction = 0.5;
		return owners.Length >= typeNames.Length * RequiredFraction
			? owners.Distinct().ToArray()
			: [];
	}

	private static string GetFullName(string @namespace, string name) => @namespace.Length == 0 ? name : $"{@namespace}.{name}";

	private static readonly Dictionary<string, UnityPackageIndex> cache = new();

	/// <summary>
	/// Get the package index for a project, using the editor and the extra packages in the export settings.
	/// </summary>
	public static UnityPackageIndex? GetForProject(UnityVersion projectVersion, ExportSettings exportSettings)
	{
		return GetForProject(projectVersion, exportSettings.UnityEditorPath, exportSettings.ExtraPackages);
	}

	/// <summary>
	/// Get the package index of the editor to use for a project, or null if no editor was found.
	/// </summary>
	/// <remarks>
	/// Reading the packages takes a while, so the index is cached for each editor and set of extra packages.
	/// </remarks>
	/// <param name="extraPackages">Packages to index in addition to the editor's, see <see cref="ExportSettings.ExtraPackages"/>.</param>
	public static UnityPackageIndex? GetForProject(UnityVersion projectVersion, string? configuredEditorPath, IReadOnlyList<string>? extraPackages = null)
	{
		extraPackages ??= [];
		if (!UnityEditorLocator.TryFind(projectVersion, configuredEditorPath, out string? editorDirectory, out UnityVersion editorVersion))
		{
			return null;
		}
		lock (cache)
		{
			string cacheKey = string.Join('\n', extraPackages.Prepend(editorDirectory));
			if (!cache.TryGetValue(cacheKey, out UnityPackageIndex? index))
			{
				Logger.Info(LogCategory.Export, $"Reading the packages bundled with Unity {editorVersion} at '{editorDirectory}'...");
				index = Load(editorDirectory, editorVersion, extraPackages);
				Logger.Info(LogCategory.Export, $"Found {index.assemblies.Count} package assemblies");
				cache.Add(cacheKey, index);
			}
			return index;
		}
	}

	public static UnityPackageIndex Load(string editorDirectory, UnityVersion editorVersion, IReadOnlyList<string>? extraPackages = null)
	{
		string packageManagerDirectory = GetPackageManagerDirectory(editorDirectory)
			?? throw new DirectoryNotFoundException($"No package manager directory was found in '{editorDirectory}'.");

		Dictionary<string, PackageAssembly> assemblies = new();
		Dictionary<string, PackageShader?> shaders = new();
		Dictionary<string, PackageResource?> resources = new();
		Dictionary<string, HashSet<string>> pluginPackages = new(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, string> extraPackageSources = new();
		PackageTables tables = new(assemblies, shaders, resources, pluginPackages, extraPackageSources);
		string builtInDirectory = Path.Join(packageManagerDirectory, "BuiltInPackages");
		if (Directory.Exists(builtInDirectory))
		{
			foreach (string packageDirectory in Directory.EnumerateDirectories(builtInDirectory))
			{
				AddPackage(tables, ReadDirectory(packageDirectory));
			}
		}
		string archiveDirectory = Path.Join(packageManagerDirectory, "Editor");
		if (Directory.Exists(archiveDirectory))
		{
			foreach (string archivePath in Directory.EnumerateFiles(archiveDirectory, "*.tgz"))
			{
				try
				{
					AddPackage(tables, ReadArchive(archivePath));
				}
				catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
				{
					Logger.Warning(LogCategory.Export, $"Could not read package archive '{archivePath}': {ex.Message}");
				}
			}
		}
		foreach (string extraPackage in extraPackages ?? [])
		{
			AddExtraPackages(tables, extraPackage);
		}
		return new UnityPackageIndex(editorDirectory, editorVersion, assemblies, shaders, resources, pluginPackages, extraPackageSources);
	}

	/// <summary>
	/// Add packages that don't come with the editor: a package archive (.tgz, as published to npm registries),
	/// a package directory (containing package.json), or a directory of those.
	/// </summary>
	private static void AddExtraPackages(PackageTables tables, string path)
	{
		try
		{
			if (File.Exists(path))
			{
				AddPackage(tables, ReadArchive(path), Path.GetFullPath(path));
			}
			else if (File.Exists(Path.Join(path, "package.json")))
			{
				AddPackage(tables, ReadDirectory(path), Path.GetFullPath(path));
			}
			else if (Directory.Exists(path))
			{
				foreach (string archivePath in Directory.EnumerateFiles(path, "*.tgz").Order(StringComparer.Ordinal))
				{
					AddExtraPackages(tables, archivePath);
				}
				foreach (string packageDirectory in Directory.EnumerateDirectories(path).Order(StringComparer.Ordinal))
				{
					if (File.Exists(Path.Join(packageDirectory, "package.json")))
					{
						AddExtraPackages(tables, packageDirectory);
					}
				}
			}
			else
			{
				Logger.Warning(LogCategory.Export, $"Extra package '{path}' was not found");
			}
		}
		catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
		{
			Logger.Warning(LogCategory.Export, $"Could not read extra package '{path}': {ex.Message}");
		}
	}

	/// <summary>
	/// Find the directory containing the bundled packages, given the root directory of an editor installation.
	/// </summary>
	public static string? GetPackageManagerDirectory(string editorDirectory)
	{
		string[] candidates =
		[
			editorDirectory,
			Path.Join(editorDirectory, "Editor", "Data", "Resources", "PackageManager"), // Windows and Linux
			Path.Join(editorDirectory, "Data", "Resources", "PackageManager"),
			Path.Join(editorDirectory, "Unity.app", "Contents", "Resources", "PackageManager"), // macOS
		];
		return candidates.FirstOrDefault(c => Directory.Exists(Path.Join(c, "BuiltInPackages")) || Directory.Exists(Path.Join(c, "Editor")) && Path.GetFileName(c) == "PackageManager");
	}

	private sealed record class PackageTables(
		Dictionary<string, PackageAssembly> Assemblies,
		Dictionary<string, PackageShader?> Shaders,
		Dictionary<string, PackageResource?> Resources,
		Dictionary<string, HashSet<string>> PluginPackages,
		Dictionary<string, string> ExtraPackageSources);

	private static void AddPackage(PackageTables tables, PackageContents? contents, string? extraPackageSource = null)
	{
		if (contents is null)
		{
			return;
		}
		if (extraPackageSource is not null)
		{
			tables.ExtraPackageSources.TryAdd(contents.PackageName, extraPackageSource);
		}
		(Dictionary<string, PackageAssembly> assemblies, Dictionary<string, PackageShader?> shaders, Dictionary<string, PackageResource?> resources, Dictionary<string, HashSet<string>> pluginPackages, _) = tables;
		foreach (string fileName in contents.PluginFileNames)
		{
			if (!pluginPackages.TryGetValue(fileName, out HashSet<string>? packages))
			{
				pluginPackages.Add(fileName, packages = new());
			}
			packages.Add(contents.PackageName);
		}
		foreach (PackageAssembly assembly in contents.CreateAssemblies())
		{
			// Assembly names are unique within an editor, so a duplicate means a malformed package.
			assemblies.TryAdd(assembly.AssemblyName, assembly);
		}
		foreach (PackageShader shader in contents.CreateShaders())
		{
			// A name declared by more than one file is ambiguous, so it isn't used.
			shaders[shader.Name] = shaders.ContainsKey(shader.Name) ? null : shader;
		}
		foreach (PackageResource resource in contents.CreateResources())
		{
			// A resource name used by more than one file is ambiguous, so it isn't used.
			resources[resource.Name] = resources.ContainsKey(resource.Name) ? null : resource;
		}
	}

	private static PackageContents? ReadDirectory(string packageDirectory)
	{
		PackageContents contents = new();
		foreach (string path in Directory.EnumerateFiles(packageDirectory, "*", SearchOption.AllDirectories))
		{
			if (PackageContents.IsRelevant(path))
			{
				string relativePath = Path.GetRelativePath(packageDirectory, path).Replace('\\', '/');
				contents.Add(relativePath, File.ReadAllText(path));
			}
			else if (PackageContents.IsPluginFile(path))
			{
				contents.AddPluginFile(path);
			}
		}
		return contents.IsValid ? contents : null;
	}

	private static PackageContents? ReadArchive(string archivePath)
	{
		PackageContents contents = new();
		using FileStream fileStream = File.OpenRead(archivePath);
		using GZipStream gzipStream = new(fileStream, CompressionMode.Decompress);
		using TarReader reader = new(gzipStream);
		while (reader.GetNextEntry() is { } entry)
		{
			if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream is null)
			{
				continue;
			}
			if (PackageContents.IsRelevant(entry.Name))
			{
				// Archive entries are rooted in a "package" directory.
				string relativePath = entry.Name.StartsWith("package/", StringComparison.Ordinal) ? entry.Name["package/".Length..] : entry.Name;
				using StreamReader streamReader = new(entry.DataStream);
				contents.Add(relativePath, streamReader.ReadToEnd());
			}
			else if (PackageContents.IsPluginFile(entry.Name))
			{
				contents.AddPluginFile(entry.Name);
			}
		}
		return contents.IsValid ? contents : null;
	}

	/// <summary>
	/// The files of a package that are needed to map its scripts.
	/// </summary>
	private sealed class PackageContents
	{
		private string? packageName;
		private string? packageVersion;
		private readonly Dictionary<string, string> assemblyDefinitions = new(); // directory => assembly name
		private readonly Dictionary<string, string> assemblyReferences = new(); // directory => referenced assembly name or guid
		private readonly Dictionary<string, string> assemblyDefinitionGuids = new(); // guid => assembly name
		private readonly Dictionary<string, string> scripts = new(); // path => source
		private readonly Dictionary<string, UnityGuid> metaGuids = new(); // asset path => guid
		private readonly Dictionary<string, string> assemblyDefinitionPaths = new(); // path => assembly name
		private readonly Dictionary<string, string> shaderNames = new(); // path => shader name
		private readonly HashSet<string> pluginFileNames = new(StringComparer.OrdinalIgnoreCase);

		public string PackageName => packageName!;

		public IReadOnlyCollection<string> PluginFileNames => pluginFileNames;

		public bool IsValid => packageName is not null && packageVersion is not null && (assemblyDefinitions.Count > 0 || shaderNames.Count > 0);

		public static bool IsRelevant(string path)
		{
			// Unity ignores folders ending with a tilde, such as "Samples~" and "Tests~".
			if (path.Replace('\\', '/').Split('/').Any(segment => segment.EndsWith('~')))
			{
				return false;
			}
			return path.EndsWith(".cs", StringComparison.Ordinal)
				|| path.EndsWith(".cs.meta", StringComparison.Ordinal)
				|| path.EndsWith(".asmdef", StringComparison.Ordinal)
				|| path.EndsWith(".asmdef.meta", StringComparison.Ordinal)
				|| path.EndsWith(".asmref", StringComparison.Ordinal)
				|| path.EndsWith(".shader", StringComparison.Ordinal)
				|| path.EndsWith(".shader.meta", StringComparison.Ordinal)
				|| path.EndsWith("package.json", StringComparison.Ordinal)
				|| path.EndsWith(".meta", StringComparison.Ordinal) && GetResourceName(path[..^".meta".Length]) is not null;
		}

		/// <summary>
		/// Whether the file is a library a package can ship as a plugin, such as a native library.
		/// </summary>
		public static bool IsPluginFile(string path)
		{
			return !path.Replace('\\', '/').Split('/').Any(segment => segment.EndsWith('~'))
				&& Path.GetExtension(path).ToLowerInvariant() is ".dll" or ".so" or ".dylib" or ".bundle";
		}

		public void AddPluginFile(string path)
		{
			pluginFileNames.Add(Path.GetFileName(path.Replace('\\', '/')));
		}

		public void Add(string relativePath, string text)
		{
			string directory = GetDirectory(relativePath);
			if (relativePath == "package.json")
			{
				using JsonDocument document = JsonDocument.Parse(text, JsonOptions);
				packageName = document.RootElement.GetProperty("name").GetString();
				packageVersion = document.RootElement.GetProperty("version").GetString();
			}
			else if (relativePath.EndsWith(".asmdef", StringComparison.Ordinal))
			{
				using JsonDocument document = JsonDocument.Parse(text, JsonOptions);
				string? name = document.RootElement.GetProperty("name").GetString();
				if (!string.IsNullOrEmpty(name))
				{
					assemblyDefinitions[directory] = name;
					assemblyDefinitionPaths[relativePath] = name;
				}
			}
			else if (relativePath.EndsWith(".asmref", StringComparison.Ordinal))
			{
				using JsonDocument document = JsonDocument.Parse(text, JsonOptions);
				string? reference = document.RootElement.GetProperty("reference").GetString();
				if (!string.IsNullOrEmpty(reference))
				{
					assemblyReferences[directory] = reference;
				}
			}
			else if (relativePath.EndsWith(".meta", StringComparison.Ordinal))
			{
				if (TryParseMetaGuid(text, out UnityGuid guid))
				{
					metaGuids[relativePath[..^".meta".Length]] = guid;
				}
			}
			else if (relativePath.EndsWith(".shader", StringComparison.Ordinal))
			{
				Match match = ShaderNameRegex().Match(text);
				if (match.Success)
				{
					shaderNames[relativePath] = match.Groups[1].Value;
				}
			}
			else
			{
				scripts[relativePath] = text;
			}
		}

		public IEnumerable<PackageResource> CreateResources()
		{
			foreach ((string path, UnityGuid guid) in metaGuids)
			{
				if (GetResourceName(path) is { } name)
				{
					yield return new PackageResource(name, guid, packageName!, packageVersion!);
				}
			}
		}

		/// <summary>
		/// The name Resources.Load finds a file by: its path inside the innermost Resources folder, without extension,
		/// in lower case. Null if the file isn't in a Resources folder, is a script, or has no extension (as folders don't).
		/// </summary>
		public static string? GetResourceName(string path)
		{
			path = path.Replace('\\', '/');
			int folder = path.LastIndexOf("Resources/", StringComparison.Ordinal);
			if (folder < 0 || folder > 0 && path[folder - 1] != '/' || path.EndsWith(".cs", StringComparison.Ordinal))
			{
				return null;
			}
			string relative = path[(folder + "Resources/".Length)..];
			int dot = relative.LastIndexOf('.');
			return dot > relative.LastIndexOf('/') ? relative[..dot].ToLowerInvariant() : null;
		}

		public IEnumerable<PackageShader> CreateShaders()
		{
			foreach ((string path, string name) in shaderNames)
			{
				if (metaGuids.TryGetValue(path, out UnityGuid guid))
				{
					yield return new PackageShader(name, guid, packageName!, packageVersion!);
				}
			}
		}

		public IEnumerable<PackageAssembly> CreateAssemblies()
		{
			foreach ((string path, string name) in assemblyDefinitionPaths)
			{
				if (metaGuids.TryGetValue(path, out UnityGuid guid))
				{
					assemblyDefinitionGuids[guid.ToString()] = name;
				}
			}

			Dictionary<string, PackageAssembly> result = new();
			foreach (string name in assemblyDefinitions.Values)
			{
				result.TryAdd(name, new PackageAssembly(name, packageName!, packageVersion!));
			}

			foreach ((string path, string source) in scripts)
			{
				if (GetOwningAssembly(GetDirectory(path)) is not { } assemblyName
					|| !result.TryGetValue(assemblyName, out PackageAssembly? assembly))
				{
					continue;
				}

				foreach (string typeName in FindDeclaredTypes(source))
				{
					assembly.AddDeclaredType(typeName);
				}

				if (!metaGuids.TryGetValue(path, out UnityGuid guid))
				{
					continue;
				}

				// Unity associates a script file with the type that has the same name as the file.
				string className = Path.GetFileNameWithoutExtension(path);
				if (TryFindNamespace(source, className, out string? @namespace))
				{
					assembly.AddScript(@namespace, className, guid);
				}
			}

			return result.Values;
		}

		private string? GetOwningAssembly(string directory)
		{
			while (true)
			{
				if (assemblyDefinitions.TryGetValue(directory, out string? name))
				{
					return name;
				}
				if (assemblyReferences.TryGetValue(directory, out string? reference))
				{
					const string GuidPrefix = "GUID:";
					return reference.StartsWith(GuidPrefix, StringComparison.Ordinal)
						? assemblyDefinitionGuids.GetValueOrDefault(reference[GuidPrefix.Length..])
						: reference;
				}
				if (directory.Length == 0)
				{
					return null;
				}
				directory = GetDirectory(directory);
			}
		}

		private static string GetDirectory(string path)
		{
			int index = path.LastIndexOf('/');
			return index < 0 ? "" : path[..index];
		}
	}

	private static readonly JsonDocumentOptions JsonOptions = new()
	{
		AllowTrailingCommas = true,
		CommentHandling = JsonCommentHandling.Skip,
	};

	private static bool TryParseMetaGuid(string text, out UnityGuid guid)
	{
		Match match = MetaGuidRegex().Match(text);
		if (match.Success)
		{
			guid = UnityGuid.Parse(match.Groups[1].Value);
			return true;
		}
		guid = default;
		return false;
	}

	/// <summary>
	/// Find the namespace of the type declared with <paramref name="className"/>.
	/// </summary>
	/// <remarks>
	/// This is a lightweight scan rather than a full parse; see <see cref="GetNamespaceScopes"/>.
	/// </remarks>
	private static bool TryFindNamespace(string source, string className, [NotNullWhen(true)] out string? @namespace)
	{
		string code = BlankCommentsAndStrings(source);
		Match typeMatch = Regex.Match(code, $@"\b(?:class|struct|interface|enum|record)\s+{Regex.Escape(className)}\b");
		if (!typeMatch.Success)
		{
			@namespace = null;
			return false;
		}
		@namespace = GetNamespaceAt(GetNamespaceScopes(code), typeMatch.Index);
		return true;
	}

	/// <summary>
	/// Find the full names of the types declared in a source file.
	/// </summary>
	/// <remarks>
	/// Like <see cref="TryFindNamespace"/>, this is a lightweight scan. Nested types are treated as top level.
	/// </remarks>
	private static IEnumerable<string> FindDeclaredTypes(string source)
	{
		string code = BlankCommentsAndStrings(source);
		List<(int Start, int End, string Name)> scopes = GetNamespaceScopes(code);
		foreach (Match type in TypeDeclarationRegex().Matches(code))
		{
			yield return GetFullName(GetNamespaceAt(scopes, type.Index), type.Groups[1].Value);
		}
	}

	/// <summary>
	/// The namespace declarations of a file and the text they cover: to its end for a file-scoped one, otherwise to
	/// the brace closing its block. Namespaces can be nested, as in "namespace A { namespace B { } class C { } }".
	/// </summary>
	/// <param name="code">The source, with comments and strings blanked.</param>
	private static List<(int Start, int End, string Name)> GetNamespaceScopes(string code)
	{
		List<(int Start, int End, string Name)> scopes = new();
		foreach (Match match in NamespaceRegex().Matches(code))
		{
			int next = match.Index + match.Length;
			while (next < code.Length && char.IsWhiteSpace(code[next]))
			{
				next++;
			}
			if (next >= code.Length || code[next] == ';')
			{
				scopes.Add((match.Index, code.Length, match.Groups[1].Value));
				continue;
			}
			if (code[next] != '{')
			{
				continue;
			}
			int depth = 0;
			int end = code.Length;
			for (int i = next; i < code.Length; i++)
			{
				if (code[i] == '{')
				{
					depth++;
				}
				else if (code[i] == '}' && --depth == 0)
				{
					end = i;
					break;
				}
			}
			scopes.Add((match.Index, end, match.Groups[1].Value));
		}
		return scopes;
	}

	private static string GetNamespaceAt(List<(int Start, int End, string Name)> scopes, int index)
	{
		return string.Join('.', scopes.Where(s => s.Start < index && index < s.End).OrderBy(s => s.Start).Select(s => s.Name));
	}

	/// <summary>
	/// Replace comments and string and character literals with spaces, keeping every other character where it is,
	/// so that braces and keywords inside them aren't mistaken for code.
	/// </summary>
	internal static string BlankCommentsAndStrings(string source)
	{
		char[] code = source.ToCharArray();
		int i = 0;
		while (i < code.Length)
		{
			int start = i;
			if (code[i] == '/' && i + 1 < code.Length && code[i + 1] == '/')
			{
				while (i < code.Length && code[i] != '\n')
				{
					i++;
				}
			}
			else if (code[i] == '/' && i + 1 < code.Length && code[i + 1] == '*')
			{
				int close = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
				i = close < 0 ? code.Length : close + 2;
			}
			else if (code[i] is '"' or '\'')
			{
				bool verbatim = code[i] == '"' && (i > 0 && code[i - 1] == '@' || i > 1 && code[i - 1] == '$' && code[i - 2] == '@');
				char quote = code[i];
				i++;
				while (i < code.Length)
				{
					if (verbatim && code[i] == '"')
					{
						if (i + 1 < code.Length && code[i + 1] == '"')
						{
							i += 2;
							continue;
						}
						i++;
						break;
					}
					if (!verbatim && code[i] == '\\')
					{
						i += 2;
						continue;
					}
					if (!verbatim && (code[i] == quote || code[i] == '\n'))
					{
						i++;
						break;
					}
					i++;
				}
			}
			else
			{
				i++;
				continue;
			}
			for (int j = start; j < Math.Min(i, code.Length); j++)
			{
				if (code[j] != '\n')
				{
					code[j] = ' ';
				}
			}
		}
		return new string(code);
	}

	[GeneratedRegex(@"\b(?:class|struct|interface|enum|record)\s+(\w+)")]
	private static partial Regex TypeDeclarationRegex();

	[GeneratedRegex(@"^guid:\s*([0-9a-fA-F]{32})", RegexOptions.Multiline)]
	private static partial Regex MetaGuidRegex();

	/// <summary>
	/// The name declared at the start of a shader file. Lines of comments before it don't match, because they start with a slash.
	/// </summary>
	[GeneratedRegex(@"^\s*Shader\s+""([^""]+)""", RegexOptions.Multiline)]
	private static partial Regex ShaderNameRegex();

	[GeneratedRegex(@"^\s*namespace\s+([\w.]+)", RegexOptions.Multiline)]
	private static partial Regex NamespaceRegex();
}

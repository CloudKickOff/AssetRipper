using AssetRipper.Export.UnityProjects.Scripts.UnityPackages;
using AssetRipper.Primitives;

namespace AssetRipper.Tests;

internal class UnityPackageIndexTests
{
	private const string RuntimeScriptGuid = "0123456789abcdef0123456789abcdef";
	private const string ReferencedScriptGuid = "fedcba9876543210fedcba9876543210";
	private const string SampleScriptGuid = "00112233445566778899aabbccddeeff";
	private const string AssemblyDefinitionGuid = "aaaabbbbccccddddeeeeffff00001111";
	private const string ShaderGuid = "11112222333344445555666677778888";
	private const string ResourceGuid = "99990000aaaabbbbccccddddeeeeffff";
	private const string ExtraScriptGuid = "abcdefabcdefabcdefabcdefabcdefab";

	private string editorDirectory = "";

	[SetUp]
	public void CreateFakeEditor()
	{
		editorDirectory = Path.Join(Path.GetTempPath(), $"AssetRipperPackageIndex_{Guid.NewGuid():N}");
		string package = Path.Join(editorDirectory, "Editor", "Data", "Resources", "PackageManager", "BuiltInPackages", "com.example.package");

		Write(package, "package.json", """{ "name": "com.example.package", "version": "1.2.3" }""");
		Write(package, "Runtime/Example.Runtime.asmdef", """{ "name": "Example.Runtime", }""");
		Write(package, "Runtime/Example.Runtime.asmdef.meta", $"fileFormatVersion: 2\nguid: {AssemblyDefinitionGuid}\n");
		Write(package, "Runtime/Components/ExampleBehaviour.cs", "using UnityEngine;\nnamespace Example.Components\n{\n\tpublic class ExampleBehaviour : MonoBehaviour { }\n}\n");
		Write(package, "Runtime/Components/ExampleBehaviour.cs.meta", $"fileFormatVersion: 2\nguid: {RuntimeScriptGuid}\n");

		// A folder that belongs to the runtime assembly through an assembly definition reference
		Write(package, "Extra/Example.Extra.asmref", $$"""{ "reference": "GUID:{{AssemblyDefinitionGuid}}" }""");
		Write(package, "Extra/GlobalBehaviour.cs", "public class GlobalBehaviour : UnityEngine.MonoBehaviour { }\n");
		Write(package, "Extra/GlobalBehaviour.cs.meta", $"fileFormatVersion: 2\nguid: {ReferencedScriptGuid}\n");

		// Unity ignores folders ending with a tilde
		Write(package, "Samples~/Example.Samples.asmdef", """{ "name": "Example.Samples" }""");
		Write(package, "Samples~/SampleBehaviour.cs", "public class SampleBehaviour : UnityEngine.MonoBehaviour { }\n");
		Write(package, "Samples~/SampleBehaviour.cs.meta", $"fileFormatVersion: 2\nguid: {SampleScriptGuid}\n");

		Write(package, "Shaders/Lit.shader", "// Shader \"Commented/Out\"\nShader \"Example/Lit\"\n{\n\tSubShader { Pass { } }\n}\n");
		Write(package, "Shaders/Lit.shader.meta", $"fileFormatVersion: 2\nguid: {ShaderGuid}\n");
		// Two files declaring the same name
		Write(package, "Shaders/Copy1.shader", "Shader \"Example/Duplicate\" { }\n");
		Write(package, "Shaders/Copy1.shader.meta", $"fileFormatVersion: 2\nguid: {SampleScriptGuid}\n");
		Write(package, "Shaders/Copy2.shader", "Shader \"Example/Duplicate\" { }\n");
		Write(package, "Shaders/Copy2.shader.meta", $"fileFormatVersion: 2\nguid: {ReferencedScriptGuid}\n");

		Write(package, "Runtime/Resources/Styles/Panel.asset", "");
		Write(package, "Runtime/Resources/Styles/Panel.asset.meta", $"fileFormatVersion: 2\nguid: {ResourceGuid}\n");
		Write(package, "Runtime/Resources/Styles.meta", "fileFormatVersion: 2\nguid: 00000000000000000000000000000001\n");
		Write(package, "Runtime/MyResources/NotAResource.asset.meta", "fileFormatVersion: 2\nguid: 00000000000000000000000000000002\n");
		Write(package, "Runtime/Plugins/x86_64/ExampleNative.dll", "");
		Write(package, "Samples~/Plugins/SampleNative.dll", "");

		static void Write(string root, string relativePath, string text)
		{
			string path = Path.Join(root, relativePath);
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, text);
		}
	}

	[TearDown]
	public void DeleteFakeEditor()
	{
		Directory.Delete(editorDirectory, true);
	}

	[Test]
	public void ScriptsAreMappedToTheirPackageAssembly()
	{
		UnityPackageIndex index = UnityPackageIndex.Load(editorDirectory, new UnityVersion(6000, 3));
		Assert.That(index.TryGetAssembly("Example.Runtime", out PackageAssembly? assembly), Is.True);
		using (Assert.EnterMultipleScope())
		{
			Assert.That(assembly!.PackageName, Is.EqualTo("com.example.package"));
			Assert.That(assembly.PackageVersion, Is.EqualTo("1.2.3"));
			Assert.That(assembly.TryGetScriptGuid("Example.Components", "ExampleBehaviour", out UnityGuid guid), Is.True);
			Assert.That(guid, Is.EqualTo(UnityGuid.Parse(RuntimeScriptGuid)));
		}
	}

	[Test]
	public void AssemblyDefinitionReferencesAddScriptsToTheReferencedAssembly()
	{
		UnityPackageIndex index = UnityPackageIndex.Load(editorDirectory, new UnityVersion(6000, 3));
		Assert.That(index.TryGetAssembly("Example.Runtime", out PackageAssembly? assembly), Is.True);
		Assert.That(assembly!.TryGetScriptGuid("", "GlobalBehaviour", out UnityGuid guid), Is.True);
		Assert.That(guid, Is.EqualTo(UnityGuid.Parse(ReferencedScriptGuid)));
	}

	[Test]
	public void ScriptsInAMovedNamespaceAreFoundByClassName()
	{
		UnityPackageIndex index = UnityPackageIndex.Load(editorDirectory, new UnityVersion(6000, 3));
		Assert.That(index.TryGetAssembly("Example.Runtime", out PackageAssembly? assembly), Is.True);
		Assert.That(assembly!.TryGetScriptGuid("Example.OldNamespace", "ExampleBehaviour", out UnityGuid guid), Is.True);
		Assert.That(guid, Is.EqualTo(UnityGuid.Parse(RuntimeScriptGuid)));
	}

	[Test]
	public void FoldersEndingWithATildeAreIgnored()
	{
		UnityPackageIndex index = UnityPackageIndex.Load(editorDirectory, new UnityVersion(6000, 3));
		Assert.That(index.TryGetAssembly("Example.Samples", out _), Is.False);
	}

	[Test]
	public void ShadersAreMappedByTheNameTheyDeclare()
	{
		UnityPackageIndex index = UnityPackageIndex.Load(editorDirectory, new UnityVersion(6000, 3));
		Assert.That(index.TryGetShader("Example/Lit", out PackageShader? shader), Is.True);
		using (Assert.EnterMultipleScope())
		{
			Assert.That(shader!.Guid, Is.EqualTo(UnityGuid.Parse(ShaderGuid)));
			Assert.That(shader.PackageName, Is.EqualTo("com.example.package"));
			Assert.That(index.TryGetShader("Commented/Out", out _), Is.False);
		}
	}

	[Test]
	public void PluginFilesAreFoundOnlyInTheirPackage()
	{
		UnityPackageIndex index = UnityPackageIndex.Load(editorDirectory, new UnityVersion(6000, 3));
		using (Assert.EnterMultipleScope())
		{
			Assert.That(index.HasPluginFile("examplenative.dll", new HashSet<string> { "com.example.package" }), Is.True);
			Assert.That(index.HasPluginFile("ExampleNative.dll", new HashSet<string> { "com.other.package" }), Is.False);
			Assert.That(index.HasPluginFile("SampleNative.dll", new HashSet<string> { "com.example.package" }), Is.False);
		}
	}

	[Test]
	public void ResourcesAreMappedByTheirNameInsideTheResourcesFolder()
	{
		UnityPackageIndex index = UnityPackageIndex.Load(editorDirectory, new UnityVersion(6000, 3));
		using (Assert.EnterMultipleScope())
		{
			Assert.That(index.TryGetResource("Styles/Panel", out PackageResource? resource), Is.True);
			Assert.That(resource!.Guid, Is.EqualTo(UnityGuid.Parse(ResourceGuid)));
			Assert.That(resource.PackageName, Is.EqualTo("com.example.package"));
			Assert.That(index.TryGetResource("Styles", out _), Is.False, "folders aren't resources");
			Assert.That(index.TryGetResource("NotAResource", out _), Is.False, "MyResources isn't a Resources folder");
		}
	}

	[Test]
	public void ExtraPackageArchivesAreIndexed()
	{
		string archive = Path.Join(editorDirectory, "com.extra.package-2.0.0.tgz");
		using (FileStream file = File.Create(archive))
		using (System.IO.Compression.GZipStream gzip = new(file, System.IO.Compression.CompressionLevel.Fastest))
		using (System.Formats.Tar.TarWriter tar = new(gzip))
		{
			AddEntry(tar, "package/package.json", """{ "name": "com.extra.package", "version": "2.0.0" }""");
			AddEntry(tar, "package/Runtime/Extra.asmdef", """{ "name": "Extra.Runtime" }""");
			AddEntry(tar, "package/Runtime/ExtraBehaviour.cs", "namespace Extra { public class ExtraBehaviour : UnityEngine.MonoBehaviour { } }\n");
			AddEntry(tar, "package/Runtime/ExtraBehaviour.cs.meta", $"fileFormatVersion: 2\nguid: {ExtraScriptGuid}\n");
		}

		UnityPackageIndex index = UnityPackageIndex.Load(editorDirectory, new UnityVersion(6000, 3), [archive]);
		Assert.That(index.TryGetAssembly("Extra.Runtime", out PackageAssembly? assembly), Is.True);
		using (Assert.EnterMultipleScope())
		{
			Assert.That(assembly!.PackageName, Is.EqualTo("com.extra.package"));
			Assert.That(assembly.PackageVersion, Is.EqualTo("2.0.0"));
			Assert.That(assembly.TryGetScriptGuid("Extra", "ExtraBehaviour", out UnityGuid guid), Is.True);
			Assert.That(guid, Is.EqualTo(UnityGuid.Parse(ExtraScriptGuid)));
			Assert.That(index.TryGetAssembly("Example.Runtime", out _), Is.True, "the editor's packages are still indexed");
			Assert.That(index.TryGetExtraPackageSource("com.extra.package", out string? source), Is.True);
			Assert.That(source, Is.EqualTo(Path.GetFullPath(archive)));
			Assert.That(index.TryGetExtraPackageSource("com.example.package", out _), Is.False, "the editor's packages have no source");
		}

		static void AddEntry(System.Formats.Tar.TarWriter tar, string name, string text)
		{
			System.Formats.Tar.PaxTarEntry entry = new(System.Formats.Tar.TarEntryType.RegularFile, name)
			{
				DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)),
			};
			tar.WriteEntry(entry);
		}
	}

	[Test]
	public void ShaderNamesDeclaredTwiceAreNotMapped()
	{
		UnityPackageIndex index = UnityPackageIndex.Load(editorDirectory, new UnityVersion(6000, 3));
		Assert.That(index.TryGetShader("Example/Duplicate", out _), Is.False);
	}
}

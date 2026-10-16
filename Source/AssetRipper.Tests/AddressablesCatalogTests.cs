using AssetRipper.Export.UnityProjects.Project;
using System.Buffers.Binary;
using System.Text;

namespace AssetRipper.Tests;

internal class AddressablesCatalogTests
{
	private const uint Null = uint.MaxValue;
	private const uint DynamicFlag = 0x40000000;
	private const string Guid = "0123456789abcdef0123456789abcdef";

	[Test]
	public void GuidKeysMapToTheInternalIdOfTheirLocation()
	{
		Dictionary<string, string> paths = AddressablesCatalog.ReadAssetPaths(CreateCatalog());

		Assert.That(paths, Has.Count.EqualTo(1));
		Assert.That(paths[Guid], Is.EqualTo("Assets/Cosmetics/Hat.prefab"));
	}

	[Test]
	public void OtherVersionsAreRejected()
	{
		byte[] catalog = CreateCatalog();
		BinaryPrimitives.WriteInt32LittleEndian(catalog.AsSpan(4), 1);

		Assert.Throws<InvalidDataException>(() => AddressablesCatalog.ReadAssetPaths(catalog));
	}

	/// <summary>
	/// Compares with the catalog of a real game when one is available, set by the ADDRESSABLES_CATALOG environment variable.
	/// </summary>
	[Test]
	public void RealCatalog()
	{
		string? path = Environment.GetEnvironmentVariable("ADDRESSABLES_CATALOG");
		if (string.IsNullOrEmpty(path) || !File.Exists(path))
		{
			Assert.Ignore("ADDRESSABLES_CATALOG is not set");
		}
		Dictionary<string, string> paths = AddressablesCatalog.ReadAssetPaths(File.ReadAllBytes(path));
		Assert.That(paths, Is.Not.Empty);
		Assert.That(paths.Keys, Is.All.Length.EqualTo(32));
		Assert.That(paths.Values, Is.All.StartsWith("Assets/").Or.StartsWith("Packages/"));
		TestContext.Out.WriteLine($"{paths.Count} guids");
		string? expectedPath = Environment.GetEnvironmentVariable("ADDRESSABLES_CATALOG_EXPECTED");
		if (!string.IsNullOrEmpty(expectedPath) && File.Exists(expectedPath))
		{
			Dictionary<string, string> expected = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(expectedPath))!;
			Assert.That(paths, Is.EquivalentTo(expected));
		}
	}

	/// <summary>
	/// A catalog with a guid key and an address key for one location, whose internal id is a dynamic string.
	/// </summary>
	private static byte[] CreateCatalog()
	{
		Builder b = new();
		b.Raw(new byte[32]); // header, written last

		uint assembly = b.String("mscorlib");
		uint className = b.String("System.String");
		uint stringType = b.Raw(assembly, className);

		uint guidKey = b.Raw(stringType, b.Raw(b.String(Guid), 0u));
		uint addressKey = b.Raw(stringType, b.Raw(b.String("Hat"), 0u));

		// "Assets/Cosmetics/Hat.prefab" as records stored last part first
		uint first = b.Raw(b.String("Assets"), Null);
		uint second = b.Raw(b.String("Cosmetics"), first | DynamicFlag);
		uint third = b.Raw(b.String("Hat.prefab"), second | DynamicFlag);
		uint location = b.Raw(b.String("Hat"), third | DynamicFlag, Null, Null, 0u, Null, Null);

		uint locationSet = b.Array(location);
		uint keys = b.Array(guidKey, locationSet, addressKey, locationSet);

		byte[] data = b.ToArray();
		BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 0x0de38942);
		BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 2);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), keys);
		for (int i = 12; i < 32; i += 4)
		{
			BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i), Null);
		}
		return data;
	}

	private sealed class Builder
	{
		private readonly MemoryStream stream = new();

		public uint Raw(byte[] bytes)
		{
			uint offset = (uint)stream.Position;
			stream.Write(bytes);
			return offset;
		}

		public uint Raw(params uint[] values) => Raw(Encode(values));

		/// <summary>A length-prefixed block. The returned offset points at the data after the length.</summary>
		public uint Block(byte[] bytes)
		{
			Raw(Encode([(uint)bytes.Length]));
			return Raw(bytes);
		}

		public uint String(string value) => Block(Encoding.ASCII.GetBytes(value));

		public uint Array(params uint[] values) => Block(Encode(values));

		public byte[] ToArray() => stream.ToArray();

		private static byte[] Encode(uint[] values)
		{
			byte[] bytes = new byte[values.Length * 4];
			for (int i = 0; i < values.Length; i++)
			{
				BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), values[i]);
			}
			return bytes;
		}
	}
}

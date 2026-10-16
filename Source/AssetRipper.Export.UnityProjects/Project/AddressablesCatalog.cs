using System.Buffers.Binary;
using System.Text;

namespace AssetRipper.Export.UnityProjects.Project;

/// <summary>
/// Reads the asset guids from an Addressables binary content catalog (catalog.bin, format version 2).
/// </summary>
/// <remarks>
/// Game code finds Addressables content by the original asset guids, which are stored in the catalog as keys,
/// and the location each key points to has the asset's original path as its internal id.
/// The format follows the reader in the Addressables package (BinaryStorageBuffer, ContentCatalogData):
/// the file is one buffer, every reference is a byte offset into it (0xFFFFFFFF for null),
/// and arrays and strings are stored as a uint32 byte length followed by the data, with the offset pointing at the data.
/// </remarks>
public static class AddressablesCatalog
{
	private const uint Null = uint.MaxValue;
	private const uint UnicodeFlag = 0x80000000;
	private const uint DynamicFlag = 0x40000000;
	private const uint ClearMask = 0x3FFFFFFF;
	private const int SupportedVersion = 2;

	/// <summary>
	/// Get the original asset path for each asset guid in the catalog.
	/// </summary>
	/// <param name="data">The contents of catalog.bin.</param>
	/// <returns>The asset path (such as Assets/Folder/Asset.prefab) by guid, as lower-case hex.</returns>
	/// <exception cref="InvalidDataException">The data isn't a supported binary catalog.</exception>
	public static Dictionary<string, string> ReadAssetPaths(ReadOnlySpan<byte> data)
	{
		if (data.Length < 32)
		{
			throw new InvalidDataException("The catalog is too short");
		}
		int version = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
		if (version != SupportedVersion)
		{
			throw new InvalidDataException($"Unsupported catalog version {version}");
		}
		uint keysOffset = ReadUInt32(data, 8);

		Dictionary<string, string> paths = new();
		int keyCount = (int)(ReadUInt32(data, keysOffset - 4) / 8);
		for (int i = 0; i < keyCount; i++)
		{
			uint keyEntry = keysOffset + (uint)i * 8;
			string? key = ReadStringObject(data, ReadUInt32(data, keyEntry));
			if (key is null || !IsGuid(key))
			{
				continue;
			}
			uint locationSet = ReadUInt32(data, keyEntry + 4);
			if (locationSet == Null)
			{
				continue;
			}
			int locationCount = (int)(ReadUInt32(data, locationSet - 4) / 4);
			for (int j = 0; j < locationCount; j++)
			{
				uint location = ReadUInt32(data, locationSet + (uint)j * 4);
				// ResourceLocation.Data { primaryKey, internalId, provider, dependencySet, depHash, extraData, typeId }
				string? internalId = ReadString(data, ReadUInt32(data, location + 4), '/');
				if (internalId is not null && IsProjectPath(internalId))
				{
					paths.TryAdd(key.ToLowerInvariant(), internalId);
					break;
				}
			}
		}
		return paths;
	}

	private static bool IsProjectPath(string path)
	{
		return path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
			|| path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsGuid(string key)
	{
		return key.Length == 32 && key.All(Uri.IsHexDigit);
	}

	/// <summary>
	/// Read an object written with its type, which for keys is a string: { typeId, objectId }, where the type is
	/// { assemblyName, className } and a string object is { stringId, char16 separator }.
	/// </summary>
	private static string? ReadStringObject(ReadOnlySpan<byte> data, uint offset)
	{
		if (offset == Null)
		{
			return null;
		}
		uint typeId = ReadUInt32(data, offset);
		uint objectId = ReadUInt32(data, offset + 4);
		if (typeId == Null || objectId == Null || ReadString(data, ReadUInt32(data, typeId + 4), '.') != "System.String")
		{
			return null;
		}
		uint stringId = ReadUInt32(data, objectId);
		char separator = (char)BinaryPrimitives.ReadUInt16LittleEndian(data[(int)(objectId + 4)..]);
		return ReadString(data, stringId, separator);
	}

	/// <summary>
	/// Read a string. A dynamic string is a linked list of { stringId, nextId } records, stored last part first,
	/// whose parts are joined with a separator that the reader supplies.
	/// </summary>
	private static string? ReadString(ReadOnlySpan<byte> data, uint id, char separator)
	{
		if (id == Null)
		{
			return null;
		}
		if ((id & DynamicFlag) == 0 || separator == '\0')
		{
			return ReadPlainString(data, id);
		}
		List<string?> parts = new();
		for (uint next = id; next != Null;)
		{
			uint record = next & ClearMask;
			parts.Add(ReadPlainString(data, ReadUInt32(data, record)));
			next = ReadUInt32(data, record + 4);
		}
		parts.Reverse();
		return string.Join(separator, parts);
	}

	private static string? ReadPlainString(ReadOnlySpan<byte> data, uint id)
	{
		if (id == Null)
		{
			return null;
		}
		uint offset = id & ClearMask;
		int length = (int)ReadUInt32(data, offset - 4);
		ReadOnlySpan<byte> bytes = data.Slice((int)offset, length);
		return (id & UnicodeFlag) != 0 ? Encoding.Unicode.GetString(bytes) : Encoding.ASCII.GetString(bytes);
	}

	private static uint ReadUInt32(ReadOnlySpan<byte> data, uint offset)
	{
		return BinaryPrimitives.ReadUInt32LittleEndian(data[(int)offset..]);
	}
}

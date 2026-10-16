using System.Text.Json.Serialization;

namespace AssetRipper.Export.Configuration;

/// <summary>
/// A package registry entry in a project's package manifest, such as the one for a platform SDK.
/// </summary>
/// <param name="Name">The name shown in the Package Manager.</param>
/// <param name="Url">The registry's address.</param>
/// <param name="Scopes">The package name prefixes that are downloaded from this registry, such as "com.meta.xr".</param>
public sealed record class ScopedRegistry(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("url")] string Url,
	[property: JsonPropertyName("scopes")] string[] Scopes);

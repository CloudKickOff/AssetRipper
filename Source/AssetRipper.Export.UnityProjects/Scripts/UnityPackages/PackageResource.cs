namespace AssetRipper.Export.UnityProjects.Scripts.UnityPackages;

/// <summary>
/// A file in a Resources folder of a Unity package.
/// </summary>
/// <param name="Name">The path inside the Resources folder, without extension, in lower case.</param>
/// <param name="Guid">The guid of the file.</param>
public sealed record class PackageResource(string Name, UnityGuid Guid, string PackageName, string PackageVersion);

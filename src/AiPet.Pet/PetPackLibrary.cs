using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AiPet.Common;

namespace AiPet.Pet;

public enum PetPackError
{
    FolderUnavailable,
    UnsafePath,
    InvalidManifest,
    InvalidIdentity,
    UnsupportedLicense,
    InvalidSourceEvidence,
    UnsupportedInventory,
    UnsupportedRenderContract,
    ResourceBudgetExceeded,
    InvalidImage,
    DuplicateId,
    PreviewChanged,
    BuiltInProtected,
    PackageNotFound,
}

public sealed class PetPackException : IOException
{
    public PetPackException(PetPackError error, string message, Exception? innerException = null)
        : base(message, innerException) => Error = error;

    public PetPackError Error { get; }
}

public sealed record PetPackDescriptor(
    string Id,
    string DisplayName,
    string Version,
    string Author,
    string License,
    Uri SourceUri,
    int Frames,
    long EncodedBytes,
    long DecodedBytes,
    string Fingerprint,
    string RootPath,
    bool IsBuiltIn,
    PetManifest Manifest,
    IReadOnlyList<string> Files)
{
    public IReadOnlyList<string> Characters => Manifest.FrameInventory?.Characters is { } characters
        ? characters
        : Array.Empty<string>();
    public string PreferredCharacter => Manifest.FrameInventory?.Preferred ?? Characters.FirstOrDefault() ?? string.Empty;
    public string LicensePath => Path.Combine(RootPath, "License.txt");
}

public sealed record PetPackScanResult(
    IReadOnlyList<PetPackDescriptor> Packages,
    int InvalidPackageCount);

/// <summary>
/// Validates the deliberately small, data-only 2D character-pack contract.
/// It never downloads content and only returns the files that the importer is
/// allowed to copy.
/// </summary>
public static class PetPackValidator
{
    public const long MaximumDecodedBytes = 128L * 1024 * 1024;
    public const long MaximumEncodedBytes = 32L * 1024 * 1024;
    public const int MaximumFrames = 1000;
    public const int MaximumImageDimension = 512;
    public const int MaximumFileCount = MaximumFrames + 3;
    public const string BuiltInPackageId = "rgs-8dir";

    private const long MaximumImageBytes = 4L * 1024 * 1024;
    private const long MaximumManifestBytes = 64L * 1024;
    private const long MaximumEvidenceBytes = 64L * 1024;
    private const long MaximumLicenseBytes = 256L * 1024;
    private const string FramePathPattern = "frames/<character>/<action>_<direction>_<NN>.png";
    private const string DeathPathPattern = "frames/_global/death_<NN>.png";

    private static readonly HashSet<string> SupportedLicenses = new(StringComparer.Ordinal)
    {
        "CC0-1.0",
        "CC-BY-4.0",
        "CC-BY-SA-4.0",
        "MIT",
        "Apache-2.0",
    };

    private static readonly string[] SupportedDirections =
        { "down", "down_right", "right", "up_right", "up" };

    private static readonly string[] RequiredActions = { "idle", "jump" };

    public static PetPackDescriptor Validate(string directory, bool isBuiltIn = false)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            throw Failure(PetPackError.FolderUnavailable, "角色包文件夹不可用。");

        directory = Path.GetFullPath(directory);
        EnsureNoLinks(directory);

        var manifestPath = ResolveDeclaredFile(directory, "pet.json");
        RequireSize(manifestPath, 1, MaximumManifestBytes, PetPackError.InvalidManifest, "角色包清单大小无效。");

        JsonDocument json;
        try
        {
            json = JsonDocument.Parse(
                File.ReadAllBytes(manifestPath),
                new JsonDocumentOptions { MaxDepth = 24 });
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            throw Failure(PetPackError.InvalidManifest, "角色包清单无法读取。", ex);
        }

        using (json)
        {
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw Failure(PetPackError.InvalidManifest, "角色包清单必须是 JSON 对象。");

            var allowed = new HashSet<string>(StringComparer.Ordinal)
            {
                "$schema", "id", "displayName", "version", "source", "frameInventory",
                "directionModel", "stateMachine", "render", "notes",
            };
            var properties = root.EnumerateObject().ToArray();
            if (properties.Any(property => !allowed.Contains(property.Name))
                || properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                throw Failure(PetPackError.InvalidManifest, "角色包清单包含未知或重复顶层字段。");

            PetManifest manifest;
            try
            {
                manifest = PetManifestLoader.Load(manifestPath);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                throw Failure(PetPackError.InvalidManifest, "角色包清单无法解释。", ex);
            }

            var id = manifest.Id?.Trim() ?? string.Empty;
            var displayName = manifest.DisplayName?.Trim() ?? string.Empty;
            var version = manifest.Version?.Trim() ?? string.Empty;
            if (!Regex.IsMatch(id, "^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant)
                || (!isBuiltIn && string.Equals(id, BuiltInPackageId, StringComparison.Ordinal))
                || displayName.Length is < 1 or > 100
                || !Regex.IsMatch(version, "^\\d+\\.\\d+\\.\\d+$", RegexOptions.CultureInvariant))
                throw Failure(PetPackError.InvalidIdentity, "角色包身份或版本无效。");

            if (!root.TryGetProperty("source", out var source) || source.ValueKind != JsonValueKind.Object)
                throw Failure(PetPackError.InvalidSourceEvidence, "角色包缺少来源信息。");

            var author = manifest.Source?.Author?.Trim() ?? string.Empty;
            var license = manifest.Source?.License?.Trim() ?? string.Empty;
            var sourceHash = manifest.Source?.ZipSha256?.Trim() ?? string.Empty;
            var sourceUrlText = source.TryGetProperty("url", out var sourceUrl) && sourceUrl.ValueKind == JsonValueKind.String
                ? sourceUrl.GetString()
                : null;
            if (author.Length is < 1 or > 100
                || !SupportedLicenses.Contains(license)
                || !Regex.IsMatch(sourceHash, "^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)
                || !Uri.TryCreate(sourceUrlText, UriKind.Absolute, out var sourceUri)
                || !string.Equals(sourceUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                var error = SupportedLicenses.Contains(license)
                    ? PetPackError.InvalidSourceEvidence
                    : PetPackError.UnsupportedLicense;
                throw Failure(error, "角色包来源或 SPDX 许可不受支持。");
            }

            var sourceFile = ResolveDeclaredFile(directory, "SOURCE.txt");
            RequireSize(sourceFile, 20, MaximumEvidenceBytes, PetPackError.InvalidSourceEvidence, "来源留痕大小无效。");
            var evidence = ReadBoundedText(sourceFile, MaximumEvidenceBytes, PetPackError.InvalidSourceEvidence);
            if (!evidence.Contains(license, StringComparison.Ordinal)
                || !evidence.Contains(sourceHash, StringComparison.OrdinalIgnoreCase))
                throw Failure(PetPackError.InvalidSourceEvidence, "来源留痕未绑定 SPDX 与来源摘要。");

            var licenseFile = ResolveDeclaredFile(directory, "License.txt");
            RequireSize(licenseFile, 20, MaximumLicenseBytes, PetPackError.UnsupportedLicense, "本地许可文件大小无效。");

            var inventory = manifest.FrameInventory
                ?? throw Failure(PetPackError.UnsupportedInventory, "角色包缺少帧清单。");
            if (!Tokens(inventory.Characters, 16)
                || !Tokens(inventory.ActionsPerCharacter, RequiredActions.Length)
                || inventory.ActionsPerCharacter.Count != RequiredActions.Length
                || inventory.ActionsPerCharacter.Except(RequiredActions, StringComparer.Ordinal).Any()
                || !Tokens(inventory.Directions, SupportedDirections.Length)
                || inventory.Directions.Count != SupportedDirections.Length
                || inventory.Directions.Except(SupportedDirections, StringComparer.Ordinal).Any()
                || inventory.Preferred is null
                || !inventory.Characters.Contains(inventory.Preferred, StringComparer.Ordinal)
                || inventory.FramesPerAction is null
                || inventory.FramesPerAction.Count != RequiredActions.Length
                || RequiredActions.Any(action => !inventory.FramesPerAction.TryGetValue(action, out var frames) || frames is < 1 or > 60))
                throw Failure(PetPackError.UnsupportedInventory, "角色、动作、方向或帧数不符合受限 2D 合同。");

            if (!string.Equals(inventory.FilePathPattern, FramePathPattern, StringComparison.Ordinal)
                || !string.Equals(inventory.DeathFilePathPattern, DeathPathPattern, StringComparison.Ordinal)
                || inventory.GlobalActions is null
                || inventory.GlobalActions.Count > 1
                || inventory.GlobalActions.Any(action => action.Name != "death" || action.Frames is < 1 or > 60)
                || manifest.Render is null
                || manifest.Render.Fps is < 1 or > 30
                || !double.IsFinite(manifest.Render.DefaultScale)
                || manifest.Render.DefaultScale is < 0.25 or > 4)
                throw Failure(PetPackError.UnsupportedRenderContract, "角色包渲染合同不受支持。");

            var expectedFrames = inventory.Characters.Count
                * inventory.Directions.Count
                * RequiredActions.Sum(action => inventory.FramesPerAction[action])
                + inventory.GlobalActions.Sum(action => action.Frames);
            if (expectedFrames is < 1 or > MaximumFrames
                || !root.TryGetProperty("frameInventory", out var inventoryJson)
                || !inventoryJson.TryGetProperty("totalFrames", out var totalFramesJson)
                || !totalFramesJson.TryGetInt32(out var declaredFrames)
                || declaredFrames != expectedFrames)
                throw Failure(PetPackError.ResourceBudgetExceeded, "角色包声明帧数与实际合同不一致。");

            var paths = new List<string> { "pet.json", "SOURCE.txt", "License.txt" };
            foreach (var character in inventory.Characters)
            foreach (var action in RequiredActions)
            foreach (var direction in SupportedDirections)
            for (var frame = 1; frame <= inventory.FramesPerAction[action]; frame++)
                paths.Add($"frames/{character}/{action}_{direction}_{frame:D2}.png");
            foreach (var action in inventory.GlobalActions)
            for (var frame = 1; frame <= action.Frames; frame++)
                paths.Add($"frames/_global/death_{frame:D2}.png");

            if (paths.Count > MaximumFileCount)
                throw Failure(PetPackError.ResourceBudgetExceeded, "角色包文件数量超过上限。");

            long decodedBytes = 0;
            long encodedBytes = 0;
            var hashes = new List<string>(paths.Count);
            foreach (var relativePath in paths.OrderBy(path => path, StringComparer.Ordinal))
            {
                var file = ResolveDeclaredFile(directory, relativePath);
                var size = new FileInfo(file).Length;
                if (size < 1 || size > (relativePath.EndsWith(".png", StringComparison.Ordinal) ? MaximumImageBytes : MaximumLicenseBytes))
                    throw Failure(PetPackError.ResourceBudgetExceeded, "角色包单文件大小超过上限。");
                encodedBytes = checked(encodedBytes + size);
                if (encodedBytes > MaximumEncodedBytes)
                    throw Failure(PetPackError.ResourceBudgetExceeded, "角色包编码数据超过上限。");

                var bytes = File.ReadAllBytes(file);
                if (relativePath.EndsWith(".png", StringComparison.Ordinal))
                    decodedBytes = ValidatePng(bytes, decodedBytes);
                hashes.Add(relativePath + ":" + Convert.ToHexString(SHA256.HashData(bytes)));
            }

            var fingerprint = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', hashes))));
            return new PetPackDescriptor(
                id,
                displayName,
                version,
                author,
                license,
                sourceUri,
                expectedFrames,
                encodedBytes,
                decodedBytes,
                fingerprint,
                directory,
                isBuiltIn,
                manifest,
                paths.AsReadOnly());
        }
    }

    internal static string ResolveDeclaredFile(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathRooted(relativePath)
            || relativePath.Contains(':', StringComparison.Ordinal)
            || relativePath.Split('/', '\\').Any(part => part is "" or "." or ".."))
            throw Failure(PetPackError.UnsafePath, "角色包包含不安全路径。");

        root = Path.GetFullPath(root);
        var file = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (!file.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw Failure(PetPackError.UnsafePath, "角色包路径越过根目录。");
        EnsureNoLinks(file);
        if (!File.Exists(file))
            throw Failure(PetPackError.InvalidManifest, "角色包缺少声明文件。");
        return file;
    }

    internal static void EnsureNoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current))
                && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw Failure(PetPackError.UnsafePath, "角色包路径不能经过链接或重解析点。");
        }
    }

    private static bool Tokens(IReadOnlyList<string>? values, int maximum) =>
        values is { Count: > 0 }
        && values.Count <= maximum
        && values.Distinct(StringComparer.Ordinal).Count() == values.Count
        && values.All(value => value is not null
            && Regex.IsMatch(value, "^[a-z][a-z0-9_]{0,31}$", RegexOptions.CultureInvariant));

    private static long ValidatePng(byte[] bytes, long decodedSoFar)
    {
        try
        {
            if (bytes.Length < 33
                || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                || !Encoding.ASCII.GetString(bytes, 12, 4).Equals("IHDR", StringComparison.Ordinal))
                throw Failure(PetPackError.InvalidImage, "PNG 文件头无效。");

            var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
            var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
            if (width is < 1 or > MaximumImageDimension || height is < 1 or > MaximumImageDimension)
                throw Failure(PetPackError.ResourceBudgetExceeded, "PNG 尺寸超过上限。");

            var decoded = checked(decodedSoFar + (long)width * height * 4);
            if (decoded > MaximumDecodedBytes)
                throw Failure(PetPackError.ResourceBudgetExceeded, "角色包解码预算超过上限。");

            using var stream = new MemoryStream(bytes, writable: false);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count != 1
                || decoder.Frames[0].PixelWidth != width
                || decoder.Frames[0].PixelHeight != height)
                throw Failure(PetPackError.InvalidImage, "PNG 实际尺寸与文件头不一致。");

            var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            var stride = checked(converted.PixelWidth * 4);
            var pixels = new byte[checked(stride * converted.PixelHeight)];
            converted.CopyPixels(pixels, stride, 0);
            return decoded;
        }
        catch (PetPackException)
        {
            throw;
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or InvalidOperationException or OverflowException)
        {
            throw Failure(PetPackError.InvalidImage, "PNG 像素无法完整解码。", ex);
        }
    }

    private static string ReadBoundedText(string path, long maximumBytes, PetPackError error)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length > maximumBytes) throw Failure(error, "文本文件超过上限。");
            return File.ReadAllText(path, Encoding.UTF8);
        }
        catch (PetPackException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            throw Failure(error, "文本文件无法读取。", ex);
        }
    }

    private static void RequireSize(
        string path,
        long minimum,
        long maximum,
        PetPackError error,
        string message)
    {
        var size = new FileInfo(path).Length;
        if (size < minimum || size > maximum) throw Failure(error, message);
    }

    private static PetPackException Failure(PetPackError error, string message, Exception? inner = null) =>
        new(error, message, inner);
}

/// <summary>
/// Account-local library for validated character packs. Imported files are
/// copied through a same-volume staging directory and are never executed.
/// </summary>
public sealed class PetPackLibrary
{
    private readonly string _builtInRoot;
    private readonly string _userRoot;

    public PetPackLibrary(string appDataRoot, string builtInRoot)
    {
        if (string.IsNullOrWhiteSpace(appDataRoot))
            throw new ArgumentException("Application data root is required.", nameof(appDataRoot));
        _builtInRoot = Path.GetFullPath(builtInRoot);
        _userRoot = Path.GetFullPath(Path.Combine(appDataRoot, "pet-packs"));
        Directory.CreateDirectory(_userRoot);
        PetPackValidator.EnsureNoLinks(_userRoot);
    }

    public string UserRoot => _userRoot;

    public PetPackDescriptor GetBuiltIn() => PetPackValidator.Validate(_builtInRoot, isBuiltIn: true);

    public PetPackDescriptor PreviewImport(string sourceDirectory) =>
        PetPackValidator.Validate(sourceDirectory, isBuiltIn: false);

    public PetPackScanResult Scan()
    {
        var packages = new List<PetPackDescriptor> { GetBuiltIn() };
        var invalid = 0;
        foreach (var directory in Directory.EnumerateDirectories(_userRoot, "*", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (Path.GetFileName(directory).StartsWith(".staging-", StringComparison.Ordinal)) continue;
            try
            {
                var descriptor = PetPackValidator.Validate(directory, isBuiltIn: false);
                if (packages.Any(package => string.Equals(package.Id, descriptor.Id, StringComparison.Ordinal)))
                {
                    invalid++;
                    continue;
                }
                packages.Add(descriptor);
            }
            catch
            {
                invalid++;
            }
        }
        return new(packages.AsReadOnly(), invalid);
    }

    public PetPackDescriptor Import(string sourceDirectory, PetPackDescriptor confirmedPreview)
    {
        ArgumentNullException.ThrowIfNull(confirmedPreview);
        var current = PreviewImport(sourceDirectory);
        if (!string.Equals(current.Id, confirmedPreview.Id, StringComparison.Ordinal)
            || !string.Equals(current.Fingerprint, confirmedPreview.Fingerprint, StringComparison.Ordinal))
            throw new PetPackException(PetPackError.PreviewChanged, "角色包在预览后发生变化，请重新选择并确认。");

        var target = PackagePath(current.Id);
        if (Directory.Exists(target))
            throw new PetPackException(PetPackError.DuplicateId, "同 ID 的角色包已经存在。");

        var staging = Path.Combine(_userRoot, ".staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var relativePath in current.Files)
            {
                var source = PetPackValidator.ResolveDeclaredFile(sourceDirectory, relativePath);
                var destination = Path.GetFullPath(Path.Combine(staging, relativePath.Replace('/', Path.DirectorySeparatorChar)));
                EnsureUnderRoot(staging, destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: false);
            }

            var imported = PetPackValidator.Validate(staging, isBuiltIn: false);
            if (!string.Equals(imported.Fingerprint, current.Fingerprint, StringComparison.Ordinal))
                throw new PetPackException(PetPackError.PreviewChanged, "角色包复制期间发生变化，导入已取消。");
            Directory.Move(staging, target);
            return PetPackValidator.Validate(target, isBuiltIn: false);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    public PetPackDescriptor Resolve(string packageId)
    {
        if (string.Equals(packageId, PetPackValidator.BuiltInPackageId, StringComparison.Ordinal))
            return GetBuiltIn();
        var path = PackagePath(packageId);
        if (!Directory.Exists(path))
            throw new PetPackException(PetPackError.PackageNotFound, "角色包不存在。");
        return PetPackValidator.Validate(path, isBuiltIn: false);
    }

    public void Remove(string packageId)
    {
        if (string.Equals(packageId, PetPackValidator.BuiltInPackageId, StringComparison.Ordinal))
            throw new PetPackException(PetPackError.BuiltInProtected, "内置角色包不能移除。");
        var target = PackagePath(packageId);
        if (!Directory.Exists(target))
            throw new PetPackException(PetPackError.PackageNotFound, "角色包不存在。");
        EnsureTreeHasNoLinks(target);
        Directory.Delete(target, recursive: true);
    }

    private static void EnsureTreeHasNoLinks(string root)
    {
        PetPackValidator.EnsureNoLinks(root);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly))
            {
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw new PetPackException(PetPackError.UnsafePath, "角色包库包含链接或重解析点，已拒绝移除。");
                if (attributes.HasFlag(FileAttributes.Directory)) pending.Push(entry);
            }
        }
    }

    private string PackagePath(string packageId)
    {
        if (!Regex.IsMatch(packageId, "^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant))
            throw new PetPackException(PetPackError.InvalidIdentity, "角色包 ID 无效。");
        var path = Path.GetFullPath(Path.Combine(_userRoot, packageId));
        EnsureUnderRoot(_userRoot, path);
        return path;
    }

    private static void EnsureUnderRoot(string root, string path)
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new PetPackException(PetPackError.UnsafePath, "角色包路径越过库目录。");
    }
}

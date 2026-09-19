using System.IO;
using System.Text.Json.Nodes;
using AiPet.AI;
using AiPet.Pet;
using AiPet.Search;
using AiPet.Shortcuts;
using AiPet.Storage;
using AiPet.ToolWindow;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class PetPackLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "aipet-pet-packs-" + Guid.NewGuid().ToString("N"));

    public PetPackLibraryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Built_in_and_external_preview_enforce_the_same_bounded_contract()
    {
        var builtIn = PetPackValidator.Validate(FindBuiltIn(), isBuiltIn: true);
        var source = CopyExternalPack("preview-source", "fixture-pack");
        var external = PetPackValidator.Validate(source);

        Assert.True(builtIn.IsBuiltIn);
        Assert.False(external.IsBuiltIn);
        Assert.Equal(247, external.Frames);
        Assert.Equal(4, external.Characters.Count);
        Assert.InRange(external.EncodedBytes, 1, PetPackValidator.MaximumEncodedBytes);
        Assert.InRange(external.DecodedBytes, 1, PetPackValidator.MaximumDecodedBytes);
        Assert.Equal(64, external.Fingerprint.Length);
        Assert.Contains("License.txt", external.Files);
        Assert.Equal("CC0-1.0", external.License);
    }

    [Fact]
    public void Import_rechecks_preview_copies_only_declared_files_and_never_changes_source()
    {
        var source = CopyExternalPack("import-source", "fixture-pack");
        File.WriteAllText(Path.Combine(source, "not-declared.exe"), "must not be copied");
        var appData = Path.Combine(_root, "app-data");
        var library = new PetPackLibrary(appData, FindBuiltIn());
        var preview = library.PreviewImport(source);

        var imported = library.Import(source, preview);

        Assert.Equal(preview.Fingerprint, imported.Fingerprint);
        Assert.True(File.Exists(Path.Combine(source, "pet.json")));
        Assert.True(File.Exists(Path.Combine(source, "not-declared.exe")));
        Assert.False(File.Exists(Path.Combine(imported.RootPath, "not-declared.exe")));
        Assert.Throws<PetPackException>(() => library.Import(source, preview));
        Assert.DoesNotContain(
            Directory.EnumerateDirectories(library.UserRoot),
            path => Path.GetFileName(path).StartsWith(".staging-", StringComparison.Ordinal));
    }

    [Fact]
    public void Preview_change_prevents_import_without_a_partial_target()
    {
        var source = CopyExternalPack("changed-source", "changed-pack");
        var appData = Path.Combine(_root, "app-data");
        var library = new PetPackLibrary(appData, FindBuiltIn());
        var preview = library.PreviewImport(source);
        File.AppendAllText(Path.Combine(source, "SOURCE.txt"), "\nchanged after preview");

        var error = Assert.Throws<PetPackException>(() => library.Import(source, preview));

        Assert.Equal(PetPackError.PreviewChanged, error.Error);
        Assert.False(Directory.Exists(Path.Combine(library.UserRoot, "changed-pack")));
        Assert.DoesNotContain(
            Directory.EnumerateDirectories(library.UserRoot),
            path => Path.GetFileName(path).StartsWith(".staging-", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("reserved-id", PetPackError.InvalidIdentity)]
    [InlineData("unknown-field", PetPackError.InvalidManifest)]
    [InlineData("license", PetPackError.UnsupportedLicense)]
    [InlineData("missing-license-file", PetPackError.InvalidManifest)]
    [InlineData("action", PetPackError.UnsupportedInventory)]
    [InlineData("path", PetPackError.UnsupportedRenderContract)]
    [InlineData("missing-frame", PetPackError.InvalidManifest)]
    [InlineData("oversized-image", PetPackError.ResourceBudgetExceeded)]
    public void Invalid_pack_is_rejected_before_it_enters_the_library(
        string variant,
        PetPackError expectedError)
    {
        var id = variant == "reserved-id" ? PetPackValidator.BuiltInPackageId : "invalid-" + variant;
        var source = CopyExternalPack("invalid-" + variant, id);
        var manifestPath = Path.Combine(source, "pet.json");
        var json = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        switch (variant)
        {
            case "unknown-field":
                json["runScript"] = "untrusted";
                break;
            case "license":
                json["source"]!["license"] = "LicenseRef-Proprietary";
                break;
            case "action":
                json["frameInventory"]!["actionsPerCharacter"] = new JsonArray("idle", "walk");
                break;
            case "path":
                json["frameInventory"]!["filePathPattern"] = "../<character>.png";
                break;
        }
        File.WriteAllText(manifestPath, json.ToJsonString());

        if (variant == "missing-license-file") File.Delete(Path.Combine(source, "License.txt"));
        var frame = Path.Combine(source, "frames", "hero", "idle_down_01.png");
        if (variant == "missing-frame") File.Delete(frame);
        if (variant == "oversized-image")
        {
            var bytes = File.ReadAllBytes(frame);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), 100000);
            File.WriteAllBytes(frame, bytes);
        }

        var error = Assert.Throws<PetPackException>(() => PetPackValidator.Validate(source));

        Assert.Equal(expectedError, error.Error);
    }

    [Fact]
    public void Scan_isolates_corruption_and_remove_keeps_the_original_folder()
    {
        var source = CopyExternalPack("source", "valid-pack");
        var appData = Path.Combine(_root, "app-data");
        var library = new PetPackLibrary(appData, FindBuiltIn());
        var imported = library.Import(source, library.PreviewImport(source));
        var corrupt = Path.Combine(library.UserRoot, "corrupt-pack");
        Directory.CreateDirectory(corrupt);
        File.WriteAllText(Path.Combine(corrupt, "pet.json"), "broken");

        var scan = library.Scan();

        Assert.Equal(2, scan.Packages.Count);
        Assert.Contains(scan.Packages, package => package.Id == PetPackValidator.BuiltInPackageId);
        Assert.Contains(scan.Packages, package => package.Id == "valid-pack");
        Assert.Equal(1, scan.InvalidPackageCount);

        library.Remove(imported.Id);
        Assert.False(Directory.Exists(imported.RootPath));
        Assert.True(File.Exists(Path.Combine(source, "pet.json")));
        Assert.Equal(PetPackError.BuiltInProtected,
            Assert.Throws<PetPackException>(() => library.Remove(PetPackValidator.BuiltInPackageId)).Error);
    }

    [Fact]
    public void View_model_switches_persists_and_removes_an_external_pack_with_built_in_fallback()
    {
        var source = CopyExternalPack("view-model-source", "view-model-pack");
        var appData = Path.Combine(_root, "view-model-app-data");
        var library = new PetPackLibrary(appData, FindBuiltIn());
        library.Import(source, library.PreviewImport(source));
        var settings = new SettingsStore(appData);
        var vm = new HomeViewModel(
            new SearchService(settings.IndexPath),
            new ShortcutStore(appData),
            new OpenAiCompatibleClient(),
            settings);
        var applied = new List<PetCharacterSelection>();
        vm.PetSelectionChanged += applied.Add;
        vm.ConfigurePetLibrary(library, library.Scan(), PetPackValidator.BuiltInPackageId, "hero");
        var external = Assert.Single(vm.PetCharacters, option =>
            option.PackageId == "view-model-pack" && option.CharacterId == "hero");

        vm.SelectedCharacter = external.Id;

        Assert.Equal("view-model-pack", settings.Load().Pet.Id);
        Assert.Equal("hero", settings.Load().Pet.PreferredCharacter);
        Assert.Equal("view-model-pack", Assert.Single(applied).PackageId);
        Assert.True(vm.SelectedPetCanRemove);
        vm.RequestRemovePetPackCommand.Execute(null);
        Assert.True(vm.ShowRemovePetPackConfirmation);

        vm.ConfirmRemovePetPackCommand.Execute(null);

        Assert.Equal(PetPackValidator.BuiltInPackageId, settings.Load().Pet.Id);
        Assert.False(vm.SelectedPetCanRemove);
        Assert.False(vm.ShowRemovePetPackConfirmation);
        Assert.DoesNotContain(vm.PetCharacters, option => option.PackageId == "view-model-pack");
        Assert.Throws<PetPackException>(() => library.Resolve("view-model-pack"));
        Assert.True(File.Exists(Path.Combine(source, "pet.json")));
    }

    private string CopyExternalPack(string directoryName, string id)
    {
        var source = FindBuiltIn();
        var destination = Path.Combine(_root, directoryName);
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        var manifestPath = Path.Combine(destination, "pet.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        manifest["id"] = id;
        manifest["displayName"] = "Fixture " + id;
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        return destination;
    }

    private static string FindBuiltIn()
    {
        var cursor = new DirectoryInfo(AppContext.BaseDirectory);
        while (cursor is not null)
        {
            var candidate = Path.Combine(cursor.FullName, "assets", "pets", "RGS_8Directional");
            if (File.Exists(Path.Combine(candidate, "pet.json"))) return candidate;
            cursor = cursor.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the built-in RGS pack.");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best effort test cleanup */ }
    }
}

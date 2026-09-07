using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using AiPet.Common;

namespace AiPet.Prototypes;

public sealed record PetPackPreview(string Id, string Name, string License, int Frames, long DecodedBytes, string Fingerprint, IReadOnlyList<string> Files);

/// <summary>Bounded 2D import experiment. No scripts, downloads, archives or runtime app integration.</summary>
public static class StrictPetPack
{
    public const long MaximumDecodedBytes=128L*1024*1024;
    private static readonly HashSet<string> Licenses=new(StringComparer.Ordinal) {"CC0-1.0","CC-BY-4.0","CC-BY-SA-4.0","MIT","Apache-2.0"};
    private static readonly string[] Directions={"down","down_right","right","up_right","up"};
    public static PetPackPreview Preview(string directory)
    {
        directory=Path.GetFullPath(directory); ControlledTextIndex.EnsureNoLinks(directory);
        var manifestPath=SafeFile(directory,"pet.json");
        if (new FileInfo(manifestPath).Length>64*1024) throw new InvalidDataException("Manifest budget exceeded.");
        using var json=JsonDocument.Parse(File.ReadAllBytes(manifestPath),new JsonDocumentOptions {MaxDepth=24});
        var root=json.RootElement;
        if (root.ValueKind!=JsonValueKind.Object) throw new InvalidDataException("Manifest must be an object.");
        var allowed=new HashSet<string>{"$schema","id","displayName","version","source","frameInventory","directionModel","stateMachine","render","notes"};
        if (root.EnumerateObject().Any(p=>!allowed.Contains(p.Name)) || root.EnumerateObject().Select(p=>p.Name).Distinct().Count()!=root.EnumerateObject().Count())
            throw new InvalidDataException("Unknown or duplicate manifest fields.");
        var manifest=PetManifestLoader.Load(manifestPath);
        if (!Regex.IsMatch(manifest.Id??"","^[a-z0-9][a-z0-9-]{0,63}$") || string.IsNullOrWhiteSpace(manifest.DisplayName) || manifest.DisplayName.Length>100 || !Regex.IsMatch(manifest.Version??"","^\\d+\\.\\d+\\.\\d+$"))
            throw new InvalidDataException("Invalid pack identity.");
        var source=root.GetProperty("source");
        var license=manifest.Source?.License??"";
        if (!Licenses.Contains(license) || string.IsNullOrWhiteSpace(manifest.Source?.Author) || !Regex.IsMatch(manifest.Source?.ZipSha256??"","^[0-9a-fA-F]{64}$") ||
            !Uri.TryCreate(source.GetProperty("url").GetString(),UriKind.Absolute,out var url) || url.Scheme!="https")
            throw new InvalidDataException("Source and supported SPDX license are required.");
        var sourceFile=SafeFile(directory,"SOURCE.txt");
        if (new FileInfo(sourceFile).Length is < 20 or > 64*1024) throw new InvalidDataException("Source evidence budget invalid.");
        var evidence=File.ReadAllText(sourceFile);
        if (!evidence.Contains(license,StringComparison.Ordinal) || !evidence.Contains(manifest.Source!.ZipSha256!,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Source evidence must bind SPDX and archive hash.");
        var inventory=manifest.FrameInventory??throw new InvalidDataException("Missing frame inventory.");
        static bool Tokens(IReadOnlyList<string>? list,int maximum) => list is {Count:>0} && list.Count<=maximum && list.Distinct().Count()==list.Count && list.All(value=>value is not null && Regex.IsMatch(value,"^[a-z][a-z0-9_]{0,31}$"));
        if (!Tokens(inventory.Characters,16) || !Tokens(inventory.ActionsPerCharacter,16) || !Tokens(inventory.Directions,8) ||
            inventory.Directions.Except(Directions).Any() || inventory.Preferred is null || !inventory.Characters.Contains(inventory.Preferred) ||
            inventory.FramesPerAction is null || inventory.FramesPerAction.Count!=inventory.ActionsPerCharacter.Count ||
            inventory.ActionsPerCharacter.Any(action=>!inventory.FramesPerAction.TryGetValue(action,out var frames)||frames is <1 or >60))
            throw new InvalidDataException("Unsupported frame inventory.");
        if (inventory.FilePathPattern!="frames/<character>/<action>_<direction>_<NN>.png" || inventory.DeathFilePathPattern!="frames/_global/death_<NN>.png" ||
            inventory.GlobalActions is null || inventory.GlobalActions.Count>1 || inventory.GlobalActions.Any(action=>action.Name!="death" || action.Frames is <1 or >60) ||
            manifest.Render is null || manifest.Render.Fps is <1 or >30 || !double.IsFinite(manifest.Render.DefaultScale) || manifest.Render.DefaultScale is <0.25 or >4)
            throw new InvalidDataException("Unsupported 2D render contract.");
        if (inventory.TotalFrames is <1 or >1000 || root.GetProperty("frameInventory").GetProperty("totalFrames").GetInt32()!=inventory.TotalFrames)
            throw new InvalidDataException("Frame count budget invalid.");
        var paths=new List<string>{"pet.json","SOURCE.txt"};
        foreach (var character in inventory.Characters)
        foreach (var action in inventory.ActionsPerCharacter)
        foreach (var direction in inventory.Directions)
        for (var frame=1;frame<=inventory.FramesPerAction[action];frame++) paths.Add($"frames/{character}/{action}_{direction}_{frame:D2}.png");
        foreach (var action in inventory.GlobalActions)
        for (var frame=1;frame<=action.Frames;frame++) paths.Add($"frames/_global/death_{frame:D2}.png");
        long decoded=0; long compressed=0;
        var hashes=new List<string>();
        foreach (var path in paths)
        {
            var file=SafeFile(directory,path); var size=new FileInfo(file).Length;
            if (size>4*1024*1024 || (compressed+=size)>32*1024*1024) throw new InvalidDataException("Encoded image budget exceeded.");
            var bytes=File.ReadAllBytes(file);
            if (path.EndsWith(".png",StringComparison.Ordinal))
            {
                if (bytes.Length<33 || !bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}) || Encoding.ASCII.GetString(bytes,12,4)!="IHDR")
                    throw new InvalidDataException("Invalid PNG header.");
                var width=BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16,4)); var height=BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20,4));
                if (width is <1 or >512 || height is <1 or >512 || (decoded+=(long)width*height*4)>MaximumDecodedBytes)
                    throw new InvalidDataException("Decoded image budget exceeded.");
                using var stream=new MemoryStream(bytes,false);
                var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count!=1 || decoder.Frames[0].PixelWidth!=width || decoder.Frames[0].PixelHeight!=height)
                    throw new InvalidDataException("Image dimensions disagree with header.");
                // Force pixel decoding after the header budget check; catches truncated image payloads.
                var pixels=new byte[checked((int)width*(int)height*8)];
                decoder.Frames[0].CopyPixels(pixels,checked((int)width*8),0);
            }
            hashes.Add(path+" "+Convert.ToHexString(SHA256.HashData(bytes)));
        }
        var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',hashes))));
        return new(manifest.Id!,manifest.DisplayName,license,inventory.TotalFrames,decoded,fingerprint,paths.AsReadOnly());
    }
    private static string SafeFile(string root,string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(':') || relative.Split('/','\\').Any(part=>part is ".." or "." or "")) throw new InvalidDataException("Unsafe pack path.");
        var file=Path.GetFullPath(Path.Combine(root,relative));
        if (!file.StartsWith(Path.TrimEndingDirectorySeparator(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Pack path escaped root.");
        ControlledTextIndex.EnsureNoLinks(file);
        if (!File.Exists(file)) throw new InvalidDataException("Missing pack file.");
        return file;
    }
}

public sealed class ExperimentalPetLibrary
{
    private readonly string _root;
    private readonly string _builtin;
    private string? _active;
    public ExperimentalPetLibrary(string isolatedRoot,string builtin)
    { _root=Path.GetFullPath(isolatedRoot); _builtin=Path.GetFullPath(builtin); ControlledTextIndex.EnsureNoLinks(_root); Directory.CreateDirectory(_root); }
    private string PackPath(string id)
    {
        if (!Regex.IsMatch(id,"^[a-z0-9][a-z0-9-]{0,63}$")) throw new InvalidDataException("Invalid pack ID.");
        var path=Path.Combine(_root,id); ControlledTextIndex.EnsureNoLinks(path); return path;
    }
    public void Import(string source,PetPackPreview confirmedPreview)
    {
        var current=StrictPetPack.Preview(source);
        if (current.Fingerprint!=confirmedPreview.Fingerprint || current.Id!=confirmedPreview.Id) throw new InvalidDataException("Preview is stale.");
        var target=PackPath(current.Id);
        if (Directory.Exists(target)) throw new InvalidOperationException("Duplicate pack ID.");
        var staging=Path.Combine(_root,".staging-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
        try
        {
            foreach (var file in current.Files)
            {
                var destination=Path.Combine(staging,file); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(Path.Combine(source,file),destination);
            }
            if (StrictPetPack.Preview(staging).Fingerprint!=current.Fingerprint) throw new InvalidDataException("Pack changed during import.");
            Directory.Move(staging,target);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging,true); }
    }
    public void Enable(string id) { _=StrictPetPack.Preview(PackPath(id)); _active=id; }
    public string ResolveActive()
    {
        if (_active is null) return _builtin;
        try { var path=PackPath(_active); _=StrictPetPack.Preview(path); return path; }
        catch { _active=null; return _builtin; }
    }
    public void Remove(string id)
    {
        var path=PackPath(id); if (_active==id) _active=null;
        if (Directory.Exists(path)) Directory.Delete(path,true);
    }
}

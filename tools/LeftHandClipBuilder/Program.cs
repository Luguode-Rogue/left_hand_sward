using System.Text;
using System.Xml.Linq;

internal static class Program
{
    private static readonly Guid AnimationClipType =
        new("506509c8-e563-4ca4-b166-a53b92e913a7");
    private static readonly Guid OutputPackageGuid =
        new("9134b16e-6bca-4d43-9ae5-9342794f8f80");
    private static readonly Guid OutputAssetGuid =
        new("df24ae53-2ac7-4fa5-88b7-4ee0a6e7f9cc");

    private const string SourceAction =
        "act_release_slashright_1h_left_stance";
    private const string OutputClip =
        "lhs_release_slashright_1h_left_stance_clip";
    private const string LeftColliderFlag =
        "use_left_hand_during_attack";

    private sealed class ClipRecord
    {
        public int PackageVersion;
        public int AssetVersion;
        public byte[] Metadata = Array.Empty<byte>();
        public byte[] Dependencies = Array.Empty<byte>();
        public int DependencyCount;
        public string SourcePackage = "";
    }

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2)
                throw new ArgumentException(
                    "usage: LeftHandClipBuilder <BannerlordGameDir> <output.tpac>");

            string game = Path.GetFullPath(args[0]);
            string output = Path.GetFullPath(args[1]);

            string actionSets = Path.Combine(
                game, "Modules", "Native", "ModuleData", "action_sets.xml");
            if (!File.Exists(actionSets))
                throw new FileNotFoundException("Native action_sets.xml not found", actionSets);

            string sourceClip = ResolveAnimationName(actionSets, SourceAction);
            ClipRecord source = FindClip(game, sourceClip)
                ?? throw new InvalidOperationException(
                    "AnimationClip not found in Native TPACs: " + sourceClip);

            byte[] patchedMetadata =
                AddFlag(source.Metadata, LeftColliderFlag);

            Directory.CreateDirectory(
                Path.GetDirectoryName(output)
                ?? throw new InvalidOperationException("Output directory missing"));

            WriteSingleClipPackage(
                output,
                source.PackageVersion,
                source.AssetVersion,
                patchedMetadata,
                source.Dependencies,
                source.DependencyCount);

            Console.WriteLine(
                "[LeftHandClipBuilder] sourceAction=" + SourceAction
                + " sourceClip=" + sourceClip
                + " sourcePackage=" + source.SourcePackage
                + " outputClip=" + OutputClip
                + " output=" + output);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[LeftHandClipBuilder] ERROR: " + ex);
            return 1;
        }
    }

    private static string ResolveAnimationName(
        string actionSetsPath,
        string actionName)
    {
        XDocument doc = XDocument.Load(actionSetsPath);
        XElement? warrior = doc
            .Descendants("action_set")
            .FirstOrDefault(x =>
                string.Equals(
                    (string?)x.Attribute("id"),
                    "as_human_warrior",
                    StringComparison.Ordinal));

        XElement? action = warrior?
            .Elements("action")
            .FirstOrDefault(x =>
                string.Equals(
                    (string?)x.Attribute("type"),
                    actionName,
                    StringComparison.Ordinal));

        string? animation =
            (string?)action?.Attribute("animation");

        if (string.IsNullOrWhiteSpace(animation))
            throw new InvalidOperationException(
                "Native action mapping not found: " + actionName);

        return animation;
    }

    private static ClipRecord? FindClip(
        string game,
        string clipName)
    {
        string native = Path.Combine(game, "Modules", "Native");
        string[] roots =
        {
            Path.Combine(native, "AssetPackages"),
            Path.Combine(native, "Assets"),
            Path.Combine(native, "EmAssetPackages")
        };

        foreach (string root in roots)
        {
            if (!Directory.Exists(root))
                continue;

            foreach (string path in
                     Directory.EnumerateFiles(
                         root,
                         "*.tpac",
                         SearchOption.AllDirectories))
            {
                ClipRecord? clip = TryFindClipInPackage(path, clipName);
                if (clip != null)
                    return clip;
            }
        }

        return null;
    }

    private static ClipRecord? TryFindClipInPackage(
        string path,
        string clipName)
    {
        try
        {
            using FileStream fs = File.Open(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using BinaryReader br =
                new(fs, Encoding.UTF8, leaveOpen: false);

            uint magic = br.ReadUInt32();
            if (magic != 0x43415054)
                return null;

            int packageVersion = br.ReadInt32();
            if (packageVersion is not (1 or 2))
                return null;

            br.ReadBytes(16); // package guid
            int resourceCount = br.ReadInt32();
            br.ReadUInt32(); // data offset
            br.ReadUInt32(); // reserved

            for (int i = 0; i < resourceCount; i++)
            {
                Guid typeGuid = new(br.ReadBytes(16));
                br.ReadBytes(16); // resource guid
                int assetVersion =
                    packageVersion >= 2 ? br.ReadInt32() : 0;
                string name = ReadSizedString(br);

                ulong metadataSize = br.ReadUInt64();
                if (metadataSize > int.MaxValue)
                    throw new InvalidDataException("metadata too large");

                byte[] metadata =
                    br.ReadBytes((int)metadataSize);
                br.ReadInt64(); // checksum

                int segmentCount = br.ReadInt32();
                if (segmentCount < 0)
                    throw new InvalidDataException("negative segment count");

                // AnimationClip resources are metadata-only. We can scan other
                // asset types by skipping their segment descriptors.
                const int SegmentDescriptorSize =
                    8 + 8 + 8 + 16 + 16 + 8 + 4 + 1;
                br.BaseStream.Seek(
                    (long)segmentCount * SegmentDescriptorSize,
                    SeekOrigin.Current);

                int dependencyCount = br.ReadInt32();
                if (dependencyCount < 0)
                    throw new InvalidDataException("negative dependency count");

                byte[] dependencies =
                    br.ReadBytes(checked(dependencyCount * 48));

                if (typeGuid == AnimationClipType &&
                    string.Equals(name, clipName, StringComparison.Ordinal))
                {
                    if (segmentCount != 0)
                        throw new InvalidDataException(
                            "AnimationClip unexpectedly has external data segments");

                    return new ClipRecord
                    {
                        PackageVersion = packageVersion,
                        AssetVersion = assetVersion,
                        Metadata = metadata,
                        Dependencies = dependencies,
                        DependencyCount = dependencyCount,
                        SourcePackage = path
                    };
                }
            }
        }
        catch (EndOfStreamException)
        {
            // Not a package shape we understand; keep scanning.
        }
        catch (IOException)
        {
            // Locked/optional package; keep scanning.
        }

        return null;
    }

    private static byte[] AddFlag(
        byte[] metadata,
        string flag)
    {
        using MemoryStream ms = new(metadata, writable: false);
        using BinaryReader br = new(ms, Encoding.UTF8, leaveOpen: true);

        uint version = br.ReadUInt32();

        // duration, source1, source2, param1, param2, param3
        br.BaseStream.Seek(6 * sizeof(float), SeekOrigin.Current);
        br.ReadInt32(); // priority
        br.BaseStream.Seek(16, SeekOrigin.Current); // animation guid
        br.BaseStream.Seek(4 * sizeof(float), SeekOrigin.Current); // step points

        for (int i = 0; i < 5; i++)
            SkipSizedString(br); // sound, voice, facial, blends, continue

        br.ReadInt32(); // left hand pose
        br.ReadInt32(); // right hand pose
        SkipSizedString(br); // combat parameter id
        br.ReadSingle(); // blend in
        br.ReadSingle(); // blend out
        br.ReadBoolean();
        br.ReadInt32();

        if (version >= 4)
        {
            SkipSizedString(br);
            SkipSizedString(br);
            SkipSizedString(br);
            if (version >= 5)
                br.ReadSByte();
            br.ReadUInt32();
            br.ReadUInt16();
        }
        else
        {
            br.ReadUInt32();
        }

        long flagsStart = br.BaseStream.Position;
        int count = br.ReadInt32();
        if (count < 0 || count > 1024)
            throw new InvalidDataException("invalid animation flag count");

        List<string> flags = new(count + 1);
        for (int i = 0; i < count; i++)
            flags.Add(ReadSizedString(br));

        long tailStart = br.BaseStream.Position;

        if (!flags.Any(x =>
                string.Equals(x, flag, StringComparison.Ordinal)))
        {
            flags.Add(flag);
        }

        using MemoryStream output = new();
        output.Write(metadata, 0, checked((int)flagsStart));

        using (BinaryWriter bw =
               new(output, Encoding.UTF8, leaveOpen: true))
        {
            bw.Write(flags.Count);
            foreach (string value in flags)
                WriteSizedString(bw, value);
        }

        output.Write(
            metadata,
            checked((int)tailStart),
            metadata.Length - checked((int)tailStart));

        return output.ToArray();
    }

    private static void WriteSingleClipPackage(
        string output,
        int sourcePackageVersion,
        int assetVersion,
        byte[] metadata,
        byte[] dependencies,
        int dependencyCount)
    {
        int packageVersion =
            sourcePackageVersion is 1 or 2
                ? sourcePackageVersion
                : 2;

        byte[] nameBytes = Encoding.UTF8.GetBytes(OutputClip);

        long itemSize =
            16 + // type guid
            16 + // asset guid
            (packageVersion >= 2 ? 4 : 0) +
            4 + nameBytes.Length +
            8 + metadata.Length +
            8 + // checksum
            4 + // segment count
            4 + dependencies.Length; // dependency count + records

        const int headerSize = 36;
        long totalSize = headerSize + itemSize;
        if (totalSize > uint.MaxValue)
            throw new InvalidDataException("output package too large");

        using FileStream fs =
            File.Create(output);
        using BinaryWriter bw =
            new(fs, Encoding.UTF8, leaveOpen: false);

        bw.Write(0x43415054u);
        bw.Write(packageVersion);
        bw.Write(OutputPackageGuid.ToByteArray());
        bw.Write(1); // resource count
        bw.Write(checked((uint)(totalSize - headerSize)));
        bw.Write(0u);

        bw.Write(AnimationClipType.ToByteArray());
        bw.Write(OutputAssetGuid.ToByteArray());
        if (packageVersion >= 2)
            bw.Write(assetVersion);

        WriteSizedString(bw, OutputClip);
        bw.Write((ulong)metadata.Length);
        bw.Write(metadata);
        bw.Write(0L); // metadata checksum
        bw.Write(0); // no external data segments
        bw.Write(dependencyCount);
        bw.Write(dependencies);
    }

    private static string ReadSizedString(BinaryReader br)
    {
        int length = br.ReadInt32();
        if (length < 0 || length > 16 * 1024 * 1024)
            throw new InvalidDataException("invalid string length");
        if (length == 0)
            return string.Empty;

        byte[] bytes = br.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException();
        return Encoding.UTF8.GetString(bytes);
    }

    private static void SkipSizedString(BinaryReader br)
    {
        int length = br.ReadInt32();
        if (length < 0 || length > 16 * 1024 * 1024)
            throw new InvalidDataException("invalid string length");
        br.BaseStream.Seek(length, SeekOrigin.Current);
    }

    private static void WriteSizedString(
        BinaryWriter bw,
        string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        bw.Write(bytes.Length);
        bw.Write(bytes);
    }
}

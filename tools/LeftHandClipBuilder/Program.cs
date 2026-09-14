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

    private sealed class SegmentRecord
    {
        public ulong ActualSize;
        public ulong StorageSize;
        public Guid OwnerGuid;
        public Guid TypeGuid;
        public ulong UnknownUlong;
        public uint UnknownUint;
        public byte StorageFormat;
        public byte[] StoredData = Array.Empty<byte>();
    }

    private sealed class ClipRecord
    {
        public int PackageVersion;
        public int AssetVersion;
        public Guid ResourceGuid;
        public byte[] Metadata = Array.Empty<byte>();
        public byte[] Dependencies = Array.Empty<byte>();
        public int DependencyCount;
        public List<SegmentRecord> Segments = new();
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
                source.DependencyCount,
                source.Segments);

            Console.WriteLine(
                "[LeftHandClipBuilder] sourceAction=" + SourceAction
                + " sourceClip=" + sourceClip
                + " sourcePackage=" + source.SourcePackage
                + " sourceSegments=" + source.Segments.Count
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
                Guid resourceGuid = new(br.ReadBytes(16));
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
                if (segmentCount < 0 || segmentCount > 4096)
                    throw new InvalidDataException("invalid segment count");

                List<(ulong Offset, SegmentRecord Segment)> segmentHeaders =
                    new(segmentCount);

                for (int j = 0; j < segmentCount; j++)
                {
                    ulong offset = br.ReadUInt64();
                    ulong actualSize = br.ReadUInt64();
                    ulong storageSize = br.ReadUInt64();
                    Guid ownerGuid = new(br.ReadBytes(16));
                    Guid typeGuidOfSegment = new(br.ReadBytes(16));
                    ulong unknownUlong = br.ReadUInt64();
                    uint unknownUint = br.ReadUInt32();
                    byte storageFormat = br.ReadByte();

                    segmentHeaders.Add(
                        (offset, new SegmentRecord
                        {
                            ActualSize = actualSize,
                            StorageSize = storageSize,
                            OwnerGuid = ownerGuid,
                            TypeGuid = typeGuidOfSegment,
                            UnknownUlong = unknownUlong,
                            UnknownUint = unknownUint,
                            StorageFormat = storageFormat
                        }));
                }

                int dependencyCount = br.ReadInt32();
                if (dependencyCount < 0)
                    throw new InvalidDataException("negative dependency count");

                byte[] dependencies =
                    br.ReadBytes(checked(dependencyCount * 48));

                if (typeGuid == AnimationClipType &&
                    string.Equals(name, clipName, StringComparison.Ordinal))
                {
                    List<SegmentRecord> segments =
                        ReadStoredSegments(br, segmentHeaders);

                    return new ClipRecord
                    {
                        PackageVersion = packageVersion,
                        AssetVersion = assetVersion,
                        ResourceGuid = resourceGuid,
                        Metadata = metadata,
                        Dependencies = dependencies,
                        DependencyCount = dependencyCount,
                        Segments = segments,
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

    private static List<SegmentRecord> ReadStoredSegments(
        BinaryReader br,
        List<(ulong Offset, SegmentRecord Segment)> headers)
    {
        List<SegmentRecord> result = new(headers.Count);
        long returnPosition = br.BaseStream.Position;

        try
        {
            foreach ((ulong offset, SegmentRecord segment) in headers)
            {
                if (offset > long.MaxValue ||
                    segment.StorageSize > int.MaxValue)
                {
                    throw new InvalidDataException(
                        "AnimationClip external data segment is too large");
                }

                long end = checked(
                    (long)offset + (long)segment.StorageSize);

                if ((long)offset < 0 ||
                    end < (long)offset ||
                    end > br.BaseStream.Length)
                {
                    throw new InvalidDataException(
                        "AnimationClip external data segment points outside its TPAC");
                }

                br.BaseStream.Seek((long)offset, SeekOrigin.Begin);
                byte[] stored =
                    br.ReadBytes(checked((int)segment.StorageSize));

                if ((ulong)stored.Length != segment.StorageSize)
                {
                    throw new EndOfStreamException(
                        "AnimationClip external data segment is truncated");
                }

                segment.StoredData = stored;
                result.Add(segment);
            }
        }
        finally
        {
            br.BaseStream.Seek(returnPosition, SeekOrigin.Begin);
        }

        return result;
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
        int dependencyCount,
        IReadOnlyList<SegmentRecord> segments)
    {
        int packageVersion =
            sourcePackageVersion is 1 or 2
                ? sourcePackageVersion
                : 2;

        byte[] nameBytes = Encoding.UTF8.GetBytes(OutputClip);

        const int SegmentDescriptorSize =
            8 + 8 + 8 + 16 + 16 + 8 + 4 + 1;

        long itemHeaderSize =
            16 + // type guid
            16 + // asset guid
            (packageVersion >= 2 ? 4 : 0) +
            4 + nameBytes.Length +
            8 + metadata.Length +
            8 + // metadata checksum
            4 + // segment count
            checked((long)segments.Count * SegmentDescriptorSize) +
            4 + dependencies.Length; // dependency count + records

        const int packageHeaderSize = 36;
        long dataStart = checked(packageHeaderSize + itemHeaderSize);

        long payloadSize = 0;
        foreach (SegmentRecord segment in segments)
        {
            if ((ulong)segment.StoredData.LongLength != segment.StorageSize)
            {
                throw new InvalidDataException(
                    "segment storage size does not match copied payload");
            }

            payloadSize = checked(
                payloadSize + (long)segment.StorageSize);
        }

        long totalFileSize = checked(dataStart + payloadSize);
        if (dataStart - packageHeaderSize > uint.MaxValue)
        {
            throw new InvalidDataException(
                "output TPAC metadata region is too large");
        }

        string? directory = Path.GetDirectoryName(output);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Output directory missing");

        Directory.CreateDirectory(directory);

        string temp =
            output + ".tmp." + Environment.ProcessId + "." + Guid.NewGuid().ToString("N");

        try
        {
            using (FileStream fs =
                   new(
                       temp,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            using (BinaryWriter bw =
                   new(fs, Encoding.UTF8, leaveOpen: false))
            {
                bw.Write(0x43415054u);
                bw.Write(packageVersion);
                bw.Write(OutputPackageGuid.ToByteArray());
                bw.Write(1); // resource count

                // TPAC v1/v2 stores the start of external data relative to
                // the end of the fixed 36-byte package header.
                bw.Write(checked((uint)(dataStart - packageHeaderSize)));
                bw.Write(0u);

                bw.Write(AnimationClipType.ToByteArray());
                bw.Write(OutputAssetGuid.ToByteArray());
                if (packageVersion >= 2)
                    bw.Write(assetVersion);

                WriteSizedString(bw, OutputClip);
                bw.Write((ulong)metadata.Length);
                bw.Write(metadata);
                bw.Write(0L); // metadata checksum

                bw.Write(segments.Count);

                ulong nextSegmentOffset = checked((ulong)dataStart);
                foreach (SegmentRecord segment in segments)
                {
                    bw.Write(nextSegmentOffset);
                    bw.Write(segment.ActualSize);
                    bw.Write(segment.StorageSize);

                    // This segment now belongs to the cloned AnimationClip.
                    bw.Write(OutputAssetGuid.ToByteArray());
                    bw.Write(segment.TypeGuid.ToByteArray());
                    bw.Write(segment.UnknownUlong);
                    bw.Write(segment.UnknownUint);
                    bw.Write(segment.StorageFormat);

                    nextSegmentOffset = checked(
                        nextSegmentOffset + segment.StorageSize);
                }

                bw.Write(dependencyCount);
                bw.Write(dependencies);

                if (fs.Position != dataStart)
                {
                    throw new InvalidDataException(
                        "generated TPAC header size mismatch");
                }

                foreach (SegmentRecord segment in segments)
                    bw.Write(segment.StoredData);

                if (fs.Position != totalFileSize)
                {
                    throw new InvalidDataException(
                        "generated TPAC file size mismatch");
                }
            }

            ValidateGeneratedPackage(
                temp,
                metadata.Length,
                dependencyCount,
                segments);

            // The MSBuild target can be reached by both inner target frameworks.
            // Publish atomically; if another process won the race, keep its valid file.
            if (File.Exists(output))
            {
                File.Delete(temp);
                return;
            }

            File.Move(temp, output);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private static void ValidateGeneratedPackage(
        string path,
        int expectedMetadataLength,
        int expectedDependencyCount,
        IReadOnlyList<SegmentRecord> expectedSegments)
    {
        using FileStream fs =
            File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using BinaryReader br =
            new(fs, Encoding.UTF8, leaveOpen: false);

        if (br.ReadUInt32() != 0x43415054u)
            throw new InvalidDataException("generated TPAC has invalid magic");

        int version = br.ReadInt32();
        if (version is not (1 or 2))
            throw new InvalidDataException("generated TPAC has invalid version");

        br.ReadBytes(16);
        if (br.ReadInt32() != 1)
            throw new InvalidDataException("generated TPAC resource count is not 1");

        uint relativeDataStart = br.ReadUInt32();
        br.ReadUInt32();

        Guid typeGuid = new(br.ReadBytes(16));
        Guid assetGuid = new(br.ReadBytes(16));
        if (typeGuid != AnimationClipType || assetGuid != OutputAssetGuid)
            throw new InvalidDataException("generated TPAC clip identity mismatch");

        if (version >= 2)
            br.ReadInt32();

        string name = ReadSizedString(br);
        if (!string.Equals(name, OutputClip, StringComparison.Ordinal))
            throw new InvalidDataException("generated TPAC clip name mismatch");

        ulong metadataSize = br.ReadUInt64();
        if (metadataSize != (ulong)expectedMetadataLength)
            throw new InvalidDataException("generated TPAC metadata size mismatch");

        br.BaseStream.Seek((long)metadataSize, SeekOrigin.Current);
        br.ReadInt64();

        int segmentCount = br.ReadInt32();
        if (segmentCount != expectedSegments.Count)
            throw new InvalidDataException("generated TPAC segment count mismatch");

        ulong expectedOffset = checked(
            (ulong)packageHeaderSizeForValidation(relativeDataStart));

        for (int i = 0; i < segmentCount; i++)
        {
            ulong offset = br.ReadUInt64();
            ulong actualSize = br.ReadUInt64();
            ulong storageSize = br.ReadUInt64();
            Guid ownerGuid = new(br.ReadBytes(16));
            Guid segmentTypeGuid = new(br.ReadBytes(16));
            ulong unknownUlong = br.ReadUInt64();
            uint unknownUint = br.ReadUInt32();
            byte storageFormat = br.ReadByte();

            SegmentRecord expected = expectedSegments[i];

            if (offset != expectedOffset ||
                actualSize != expected.ActualSize ||
                storageSize != expected.StorageSize ||
                ownerGuid != OutputAssetGuid ||
                segmentTypeGuid != expected.TypeGuid ||
                unknownUlong != expected.UnknownUlong ||
                unknownUint != expected.UnknownUint ||
                storageFormat != expected.StorageFormat)
            {
                throw new InvalidDataException(
                    "generated TPAC segment descriptor mismatch");
            }

            expectedOffset = checked(expectedOffset + storageSize);
        }

        int dependencyCount = br.ReadInt32();
        if (dependencyCount != expectedDependencyCount)
            throw new InvalidDataException("generated TPAC dependency count mismatch");

        br.BaseStream.Seek(
            checked((long)dependencyCount * 48),
            SeekOrigin.Current);

        long expectedDataStart =
            checked(36L + relativeDataStart);

        if (br.BaseStream.Position != expectedDataStart)
            throw new InvalidDataException("generated TPAC data offset mismatch");

        for (int i = 0; i < expectedSegments.Count; i++)
        {
            SegmentRecord expected = expectedSegments[i];
            byte[] stored =
                br.ReadBytes(checked((int)expected.StorageSize));

            if (!stored.AsSpan().SequenceEqual(expected.StoredData))
            {
                throw new InvalidDataException(
                    "generated TPAC segment payload mismatch");
            }
        }

        static long packageHeaderSizeForValidation(uint relativeDataStart)
        {
            return checked(36L + relativeDataStart);
        }
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

namespace InMemoryHLSSegmenter
{
    /// <summary>
    /// a.k.a. Access Unit
    /// </summary>
    class Sample
    {
        public Sample()
        {
        }
        public Sample(Sample sample)
        {
            Size = sample.Size;
            Offset = sample.Offset;
            CTS = sample.CTS;
            DTS = sample.DTS;
            Duration = sample.Duration;
            IsSync = sample.IsSync;
            DescriptionIndex = sample.DescriptionIndex;
        }

        public long Size { get; set; }
        public long Offset { get; set; }
        public long? CTS { get; set; }
        public long DTS { get; set; }
        public uint Duration { get; set; }
        public bool IsSync { get; set; }
        public int DescriptionIndex { get; set; }
    }

    class Box
    {
        public ulong Size { get; set; }
        public string Type { get; set; } = "";
    }
    class FullBox : Box
    {
        public byte Version { get; set; }
        public uint Flags { get; set; }
    }
    class FileTypeBox : Box
    {
        public string MajorBrand { get; set; } = "";
        public uint MajorVersion { get; set; }
        public IReadOnlyList<string> CompatibleBrands { get; set; } = Array.Empty<string>();
    }
    class BoxContainer : Box
    {
        public IReadOnlyList<Box> Boxes { get; set; } = Array.Empty<Box>();
    }
    class MovieHeaderBox : FullBox
    {
        public ulong CreationTime { get; set; }
        public ulong ModificationTime { get; set; }
        public uint Timescale { get; set; }
        public ulong Duration { get; set; }
        public int Rate { get; set; }
        public short Volume { get; set; }
        public int[] Matrix { get; set; } = Array.Empty<int>();
        public uint NextTrackId { get; set; }
    }
    class TrackHeaderBox : FullBox
    {
        public const uint TrackEnabledFlag = 0x000001;
        public const uint TrackInMovieFlag = 0x000002;
        public const uint TrackInPreviewFlag = 0x000004;
        public const uint TrackSizeIsAspectRatioFlag = 0x000008;
        public ulong CreationTime { get; set; }
        public ulong ModificationTime { get; set; }
        public uint TrackId { get; set; }
        public ulong Duration { get; set; }
        public short Layer{ get; set; }
        public short AlternateGroup { get; set; }
        public short Volume { get; set; }
        public int[] Matrix { get; set; } = Array.Empty<int>();
        public uint Width { get; set; }
        public uint Height { get; set; }
    }
    record EditListEntry(ulong SegmentDuration, long MediaTime, short MediaRateInteger, short MediaRateFraction);
    class EditListBox : FullBox
    {
        public IReadOnlyList<EditListEntry> Entries = Array.Empty<EditListEntry>();
    }
    class MediaHeaderBox : FullBox
    {
        public ulong CreationTime { get; set; }
        public ulong ModificationTime { get; set; }
        public uint Timescale { get; set; }
        public ulong Duration { get; set; }
        public ushort Language { get; set; }
    }
    class HandlerBox : FullBox
    {
        public string HandlerType { get; set; } = "";
        public string Name { get; set; } = "";
    }
    class SampleEntry : Box
    {
        public ushort DataReferenceIndex { get; set; }
        public byte[] Bytes { get; set; } = Array.Empty<byte>();
    }
    class SampleDescriptionBox : FullBox
    {
        public IReadOnlyList<SampleEntry> SampleEntries { get; set; } = Array.Empty<SampleEntry>();
    }
    record SampleToChunkEntry(uint FirstChunk, uint SamplesPerChunk, uint SampleDescriptionIndex);
    class SampleToChunkBox : FullBox
    {
        public IReadOnlyList<SampleToChunkEntry> Entries { get; set; } = Array.Empty<SampleToChunkEntry>();
    }
    // stsz, stz2
    class SampleSizeBox : FullBox
    {
        public uint SampleSize { get; set; }
        public IReadOnlyList<uint> Entries { get; set; } = Array.Empty<uint>();
    }
    class ChunkOffsetBox : FullBox
    {
        public IReadOnlyList<ulong> ChunkOffsets { get; set; } = Array.Empty<ulong>();
    }
    record TimeToSampleEntry(uint SampleCount, uint SampleDelta);
    // Decoding Time to Sample Box 
    class TimeToSampleBox : FullBox
    {
        public IReadOnlyList<TimeToSampleEntry> Entries { get; set; } = Array.Empty<TimeToSampleEntry>();
    }
    // Version == 0: unsigned
    // Version == 1: signed
    record CompositionOffsetEntry(uint SampleCount, long SampleOffset);
    class CompositionOffsetBox : FullBox
    {
        public IReadOnlyList<CompositionOffsetEntry> Entries { get; set; } = Array.Empty<CompositionOffsetEntry>();
    }
    class SyncSampleBox : FullBox
    {
        public IReadOnlyList<uint> SampleNumbers { get; set; } = Array.Empty<uint>();
    }
    class CompositionToDecodeBox : FullBox
    {
        public long CompositionToDTSShift { get; set; }
        public long LeastDecodeToDisplayDelta { get; set; }
        public long GreatestDecodeToDisplayDelta { get; set; }
        public long CompositionStartTime { get; set; }
        public long CompositionEndTime { get; set; }
    }
    class ESDBox : FullBox
    {
        public ElementaryStreamDescriptor ElementaryStream { get; set; }

        public ESDBox(ElementaryStreamDescriptor elementaryStream)
        {
            ElementaryStream = elementaryStream;
        }
    }
    class VisualSampleEntry : SampleEntry
    {
        public ushort Width { get; set; }
        public ushort Height { get; set; }
        public uint HorizResolution { get; set; }
        public uint VertResolution { get; set; }
        public ushort FrameCount { get; set; }
        public byte[] CompressorName { get; set; } = Array.Empty<byte>();
        public ushort Depth { get; set; }
        public IReadOnlyList<Box> Boxes { get; set; } = Array.Empty<Box>();
    }
    record AVCDecoderConfigurationRecord(
        byte ConfigurationVersion,
        byte AVCProfileIndication,
        byte ProfileCompatibility,
        byte AVCLevelIndication,
        byte LengthSizeMinusOne,
        byte[][] SPSNALUnits,
        byte[][] PPSNALUnits
    );
    class AVCConfigurationBox : Box
    {
        public AVCDecoderConfigurationRecord AVCConfig { get; set; }

        public AVCConfigurationBox(AVCDecoderConfigurationRecord avcConfig)
        {
            AVCConfig = avcConfig;
        }
    }
    class AudioSampleEntry : SampleEntry
    {
        public ushort ChannelCount { get; set; }
        public ushort SampleSize { get; set; }
        public uint SampleRate { get; set; }
        public IReadOnlyList<Box> Boxes { get; set; } = Array.Empty<Box>();
    }
}

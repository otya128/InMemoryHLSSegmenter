namespace InMemoryHLSSegmenter
{
    class ElementaryStreamDescriptor
    {
        public ElementaryStreamDescriptor(DecoderConfigDescriptor decDesc)
        {
            DecoderConfigDescriptor = decDesc;
        }

        public ushort ElementaryStreamId { get; set; }
        public byte StreamPriority { get; set; }
        public ushort? DependsOnElementaryStreamId { get; set; }
        public byte[]? UrlString { get; set; }
        public ushort? OCRElementaryStreamId { get; set; }
        public DecoderConfigDescriptor DecoderConfigDescriptor { get; set; }
    }
    class DecoderConfigDescriptor
    {
        public byte ObjectTypeIndication { get; set; }
        public byte StreamType { get; set; }
        public bool UpStream { get; set; }
        public uint BufferSizeDB { get; set; }
        public uint MaxBitrate { get; set; }
        public uint AvgBitrate { get; set; }
        public DecoderSpecificInfo? DecoderSpecificInfo { get; set; }
    }
    class DecoderSpecificInfo
    {

    }
    /// <summary>
    /// ISO/IEC 14496-1 MPEG-4 Systems
    /// </summary>
    class MPEG4
    {
        // ISO/IEC 14496-1 Expandable classes
        static int ReadExpandableLength(BigBinaryReader br)
        {
            var b = br.ReadByte();
            var nextByte = (b & 0x80) != 0;
            int sizeOfInstance = b & 0x7f;
            while (nextByte)
            {
                b = br.ReadByte();
                nextByte = (b & 0x80) != 0;
                var sizeByte = b & 0x7f;
                sizeOfInstance = (sizeOfInstance << 7) | sizeByte;
            }
            return sizeOfInstance;
        }
        // List of Class Tags for Descriptors
        const byte ES_DescrTag = 0x03;
        const byte DecoderConfigDescrTag = 0x04;
        const byte DecSpecificInfoTag = 0x05;
        public static ElementaryStreamDescriptor? ParseElementaryStreamDescriptor(BigBinaryReader br)
        {
            // ISO/IEC 14496-1 ES_Descriptor
            if (br.ReadByte() != ES_DescrTag)
            {
                return null;
            }
            var decDesc = new DecoderConfigDescriptor();
            var esDesc = new ElementaryStreamDescriptor(decDesc);
            ReadExpandableLength(br);
            esDesc.ElementaryStreamId = br.ReadUInt16();
            var f = br.ReadByte();
            var streamDependenceFlag = (f & 0x80) != 0;
            var urlFlag = (f & 0x40) != 0;
            var ocrStreamFlag = (f & 0x20) != 0;
            esDesc.StreamPriority = (byte)(f & 0x1f);
            if (streamDependenceFlag)
            {
                esDesc.DependsOnElementaryStreamId = br.ReadUInt16();
            }
            if (urlFlag)
            {
                var urlLength = br.ReadByte();
                esDesc.UrlString = br.ReadBytes(urlLength);
            }
            if (ocrStreamFlag)
            {
                esDesc.OCRElementaryStreamId = br.ReadUInt16();
            }
            if (br.ReadByte() != DecoderConfigDescrTag)
            {
                return null;
            }
            ReadExpandableLength(br);
            // ISO/IEC 14496-1 DecoderConfigDescriptor
            var objectTypeIndication = br.ReadByte();
            var b = br.ReadByte();
            decDesc.StreamType = (byte)(b >> 2);
            decDesc.UpStream = (b & 2) != 1;
            decDesc.BufferSizeDB = (uint)((br.ReadByte() << 16) | br.ReadUInt16());
            decDesc.MaxBitrate = br.ReadUInt32();
            decDesc.AvgBitrate = br.ReadUInt32();
            // ISO/IEC 14496-1 Table objectTypeIndication Values
            if (objectTypeIndication == 0x40)
            {
                if (br.ReadByte() != DecSpecificInfoTag)
                {
                    return null;
                }
                var audioSpecificConfigLength = ReadExpandableLength(br);
                var audioSpecificConfig = br.ReadBytes(audioSpecificConfigLength);
                var bitReader = new BitReader(audioSpecificConfig);
                // ISO/IEC 14496-3 AudioSpecificConfig
                var audioObjectType = bitReader.ReadBitsByte(5);
                if (audioObjectType == 31)
                {
                    audioObjectType = (byte)(32 + bitReader.ReadBitsByte(6));
                }
                uint? samplingFrequency = null;
                var samplingFrequencyIndex = bitReader.ReadBitsByte(4);
                if (samplingFrequencyIndex == 0xf)
                {
                    samplingFrequency = bitReader.ReadBitsUInt32(24);
                }
                var channelConfiguration = bitReader.ReadBitsByte(4);
                if (audioObjectType == 5 || audioObjectType == 29)
                {
                    var extensionSamplingFrequencyIndex = bitReader.ReadBitsByte(4);
                    if (extensionSamplingFrequencyIndex == 0xf)
                    {
                        var extensionSamplingFrequency = bitReader.ReadBitsUInt32(24);
                        audioObjectType = bitReader.ReadBitsByte(5);
                        if (audioObjectType == 31)
                        {
                            audioObjectType = (byte)(32 + bitReader.ReadBitsByte(6));
                        }
                        if (audioObjectType == 22)
                        {
                            // var extensionChannelConfiguration = bitReader.ReadBitsUInt32(4);
                        }
                    }
                }
                decDesc.DecoderSpecificInfo = new AudioSpecificConfig
                {
                    AudioObjectType = audioObjectType,
                    SamplingFrequencyIndex = samplingFrequencyIndex,
                    SamplingFrequency = samplingFrequency,
                    ChannelConfiguration = channelConfiguration,
                };
            }
            // ...
            return esDesc;
        }
    }
}

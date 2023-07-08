namespace InMemoryHLSSegmenter
{
    static class ADTS
    {
        public static void WriteSample(BinaryReader br, BigBinaryWriter aacWriter, Sample sample, AudioSpecificConfig config)
        {
            br.BaseStream.Position = sample.Offset;
            int size = checked((int)sample.Size);
            var l = br.ReadBytes(size);
            // syncword: 0xfff
            // ID: 0
            // layer: 0b00
            // protection_absent: 1
            aacWriter.Write((ushort)0b111111111111_0_00_1);
            var aac_frame_length = size + 7;
            // profile_ObjectType: 0bxx
            // sampling_frequency_index: 0bxxxx
            // private_bit: 0
            // channel_configuration: 0bxxx
            // original_copy: 0
            // home: 0
            // copyright_identification_bit: 0
            // copyright_identification_start: 0
            // aac_frame_length
            // adts_buffer_fullness: 0b11111111111
            // number_of_raw_data_blocks_in_frame: 0b00
            var channel_configuration = config.ChannelConfiguration;
            byte samplingFrequencyIndex = config.SamplingFrequencyIndex;
            switch (config.SamplingFrequency)
            {
                // 0xd reserved
                // 0xe reserved
                // 0xf escape value
                case 96000: samplingFrequencyIndex = 0x0; break;
                case 88200: samplingFrequencyIndex = 0x1; break;
                case 64000: samplingFrequencyIndex = 0x2; break;
                case 48000: samplingFrequencyIndex = 0x3; break;
                case 44100: samplingFrequencyIndex = 0x4; break;
                case 32000: samplingFrequencyIndex = 0x5; break;
                case 24000: samplingFrequencyIndex = 0x6; break;
                case 22050: samplingFrequencyIndex = 0x7; break;
                case 16000: samplingFrequencyIndex = 0x8; break;
                case 12000: samplingFrequencyIndex = 0x9; break;
                case 11025: samplingFrequencyIndex = 0xa; break;
                case 8000: samplingFrequencyIndex = 0xb; break;
                case 7350: samplingFrequencyIndex = 0xc; break;
            }
            var profile_ObjectType = config.AudioObjectType - 1; // 0=AAC Main, 1=AAC LC, 2=AAC SSR, 3=AAC LTP
            aacWriter.Write((ushort)(0b00_0000_0_000_0_0_0_0_00 | (aac_frame_length >> 11) | (channel_configuration << 6) | (samplingFrequencyIndex << 10) | (profile_ObjectType << 14)));
            aacWriter.Write((byte)(aac_frame_length >> 3));
            var adts_buffer_fullness = 0b11111111111;
            var number_of_raw_data_blocks_in_frame = 0;
            aacWriter.Write((byte)(((aac_frame_length & 0b00_00000000_111) << 5) | (adts_buffer_fullness >> 6)));
            aacWriter.Write((byte)((adts_buffer_fullness << 2) | number_of_raw_data_blocks_in_frame));
            aacWriter.Write(l);
        }
    }
    class AudioSpecificConfig : DecoderSpecificInfo
    {
        public byte AudioObjectType { get; set; }
        public byte SamplingFrequencyIndex { get; set; }
        public uint? SamplingFrequency { get; set; }
        public byte ChannelConfiguration { get; set; }
    }
}

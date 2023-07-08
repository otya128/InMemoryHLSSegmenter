namespace InMemoryHLSSegmenter
{
    static class H264
    {
        public static void WriteSample(BigBinaryReader br, BigBinaryWriter bw, Sample sample, AVCDecoderConfigurationRecord avcC)
        {
            WriteSample(br, bw, sample, avcC, sample.IsSync, sample.IsSync);
        }
        public static void WriteSample(BigBinaryReader br, BigBinaryWriter bw, Sample sample, AVCDecoderConfigurationRecord avcC, bool sps, bool pps)
        {
            br.BaseStream.Position = sample.Offset;
            int size = checked((int)sample.Size);
            var buffer = br.ReadBytes(size);
            int pos = 0;
            // Safari requires Access unit delimiter
            if (sps)
            {
                bw.Write(0x00000001);
                // Access unit delimiter
                bw.Write((byte)0x09);
                var primaryPicType = 0; // I
                var rbspStopOneBit = 0b000_1_0000; // +rbsp_alignment_zero_bit
                bw.Write((byte)((primaryPicType << 5) | rbspStopOneBit));
                foreach (var item in avcC.SPSNALUnits)
                {
                    bw.Write(0x00000001);
                    bw.Write(item);
                }
            }
            if (pps)
            {
                foreach (var item in avcC.PPSNALUnits)
                {
                    bw.Write(0x00000001);
                    bw.Write(item);
                }
            }
            else
            {
                bw.Write(0x00000001);
                bw.Write((byte)0x09);
                var primaryPicType = 1; // I, P
                var rbspStopOneBit = 0b000_1_0000; // +rbsp_alignment_zero_bit
                bw.Write((byte)((primaryPicType << 5) | rbspStopOneBit));
            }
            while (size > 0)
            {
                uint length = 0;
                for (int i = 0; i <= avcC.LengthSizeMinusOne; i++)
                {
                    length <<= 8;
                    length |= buffer[pos];
                    pos++;
                }
                size -= avcC.LengthSizeMinusOne;
                size -= 1;
                size -= (int)length;
                bw.Write(0x00000001);
                if (buffer[0] == 0x09)
                {
                    throw new Exception();
                }
                bw.Write(buffer, pos, (int)length);
                pos += (int)length;
            }
        }
    }
}

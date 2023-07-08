namespace InMemoryHLSSegmenter
{
    class BitReader
    {
        readonly byte[] data;
        long offset = 0;
        public BitReader(byte[] data)
        {
            this.data = data;
        }
        public byte ReadBitsByte(int count)
        {
            return (byte)ReadBitsUInt64(count);
        }
        public ushort ReadBitsUInt16(int count)
        {
            return (ushort)ReadBitsUInt64(count);
        }
        public uint ReadBitsUInt32(int count)
        {
            return (uint)ReadBitsUInt64(count);
        }
        public ulong ReadBitsUInt64(int count)
        {
            ulong result = 0;
            for (int i = 0; i < count; i++)
            {
                result <<= 1;
                var b = (data[offset / 8] >> (int)(7 - offset % 8)) & 1;
                result |= (uint)b;
                offset++;
            }
            return result;
        }
    }
}

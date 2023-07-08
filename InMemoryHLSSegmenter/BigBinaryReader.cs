using System.Buffers.Binary;
using System.Text;

namespace InMemoryHLSSegmenter
{
    public class BigBinaryReader : BinaryReader
    {
        public BigBinaryReader(Stream input) : base(input)
        {
        }

        public BigBinaryReader(Stream input, Encoding encoding) : base(input, encoding)
        {
        }

        public BigBinaryReader(Stream input, Encoding encoding, bool leaveOpen) : base(input, encoding, leaveOpen)
        {
        }

        public override ushort ReadUInt16()
        {
            return !BitConverter.IsLittleEndian ? base.ReadUInt16() : BinaryPrimitives.ReverseEndianness(base.ReadUInt16());
        }
        public override short ReadInt16()
        {
            return !BitConverter.IsLittleEndian ? base.ReadInt16() : BinaryPrimitives.ReverseEndianness(base.ReadInt16());
        }
        public override uint ReadUInt32()
        {
            return !BitConverter.IsLittleEndian ? base.ReadUInt32() : BinaryPrimitives.ReverseEndianness(base.ReadUInt32());
        }
        public override int ReadInt32()
        {
            return !BitConverter.IsLittleEndian ? base.ReadInt32() : BinaryPrimitives.ReverseEndianness(base.ReadInt32());
        }
        public override ulong ReadUInt64()
        {
            return !BitConverter.IsLittleEndian ? base.ReadUInt64() : BinaryPrimitives.ReverseEndianness(base.ReadUInt64());
        }
        public override long ReadInt64()
        {
            return !BitConverter.IsLittleEndian ? base.ReadInt64() : BinaryPrimitives.ReverseEndianness(base.ReadInt64());
        }
    }
}

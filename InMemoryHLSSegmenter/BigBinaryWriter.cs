using System.Buffers.Binary;
using System.Text;

namespace InMemoryHLSSegmenter
{
    public class BigBinaryWriter : BinaryWriter
    {
        public BigBinaryWriter(Stream output) : base(output)
        {
        }

        public BigBinaryWriter(Stream output, Encoding encoding) : base(output, encoding)
        {
        }

        public BigBinaryWriter(Stream output, Encoding encoding, bool leaveOpen) : base(output, encoding, leaveOpen)
        {
        }

        protected BigBinaryWriter()
        {
        }

        public override void Write(ushort value)
        {
            base.Write(!BitConverter.IsLittleEndian ? value : BinaryPrimitives.ReverseEndianness(value));
        }

        public override void Write(short value)
        {
            base.Write(!BitConverter.IsLittleEndian ? value : BinaryPrimitives.ReverseEndianness(value));
        }

        public override void Write(uint value)
        {
            base.Write(!BitConverter.IsLittleEndian ? value : BinaryPrimitives.ReverseEndianness(value));
        }

        public override void Write(int value)
        {
            base.Write(!BitConverter.IsLittleEndian ? value : BinaryPrimitives.ReverseEndianness(value));
        }

        public override void Write(ulong value)
        {
            base.Write(!BitConverter.IsLittleEndian ? value : BinaryPrimitives.ReverseEndianness(value));
        }

        public override void Write(long value)
        {
            base.Write(!BitConverter.IsLittleEndian ? value : BinaryPrimitives.ReverseEndianness(value));
        }
    }
}

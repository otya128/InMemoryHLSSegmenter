namespace InMemoryHLSSegmenter
{
    static class MPEG2TransportStreamId
    {
        /// <summary>
        /// ISO/IEC 13818-3 or ISO/IEC 11172-3 or ISO/IEC 13818-7 or ISO/IEC 14496-3 or ISO / IEC 23008 - 3 audio stream number 'x xxxx'
        /// </summary>
        public const byte Audio = 0b110_00000;
        /// <summary>
        /// H.262, H.264 or H.265 video stream number 'xxxx'
        /// </summary>
        public const byte Video = 0b1110_0000;
    }
    static class MPEG2TransportStreamType
    {
        public const byte AAC = 0x0f;
        public const byte AVC = 0x1b;
    }
    class MPEG2TransportStreamMuxer
    {
        const long Timescale = 90000; // 90 kHz
        const long Timescale27M = 90000 * 300;
        const int PCRBit = 33;
        const long PCRMask = (1L << PCRBit) - 1;
        const int PacketSize = 188;
        const int PacketPayloadSize = 184;
        const byte SyncByte = 0x47;
        readonly BigBinaryWriter tsWriter = null!;
        readonly byte[] patPacket = new byte[PacketSize];
        readonly byte[] pmtPacket = new byte[PacketSize];
        readonly ElementaryStream[] esList;
        readonly IDictionary<ElementaryStream, Sample?> prevSamples;
        bool patTransmitted = false;
        bool pcrTransmitted = false;
        long previousPCR;
        readonly ElementaryStream pcrES;
        readonly long PCRInterval = Timescale / 10;
        public MPEG2TransportStreamMuxer(Stream output, IList<ElementaryStream> esList, long initialTimeNum, long initialTimeDen)
        {
            tsWriter = new BigBinaryWriter(output);
            this.esList = esList.ToArray();
            prevSamples = esList.ToDictionary(x => x, x => (Sample?)null);
            pcrES = this.esList[0];
            previousPCR = initialTimeNum * Timescale / initialTimeDen;
        }
        static ReadOnlySpan<byte> SampleToPES(ElementaryStream es, Sample? prevSample, Sample sample)
        {
            var pes = new MemoryStream();
            var writer = new BigBinaryWriter(pes);
            // packet_start_code_prefix
            writer.Write((byte)0x00);
            writer.Write((byte)0x00);
            writer.Write((byte)0x01);
            var dts = sample.DTS * Timescale / es.Timescale;
            var dts32_30 = (dts >> 30) & 0b111;
            var dts29_15 = (dts >> 15) & 0x7fff;
            var dts14_0 = dts & 0x7fff;
            writer.Write(es.StreamId);
            var packetLengthIndex = (int)pes.Position;
            writer.Write((ushort)0); // PES_packet_length
            var payloadBegin = (int)pes.Position;
            if (sample.CTS is not long cts)
            {
                writer.Write((byte)0b10_00_0_0_0_0);
                writer.Write((byte)0b10_00_0_0_0_0); // PTS_DTS_flags=0b10 PTS
                writer.Write((byte)0x05); // PES_header_data_length
                writer.Write((byte)(0b0010_000_1 | (dts32_30 << 1)));
                writer.Write((ushort)((dts29_15 << 1) | 1));
                writer.Write((ushort)((dts14_0 << 1) | 1));
            }
            else
            {
                var pts = cts * Timescale / es.Timescale;
                var pts32_30 = (pts >> 30) & 0b111;
                var pts29_15 = (pts >> 15) & 0x7fff;
                var pts14_0 = pts & 0x7fff;
                writer.Write((byte)0b10_00_0_0_0_0);
                writer.Write((byte)0b11_00_0_0_0_0); // PTS_DTS_flags=0b11 PTS+DTS
                writer.Write((byte)0x0a); // PES_header_data_length
                writer.Write((byte)(0b0011_000_1 | (pts32_30 << 1)));
                writer.Write((ushort)((pts29_15 << 1) | 1));
                writer.Write((ushort)((pts14_0 << 1) | 1));
                writer.Write((byte)(0b0001_000_1 | (dts32_30 << 1)));
                writer.Write((ushort)((dts29_15 << 1) | 1));
                writer.Write((ushort)((dts14_0 << 1) | 1));
            }
            es.Write!(prevSample, sample, writer);
            var payloadEnd = (int)pes.Position;
            var packetLength = payloadEnd - payloadBegin;
            if (packetLength <= 0xffff)
            {
                pes.Position = packetLengthIndex;
                writer.Write((ushort)packetLength); // PES_packet_length
            }
            return pes.GetBuffer().AsSpan(..(int)pes.Length);
        }
        public void Transmit(ElementaryStream es, Sample sample)
        {
            TransmitPES(es, SampleToPES(es, prevSamples[es], sample), sample.DTS);
            prevSamples[es] = sample;
        }
        void TransmitPES(ElementaryStream es, ReadOnlySpan<byte> pes, long dts)
        {
            // RFC 8216 HLS
            // The first two Transport Stream packets in a Segment without an EXT-X-MAP tag SHOULD be a PAT and a PMT.
            if (!patTransmitted)
            {
                PreparePAT();
                tsWriter.Write(patPacket);
                PreparePMT();
                tsWriter.Write(pmtPacket);
                patTransmitted = true;
            }
            var pcr = (dts * Timescale / es.Timescale) & PCRMask;
            var pcrExtension = (dts * Timescale27M / es.Timescale) % 300;
            bool sendPCR = false;
            if (es == pcrES && (!pcrTransmitted || ((pcr - previousPCR) & PCRMask) > PCRInterval))
            {
                previousPCR = pcr;
                sendPCR = true;
                pcrTransmitted = true;
            }
            var remainBytes = pes.Length;
            var offset = 0;
            while (remainBytes > 0)
            {
                ReadOnlySpan<byte> payload;
                tsWriter.Write(SyncByte); // sync_byte
                tsWriter.Write((ushort)((offset == 0 ? 0x4000 : 0) | es.PID)); // payload_unit_start_indicator=1, PID=PMT=0x1000
                /*
                 * adaptation_field_control=0b00: reserved
                 * adaptation_field_control=0b01: No adaptation_field, payload only
                 * adaptation_field_control=0b10: Adaptation_field only, no payload
                 * adaptation_field_control=0b11: Adaptation_field followed by payload
                 */
                var packetPayloadSize = PacketPayloadSize;
                if (sendPCR)
                {
                    packetPayloadSize -= 8;
                }
                if (remainBytes >= packetPayloadSize)
                {
                    if (sendPCR)
                    {
                        tsWriter.Write((byte)(0x30 | es.ContinuityCounter));
                        tsWriter.Write((byte)7); // adaptation_field_length
                        var randomAccessIndicator = false;
                        tsWriter.Write((byte)(0b00010000 | (randomAccessIndicator ? 0x40 : 0))); // random_access_indicator=1 PCR_flag=1
                        tsWriter.Write((uint)(pcr >> 1));
                        tsWriter.Write((ushort)(((uint)pcr & 1) << 15 | 0b0_111111_000000000 | pcrExtension));
                    }
                    else
                    {
                        tsWriter.Write((byte)(0x10 | es.ContinuityCounter));
                    }
                    payload = pes[offset..(offset + packetPayloadSize)];
                }
                else
                {
                    tsWriter.Write((byte)(0x30 | es.ContinuityCounter));
                    payload = pes[offset..];
                    if (sendPCR)
                    {
                        var adaptationFieldLength = 7 + packetPayloadSize - payload.Length;
                        tsWriter.Write((byte)adaptationFieldLength ); // adaptation_field_length
                        tsWriter.Write((byte)0b01010000); // random_access_indicator=1 PCR_flag=1
                        tsWriter.Write((uint)(pcr >> 1));
                        tsWriter.Write((ushort)(((uint)pcr & 1) << 15 | 0b0_111111_000000000 | pcrExtension));
                        var stuffBytes = new byte[packetPayloadSize - payload.Length];
                        Array.Fill<byte>(stuffBytes, 0xff);
                        tsWriter.Write(stuffBytes);
                    }
                    else
                    {
                        var adaptationFieldLength = packetPayloadSize - payload.Length - 1;
                        tsWriter.Write((byte)adaptationFieldLength);
                        var stuffBytes = new byte[adaptationFieldLength];
                        Array.Fill<byte>(stuffBytes, 0xff);
                        if (stuffBytes.Length > 0)
                        {
                            stuffBytes[0] = 0b00000000;
                        }
                        tsWriter.Write(stuffBytes);
                    }
                }
                tsWriter.Write(payload);
                offset += payload.Length;
                remainBytes -= payload.Length;
                es.ContinuityCounter = (byte)((es.ContinuityCounter + 1) & 15);
                sendPCR = false;
            }
        }
        void PreparePAT()
        {
            var ms = new MemoryStream(patPacket);
            var writer = new BigBinaryWriter(ms);
            writer.Write(SyncByte); // sync_byte
            writer.Write((ushort)0x4000); // payload_unit_start_indicator=1, PID=PAT
            writer.Write((byte)0x10); // adaptation_field_control=0b01, continuity_counter=0
            writer.Write((byte)0x00); // pointer_field
            var sectionBegin = (int)ms.Position;
            writer.Write((byte)0x00); // table_id=0
            writer.Write((ushort)(0xb000 | 0x00d)); // section_syntax_indicator=1, reserved=0b11, section_length=0x00d
            writer.Write((ushort)1); // transport_stream_id
            writer.Write((byte)0xc1); // reserved=0b11 version_number=0 current_next_indicator=1
            writer.Write((byte)0x00); // section_number=0
            writer.Write((byte)0x00); // last_section_number=0
            writer.Write((ushort)1); // program_number=1
            writer.Write((ushort)(0b111_0000000000000 | 0x1000)); // reserved=0b111, PMT PID=0x1000
            var sectionEnd = (int)ms.Position;
            var crc = CRC32(patPacket.AsSpan(sectionBegin..sectionEnd), 0xffffffff);
            writer.Write(crc); // CRC_32
            Array.Fill<byte>(patPacket, 0xff, (int)ms.Position, (int)(PacketSize - ms.Position));
        }
        void PreparePMT()
        {
            var ms = new MemoryStream(pmtPacket);
            var writer = new BigBinaryWriter(ms);
            writer.Write(SyncByte); // sync_byte
            writer.Write((ushort)0x5000); // payload_unit_start_indicator=1, PID=PMT=0x1000
            writer.Write((byte)0x10); // adaptation_field_control=0b01, continuity_counter=0
            writer.Write((byte)0x00); // pointer_field
            var sectionBegin = (int)ms.Position;
            writer.Write((byte)0x02); // table_id=0x02
            writer.Write((ushort)(0xb000 | (0x00d + 5 * esList.Length))); // section_syntax_indicator=1, reserved=0b11, section_length
            writer.Write((ushort)1); // program_number
            writer.Write((byte)0xc1); // reserved=0b11 version_number=0 current_next_indicator=1
            writer.Write((byte)0x00); // section_number=0
            writer.Write((byte)0x00); // last_section_number=0
            writer.Write((ushort)(0xE000 | pcrES.PID)); // reserved=0b111, PCR_PID
            writer.Write((ushort)0xF000); // reserved=0b1111, program_info_length=0
            foreach (var es in esList)
            {
                writer.Write(es.StreamType); // stream_type avc
                writer.Write((ushort)(0xe000 | es.PID)); // reserved=0b111, elementary_PID=0x100
                writer.Write((ushort)0xf000); // reserved=0b111, ES_info_length=0
            }
            var sectionEnd = (int)ms.Position;
            var crc = CRC32(pmtPacket.AsSpan(sectionBegin..sectionEnd), 0xffffffff);
            writer.Write(crc); // CRC_32
            Array.Fill<byte>(pmtPacket, 0xff, (int)ms.Position, (int)(PacketSize - ms.Position));
        }
        static uint CRC32(Span<byte> data, uint crc)
        {
            uint poly = (1 << 26) | (1 << 23) | (1 << 22) | (1 << 16) | (1 << 12) | (1 << 11) | (1 << 10) | (1 << 8) | (1 << 7) | (1 << 5) | (1 << 4) | (1 << 2) | (1 << 1) | (1 << 0);
            foreach (var b in data)
            {
                for (int i = 0; i < 8; i++)
                {
                    var v = ((crc & 0x80000000) ^ ((b & (1 << (7 - i))) != 0 ? 0x80000000 : 0)) != 0;
                    crc <<= 1;
                    if (v)
                    {
                        crc ^= poly;
                    }
                }
            }
            return crc;
        }
    }
    record ElementaryStream(Action<Sample?, Sample, BigBinaryWriter> Write)
    {
        public ushort PID { get; set; }
        public byte StreamType { get; set; }
        public byte StreamId { get; set; }
        public long Timescale { get; set; }
        public IReadOnlyList<Sample> Samples { get; set; } = Array.Empty<Sample>();
        public Action<Sample?, Sample, BigBinaryWriter> Write { get; set; } = Write;
        public byte ContinuityCounter { get; set; }
    }
}

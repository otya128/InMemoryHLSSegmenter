using System.Text;

namespace InMemoryHLSSegmenter
{
    static class ISOBMFFParser
    {
        static (ulong, string) ReadBoxHeader(BigBinaryReader br)
        {
            ulong size = br.ReadUInt32();
            var type = Encoding.Latin1.GetString(br.ReadBytes(4));
            if (size == 1)
            {
                size = br.ReadUInt64();
            }
            return (size, type);
        }
        static (byte version, uint flags) ReadFullBox(BigBinaryReader br)
        {
            var version = br.ReadByte();
            var flags1 = br.ReadByte();
            var flags2 = br.ReadByte();
            var flags3 = br.ReadByte();
            return (version, (uint)((flags1 << 16) | (flags2 << 8) | flags3));
        }
        public static List<Box> ParseISOBMFF(BigBinaryReader br, long endOffset)
        {
            List<Box> boxes = new();
            while (br.BaseStream.Position + 4 <= endOffset)
            {
                var baseOffset = br.BaseStream.Position;
                var (size, type) = ReadBoxHeader(br);
                var boxEndOffset = size == 0 ? long.MaxValue : (long)size + baseOffset;
                switch (type)
                {
                    case "ftyp":
                        {
                            var majorBrand = Encoding.Latin1.GetString(br.ReadBytes(4));
                            var majorVersion = br.ReadUInt32();
                            List<string> compatibleBrands = new();
                            while (br.BaseStream.Position + 4 <= boxEndOffset)
                            {
                                compatibleBrands.Add(Encoding.Latin1.GetString(br.ReadBytes(4)));
                            }
                            boxes.Add(new FileTypeBox
                            {
                                Type = type,
                                Size = size,
                                MajorBrand = majorBrand,
                                MajorVersion = majorVersion,
                                CompatibleBrands = compatibleBrands
                            });
                            break;
                        }
                    case "moov":
                    case "trak":
                    case "edts":
                    case "mdia":
                    case "minf":
                    case "dinf":
                    case "stbl":
                    case "mvex":
                    case "moof":
                    case "traf":
                    case "mfra":
                    case "skip":
                    case "udta":
                    case "strk":
                    case "meta":
                    case "ipro":
                    case "sinf":
                    case "fiin":
                    case "paen":
                    case "meco":
                    case "mere":
                        {
                            boxes.Add(new BoxContainer
                            {
                                Size = size,
                                Type = type,
                                Boxes = ParseISOBMFF(br, boxEndOffset)
                            });
                            break;
                        }
                    case "mvhd":
                        {
                            var (version, flags) = ReadFullBox(br);
                            var mvhd = new MovieHeaderBox
                            {
                                Size = size,
                                Type = type,
                                Version = version,
                                Flags = flags,
                            };

                            if (version == 1)
                            {
                                mvhd.CreationTime = br.ReadUInt64();
                                mvhd.ModificationTime = br.ReadUInt64();
                                mvhd.Timescale = br.ReadUInt32();
                                mvhd.Duration = br.ReadUInt64();
                            }
                            else if (version == 0)
                            {
                                mvhd.CreationTime = br.ReadUInt32();
                                mvhd.ModificationTime = br.ReadUInt32();
                                mvhd.Timescale = br.ReadUInt32();
                                mvhd.Duration = br.ReadUInt32();
                            }
                            else
                            {
                                boxes.Add(new FullBox { Size = size, Type = type, Version = version, Flags = flags });
                                break;
                            }

                            mvhd.Rate = br.ReadInt32();
                            mvhd.Volume = br.ReadInt16();
                            br.ReadUInt16();
                            br.ReadUInt32();
                            br.ReadUInt32();
                            var matrix = new int[9];
                            for (int i = 0; i < 9; i++)
                            {
                                matrix[i] = br.ReadInt32();
                            }
                            mvhd.Matrix = matrix;
                            for (int i = 0; i < 6; i++)
                            {
                                br.ReadInt32();
                            }
                            mvhd.NextTrackId = br.ReadUInt32();
                            boxes.Add(mvhd);
                            break;
                        }
                    case "tkhd":
                        {
                            var (version, flags) = ReadFullBox(br);
                            var tkhd = new TrackHeaderBox
                            {
                                Size = size,
                                Type = type,
                                Version = version,
                                Flags = flags,
                            };

                            if (version == 1)
                            {
                                tkhd.CreationTime = br.ReadUInt64();
                                tkhd.ModificationTime = br.ReadUInt64();
                                tkhd.TrackId = br.ReadUInt32();
                                br.ReadUInt32();
                                tkhd.Duration = br.ReadUInt64();
                            }
                            else if (version == 0)
                            {
                                tkhd.CreationTime = br.ReadUInt32();
                                tkhd.ModificationTime = br.ReadUInt32();
                                tkhd.TrackId = br.ReadUInt32();
                                br.ReadUInt32();
                                tkhd.Duration = br.ReadUInt32();
                            }
                            else
                            {
                                boxes.Add(new FullBox { Size = size, Type = type, Version = version, Flags = flags });
                                break;
                            }

                            br.ReadUInt32();
                            br.ReadUInt32();
                            tkhd.Layer = br.ReadInt16();
                            tkhd.AlternateGroup = br.ReadInt16();
                            tkhd.Volume = br.ReadInt16();
                            br.ReadUInt16();
                            var matrix = new int[9];
                            for (int i = 0; i < 9; i++)
                            {
                                matrix[i] = br.ReadInt32();
                            }
                            tkhd.Matrix = matrix;
                            tkhd.Width = br.ReadUInt32();
                            tkhd.Height = br.ReadUInt32();
                            boxes.Add(tkhd);
                            break;
                        }
                    case "elst":
                        {
                            var entries = new List<EditListEntry>();
                            var (version, flags) = ReadFullBox(br);
                            if (version != 0 && version != 1)
                            {
                                boxes.Add(new FullBox { Size = size, Type = type, Version = version, Flags = flags });
                                break;
                            }
                            var entryCount = br.ReadUInt32();
                            for (uint i = 1; i <= entryCount; i++)
                            {
                                ulong segmentDuration;
                                long mediaTime;
                                short mediaRateInteger;
                                short mediaRateFraction;
                                if (version == 1)
                                {
                                    segmentDuration = br.ReadUInt64();
                                    mediaTime = br.ReadInt64();
                                }
                                else
                                {
                                    segmentDuration = br.ReadUInt32();
                                    mediaTime = br.ReadInt32();
                                }
                                mediaRateInteger = br.ReadInt16();
                                mediaRateFraction = br.ReadInt16();
                                entries.Add(new(segmentDuration, mediaTime, mediaRateInteger, mediaRateFraction));
                            }
                            boxes.Add(new EditListBox
                            {
                                Size = size,
                                Type = type,
                                Version = version,
                                Flags = flags,
                                Entries = entries,
                            });
                            break;
                        }
                    case "mdhd":
                        {
                            var (version, flags) = ReadFullBox(br);
                            if (version != 0 && version != 1)
                            {
                                boxes.Add(new FullBox { Size = size, Type = type, Version = version, Flags = flags });
                                break;
                            }
                            var mdhd = new MediaHeaderBox
                            {
                                Size = size,
                                Type = type,
                                Version = version,
                                Flags = flags,
                            };
                            if (version == 1)
                            {
                                mdhd.CreationTime = br.ReadUInt64();
                                mdhd.ModificationTime = br.ReadUInt64();
                                mdhd.Timescale = br.ReadUInt32();
                                mdhd.Duration = br.ReadUInt64();
                            }
                            else
                            {
                                mdhd.CreationTime = br.ReadUInt32();
                                mdhd.ModificationTime = br.ReadUInt32();
                                mdhd.Timescale = br.ReadUInt32();
                                mdhd.Duration = br.ReadUInt32();
                            }
                            mdhd.Language = (ushort)(br.ReadUInt16() & 0x7fff);
                            boxes.Add(mdhd);
                            break;
                        }
                    case "hdlr":
                        {
                            var (version, flags) = ReadFullBox(br);
                            if (version != 0)
                            {
                                boxes.Add(new FullBox { Size = size, Type = type, Version = version, Flags = flags });
                                break;
                            }
                            br.ReadUInt32();
                            var handlerType = Encoding.Latin1.GetString(br.ReadBytes(4));
                            br.ReadUInt32();
                            br.ReadUInt32();
                            br.ReadUInt32();
                            var name = Encoding.UTF8.GetString(br.ReadBytes(Math.Max(0, (int)(endOffset - br.BaseStream.Position))));
                            var term = name.IndexOf('\0');
                            if (term != -1)
                            {
                                name = name[..term];
                            }
                            boxes.Add(new HandlerBox
                            {
                                Size = size,
                                Type = type,
                                Version = version,
                                Flags = flags,
                                HandlerType = handlerType,
                                Name = name,
                            });
                            break;
                        }
                    case "stsd":
                        {
                            var (version, flags) = ReadFullBox(br);
                            if (version != 0)
                            {
                                boxes.Add(new FullBox { Size = size, Type = type, Version = version, Flags = flags });
                                break;
                            }
                            var entryCount = br.ReadUInt32();
                            var entries = new List<SampleEntry>();
                            for (uint i = 1; i <= entryCount; i++)
                            {
                                var baseDescBoxOffset = br.BaseStream.Position;
                                var (descBoxSize, codingName) = ReadBoxHeader(br);
                                br.ReadBytes(6);
                                var dataReferenceIndex = br.ReadUInt16();
                                entries.Add(new SampleEntry
                                {
                                    Size = descBoxSize,
                                    Type = codingName,
                                    DataReferenceIndex = dataReferenceIndex,
                                    Bytes = br.ReadBytes((int)(baseDescBoxOffset + (long)descBoxSize - br.BaseStream.Position)),
                                });
                                // minf->hdlrの順でも規格違反とまでは言えないらしくこの段階ではhandler_typeは未確定
                                // It is strongly *recommended* that the Track Reference Box and Edit List (if any) *should*
                                // precede the Media Box, and the Handler Reference Box *should* precede the Media Information Box,
                                // and the Data Information Box *should* precede the Sample Table Box.
                            }
                            boxes.Add(new SampleDescriptionBox
                            {
                                Size = size,
                                Type = type,
                                Version = version,
                                Flags = flags,
                                SampleEntries = entries,
                            });
                            break;
                        }
                    case "stsc":
                        {
                            var (version, flags) = ReadFullBox(br);
                            if (version != 0)
                            {
                                boxes.Add(new FullBox { Size = size, Type = type, Version = version, Flags = flags });
                                break;
                            }
                            var entryCount = br.ReadUInt32();
                            var entries = new List<SampleToChunkEntry>(checked((int)entryCount));
                            for (int i = 1; i <= entryCount; i++)
                            {
                                var firstChunk = br.ReadUInt32();
                                var samplesPerChunk = br.ReadUInt32();
                                var sampleDescriptionIndex = br.ReadUInt32();
                                entries.Add(new(firstChunk, samplesPerChunk, sampleDescriptionIndex));
                            }
                            boxes.Add(new SampleToChunkBox
                            {
                                Size = size,
                                Type = type,
                                Version = version,
                                Flags = flags,
                                Entries = entries,
                            });
                            break;
                        }
                    case "stsz":
                        {
                            var (version, flags) = ReadFullBox(br);
                            if (version != 0)
                            {
                                boxes.Add(new FullBox { Size = size, Type = type, Version = version, Flags = flags });
                                break;
                            }
                            var sampleSize = br.ReadUInt32();
                            var entryCount = br.ReadUInt32();
                            uint[] entries = new uint[checked((int)entryCount)];
                            if (sampleSize == 0)
                            {
                                for (int i = 1; i <= entryCount; i++)
                                {
                                    entries[i - 1] = br.ReadUInt32();
                                }
                            }
                            boxes.Add(new SampleSizeBox
                            {
                                Size = size,
                                Type = type,
                                Version = version,
                                Flags = flags,
                                Entries = entries,
                            });
                            break;
                        }
                    case "stz2":
                        {
                            var (version, flags) = ReadFullBox(br);
                            if (version != 0)
                            {
                                boxes.Add(new FullBox { Size = size, Type = type, Version = version, Flags = flags });
                                break;
                            }
                            var fieldSize = (byte)br.ReadUInt32();
                            var entryCount = br.ReadUInt32();
                            uint[] entries = new uint[checked((int)entryCount)];
                            if (fieldSize == 4)
                            {
                                for (int i = 1; i <= entryCount; i += 2)
                                {
                                    var e = br.ReadByte();
                                    entries[i - 1] = (byte)(e >> 4);
                                    if (entries.Length > i)
                                        entries[i] = (byte)(e & 15);
                                }
                            }
                            else if (fieldSize == 8)
                            {
                                for (int i = 1; i <= entryCount; i++)
                                {
                                    entries[i - 1] = br.ReadByte();
                                }
                            }
                            else if (fieldSize == 16)
                            {
                                for (int i = 1; i <= entryCount; i++)
                                {
                                    entries[i - 1] = br.ReadUInt16();
                                }
                            }
                            boxes.Add(new SampleSizeBox
                            {
                                Size = size,
                                Type = type,
                                Version = version,
                                Flags = flags,
                                Entries = entries,
                            });
                            break;
                        }
                    case "stco":
                    case "co64":
                        {
                            var (version, flags) = ReadFullBox(br);
                            if (version != 0)
                            {
                                boxes.Add(new FullBox { Size = size, Type = type, Version = version, Flags = flags });
                                break;
                            }
                            var entryCount = br.ReadUInt32();
                            var chunkOffsets = new ulong[checked((int)entryCount)];
                            if (type == "stco")
                            {
                                for (int i = 1; i <= entryCount; i++)
                                {
                                    chunkOffsets[i - 1] = br.ReadUInt32();
                                }
                            }
                            else
                            {
                                for (int i = 1; i <= entryCount; i++)
                                {
                                    chunkOffsets[i - 1] = br.ReadUInt64();
                                }
                            }
                            boxes.Add(new ChunkOffsetBox
                            {
                                Size = size,
                                Type = type,
                                Version = version,
                                Flags = flags,
                                ChunkOffsets = chunkOffsets,
                            });
                            break;
                        }
                    case "stts":
                        {
                            // Decoding Time to Sample Box
                            var (version, flags) = ReadFullBox(br);
                            var entryCount = br.ReadUInt32();
                            var entries = new List<TimeToSampleEntry>();
                            for (int i = 1; i <= entryCount; i++)
                            {
                                var sampleCount = br.ReadUInt32();
                                var sampleDelta = br.ReadUInt32();
                                entries.Add(new(sampleCount, sampleDelta));
                            }
                            boxes.Add(new TimeToSampleBox
                            {
                                Size = size,
                                Type = type,
                                Version = version,
                                Flags = flags,
                                Entries = entries,
                            });
                            break;
                        }
                    case "ctts":
                        {
                            // Composition Time to Sample Box
                            var (version, flags) = ReadFullBox(br);
                            if (version != 0)
                            {
                                boxes.Add(new FullBox { Size = size, Type = type, Version = version, Flags = flags });
                                break;
                            }
                            var entryCount = br.ReadUInt32();
                            var entries = new List<CompositionOffsetEntry>();
                            if (version != 1)
                            {
                                for (int i = 1; i <= entryCount; i++)
                                {
                                    var sampleCount = br.ReadUInt32();
                                    var sampleDelta = br.ReadUInt32();
                                    entries.Add(new(sampleCount, sampleDelta));
                                }
                            }
                            else
                            {
                                for (int i = 1; i <= entryCount; i++)
                                {
                                    var sampleCount = br.ReadUInt32();
                                    var sampleDelta = br.ReadInt32();
                                    entries.Add(new(sampleCount, sampleDelta));
                                }
                            }
                            boxes.Add(new CompositionOffsetBox
                            {
                                Size = size,
                                Type = type,
                                Version = version,
                                Flags = flags,
                                Entries = entries,
                            });
                            break;
                        }
                    case "stss":
                        {
                            // Sync Sample Box
                            var (version, flags) = ReadFullBox(br);
                            if (version != 0)
                            {
                                boxes.Add(new FullBox { Size = size, Type = type, Version = version, Flags = flags });
                                break;
                            }
                            var entryCount = br.ReadUInt32();
                            var entries = new uint[checked((int)entryCount)];
                            for (int i = 1; i <= entryCount; i++)
                            {
                                entries[i - 1] = br.ReadUInt32();
                            }
                            boxes.Add(new SyncSampleBox
                            {
                                Size = size,
                                Type = type,
                                Version = version,
                                Flags = flags,
                                SampleNumbers = entries,
                            });
                            break;
                        }
                    case "cslg":
                        {
                            // Composition to Decode Box
                            var (version, flags) = ReadFullBox(br);
                            if (version == 0)
                            {
                                var compositionToDTSShift = br.ReadInt32();
                                var leastDecodeToDisplayDelta = br.ReadInt32();
                                var greatestDecodeToDisplayDelta = br.ReadInt32();
                                var compositionStartTime = br.ReadInt32();
                                var compositionEndTime = br.ReadInt32();
                                boxes.Add(new CompositionToDecodeBox
                                {
                                    Size = size,
                                    Type = type,
                                    Version = version,
                                    Flags = flags,
                                    CompositionToDTSShift = compositionToDTSShift,
                                    LeastDecodeToDisplayDelta = leastDecodeToDisplayDelta,
                                    GreatestDecodeToDisplayDelta = greatestDecodeToDisplayDelta,
                                    CompositionStartTime = compositionStartTime,
                                    CompositionEndTime = compositionEndTime,
                                });
                            }
                            else if (version == 1)
                            {
                                var compositionToDTSShift = br.ReadInt64();
                                var leastDecodeToDisplayDelta = br.ReadInt64();
                                var greatestDecodeToDisplayDelta = br.ReadInt64();
                                var compositionStartTime = br.ReadInt64();
                                var compositionEndTime = br.ReadInt64();
                                boxes.Add(new CompositionToDecodeBox
                                {
                                    Size = size,
                                    Type = type,
                                    Version = version,
                                    Flags = flags,
                                    CompositionToDTSShift = compositionToDTSShift,
                                    LeastDecodeToDisplayDelta = leastDecodeToDisplayDelta,
                                    GreatestDecodeToDisplayDelta = greatestDecodeToDisplayDelta,
                                    CompositionStartTime = compositionStartTime,
                                    CompositionEndTime = compositionEndTime,
                                });
                            }
                            else
                            {
                                boxes.Add(new FullBox { Size = size, Type = type, Version = version, Flags = flags });
                            }
                            break;
                        }
                    case "esds":
                        {
                            // ESDBox
                            var (version, flags) = ReadFullBox(br);
                            var es = MPEG4.ParseElementaryStreamDescriptor(br);
                            if (version != 0 || es == null)
                            {
                                boxes.Add(new FullBox { Size = size, Type = type, Version = version, Flags = flags });
                                break;
                            }
                            boxes.Add(new ESDBox(es)
                            {
                                Size = size,
                                Type = type,
                                Version = version,
                                Flags = flags,
                            });
                            break;
                        }
                    case "avcC":
                        {
                            var configurationVersion = br.ReadByte();
                            var AVCProfileIndication = br.ReadByte(); // profile_idc
                            var profileCompatibility = br.ReadByte(); // constraint_set0_flag-reserved_zero_5bits
                            var AVCLevelIndication = br.ReadByte(); // level_idc
                            var lengthSizeMinusOne = (byte)(br.ReadByte() & 0b11);
                            var numOfSequenceParameterSets = br.ReadByte() & 0b11111;
                            var sps = new byte[numOfSequenceParameterSets][];
                            for (int j = 0; j < numOfSequenceParameterSets; j++)
                            {
                                var sequenceParameterSetLength = br.ReadUInt16();
                                sps[j] = br.ReadBytes(sequenceParameterSetLength);
                            }
                            var numOfPictureParameterSets = br.ReadByte();
                            var pps = new byte[numOfSequenceParameterSets][];
                            for (int j = 0; j < numOfPictureParameterSets; j++)
                            {
                                var pictureParameterSetLength = br.ReadUInt16();
                                pps[j] = br.ReadBytes(pictureParameterSetLength);
                            }
                            // AVCProfileIndication == 100...
                            // ...
                            boxes.Add(new AVCConfigurationBox(new(
                                configurationVersion,
                                AVCProfileIndication,
                                profileCompatibility,
                                AVCLevelIndication,
                                lengthSizeMinusOne,
                                sps,
                                pps
                            )));
                            break;
                        }
                    default:
                        boxes.Add(new Box { Type = type, Size = size });
                        break;
                }
                if (size == 0)
                    break;
                br.BaseStream.Position = boxEndOffset;
            }
            return boxes;
        }
        public static AudioSampleEntry ParseAudioSampleEntry(SampleEntry entry)
        {
            var ms = new MemoryStream(entry.Bytes);
            var br = new BigBinaryReader(ms);
            br.ReadUInt32();
            br.ReadUInt32();
            var channelCount = br.ReadUInt16();
            var sampleSize = br.ReadUInt16();
            br.ReadUInt16();
            br.ReadUInt16();
            var sampleRate = br.ReadUInt16();
            br.ReadUInt16();
            var boxes = ParseISOBMFF(br, br.BaseStream.Length);
            return new AudioSampleEntry
            {
                Size = entry.Size,
                Type = entry.Type,
                DataReferenceIndex = entry.DataReferenceIndex,
                Bytes = entry.Bytes,
                ChannelCount = channelCount,
                SampleSize = sampleSize,
                SampleRate = sampleRate,
                Boxes = boxes,
            };
        }
        public static VisualSampleEntry ParseVisualSampleEntry(SampleEntry entry)
        {
            var ms = new MemoryStream(entry.Bytes);
            var br = new BigBinaryReader(ms);
            br.ReadUInt16();
            br.ReadUInt16();
            br.ReadUInt32();
            br.ReadUInt32();
            br.ReadUInt32();
            var width = br.ReadUInt16();
            var height = br.ReadUInt16();
            var horizResolution = br.ReadUInt32();
            var vertResolution = br.ReadUInt32();
            br.ReadUInt32();
            var frameCount = br.ReadUInt16();
            var compressorName = br.ReadBytes(32);
            var depth = br.ReadUInt16();
            br.ReadUInt16();
            var boxes = ParseISOBMFF(br, br.BaseStream.Length);
            return new VisualSampleEntry
            {
                Size = entry.Size,
                Type = entry.Type,
                DataReferenceIndex = entry.DataReferenceIndex,
                Bytes = entry.Bytes,
                Width = width,
                Height = height,
                HorizResolution = horizResolution,
                VertResolution = vertResolution,
                FrameCount = frameCount,
                CompressorName = compressorName,
                Depth = depth,
                Boxes = boxes,
            };
        }
        /// <summary>
        /// stblに格納された圧縮表現のサンプル/チャンク情報を展開
        /// </summary>
        /// <param name="stbl"></param>
        /// <returns></returns>
        public static IReadOnlyList<Sample>? IndexSamples(BoxContainer stbl)
        {
            if (stbl.Boxes.FirstOrDefault(x => x is SampleToChunkBox) is not SampleToChunkBox stsc)
                return null;
            if (stbl.Boxes.FirstOrDefault(x => x is ChunkOffsetBox) is not ChunkOffsetBox co64)
                return null;
            if (stbl.Boxes.FirstOrDefault(x => x is TimeToSampleBox) is not TimeToSampleBox stts)
                return null;
            if (stbl.Boxes.FirstOrDefault(x => x is SampleSizeBox) is not SampleSizeBox stsz)
                return null;
            var ctts = stbl.Boxes.FirstOrDefault(x => x is CompositionOffsetBox) as CompositionOffsetBox;
            var stss = stbl.Boxes.FirstOrDefault(x => x is SyncSampleBox) as SyncSampleBox;
            var time = 0u;
            var samples = new List<Sample>();
            HashSet<uint>? syncPoints = null;
            if (stss != null)
            {
                syncPoints = new HashSet<uint>(stss.SampleNumbers);
            }
            foreach (var entry in stts.Entries)
            {
                for (int j = 0; j < entry.SampleCount; j++)
                {
                    samples.Add(new Sample
                    {
                        DTS = time,
                        Duration = entry.SampleDelta,
                        Size = stsz.SampleSize != 0 ? stsz.SampleSize : stsz.Entries[samples.Count],
                        IsSync = syncPoints?.Contains((uint)samples.Count + 1) ?? true,
                    });
                    time += entry.SampleDelta;
                }
            }
            if (ctts != null)
            {
                int i = 0;
                foreach (var entry in ctts.Entries)
                {
                    for (int j = 0; j < entry.SampleCount; j++)
                    {
                        samples[i].CTS = checked((uint)(samples[i].DTS + entry.SampleOffset));
                        i++;
                    }
                }
            }
            int sampleIndex = 0;
            for (int i = 0; i < stsc.Entries.Count; i++)
            {
                var next = stsc.Entries.Count == i + 1 ? co64.ChunkOffsets.Count : (int)stsc.Entries[i + 1].FirstChunk - 1;
                for (int j = (int)stsc.Entries[i].FirstChunk - 1; j < next; j++)
                {
                    var offset = co64.ChunkOffsets[j];
                    for (int k = 0; k < stsc.Entries[i].SamplesPerChunk; k++)
                    {
                        samples[sampleIndex].Offset = checked((long)offset);
                        samples[sampleIndex].DescriptionIndex = checked((int)(stsc.Entries[i].SampleDescriptionIndex - 1));
                        offset += (ulong)samples[sampleIndex].Size;
                        sampleIndex++;
                    }
                }
            }
            return samples;
        }
        // FIXME
        public static (long CompositionStartTime, long CompositionEndTime) GetCompositionRange(MovieHeaderBox mvhd, MediaHeaderBox mdhd, BoxContainer stbl, IReadOnlyList<EditListEntry>? editList, IReadOnlyList<Sample> samples)
        {
            if (stbl.Boxes.FirstOrDefault(x => x is CompositionToDecodeBox) is CompositionToDecodeBox cslg)
            {
                return (cslg.CompositionStartTime, cslg.CompositionEndTime);
            }
            if (editList?.Count == 1)
            {
                var entry = editList[0];
                var mediaTime = entry.MediaTime;
                var duration = entry.SegmentDuration * mdhd.Timescale / mvhd.Timescale;
                return (mediaTime, mediaTime + (long)duration);
            }
            else if (samples.Count != 0)
            {
                var start = samples[0];
                var end = samples[^1];
                return (start.CTS ?? start.DTS, end.CTS ?? end.DTS + end.Duration);
            }
            return (0, (long)mdhd.Duration);
        }
    }
}

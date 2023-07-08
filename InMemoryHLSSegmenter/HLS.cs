using System.Text;

namespace InMemoryHLSSegmenter
{
    static class HLS
    {
        public static IReadOnlyList<MediaSegment> Segment(long compositionStartTime, long minSegmentDuration, IReadOnlyList<uint> syncPoints, IReadOnlyList<Sample> samples)
        {
            var endTime = (samples[^1].CTS ?? samples[^1].DTS) + samples[^1].Duration;
            var prevPoint = 0L;
            List<MediaSegment> segments = new();
            for (int i = 1; i < syncPoints.Count; i++)
            {
                var sampleNumber = syncPoints[i];
                var sampleIndex = checked((int)(sampleNumber - 1));
                var ts = samples[sampleIndex].DTS;
                if (compositionStartTime > ts)
                    continue;
                var durationBetweenSyncPoint = ts - prevPoint;
                if (durationBetweenSyncPoint >= minSegmentDuration)
                {
                    segments.Add(new(durationBetweenSyncPoint, prevPoint));
                    prevPoint = ts;
                }
            }
            if (endTime - prevPoint != 0)
            {
                segments.Add(new(endTime - prevPoint, prevPoint));
            }
            return segments;
        }
        public static string MakePlaylist(long compositionStartTime, long Timescale, IReadOnlyList<MediaSegment> mediaSegments)
        {
            var sb = new StringBuilder();
            sb.Append("#EXTM3U\n");
            sb.Append("#EXT-X-VERSION:3\n");
            sb.Append($"#EXT-X-TARGETDURATION:{(int)Math.Ceiling(mediaSegments.MaxBy(x => x.Duration)!.Duration / (double)Timescale)}\n");
            sb.Append("#EXT-X-MEDIA-SEQUENCE:1\n");
            sb.Append("#EXT-X-PLAYLIST-TYPE:VOD\n");
            var index = 1;
            foreach (var segment in mediaSegments)
            {
                if (segment.Offset < compositionStartTime && segment.Offset + segment.Duration >= compositionStartTime)
                {
                    sb.Append($"#EXTINF:{(segment.Duration - compositionStartTime) / (decimal)Timescale:.000},\n");
                }
                else
                {
                    sb.Append($"#EXTINF:{segment.Duration / (decimal)Timescale:0.000},\n");
                }
                sb.Append($"{index}.ts\n");
                index++;
            }
            sb.Append("#EXT-X-ENDLIST\n");
            return sb.ToString();
        }
    }
    record MediaSegment(long Duration, long Offset);

}

using InMemoryHLSSegmenter;
using System.Diagnostics;
using System.Net;
using System.Text;

static void Usage()
{
    Console.Error.WriteLine("usage: start-server <input .mp4 file> [<min segment duration [ms] default: 10000> [<listen hosts default: http://localhost:5656/>...]]");
    Console.Error.WriteLine("usage: remux-ts <input .mp4 file> <output .ts file> ");
    Console.Error.WriteLine("usage: raw-h264 <input .mp4 file> <output .h264 file> ");
    Console.Error.WriteLine("usage: raw-adts <input .mp4 file> <output .aac file> ");
}
if (args.Length < 2)
{
    Usage();
    return 1;
}
var command = args[0] switch
{
    "start-server" => CLICommand.StartServer,
    "remux-ts" => CLICommand.RemuxTS,
    "raw-h264" => CLICommand.RawH264,
    "raw-adts" => CLICommand.RawADTS,
    _ => CLICommand.Unknwon,
};
if (command == CLICommand.Unknwon)
{
    Console.Error.WriteLine($"Unknown command \"{command}\"");
    Usage();
    return 1;
}
var inputFile = args[1];
using var fs = new FileStream(inputFile, new FileStreamOptions
{
    Access = FileAccess.Read,
    Share = FileShare.Read,
    BufferSize = 0, // use BufferedStream
    Mode = FileMode.Open,
});
var bufferSize = 512 * 1024;
var br = new BigBinaryReader(new BufferedStream(fs, bufferSize));
var fileSize = fs.Length;
var result = ISOBMFFParser.ParseISOBMFF(br, fileSize);
var moov = (BoxContainer)result.First(x => x.Type == "moov")!;
var mvhd = (MovieHeaderBox)moov.Boxes.First(x => x is MovieHeaderBox);
var moof = result.FirstOrDefault(x => x.Type == "moof") as BoxContainer;
var tracks = moov.Boxes.Where(x => x.Type == "trak").Select(x => (BoxContainer)x).ToArray();
var eses = new List<ElementaryStream>();
long compositionStartTime = 0;
long compositionEndTime = long.MaxValue;
IReadOnlyList<uint>? syncPoints = null;
MediaHeaderBox? mediaHeaderBox = null;
ElementaryStream? mainES = null;
foreach (var track in tracks)
{
    if (track.Boxes.FirstOrDefault(x => x is TrackHeaderBox) is not TrackHeaderBox header)
        continue;
    if ((header.Flags & TrackHeaderBox.TrackEnabledFlag) == 0)
        continue;
    if (track.Boxes.FirstOrDefault(x => x.Type == "mdia") is not BoxContainer mdia)
        continue;
    if (mdia.Boxes.FirstOrDefault(x => x is MediaHeaderBox) is not MediaHeaderBox mdhd)
        continue;
    if (mdia.Boxes.FirstOrDefault(x => x is HandlerBox) is not HandlerBox hdlr)
        continue;
    if (mdia.Boxes.FirstOrDefault(x => x.Type == "minf") is not BoxContainer minf)
        continue;
    if (minf.Boxes.FirstOrDefault(x => x.Type == "stbl") is not BoxContainer stbl)
        continue;
    if (stbl.Boxes.FirstOrDefault(x => x is SampleDescriptionBox) is not SampleDescriptionBox stsd)
        continue;
    var edts = track.Boxes.FirstOrDefault(x => x.Type == "edts") as BoxContainer;
    var elst = edts?.Boxes?.FirstOrDefault(x => x is EditListBox) as EditListBox;
    var samples = ISOBMFFParser.IndexSamples(stbl);
    if (samples == null)
        continue;
    if (hdlr.HandlerType == "vide")
    {
        List<AVCConfigurationBox> descriptions = new();
        foreach (var entry in stsd.SampleEntries)
        {
            var vse = ISOBMFFParser.ParseVisualSampleEntry(entry);
            if (vse.Boxes.FirstOrDefault(x => x is AVCConfigurationBox) is AVCConfigurationBox avc1)
            {
                descriptions.Add(avc1);
            }
        }
        if (descriptions.Count == stsd.SampleEntries.Count)
        {
            eses.Add(new((prevSample, sample, writer) =>
            {
                var spspps = prevSample?.DescriptionIndex != sample.DescriptionIndex || sample.IsSync;
                H264.WriteSample(br, writer, sample, descriptions[sample.DescriptionIndex].AVCConfig, spspps, spspps);
            })
            {
                PID = checked((ushort)(0x100 + eses.Count)),
                Samples = samples,
                StreamType = MPEG2TransportStreamType.AVC,
                StreamId = MPEG2TransportStreamId.Video,
                Timescale = mdhd.Timescale,
            });
            var (start, end) = ISOBMFFParser.GetCompositionRange(mvhd, mdhd, stbl, elst?.Entries, samples);
            compositionStartTime = Math.Max(compositionStartTime, start);
            compositionEndTime = Math.Min(compositionEndTime, end);
            var stss = stbl.Boxes.FirstOrDefault(x => x is SyncSampleBox) as SyncSampleBox;
            syncPoints = stss?.SampleNumbers;
            mediaHeaderBox = mdhd;
            mainES = eses.Last();
        }
    }
    else if (hdlr.HandlerType == "soun")
    {
        List<AudioSpecificConfig> descriptions = new();
        foreach (var entry in stsd.SampleEntries)
        {
            var ase = ISOBMFFParser.ParseAudioSampleEntry(entry);
            if (ase.Boxes.FirstOrDefault(x => x is ESDBox) is not ESDBox esds)
                continue;
            if (esds.ElementaryStream.DecoderConfigDescriptor.DecoderSpecificInfo is not AudioSpecificConfig config)
                continue;
            if (config.AudioObjectType >= 1 && config.AudioObjectType <= 4)
                descriptions.Add(config);
        }
        if (descriptions.Count == stsd.SampleEntries.Count)
        {
            eses.Add(new((prevSample, sample, writer) =>
            {
                ADTS.WriteSample(br, writer, sample, descriptions[sample.DescriptionIndex]);
            })
            {
                PID = checked((ushort)(0x100 + eses.Count)),
                Samples = samples,
                StreamType = MPEG2TransportStreamType.AAC,
                StreamId = MPEG2TransportStreamId.Audio,
                Timescale = mdhd.Timescale,
            });
        }
    }
}

if (command == CLICommand.RawADTS)
{
    var aacES = eses.FirstOrDefault(x => x.StreamType == MPEG2TransportStreamType.AAC);
    if (aacES == null)
    {
        Console.Error.WriteLine("Failed to find AAC stream.");
        return 1;
    }
    var output = args[2];
    using var outputStream = File.Create(output);
    using var bw = new BigBinaryWriter(outputStream);
    Sample? prevSample = null;
    foreach (var sample in aacES.Samples)
    {
        aacES.Write(prevSample , sample, bw);
        prevSample = sample;
    }
    return 0;
}

if (command == CLICommand.RawH264)
{
    var avcES = eses.FirstOrDefault(x => x.StreamType == MPEG2TransportStreamType.AVC);
    if (avcES == null)
    {
        Console.Error.WriteLine("Failed to find H.264 stream.");
        return 1;
    }
    var output = args[2];
    using var outputStream = File.Create(output);
    using var bw = new BigBinaryWriter(outputStream);
    Sample? prevSample = null;
    foreach (var sample in avcES.Samples)
    {
        avcES.Write(prevSample, sample, bw);
        prevSample = sample;
    }
    return 0;
}

List<(ElementaryStream es, Sample sample)> samplesToMux = new();

foreach (var es in eses)
{
    foreach (var sample in es.Samples)
    {
        samplesToMux.Add((es, sample));
    }
}

samplesToMux.Sort((a, b) =>
{
    var aTime = a.sample.DTS / (double)a.es.Timescale;
    var bTime = b.sample.DTS / (double)b.es.Timescale;
    if (aTime == bTime)
    {
        return a.es.PID.CompareTo(b.es.PID);
    }
    return aTime.CompareTo(bTime);
});

if (command == CLICommand.RemuxTS)
{
    using var tt = File.Create(Path.Combine(args[2]));
    var muxer = new MPEG2TransportStreamMuxer(tt, eses, 0, 1);
    foreach (var (es, sample) in samplesToMux)
    {
        muxer.Transmit(es, sample);
    }
    return 0;
}

if (syncPoints == null)
{
    Console.Error.WriteLine("The file provided does not contain a SyncSampleBox.");
    return 1;
}

if (mediaHeaderBox == null || mainES == null)
{
    Console.Error.WriteLine("Failed to find video stream.");
    return 1;
}

var segmentDurationMillis = 10000;
if (args.Length > 2)
{
    segmentDurationMillis = int.Parse(args[2]);
}

var estimatedSegmentSize = fileSize / (mvhd.Duration / (double)mvhd.Timescale) * (segmentDurationMillis / 1000.0);
var maxBufferSize = 20 * 1024 * 1024;
var minBufferSize = 1 * 1024 * 1024;

bufferSize = Math.Clamp((int)estimatedSegmentSize, minBufferSize, maxBufferSize);
br = new BigBinaryReader(new BufferedStream(fs, bufferSize));

var segments = HLS.Segment(compositionStartTime, segmentDurationMillis * mediaHeaderBox.Timescale / 1000, syncPoints, mainES.Samples);

IEnumerable<(ElementaryStream es, Sample sample)> GetMediaSegmentSamples(long timescale, MediaSegment segment, List<(ElementaryStream es, Sample sample)> samplesToMux)
{
    return samplesToMux.Where(x =>
    {
        // | video | video | video |
        // |audio|audio|audio|audio|
        // seg1
        // | video |
        // |audio|audio|
        // seg2
        //         | video |
        //             |audio|
        // seg3
        //                 | video |
        //                   |audio|
        var dur = x.sample.Duration * mediaHeaderBox.Timescale / x.es.Timescale;
        var off = x.sample.DTS * mediaHeaderBox.Timescale / x.es.Timescale;
        return segment.Offset <= off && segment.Duration + segment.Offset > off;
    });
}

var listener = new HttpListener();
var hosts = args.Length > 3 ? args.Skip(3) : new string[] { "http://localhost:5656/" };
foreach (var host in hosts)
{
    Console.WriteLine($"Listening on {host}");
    listener.Prefixes.Add(host);
}
listener.Start();

var html = @"
<!DOCTYPE html>
<html>
<head>
<meta name='viewport' content='width=device-width, initial-scale=1' />
</head>
<body>
<script src='https://cdn.jsdelivr.net/npm/hls.js@1'></script>
<video id='video' controls></video>
<script>
  var video = document.getElementById('video');
  var videoSrc = '/playlist.m3u8';
  if (video.canPlayType('application/vnd.apple.mpegurl')) {
    video.src = videoSrc;
  } else if (Hls.isSupported()) {
    var hls = new Hls();
    hls.loadSource(videoSrc);
    hls.attachMedia(video);
  }
</script>
<style>
body { margin: 0px; overflow: hidden; }
video { width: 100vw; height: 100vh; }
</style>
</body>
</html>
";


while (true)
{
    var context = listener.GetContext();
    var request = context.Request;
    var path = request?.Url?.LocalPath ?? "";
    var stw = new Stopwatch();
    stw.Start();
    Console.WriteLine(path);
    context.Response.SendChunked = false;
    if (path == "/" || path == "/index.html")
    {
        context.Response.ContentType = "text/html";
        var res = Encoding.UTF8.GetBytes(html);
        context.Response.Close(res, false);
    }
    else if (path == "/playlist.m3u8")
    {
        // HTTP Content-Type MUST be "application/vnd.apple.mpegurl" or "audio/mpegurl".
        context.Response.ContentType = "application/vnd.apple.mpegurl";
        var res = Encoding.UTF8.GetBytes(HLS.MakePlaylist(compositionStartTime, mediaHeaderBox.Timescale, segments));
        context.Response.Close(res, false);
    }
    else if (path.EndsWith(".ts"))
    {
        // Package HLS
        var file = path.TrimStart('/').Split('.')[0];
        if (int.TryParse(file, out var idx))
        {
            var ms = new MemoryStream();
            var segment = segments[idx - 1];
            var muxer = new MPEG2TransportStreamMuxer(ms, eses, segment.Offset, mediaHeaderBox.Timescale);
            foreach (var (es, sample) in GetMediaSegmentSamples(mediaHeaderBox.Timescale, segment, samplesToMux))
            {
                muxer.Transmit(es, sample);
            }
            context.Response.ContentType = "video/mp2t";
            context.Response.Close(ms.ToArray(), false);
        }
        else
        {
            context.Response.StatusCode = 404;
            context.Response.Close();
        }
    }
    else
    {
        context.Response.StatusCode = 404;
        context.Response.Close();
    }
    Console.WriteLine($"{stw.Elapsed.TotalMilliseconds:0.000} ms");
}

enum CLICommand
{
    StartServer,
    RemuxTS,
    RawH264,
    RawADTS,
    Unknwon,
}

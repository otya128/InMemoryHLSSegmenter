# InMemoryHLSSegmenter

An HLS server that segments .mp4 files (ISO Base Media File Format) on-demand, in-memory.

no dependency, fast, fully written in C#

```
usage: start-server <input .mp4 file> [<min segment duration [ms] default: 10000> [<listen hosts default: http://localhost:5656/>...]]
usage: remux-ts <input .mp4 file> <output .ts file>
usage: raw-h264 <input .mp4 file> <output .h264 file>
usage: raw-adts <input .mp4 file> <output .aac file>
```

## Requirements

* .NET 6.0 or later

using System.Buffers.Binary;
using Concentus;
using Concentus.Enums;

namespace Cs2Prak.Core.Demos;

internal static class VoiceTrack
{
    private const int SampleRate = 48000;

    private const int DecodeCapacity = SampleRate / 8;

    private const int GapTicks = (int)(CsDemoAnalyzer.TickRate * 0.35);

    private const int SilenceThreshold = (int)(SampleRate * 0.15);

    private const int MinimumSamples = SampleRate / 5;

    private const int FadeSamples = SampleRate / 200;

    private const float Knee = 22000f;
    private const float Ceiling = 32000f;

    internal static long Decoded, Inserted;

    internal readonly record struct Packet(int Tick, byte[] Opus);

    internal sealed record Clip(int Slot, int Number, double Frame, double Duration, int Side);

    public static List<Clip> Build(Dictionary<int, List<Packet>> bySlot, string voiceDir,
                                   int startTick, int step, int frameCount,
                                   Func<double, int, int> sideAt)
    {
        Decoded = Inserted = 0;
        Directory.CreateDirectory(voiceDir);
        foreach (var stale in Directory.EnumerateFiles(voiceDir))
        {
            try { File.Delete(stale); } catch (Exception) { }
        }

        var clips = new List<Clip>();
        var number = 0;

        foreach (var (slot, packets) in bySlot.OrderBy(kv => kv.Key))
        {
            packets.Sort((a, b) => a.Tick.CompareTo(b.Tick));

            var utterances = SplitOnPauses(packets);
            var decoded = utterances
                .Select(u => (Start: u[0].Tick, Pcm: Decode(u)))
                .Where(d => d.Pcm is not null)
                .ToList();
            if (decoded.Count == 0) continue;

            var gain = GainFor(decoded.Select(d => d.Pcm!));

            foreach (var (start, pcm) in decoded)
            {
                var frame = Math.Round((start - startTick) / (double)step, 2);

                if (frame < 0 || frame > frameCount - 1) continue;

                var finished = Finish(pcm!, gain);
                File.WriteAllBytes(Path.Combine(voiceDir, $"{number}.wav"), Wav(finished));

                clips.Add(new Clip(
                    slot, number, frame,
                    Math.Round(finished.Length / (double)SampleRate, 2),
                    sideAt(frame, slot)));
                number++;
            }
        }

        clips.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        return clips;
    }

    private static List<List<Packet>> SplitOnPauses(List<Packet> packets)
    {
        var utterances = new List<List<Packet>>();
        var current = new List<Packet>();

        foreach (var packet in packets)
        {
            if (current.Count > 0 && packet.Tick - current[^1].Tick > GapTicks)
            {
                utterances.Add(current);
                current = [];
            }
            current.Add(packet);
        }
        if (current.Count > 0) utterances.Add(current);
        return utterances;
    }

    private static short[]? Decode(List<Packet> utterance)
    {
        var decoder = OpusCodecFactory.CreateDecoder(SampleRate, 1);
        var buffer = new short[DecodeCapacity];
        var samples = new List<short>(DecodeCapacity);

        var start = utterance[0].Tick;
        var made = 0;

        foreach (var (tick, opus) in utterance)
        {
            var want = (int)((tick - start) / (double)CsDemoAnalyzer.TickRate * SampleRate);
            if (want - made >= SilenceThreshold)
            {
                Inserted += want - made;
                samples.AddRange(new short[want - made]);
                made = want;
            }

            int produced;
            try { produced = decoder.Decode(opus, buffer, DecodeCapacity, false); }
            catch (OpusException) { continue; }

            if (produced <= 0) continue;
            Decoded += produced;
            samples.AddRange(buffer.AsSpan(0, produced));
            made += produced;
        }

        if (samples.Count < MinimumSamples) return null;

        var peak = 0;
        foreach (var s in samples) peak = Math.Max(peak, Math.Abs((int)s));
        return peak < 16 ? null : samples.ToArray();
    }

    private static float GainFor(IEnumerable<short[]> clips)
    {
        double sumSquares = 0;
        long count = 0;
        var magnitudes = new List<int>();

        foreach (var clip in clips)
        {
            foreach (var sample in clip)
            {
                sumSquares += (double)sample * sample;
                magnitudes.Add(Math.Abs((int)sample));
            }
            count += clip.Length;
        }
        if (count == 0) return 1f;

        var rms = Math.Sqrt(sumSquares / count);
        if (rms < 1.0) return 1f;

        magnitudes.Sort();
        var loud = magnitudes[Math.Min(magnitudes.Count - 1, (int)(magnitudes.Count * 0.999))];

        return (float)Math.Min(Math.Min(2400.0 / rms, 26000.0 / Math.Max(1.0, loud)), 8.0);
    }

    private static short[] Finish(short[] pcm, float gain)
    {
        var work = new float[pcm.Length];
        for (var i = 0; i < pcm.Length; i++)
        {
            var value = pcm[i] * gain;

            var magnitude = Math.Abs(value);
            if (magnitude > Knee)
            {
                var over = (magnitude - Knee) / (Ceiling - Knee);
                value = Math.Sign(value) * (Knee + (Ceiling - Knee) * MathF.Tanh(over));
            }
            work[i] = value;
        }

        if (work.Length > FadeSamples * 2)
        {
            for (var i = 0; i < FadeSamples; i++)
            {
                var ramp = i / (float)(FadeSamples - 1);
                work[i] *= ramp;
                work[^(i + 1)] *= ramp;
            }
        }

        var output = new short[work.Length];
        for (var i = 0; i < work.Length; i++)
            output[i] = (short)Math.Clamp(work[i], -32768f, 32767f);
        return output;
    }

    private static byte[] Wav(short[] pcm)
    {
        var dataBytes = pcm.Length * 2;
        var file = new byte[44 + dataBytes];
        var span = file.AsSpan();

        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + dataBytes);
        "WAVEfmt "u8.CopyTo(span[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], SampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataBytes);

        for (var i = 0; i < pcm.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(span[(44 + i * 2)..], pcm[i]);

        return file;
    }
}

using System;
using Microsoft.Xna.Framework.Audio;

namespace U5Desktop;

// Tiny in-engine synth — renders PCM into SoundEffects so we have audio without
// any external files (U5's own music is chip-synth data baked into the game and
// not directly playable). Original chiptune stand-in for the real soundtrack.
internal static class Audio
{
    private const int SR = 22050;

    private static double Freq(int midi) => 440.0 * Math.Pow(2, (midi - 69) / 12.0);

    // Short blips for feedback.
    public static SoundEffect Step() => Sfx(RenderPcm(new[] { (72, 0.045) }, 0, 0.18));
    public static SoundEffect Bonk() => Sfx(RenderPcm(new[] { (48, 0.09) }, 0, 0.22));

    // A gentle looping medieval-ish theme in A-minor (melody + simple bass).
    public static SoundEffect MusicLoop()
    {
        int A3 = 57, C4 = 60, D4 = 62, E4 = 64, F4 = 65, G4 = 67, A4 = 69, B4 = 71,
            C5 = 72, D5 = 74, E5 = 76, F5 = 77, G5 = 79, A5 = 81;
        double q = 0.34;
        var melody = new (int, double)[]
        {
            (A4,q),(C5,q),(E5,q),(C5,q),  (D5,q),(F5,q),(A5,q),(F5,q),
            (G4,q),(B4,q),(D5,q),(B4,q),  (E4,q),(G4,q),(C5,q),(E5,q),
            (A4,q),(C5,q),(E5,q),(A5,q),  (G5,q),(E5,q),(C5,q),(A4,q),
            (F4,q),(A4,q),(C5,q),(F5,q),  (E5,q*2),(A4,q*2),
        };
        var bass = new (int, double)[]
        {
            (A3,q*2),(A3,q*2), (D4-12,q*2),(D4-12,q*2),
            (G4-12,q*2),(G4-12,q*2), (E4-12,q*2),(C4-12,q*2),
            (A3,q*2),(A3,q*2), (C4-12,q*2),(A3,q*2),
            (F4-12,q*2),(F4-12,q*2), (E4-12,q*2),(A3,q*2),
        };
        return Sfx(Mix(RenderPcm(melody, 1, 0.16), RenderPcm(bass, 2, 0.13)));
    }

    // wave: 0=square, 1=triangle, 2=soft triangle
    private static short[] RenderPcm((int midi, double dur)[] notes, int wave, double amp)
    {
        int total = 0;
        foreach (var (_, dur) in notes) total += (int)(dur * SR);
        var pcm = new short[total];
        int pos = 0;
        foreach (var (midi, dur) in notes)
        {
            int n = (int)(dur * SR);
            double f = midi <= 0 ? 0 : Freq(midi);
            for (int i = 0; i < n; i++)
            {
                double phase = f > 0 ? (i / (double)SR * f) % 1.0 : 0;
                double v = wave switch
                {
                    0 => phase < 0.5 ? 1 : -1,
                    1 => phase < 0.5 ? 4 * phase - 1 : 3 - 4 * phase,
                    _ => (phase < 0.5 ? 4 * phase - 1 : 3 - 4 * phase) * 0.6
                };
                pcm[pos++] = (short)(v * amp * Env(i, n) * short.MaxValue);
            }
        }
        return pcm;
    }

    private static short[] Mix(short[] a, short[] b)
    {
        int len = Math.Max(a.Length, b.Length);
        var o = new short[len];
        for (int i = 0; i < len; i++)
        {
            int s = (i < a.Length ? a[i] : 0) + (i < b.Length ? b[i] : 0);
            o[i] = (short)Math.Clamp(s, short.MinValue, short.MaxValue);
        }
        return o;
    }

    private static double Env(int i, int n)
    {
        int a = Math.Min(220, n / 8), d = Math.Min(1600, n / 3);
        if (i < a) return i / (double)a;
        if (i > n - d) return (n - i) / (double)d;
        return 1;
    }

    private static SoundEffect Sfx(short[] pcm)
    {
        var b = new byte[pcm.Length * 2];
        Buffer.BlockCopy(pcm, 0, b, 0, b.Length);
        return new SoundEffect(b, SR, AudioChannels.Mono);
    }
}

using UnityEngine;

/// Original procedural workout music (128 BPM) + generated sound effects. No audio assets needed.
[RequireComponent(typeof(AudioSource))]
public class Synth : MonoBehaviour
{
    public bool Playing;
    public float Volume = 0.35f;
    double sampleRate, t;
    readonly System.Random rng = new System.Random(7);
    static readonly int[] Bass = { 45, 45, 52, 52, 43, 43, 50, 50 };
    static readonly int[] Lead = { 76, 79, 81, 79, 76, 74, 72, 74, 76, 79, 84, 83, 81, 79, 76, 79 };
    AudioSource sfxSrc;
    AudioClip coin, jump, hit, whoosh;

    void Awake()
    {
        sampleRate = AudioSettings.outputSampleRate;
        var src = GetComponent<AudioSource>(); src.playOnAwake = false; src.Play(); // drives OnAudioFilterRead
        sfxSrc = gameObject.AddComponent<AudioSource>(); sfxSrc.playOnAwake = false;
        coin = Make(0.16f, (x, k) => Sq(x * (k < 0.4f ? 1320 : 1760)) * 0.25f * (1 - k));
        jump = Make(0.22f, (x, k) => Mathf.Sin(2 * Mathf.PI * x * Mathf.Lerp(300, 750, k) ) * 0.35f * (1 - k));
        hit = Make(0.35f, (x, k) => (Saw(x * 110) * 0.5f + Saw(x * 82) * 0.5f) * 0.4f * (1 - k));
        whoosh = Make(0.12f, (x, k) => (Random.value * 2 - 1) * 0.15f * Mathf.Sin(Mathf.PI * k));
    }

    static float Sq(float p) => (p % 1f) < 0.5f ? 1 : -1;
    static float Saw(float p) => 2f * (p % 1f) - 1f;

    AudioClip Make(float len, System.Func<float, float, float> f)
    {
        int sr = AudioSettings.outputSampleRate, n = (int)(len * sr);
        var data = new float[n];
        for (int i = 0; i < n; i++) data[i] = f(i / (float)sr, i / (float)n);
        var c = AudioClip.Create("sfx", n, 1, sr, false); c.SetData(data, 0); return c;
    }

    public void Coin() => sfxSrc.PlayOneShot(coin, 0.7f);
    public void Jump() => sfxSrc.PlayOneShot(jump, 0.8f);
    public void Hit() => sfxSrc.PlayOneShot(hit, 1f);
    public void Whoosh() => sfxSrc.PlayOneShot(whoosh, 0.8f);

    static double Hz(int n) => 440.0 * System.Math.Pow(2, (n - 69) / 12.0);

    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!Playing) { System.Array.Clear(data, 0, data.Length); return; }
        double dt = 1.0 / sampleRate, step = 60.0 / 128.0 / 4.0; // 16th notes
        for (int i = 0; i < data.Length; i += channels)
        {
            int s = (int)(t / step); double ph = t - s * step;
            float v = 0;
            if (s % 4 == 0) { double k = ph; v += (float)(System.Math.Sin(2 * System.Math.PI * (55 + 90 * System.Math.Exp(-k * 30)) * k) * System.Math.Exp(-k * 9)) * 0.9f; }
            if (s % 4 == 2) v += (float)((rng.NextDouble() * 2 - 1) * System.Math.Exp(-ph * 60)) * 0.18f;
            if (s % 8 == 4) v += (float)((rng.NextDouble() * 2 - 1) * System.Math.Exp(-ph * 22)) * 0.35f;
            double bf = Hz(Bass[(s / 8) % 8]);
            v += Saw((float)(t * bf)) * (float)System.Math.Exp(-ph * 6) * 0.22f;
            if (s % 2 == 0) { double lf = Hz(Lead[(s / 2) % 16]); v += Sq((float)(t * lf)) * (float)System.Math.Exp(-ph * 5) * 0.06f; }
            v *= Volume;
            for (int c = 0; c < channels; c++) data[i + c] = v;
            t += dt;
        }
    }
}

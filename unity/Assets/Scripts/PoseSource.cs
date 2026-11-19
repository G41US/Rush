using System.Runtime.InteropServices;
using UnityEngine;

/// Reads 13 body joints from the native tracker (Apple Vision on iOS, ML Kit on Android).
/// Coordinates: normalized 0..1, origin top-left, NOT mirrored (raw front-camera view).
public static class PoseSource
{
    public const int Joints = 13;
    public const int Nose = 0, LSh = 1, RSh = 2, LEl = 3, REl = 4, LWr = 5, RWr = 6,
                     LHip = 7, RHip = 8, LKnee = 9, RKnee = 10, LAnk = 11, RAnk = 12;

    public static readonly Vector2[] Pos = new Vector2[Joints];
    public static readonly float[] Conf = new float[Joints];
    public static float FrameTime;           // native timestamp of the latest frame
    public static bool HasNewFrame;
    public static int FrameCount;        // frames received since start (diagnostics)

    static readonly float[] buf = new float[1 + Joints * 3];
    static float lastTime = -1f;

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void PB_Start();
    [DllImport("__Internal")] static extern void PB_Stop();
    [DllImport("__Internal")] static extern int PB_State();
    [DllImport("__Internal")] static extern int PB_Info();
    [DllImport("__Internal")] static extern int PB_GetLatest(float[] outBuf, int len);
#elif UNITY_ANDROID && !UNITY_EDITOR
    static AndroidJavaClass bridge;
    static AndroidJavaObject Activity =>
        new AndroidJavaClass("com.unity3d.player.UnityPlayer").GetStatic<AndroidJavaObject>("currentActivity");
#endif

#if (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
    public static bool Supported => true;
#else
    public static bool Supported => false;
#endif

    /// Diagnostics line: rotation in use (iOS) and how many frames contained a person.
    public static string Info
    {
        get
        {
            int seen = 0; for (int i = 0; i < Joints; i++) if (Conf[i] > 0.3f) seen++;
#if UNITY_IOS && !UNITY_EDITOR
            int v = PB_Info();
            return $"rotation {v / 1000000 * 90}° · person found {v % 1000000}× · joints {seen}/13";
#else
            return $"joints {seen}/13";
#endif
        }
    }

    /// 1 running, 0 starting/idle, -1 denied or failed.
    public static int State
    {
        get
        {
#if UNITY_IOS && !UNITY_EDITOR
            return PB_State();
#elif UNITY_ANDROID && !UNITY_EDITOR
            return bridge != null ? bridge.CallStatic<int>("getState") : 0;
#else
            return -1;
#endif
        }
    }

    public static void Start()
    {
        FrameCount = 0;
#if UNITY_IOS && !UNITY_EDITOR
        PB_Start();
#elif UNITY_ANDROID && !UNITY_EDITOR
        bridge ??= new AndroidJavaClass("com.marvelous.posebridge.PoseBridge");
        bridge.CallStatic("start", Activity);
#endif
    }

    public static void Stop()
    {
#if UNITY_IOS && !UNITY_EDITOR
        PB_Stop();
#elif UNITY_ANDROID && !UNITY_EDITOR
        bridge?.CallStatic("stop", Activity);
#endif
    }

    /// Call once per frame. Sets HasNewFrame when the tracker produced a new result.
    public static void Poll()
    {
        HasNewFrame = false;
#if UNITY_IOS && !UNITY_EDITOR
        if (PB_GetLatest(buf, buf.Length) <= 0) return;
#elif UNITY_ANDROID && !UNITY_EDITOR
        if (bridge == null) return;
        var arr = bridge.CallStatic<float[]>("getLatest");
        if (arr == null || arr.Length < buf.Length) return;
        System.Array.Copy(arr, buf, buf.Length);
#else
        return;
#endif
#pragma warning disable CS0162
        if (buf[0] <= 0f || Mathf.Approximately(buf[0], lastTime)) return;
        lastTime = buf[0];
        FrameTime = buf[0];
        for (int i = 0; i < Joints; i++)
        {
            Pos[i] = new Vector2(buf[1 + i * 3], buf[2 + i * 3]);
            Conf[i] = buf[3 + i * 3];
        }
        HasNewFrame = true; FrameCount++;
#pragma warning restore CS0162
    }
}

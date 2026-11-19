using System.Collections.Generic;
using UnityEngine;

/// Builds and scrolls the 3D world: tracks, city, trains, barriers, coins and the runner.
public class RushWorld : MonoBehaviour
{
    public const float LaneW = 2.6f;
    const float SegLen = 24f; const int SegCount = 9;
    public const float SpawnZ = 150f;

    Material baseMat;
    readonly Dictionary<Color, Material> mats = new Dictionary<Color, Material>();
    readonly List<Transform> segs = new List<Transform>();

    public class Obstacle
    {
        public enum Kind { Train, Low, High }
        public Kind kind; public int lane; public float z, len; public Transform tr; public bool hit; public float flyT;
        public float Front => z - len * 0.5f; public float Back => z + len * 0.5f;
    }
    public readonly List<Obstacle> obstacles = new List<Obstacle>();
    public readonly List<Transform> coins = new List<Transform>();
    readonly Stack<Transform> coinPool = new Stack<Transform>();

    // runner rig
    public Transform runner; Transform body, head, armL, armR, legL, legR, shadow;
    float animPhase;

    public Material Mat(Color c, float smooth = 0.35f, float metal = 0f, Color? emission = null)
    {
        if (emission == null && mats.TryGetValue(c, out var m)) return m;
        m = new Material(baseMat) { color = c };
        m.SetFloat("_Glossiness", smooth); m.SetFloat("_Metallic", metal);
        if (emission != null) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value); }
        else mats[c] = m;
        return m;
    }

    static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

    Transform Prim(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Material m, bool shadows = true)
    {
        var go = GameObject.CreatePrimitive(t);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos; go.transform.localScale = scale;
        var r = go.GetComponent<Renderer>(); r.sharedMaterial = m;
        r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    public void Build()
    {
        baseMat = Resources.Load<Material>("BaseMat") ?? new Material(Shader.Find("Standard"));
        var sky = Resources.Load<Material>("SkyMat");
        if (sky != null) { RenderSettings.skybox = sky; }
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = Hex("#bfe6ff"); RenderSettings.fogStartDistance = 60; RenderSettings.fogEndDistance = 160;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Hex("#cfe8ff"); RenderSettings.ambientEquatorColor = Hex("#e8dcc8"); RenderSettings.ambientGroundColor = Hex("#6b6f80");

        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional; sun.color = Hex("#fff3e0"); sun.intensity = 1.25f;
        sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.55f;
        sun.transform.rotation = Quaternion.Euler(48, -35, 0);
        QualitySettings.shadowDistance = 45;

        for (int i = 0; i < SegCount; i++)
        {
            var s = BuildSegment(i);
            s.position = new Vector3(0, 0, -SegLen + i * SegLen);
            segs.Add(s);
        }
        BuildRunner();
    }

    Transform BuildSegment(int seed)
    {
        var rnd = new System.Random(seed * 31 + 5);
        var g = new GameObject("Seg").transform; g.SetParent(transform);
        var ground = Mat(Hex("#a8a29e"), 0.1f);
        var gravel = Mat(Hex("#8b7d6b"), 0.05f);
        var sleeper = Mat(Hex("#6b4f3a"), 0.1f);
        var rail = Mat(Hex("#d6d9de"), 0.8f, 0.6f);
        Prim(PrimitiveType.Cube, g, new Vector3(0, -0.25f, SegLen / 2), new Vector3(LaneW * 3 + 6, 0.5f, SegLen), ground, false);
        for (int l = -1; l <= 1; l++)
        {
            float x = l * LaneW;
            Prim(PrimitiveType.Cube, g, new Vector3(x, 0.02f, SegLen / 2), new Vector3(2.1f, 0.06f, SegLen), gravel, false);
            for (float z = 0.6f; z < SegLen; z += 1.2f)
                Prim(PrimitiveType.Cube, g, new Vector3(x, 0.08f, z), new Vector3(1.9f, 0.08f, 0.32f), sleeper, false);
            foreach (float o in new[] { -0.6f, 0.6f })
                Prim(PrimitiveType.Cube, g, new Vector3(x + o, 0.17f, SegLen / 2), new Vector3(0.09f, 0.12f, SegLen), rail, false);
        }
        Color[] walls = { Hex("#7cc4ff"), Hex("#ffd166") };
        Color[] bld = { Hex("#ff8fa3"), Hex("#9db4ff"), Hex("#ffd6a5"), Hex("#a0e7a0"), Hex("#f5a3ff"), Hex("#ffb4a2"), Hex("#90dbf4") };
        var win = Mat(Hex("#28406e"), 0.9f, 0.2f);
        var roofM = Mat(Hex("#5c5470"), 0.2f);
        for (int side = -1; side <= 1; side += 2)
        {
            float wx = side * (LaneW * 1.5f + 2.2f);
            Prim(PrimitiveType.Cube, g, new Vector3(wx, 1.1f, SegLen / 2), new Vector3(0.7f, 2.2f, SegLen), Mat(walls[side < 0 ? 0 : 1], 0.3f));
            Prim(PrimitiveType.Cube, g, new Vector3(wx, 2.25f, SegLen / 2), new Vector3(0.9f, 0.12f, SegLen), Mat(Color.white, 0.4f));
            // lamp post
            var pole = Mat(Hex("#3d3d4a"), 0.5f, 0.4f);
            Prim(PrimitiveType.Cylinder, g, new Vector3(wx - side * 0.6f, 2.4f, 4), new Vector3(0.12f, 2.4f, 0.12f), pole);
            Prim(PrimitiveType.Sphere, g, new Vector3(wx - side * 0.6f, 4.9f, 4), Vector3.one * 0.45f, Mat(Hex("#fff7d6"), 0.9f, 0, Hex("#ffe9a8")));
            for (float z = 2; z < SegLen; z += 7.5f)
            {
                float h = 6 + (float)rnd.NextDouble() * 16, w = 5 + (float)rnd.NextDouble() * 2.5f;
                float bx = side * (LaneW * 1.5f + 5.5f + (float)rnd.NextDouble() * 2.5f);
                var b = Prim(PrimitiveType.Cube, g, new Vector3(bx, h / 2, z + 3), new Vector3(w, h, 6.5f), Mat(bld[rnd.Next(bld.Length)], 0.25f));
                Prim(PrimitiveType.Cube, g, new Vector3(bx, h + 0.2f, z + 3), new Vector3(w + 0.3f, 0.4f, 6.8f), roofM);
                float face = bx - side * (w / 2 + 0.01f);
                for (float wy = 2; wy < h - 1; wy += 2.4f)
                    for (float wz = z + 1.2f; wz < z + 5.6f; wz += 1.8f)
                        Prim(PrimitiveType.Cube, g, new Vector3(face, wy, wz), new Vector3(0.05f, 1.1f, 0.9f), win, false);
            }
        }
        return g;
    }

    void BuildRunner()
    {
        runner = new GameObject("Runner").transform; runner.SetParent(transform);
        var skin = Mat(Hex("#f1c27d"), 0.3f); var hoodie = Mat(Hex("#2f6fed"), 0.35f);
        var pants = Mat(Hex("#22252d"), 0.25f); var cap = Mat(Hex("#ef3b3b"), 0.4f);
        var shoe = Mat(Color.white, 0.5f); var sole = Mat(Hex("#ff6b35"), 0.3f); var bag = Mat(Hex("#ffb703"), 0.3f);
        body = new GameObject("Body").transform; body.SetParent(runner, false); body.localPosition = new Vector3(0, 1.0f, 0);
        Prim(PrimitiveType.Capsule, body, new Vector3(0, 0.38f, 0), new Vector3(0.62f, 0.42f, 0.42f), hoodie);
        Prim(PrimitiveType.Cube, body, new Vector3(0, 0.42f, -0.26f), new Vector3(0.44f, 0.5f, 0.18f), bag);
        head = new GameObject("Head").transform; head.SetParent(body, false); head.localPosition = new Vector3(0, 0.98f, 0);
        Prim(PrimitiveType.Sphere, head, Vector3.zero, Vector3.one * 0.44f, skin);
        Prim(PrimitiveType.Sphere, head, new Vector3(0, 0.08f, 0), new Vector3(0.47f, 0.32f, 0.47f), cap);
        Prim(PrimitiveType.Cube, head, new Vector3(0, 0.06f, 0.24f), new Vector3(0.36f, 0.04f, 0.22f), cap);
        armL = Limb(body, new Vector3(-0.38f, 0.66f, 0), hoodie, skin, 0.55f, 0.15f);
        armR = Limb(body, new Vector3(0.38f, 0.66f, 0), hoodie, skin, 0.55f, 0.15f);
        legL = Limb(runner, new Vector3(-0.15f, 0.95f, 0), pants, shoe, 0.82f, 0.19f, sole);
        legR = Limb(runner, new Vector3(0.15f, 0.95f, 0), pants, shoe, 0.82f, 0.19f, sole);
        shadow = Prim(PrimitiveType.Cylinder, runner, new Vector3(0, 0.03f, 0), new Vector3(0.9f, 0.005f, 0.9f), Mat(new Color(0, 0, 0, 1), 0f), false);
        shadow.gameObject.SetActive(false);
    }

    Transform Limb(Transform parent, Vector3 pivot, Material upper, Material end, float len, float thick, Material extra = null)
    {
        var p = new GameObject("Limb").transform; p.SetParent(parent, false); p.localPosition = pivot;
        Prim(PrimitiveType.Capsule, p, new Vector3(0, -len / 2, 0), new Vector3(thick, len / 2, thick), upper);
        if (extra != null)
        {
            Prim(PrimitiveType.Cube, p, new Vector3(0, -len - 0.02f, 0.06f), new Vector3(0.22f, 0.13f, 0.36f), end);
            Prim(PrimitiveType.Cube, p, new Vector3(0, -len - 0.09f, 0.06f), new Vector3(0.23f, 0.04f, 0.37f), extra);
        }
        else Prim(PrimitiveType.Sphere, p, new Vector3(0, -len, 0), Vector3.one * thick * 1.05f, end);
        return p;
    }

    /// pose: x position, y height, lean (-1..1), runSpeed (for stride), ducking, airborne, stumble 0..1
    public void PoseRunner(float x, float y, float lean, float speed, bool duck, bool air, float stumble, float dt)
    {
        runner.localPosition = new Vector3(x, y, 0);
        animPhase += dt * (3f + speed * 0.55f);
        float s = Mathf.Sin(animPhase), amp = Mathf.Clamp01(speed / 6f);
        float swing = s * 55f * amp;
        if (air) { legL.localRotation = Quaternion.Euler(-60, 0, 0); legR.localRotation = Quaternion.Euler(30, 0, 0); armL.localRotation = Quaternion.Euler(-150, 0, -15); armR.localRotation = Quaternion.Euler(-150, 0, 15); }
        else
        {
            legL.localRotation = Quaternion.Euler(swing, 0, 0); legR.localRotation = Quaternion.Euler(-swing, 0, 0);
            armL.localRotation = Quaternion.Euler(-swing * 0.9f, 0, -6); armR.localRotation = Quaternion.Euler(swing * 0.9f, 0, 6);
        }
        float bob = air ? 0 : Mathf.Abs(Mathf.Cos(animPhase)) * 0.08f * amp;
        if (duck)
        {
            body.localPosition = new Vector3(0, 0.45f, -0.15f);
            body.localRotation = Quaternion.Euler(-70, 0, 0);
            legL.localRotation = Quaternion.Euler(-80, 0, 0); legR.localRotation = Quaternion.Euler(-70, 0, 0);
            legL.localPosition = new Vector3(-0.15f, 0.45f, 0); legR.localPosition = new Vector3(0.15f, 0.45f, 0);
        }
        else
        {
            body.localPosition = new Vector3(0, 1.0f + bob, 0);
            body.localRotation = Quaternion.Euler(12 * amp + stumble * 25, 0, -lean * 14);
            legL.localPosition = new Vector3(-0.15f, 0.95f + bob, 0); legR.localPosition = new Vector3(0.15f, 0.95f + bob, 0);
        }
        head.localRotation = Quaternion.Euler(-8 * amp, 0, lean * 6);
    }

    public void ScrollStatic(float dz)
    {
        foreach (var s in segs)
        {
            s.position += new Vector3(0, 0, -dz);
            if (s.position.z < -SegLen * 1.5f) s.position += new Vector3(0, 0, SegLen * SegCount);
        }
    }

    // ---------- obstacles ----------
    public Obstacle SpawnTrain(int lane, float z, float len)
    {
        var tr = new GameObject("Train").transform; tr.SetParent(transform);
        Color[] liveries = { Hex("#e63946"), Hex("#ffb703"), Hex("#219ebc"), Hex("#2a9d8f"), Hex("#8338ec") };
        var col = liveries[Random.Range(0, liveries.Length)];
        var bodyM = Mat(col, 0.55f, 0.2f); var white = Mat(Hex("#f1f1f1"), 0.5f); var glass = Mat(Hex("#1d3557"), 0.95f, 0.3f);
        var dark = Mat(Hex("#2b2d42"), 0.3f);
        Prim(PrimitiveType.Cube, tr, new Vector3(0, 1.75f, 0), new Vector3(2.2f, 2.9f, len), bodyM);
        Prim(PrimitiveType.Cube, tr, new Vector3(0, 3.28f, 0), new Vector3(1.9f, 0.18f, len - 0.4f), white);
        Prim(PrimitiveType.Cube, tr, new Vector3(0, 1.05f, 0), new Vector3(2.24f, 0.25f, len), white);
        Prim(PrimitiveType.Cube, tr, new Vector3(0, 0.25f, 0), new Vector3(1.8f, 0.5f, len - 1f), dark);
        float f = -len / 2 - 0.01f;
        Prim(PrimitiveType.Cube, tr, new Vector3(0, 2.25f, f), new Vector3(1.6f, 0.95f, 0.05f), glass, false);
        Prim(PrimitiveType.Sphere, tr, new Vector3(-0.65f, 1.35f, f), Vector3.one * 0.28f, Mat(Hex("#fff3b0"), 0.9f, 0, Hex("#fff3b0")), false);
        Prim(PrimitiveType.Sphere, tr, new Vector3(0.65f, 1.35f, f), Vector3.one * 0.28f, Mat(Hex("#fff3b0"), 0.9f, 0, Hex("#fff3b0")), false);
        for (int s = -1; s <= 1; s += 2)
            for (float wz = -len / 2 + 1.6f; wz < len / 2 - 1f; wz += 2.4f)
                Prim(PrimitiveType.Cube, tr, new Vector3(s * 1.11f, 2.2f, wz), new Vector3(0.04f, 0.9f, 1.5f), glass, false);
        return Add(Obstacle.Kind.Train, lane, z, len, tr);
    }

    public Obstacle SpawnBarrier(int lane, float z, bool high)
    {
        var tr = new GameObject(high ? "HighBar" : "LowBar").transform; tr.SetParent(transform);
        var red = Mat(Hex("#e63946"), 0.5f); var white = Mat(Color.white, 0.5f); var post = Mat(Hex("#8d99ae"), 0.6f, 0.5f);
        if (!high)
        {
            for (int s = -1; s <= 1; s += 2) Prim(PrimitiveType.Cube, tr, new Vector3(s * 0.95f, 0.45f, 0), new Vector3(0.14f, 0.9f, 0.14f), post);
            for (int i = 0; i < 6; i++) Prim(PrimitiveType.Cube, tr, new Vector3(-0.85f + i * 0.34f, 0.78f, 0), new Vector3(0.34f, 0.32f, 0.16f), i % 2 == 0 ? red : white);
        }
        else
        {
            for (int s = -1; s <= 1; s += 2) Prim(PrimitiveType.Cube, tr, new Vector3(s * 1.0f, 1.4f, 0), new Vector3(0.16f, 2.8f, 0.16f), post);
            for (int r = 0; r < 2; r++)
                for (int i = 0; i < 6; i++) Prim(PrimitiveType.Cube, tr, new Vector3(-0.85f + i * 0.34f, 1.95f + r * 0.4f, 0), new Vector3(0.34f, 0.4f, 0.16f), (i + r) % 2 == 0 ? red : white);
            Prim(PrimitiveType.Cube, tr, new Vector3(0, 2.75f, 0), new Vector3(2.2f, 0.1f, 0.2f), post);
        }
        return Add(high ? Obstacle.Kind.High : Obstacle.Kind.Low, lane, z, 0.4f, tr);
    }

    Obstacle Add(Obstacle.Kind k, int lane, float z, float len, Transform tr)
    {
        var o = new Obstacle { kind = k, lane = lane, z = z, len = len, tr = tr };
        tr.position = new Vector3(lane * LaneW, 0, z);
        obstacles.Add(o); return o;
    }

    public void SpawnCoin(int lane, float z, float y = 1f)
    {
        Transform c;
        if (coinPool.Count > 0) { c = coinPool.Pop(); c.gameObject.SetActive(true); }
        else
        {
            c = Prim(PrimitiveType.Cylinder, transform, Vector3.zero, new Vector3(0.62f, 0.05f, 0.62f), Mat(Hex("#ffc300"), 0.85f, 0.9f, Hex("#7a5200")));
            c.localRotation = Quaternion.Euler(90, 0, 0);
        }
        c.position = new Vector3(lane * LaneW, y, z);
        coins.Add(c);
    }

    public void RecycleCoin(int i) { var c = coins[i]; c.gameObject.SetActive(false); coinPool.Push(c); coins.RemoveAt(i); }

    public void Clear()
    {
        foreach (var o in obstacles) Destroy(o.tr.gameObject);
        obstacles.Clear();
        for (int i = coins.Count - 1; i >= 0; i--) RecycleCoin(i);
    }
}

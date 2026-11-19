using System.Collections.Generic;
using UnityEngine;

/// Turns raw body joints into game actions with calibration, hysteresis and hold times,
/// so small sways or arm waves don't trigger jumps or lane changes.
public class MotionControls
{
    public enum Phase { NoBody, Calibrating, Ready }
    public Phase State { get; private set; } = Phase.NoBody;
    public float CalibrationProgress { get; private set; }

    // Outputs read by the game
    public int Lane { get; private set; }            // -1, 0, 1 (user's left / centre / right)
    public bool JumpPressed { get; private set; }    // true for one frame
    public bool Ducking { get; private set; }
    public float Effort { get; private set; }        // 0..1 run-in-place intensity
    public float LostFor { get; private set; }

    // Tunables (torso-length units)
    const float LaneEnter = 0.42f, LaneExit = 0.22f, LaneHold = 0.12f;
    const float JumpRise = 0.20f, JumpRearm = 0.08f, JumpCooldown = 0.45f;
    const float DuckDrop = 0.32f, DuckExit = 0.18f, DuckHold = 0.10f;

    float baseHipX, baseShY, baseHipY, torso;
    readonly List<Vector3> calib = new List<Vector3>();
    float laneCandidateT; int laneCandidate;
    bool jumpArmed = true; float lastJump = -10f;
    float duckT;
    readonly Queue<(float t, float v)> legHist = new Queue<(float, float)>();
    float lastSeen;

    public void Reset()
    {
        State = Phase.NoBody; calib.Clear(); CalibrationProgress = 0;
        Lane = 0; Ducking = false; Effort = 0; jumpArmed = true; legHist.Clear();
    }

    static bool Seen(int i) => PoseSource.Conf[i] > 0.3f;

    public void Update(float now)
    {
        JumpPressed = false;
        if (!PoseSource.HasNewFrame) { LostFor = now - lastSeen; if (LostFor > 0.6f) Effort = Mathf.MoveTowards(Effort, 0, Time.deltaTime); return; }

        var P = PoseSource.Pos;
        bool body = Seen(PoseSource.LSh) && Seen(PoseSource.RSh) && Seen(PoseSource.LHip) && Seen(PoseSource.RHip);
        if (!body) { LostFor = now - lastSeen; if (State == Phase.Calibrating) { calib.Clear(); CalibrationProgress = 0; } return; }
        lastSeen = now; LostFor = 0;

        float shY = (P[PoseSource.LSh].y + P[PoseSource.RSh].y) * 0.5f;
        float hipY = (P[PoseSource.LHip].y + P[PoseSource.RHip].y) * 0.5f;
        float hipX = (P[PoseSource.LHip].x + P[PoseSource.RHip].x) * 0.5f;
        float t = Mathf.Max(0.05f, hipY - shY);

        if (State != Phase.Ready)
        {
            State = Phase.Calibrating;
            // need to stand still: reset if the body moves too much during calibration
            if (calib.Count > 0)
            {
                var first = calib[0];
                if (Mathf.Abs(hipX - first.x) > 0.06f || Mathf.Abs(shY - first.y) > 0.05f) calib.Clear();
            }
            calib.Add(new Vector3(hipX, shY, hipY));
            CalibrationProgress = calib.Count / 40f;
            if (calib.Count >= 40)
            {
                Vector3 a = Vector3.zero; foreach (var c in calib) a += c; a /= calib.Count;
                baseHipX = a.x; baseShY = a.y; baseHipY = a.z; torso = Mathf.Max(0.05f, baseHipY - baseShY);
                State = Phase.Ready;
            }
            return;
        }

        // ---- lanes: hip centre offset; camera is unmirrored so user's right = smaller x
        float lateral = -(hipX - baseHipX) / torso;
        int want = Lane;
        if (lateral > LaneEnter) want = 1;
        else if (lateral < -LaneEnter) want = -1;
        else if (Mathf.Abs(lateral) < LaneExit) want = 0;
        if (want != Lane)
        {
            if (want != laneCandidate) { laneCandidate = want; laneCandidateT = now; }
            else if (now - laneCandidateT >= LaneHold) Lane = want;
        }
        else laneCandidate = Lane;

        // ---- jump: shoulders AND hips rise together (arm waves don't count)
        float shRise = (baseShY - shY) / torso, hipRise = (baseHipY - hipY) / torso;
        if (jumpArmed && shRise > JumpRise && hipRise > JumpRise * 0.8f && now - lastJump > JumpCooldown)
        { JumpPressed = true; jumpArmed = false; lastJump = now; }
        if (!jumpArmed && shRise < JumpRearm && hipRise < JumpRearm) jumpArmed = true;

        // ---- duck: shoulders drop well below standing height, held briefly
        float drop = (shY - baseShY) / torso;
        if (!Ducking)
        {
            if (drop > DuckDrop) { if (duckT <= 0) duckT = now; else if (now - duckT >= DuckHold) Ducking = true; }
            else duckT = 0;
        }
        else if (drop < DuckExit) { Ducking = false; duckT = 0; }

        // ---- run in place: oscillation of knee height difference over the last second
        if (Seen(PoseSource.LKnee) && Seen(PoseSource.RKnee))
        {
            legHist.Enqueue((now, (P[PoseSource.LKnee].y - P[PoseSource.RKnee].y) / torso));
            while (legHist.Count > 0 && now - legHist.Peek().t > 1f) legHist.Dequeue();
            float mean = 0; foreach (var h in legHist) mean += h.v; mean /= legHist.Count;
            float variance = 0; foreach (var h in legHist) variance += (h.v - mean) * (h.v - mean); variance /= legHist.Count;
            float target = Mathf.Clamp01(Mathf.Sqrt(variance) / 0.12f);
            Effort = Mathf.Lerp(Effort, target, 0.25f);
        }

        // slowly follow the user's standing height/position when calm (they drift during a workout)
        if (Mathf.Abs(shRise) < 0.08f && Mathf.Abs(drop) < 0.08f && Lane == 0)
        {
            baseShY = Mathf.Lerp(baseShY, shY, 0.01f);
            baseHipY = Mathf.Lerp(baseHipY, hipY, 0.01f);
            baseHipX = Mathf.Lerp(baseHipX, hipX, 0.005f);
        }
    }
}

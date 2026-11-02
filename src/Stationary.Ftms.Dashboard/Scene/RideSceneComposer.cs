using System.Numerics;

using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Scene;

// Values match the material constants in Shaders/scene.frag.
internal enum SceneMaterial
{
    Solid,
    Grid,
    Neon,
    Glow,
    Streak,
    Sky,
    Track,
}

internal static class SceneMaterialExtensions
{
    public static bool IsAdditive(this SceneMaterial material) => material is SceneMaterial.Glow or SceneMaterial.Streak;

    // Glossy surfaces blend over the mirrored scene drawn beneath them, which is what makes them look wet.
    public static bool IsReflective(this SceneMaterial material) => material is SceneMaterial.Grid or SceneMaterial.Track;
}

// Color.W is the neon intensity; Param is a per-material value (such as a fade) passed to the shader.
internal readonly record struct SceneDraw(SceneMesh Mesh, SceneMaterial Material, Matrix4x4 Model, Vector4 Color, float Param = 0f);

internal readonly record struct SceneCamera(
    Matrix4x4 ViewProjection,
    Vector3 Position,
    Vector3 FogColor,
    float FogDensity,
    float Time,
    float Scroll,
    float Beat,
    float Energy,
    float HeartPhase,
    float HeartRate,
    float HeartPulse,
    Vector3 ZoneColor);

// Builds a neon, Tron-like ride scene each frame from the rider's point of view: the camera glides along the centre
// of a wet light track that mirrors the city, under a synthwave sun and a live ECG trace of the rider's heart.
// Gamification is driven by real session achievements: a milestone arch marks the next distance goal, fireworks
// burst when an achievement or medal is earned, the interval portal is coloured by the medal on pace, and the light
// walls rise as the on-target streak grows. Two rhythms drive the scene: the floor and track pulse on every pedal
// stroke (cadence), while the rings and the ECG pulse lub-dub with the heart and take the heart-rate zone colour.
// The 1 Hz ride frames are smoothed here so motion stays fluid at the display refresh rate.
internal sealed class RideSceneComposer
{
    private const float FogDensity = 0.0048f;
    private const float EyeHeight = 1.15f;
    private const float TrackHalfWidth = 2.1f;
    private const float TrackLength = 1400f;
    private const float TrackCenter = -500f;
    private const float BurstLifetime = 3f;
    private const int SparksPerBurst = 42;
    private const double DashSpacing = 6d;
    private const double RingSpacing = 30d;
    private const double TowerSpacing = 14d;
    private const double SkylineSpacing = 70d;
    private const double ParticleSpacing = 2.5d;
    private const double TickSpacing = 100d;
    private const double VisibleDistance = 320d;
    private const double StreakForFullEnergy = 120d;
    private static readonly Vector3 HorizonColor = new(0.1f, 0.02f, 0.18f);
    private static readonly Vector4 Cyan = new(0.1f, 0.92f, 1f, 1f);
    private static readonly Vector4 Magenta = new(1f, 0.16f, 0.78f, 1f);
    private static readonly Vector4 GridBlue = new(0.12f, 0.5f, 1f, 0.75f);
    private static readonly Vector4 MilestoneWhite = new(0.75f, 0.95f, 1f, 1f);

    private readonly List<Burst> bursts = [];
    private double distance;
    private double gateDistance = double.NaN;
    private double heartPhase;
    private float speed;
    private float cadence;
    private float heartRate;
    private float crankAngle;
    private float time;
    private float energy;
    private int? seenAchievements;
    private int seenMedals;
    private Vector3 accent = new(0.35f, 0.45f, 0.75f);
    private Vector3 zone = new(0.55f, 0.25f, 1f);

    public void Advance(RideSceneFrame frame, float seconds)
    {
        ArgumentNullException.ThrowIfNull(frame);
        time += seconds;
        var blend = float.Min(1f, seconds * 1.6f);
        speed += ((float)frame.SpeedKilometersPerHour - speed) * blend;
        cadence += ((float)frame.CadenceRpm - cadence) * blend;
        heartRate += ((frame.HeartRate ?? 0) - heartRate) * float.Min(1f, seconds * 0.8f);
        heartPhase += heartRate / 60f * seconds;
        distance += speed / 3.6f * seconds;

        // Follow the recorded distance so milestone arches line up with the HUD, without visible jumps.
        var drift = frame.DistanceMeters - distance;
        distance = double.Abs(drift) > 40d ? frame.DistanceMeters : distance + (drift * double.Min(1d, seconds * 0.3d));
        crankAngle = (crankAngle + (cadence / 60f * 2f * float.Pi * seconds)) % (2f * float.Pi);
        var streak = frame.HasTarget ? (float)double.Clamp(frame.StreakSeconds / StreakForFullEnergy, 0d, 1d) : 0f;
        energy += (streak - energy) * float.Min(1f, seconds * 1.2f);
        accent = Vector3.Lerp(accent, GetStatusColor(frame.Status), float.Min(1f, seconds * 3f));
        var zoneColor = GetZoneColor(frame.HeartRateZoneIndex, frame.HeartRateZoneCount);
        zone = Vector3.Lerp(zone, new(zoneColor.X, zoneColor.Y, zoneColor.Z), float.Min(1f, seconds * 2f));

        TrackAchievements(frame);
        bursts.RemoveAll(burst => time - burst.StartTime > BurstLifetime);

        if (frame is { HasTarget: true, SegmentRemainingSeconds: { } remaining })
        {
            var target = distance + (remaining * double.Max(speed / 3.6d, 4d));
            gateDistance = double.IsNaN(gateDistance) || double.Abs(target - gateDistance) > 60d || gateDistance < distance - 5d
                ? target
                : gateDistance + ((target - gateDistance) * double.Min(1d, seconds * 1.5d));
        }
        else
        {
            gateDistance = double.NaN;
        }
    }

    public SceneCamera Compose(RideSceneFrame frame, float aspectRatio, List<SceneDraw> draws)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(draws);
        draws.Clear();

        // One beat per pedal stroke, fading out when the rider stops pedalling.
        var beat = float.Pow(float.Max(0f, float.Cos(2f * crankAngle)), 6f) * float.Clamp(cadence / 30f, 0f, 1f);
        var pulse = GetHeartPulse();
        var eye = new Vector3(0f, EyeHeight + (beat * 0.02f), 0f);
        var boost = 1f + energy;

        draws.Add(new(SceneMesh.Sphere, SceneMaterial.Sky, Box(new(2000f), eye), Magenta with { W = 0.9f + (energy * 0.6f) }));
        AddTrack(draws, boost);
        AddSkyline(draws);
        AddTowers(draws, beat);
        AddRings(draws, pulse);
        AddLasers(draws, boost);
        AddDistanceTicks(draws);
        AddMilestoneArch(frame, draws, beat);
        AddGate(frame, draws, beat);
        AddBursts(draws);
        AddParticles(draws);

        var fieldOfView = 64f + (float.Min(speed, 45f) / 45f * 12f);
        var view = Matrix4x4.CreateLookAt(eye, new Vector3(0f, 0.8f, -40f), Vector3.UnitY);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(float.DegreesToRadians(fieldOfView), float.Max(aspectRatio, 0.1f), 0.1f, 2500f);
        projection.M22 = -projection.M22;
        var scroll = (float)(distance % 200d);
        return new(view * projection, eye, HorizonColor, FogDensity, time, scroll, beat, energy, (float)(heartPhase % 1000d), heartRate, pulse, zone);
    }

    // Lub-dub: a strong beat followed by a softer echo, once per heartbeat.
    private float GetHeartPulse()
    {
        if (heartRate < 30f)
        {
            return 0f;
        }

        var phase = (float)(heartPhase % 1d);
        var lub = float.Exp(-float.Pow(phase * 11f, 2f));
        var dub = 0.6f * float.Exp(-float.Pow((phase - 0.2f) * 13f, 2f));
        return float.Max(lub + float.Exp(-float.Pow((phase - 1f) * 11f, 2f)), dub);
    }

    private void TrackAchievements(RideSceneFrame frame)
    {
        if (seenAchievements is not { } seen || frame.AchievementCount < seen || frame.MedalCount < seenMedals)
        {
            seenAchievements = frame.AchievementCount;
            seenMedals = frame.MedalCount;
            return;
        }

        if (frame.MedalCount > seenMedals)
        {
            var color = GetMedalColor(frame.LatestMedal) with { W = 1f };
            Launch(color, 1.5f, 0f);
            Launch(Cyan, 1f, -9f);
            Launch(Magenta, 1f, 9f);
        }

        for (var index = seen; index < frame.AchievementCount; index++)
        {
            Launch(index % 2 == 0 ? Cyan : Magenta, 1.1f, (index % 3 - 1) * 7f);
        }

        seenAchievements = frame.AchievementCount;
        seenMedals = frame.MedalCount;
    }

    private void Launch(Vector4 color, float scale, float x) =>
        bursts.Add(new(distance + 45d + (bursts.Count * 6d), time + (bursts.Count * 0.35f), color, scale, x, bursts.Count + (int)time));

    private void AddTrack(List<SceneDraw> draws, float boost)
    {
        var accentColor = new Vector4(accent, 1f);
        draws.Add(new(SceneMesh.Quad, SceneMaterial.Grid, Plane(Vector3.UnitX * TrackLength * 2f, -Vector3.UnitZ * TrackLength, new(0f, 0f, TrackCenter)), GridBlue));
        draws.Add(new(SceneMesh.Quad, SceneMaterial.Track, Plane(Vector3.UnitX * TrackHalfWidth * 2f, -Vector3.UnitZ * TrackLength, new(0f, 0.004f, TrackCenter)), accentColor with { W = 0.6f * boost }));

        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            var x = side * (TrackHalfWidth - 0.04f);
            draws.Add(new(SceneMesh.Cube, SceneMaterial.Neon, Box(new(0.07f, 0.07f, TrackLength), new(x, 0.05f, TrackCenter)), accentColor with { W = 1.3f * boost }));
            draws.Add(new(SceneMesh.Quad, SceneMaterial.Streak, Plane(Vector3.UnitX * 1.6f, -Vector3.UnitZ * TrackLength, new(x, 0.035f, TrackCenter)), accentColor with { W = 0.5f * boost }));

            // Tron light walls rise along the track edges as the on-target streak grows.
            if (energy > 0.02f)
            {
                var height = 3.2f * energy;
                draws.Add(new(SceneMesh.Quad, SceneMaterial.Streak, Plane(Vector3.UnitY * height, -Vector3.UnitZ * TrackLength, new(x, 0f, TrackCenter)), accentColor with { W = 0.3f * energy }));
            }
        }

        foreach (var offset in Repeat(DashSpacing, -12d))
        {
            draws.Add(new(SceneMesh.Cube, SceneMaterial.Neon, Box(new(0.06f, 0.04f, 2.2f), new(0f, 0.03f, -offset)), Magenta with { W = 0.7f }));
        }
    }

    private void AddSkyline(List<SceneDraw> draws)
    {
        var first = (long)double.Floor(distance / SkylineSpacing) - 1;
        for (var index = first; index * SkylineSpacing < distance + 900d; index++)
        {
            for (var side = -1; side <= 1; side += 2)
            {
                var hash = Hash(index, side * 13);
                var offset = (float)((index * SkylineSpacing) - distance + (hash * 30d));
                var x = side * (70f + (Hash(index, side * 17) * 160f));
                var height = 30f + (hash * hash * 120f);
                var width = 14f + (Hash(index, side * 19) * 26f);
                var color = hash > 0.5f ? Cyan : Magenta;
                draws.Add(new(SceneMesh.Cube, SceneMaterial.Solid, Box(new(width, height, width), new(x, height * 0.5f, -offset)), color with { W = 0.55f }));
            }
        }
    }

    private void AddTowers(List<SceneDraw> draws, float beat)
    {
        var first = (long)double.Floor(distance / TowerSpacing) - 1;
        for (var index = first; index * TowerSpacing < distance + VisibleDistance; index++)
        {
            for (var side = -1; side <= 1; side += 2)
            {
                var hash = Hash(index, side);
                if (hash < 0.3f)
                {
                    continue;
                }

                var offset = (float)((index * TowerSpacing) - distance + (hash * 5d));
                var x = side * (6f + (Hash(index, side * 7) * 22f));
                var height = 2f + (hash * hash * 16f);
                var width = 1.2f + (Hash(index, side * 3) * 2.4f);
                var color = Hash(index, side * 5) > 0.5f ? Cyan : Magenta;
                draws.Add(new(SceneMesh.Cube, SceneMaterial.Solid, Box(new(width, height, width), new(x, height * 0.5f, -offset)), color with { W = 0.9f + (beat * 0.4f) }));
                draws.Add(new(SceneMesh.Quad, SceneMaterial.Glow, Plane(Vector3.UnitX * width * 4f, Vector3.UnitY * 3f, new(x, 0.05f, -offset)), color with { W = 0.35f }));
            }
        }
    }

    // Rings take the heart-rate zone colour and swell lub-dub with every heartbeat.
    private void AddRings(List<SceneDraw> draws, float pulse)
    {
        const int Sides = 8;
        var color = new Vector4(zone, 1f);
        var first = (long)double.Floor(distance / RingSpacing);
        for (var index = first; index * RingSpacing < distance + VisibleDistance; index++)
        {
            var offset = (float)((index * RingSpacing) - distance);
            if (offset < -2f)
            {
                continue;
            }

            var radius = 6.5f * (1f + (pulse * 0.06f));
            var spin = (index * 0.39f) + (time * 0.25f * (index % 2 == 0 ? 1f : -1f));
            var side = 2f * radius * float.Tan(float.Pi / Sides);
            var center = new Vector3(0f, 2.2f, -offset);
            for (var bar = 0; bar < Sides; bar++)
            {
                var angle = spin + (bar * 2f * float.Pi / Sides);
                var model = Matrix4x4.CreateScale(side * 0.86f, 0.14f, 0.14f)
                    * Matrix4x4.CreateTranslation(0f, radius, 0f)
                    * Matrix4x4.CreateRotationZ(angle)
                    * Matrix4x4.CreateTranslation(center);
                draws.Add(new(SceneMesh.Cube, SceneMaterial.Neon, model, color with { W = 1.2f + (pulse * 1.6f) }));
            }

            draws.Add(new(SceneMesh.Quad, SceneMaterial.Glow, Box(new(radius * 2.6f), center), color with { W = 0.08f + (pulse * 0.12f) }));
        }
    }

    private void AddLasers(List<SceneDraw> draws, float boost)
    {
        for (var laser = 0; laser < 6; laser++)
        {
            var side = laser % 2 == 0 ? -1f : 1f;
            var baseX = side * (30f + (laser / 2 * 45f));
            var angle = side * (0.25f + (float.Sin((time * 0.4f) + (laser * 1.7f)) * 0.35f));
            var color = laser % 4 < 2 ? Cyan : Magenta;
            var rotation = Matrix4x4.CreateRotationZ(angle);
            var model = Matrix4x4.CreateScale(0.6f, 900f, 0.6f) * Matrix4x4.CreateTranslation(0f, 450f, 0f) * rotation * Matrix4x4.CreateTranslation(baseX, 0f, -300f);
            draws.Add(new(SceneMesh.Cube, SceneMaterial.Neon, model, color with { W = 2.2f }));
            var glow = Matrix4x4.CreateScale(14f, 900f, 1f) * Matrix4x4.CreateTranslation(0f, 450f, 0f) * rotation * Matrix4x4.CreateTranslation(baseX, 0f, -299f);
            draws.Add(new(SceneMesh.Quad, SceneMaterial.Streak, glow, color with { W = 0.45f * boost }));
        }
    }

    // A light line crosses the track every 100 m, brighter every 500 m, so progress is felt between milestones.
    private void AddDistanceTicks(List<SceneDraw> draws)
    {
        var first = (long)double.Ceiling(distance / TickSpacing);
        for (var index = first; index * TickSpacing < distance + VisibleDistance; index++)
        {
            var offset = (float)((index * TickSpacing) - distance);
            var major = index % 5 == 0;
            var color = major ? Magenta with { W = 1.6f } : Cyan with { W = 0.9f };
            draws.Add(new(SceneMesh.Cube, SceneMaterial.Neon, Box(new(TrackHalfWidth * 2f, 0.03f, major ? 0.22f : 0.1f), new(0f, 0.02f, -offset)), color));
            draws.Add(new(SceneMesh.Quad, SceneMaterial.Streak, Plane(-Vector3.UnitZ * 1.4f, Vector3.UnitX * TrackHalfWidth * 2.4f, new(0f, 0.03f, -offset)), color with { W = color.W * 0.5f }));
        }
    }

    // A huge neon arch marks the next distance milestone; it brightens as the rider closes in.
    private void AddMilestoneArch(RideSceneFrame frame, List<SceneDraw> draws, float beat)
    {
        var offset = (float)(frame.NextDistanceMilestoneMeters - distance);
        if (offset is < -2f or > 900f)
        {
            return;
        }

        const int Segments = 9;
        var closeness = 1f - float.Clamp(offset / 300f, 0f, 1f);
        var intensity = 1.1f + (closeness * 1.4f) + (beat * 0.3f);
        var z = -offset;
        var radius = 10f;
        for (var segment = 0; segment < Segments; segment++)
        {
            var a0 = float.Pi * segment / Segments;
            var a1 = float.Pi * (segment + 1) / Segments;
            var p0 = new Vector3(float.Cos(a0) * radius, float.Sin(a0) * radius * 0.85f, z);
            var p1 = new Vector3(float.Cos(a1) * radius, float.Sin(a1) * radius * 0.85f, z);
            var color = Vector4.Lerp(Cyan, Magenta, segment / (float)(Segments - 1));
            draws.Add(new(SceneMesh.Cube, SceneMaterial.Neon, Beam(p0, p1, 0.35f), color with { W = intensity }));
            var inner0 = p0 * new Vector3(0.82f, 0.82f, 1f);
            var inner1 = p1 * new Vector3(0.82f, 0.82f, 1f);
            draws.Add(new(SceneMesh.Cube, SceneMaterial.Neon, Beam(inner0 with { Z = z - 1.5f }, inner1 with { Z = z - 1.5f }, 0.12f), MilestoneWhite with { W = intensity * 0.8f }));
        }

        draws.Add(new(SceneMesh.Quad, SceneMaterial.Glow, Box(new(30f, 22f, 1f), new(0f, 4f, z - 0.5f)), MilestoneWhite with { W = 0.12f + (closeness * 0.25f) }));
        draws.Add(new(SceneMesh.Quad, SceneMaterial.Streak, Plane(-Vector3.UnitZ * 2.5f, Vector3.UnitX * radius * 2f, new(0f, 0.04f, z)), MilestoneWhite with { W = 1.5f + closeness }));
    }

    private void AddGate(RideSceneFrame frame, List<SceneDraw> draws, float beat)
    {
        if (double.IsNaN(gateDistance))
        {
            return;
        }

        var offset = (float)(gateDistance - distance);
        if (offset is < -1f or > (float)VisibleDistance)
        {
            return;
        }

        var color = GetMedalColor(frame.ProjectedMedal);
        var z = -offset;
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            draws.Add(new(SceneMesh.Cube, SceneMaterial.Neon, Box(new(0.22f, 5f, 0.22f), new(side * 3.2f, 2.5f, z)), color));
            draws.Add(new(SceneMesh.Quad, SceneMaterial.Streak, Box(new(1.6f, 6f, 1f), new(side * 3.2f, 2.5f, z + 0.05f)), color with { W = 0.8f }));
        }

        draws.Add(new(SceneMesh.Cube, SceneMaterial.Neon, Box(new(6.6f, 0.22f, 0.22f), new(0f, 5f, z)), color));

        // An inner diamond spins faster as the portal approaches; the finish line lights the track.
        var spin = (time * 0.8f) + (2f / float.Max(offset, 1f));
        var size = 2.1f * (1f + (beat * 0.08f));
        for (var bar = 0; bar < 4; bar++)
        {
            var model = Matrix4x4.CreateScale(size * 1.414f, 0.12f, 0.12f)
                * Matrix4x4.CreateTranslation(0f, size * 0.707f, 0f)
                * Matrix4x4.CreateRotationZ(spin + (float.Pi / 4f) + (bar * float.Pi / 2f))
                * Matrix4x4.CreateTranslation(0f, 2.6f, z);
            draws.Add(new(SceneMesh.Cube, SceneMaterial.Neon, model, color));
        }

        draws.Add(new(SceneMesh.Quad, SceneMaterial.Glow, Box(new(9f), new(0f, 2.6f, z - 0.1f)), color with { W = 0.35f }));
        draws.Add(new(SceneMesh.Quad, SceneMaterial.Streak, Plane(-Vector3.UnitZ * 1.2f, Vector3.UnitX * TrackHalfWidth * 2f, new(0f, 0.04f, z)), color with { W = 1.4f }));
    }

    // Each achievement launches a firework ahead of the rider; medals launch a bigger burst in the medal colour.
    private void AddBursts(List<SceneDraw> draws)
    {
        foreach (var burst in bursts)
        {
            var age = time - burst.StartTime;
            if (age < 0f)
            {
                continue;
            }

            var life = age / BurstLifetime;
            var fade = (1f - life) * (1f - life);
            var center = new Vector3(burst.X, 9f * burst.Scale, -(float)(burst.Distance - distance));
            if (age < 0.35f)
            {
                draws.Add(new(SceneMesh.Quad, SceneMaterial.Glow, Box(new(18f * burst.Scale), center), Vector4.Lerp(Vector4.One, burst.Color, float.Min(1f, age * 3f)) with { W = 1.2f * (1f - (age / 0.35f)) }));
            }

            var travel = 11f * burst.Scale * (1f - float.Exp(-age * 2.2f));
            for (var spark = 0; spark < SparksPerBurst; spark++)
            {
                var u = (Hash(burst.Seed, spark * 3) * 2f) - 1f;
                var angle = Hash(burst.Seed, (spark * 3) + 1) * 2f * float.Pi;
                var ring = float.Sqrt(1f - (u * u));
                var direction = new Vector3(ring * float.Cos(angle), u, ring * float.Sin(angle));
                var position = center + (direction * travel) - new Vector3(0f, 1.6f * age * age, 0f);
                var tint = Hash(burst.Seed, (spark * 3) + 2) > 0.8f ? Vector4.One : burst.Color;
                var twinkle = 0.75f + (0.25f * float.Sin((time * 30f) + spark));
                draws.Add(new(SceneMesh.Quad, SceneMaterial.Glow, Box(new(0.9f * burst.Scale), position), tint with { W = 2.2f * fade * twinkle }));
                draws.Add(new(SceneMesh.Cube, SceneMaterial.Neon, Box(new(0.09f), position), tint with { W = 3f * fade }));
            }
        }
    }

    // Light dust streaks past the camera; it stretches with speed.
    private void AddParticles(List<SceneDraw> draws)
    {
        if (speed < 1f)
        {
            return;
        }

        var length = 0.15f + (float.Min(speed, 50f) * 0.06f);
        var intensity = float.Min(1f, speed / 25f) * (0.5f + energy);
        var first = (long)double.Floor(distance / ParticleSpacing);
        for (var index = first; index * ParticleSpacing < distance + 90d; index++)
        {
            var offset = (float)((index * ParticleSpacing) - distance);
            var angle = Hash(index, 23) * 2f * float.Pi;
            var radius = 2.5f + (Hash(index, 29) * 7f);
            var position = new Vector3(float.Cos(angle) * radius, float.Max(0.3f, EyeHeight + (float.Sin(angle) * radius * 0.6f)), -offset);
            var color = Hash(index, 31) > 0.5f ? Cyan : Vector4.One;
            draws.Add(new(SceneMesh.Cube, SceneMaterial.Neon, Box(new(0.025f, 0.025f, length), position), color with { W = intensity }));
        }
    }

    private IEnumerable<float> Repeat(double spacing, double behind)
    {
        var first = (long)double.Floor((distance + behind) / spacing);
        for (var index = first; index * spacing < distance + VisibleDistance; index++)
        {
            yield return (float)((index * spacing) - distance);
        }
    }

    // A box stretched between two points, used for arch segments.
    private static Matrix4x4 Beam(Vector3 from, Vector3 to, float thickness)
    {
        var delta = to - from;
        return Matrix4x4.CreateScale(delta.Length() + thickness, thickness, thickness)
            * Matrix4x4.CreateRotationZ(float.Atan2(delta.Y, delta.X))
            * Matrix4x4.CreateTranslation((from + to) * 0.5f);
    }

    private static Matrix4x4 Box(Vector3 scale, Vector3 position) => Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(position);

    // Places the unit quad so its local X and Y axes map to the given world vectors (lengths are the sizes).
    private static Matrix4x4 Plane(Vector3 xAxis, Vector3 yAxis, Vector3 position)
    {
        var normal = Vector3.Normalize(Vector3.Cross(xAxis, yAxis));
        return new(
            xAxis.X, xAxis.Y, xAxis.Z, 0f,
            yAxis.X, yAxis.Y, yAxis.Z, 0f,
            normal.X, normal.Y, normal.Z, 0f,
            position.X, position.Y, position.Z, 1f);
    }

    private static float Hash(long index, int salt)
    {
        var value = unchecked((ulong)((index * 374_761_393L) + (salt * 668_265_263L)));
        value = (value ^ (value >> 13)) * 1_274_126_177UL;
        return (value >> 40) / (float)(1UL << 24);
    }

    private static Vector3 GetStatusColor(RideGameTargetStatus status) => status switch
    {
        RideGameTargetStatus.OnTarget => new(0.1f, 0.95f, 1f),
        RideGameTargetStatus.TooLow => new(1f, 0.62f, 0.1f),
        RideGameTargetStatus.TooHigh => new(1f, 0.12f, 0.25f),
        RideGameTargetStatus.Riding => new(0.25f, 0.5f, 1f),
        _ => new(0.35f, 0.45f, 0.75f),
    };

    private static Vector4 GetMedalColor(RideGameMedal medal) => medal switch
    {
        RideGameMedal.Gold => new(1f, 0.76f, 0.12f, 1.6f),
        RideGameMedal.Silver => new(0.78f, 0.88f, 1f, 1.4f),
        RideGameMedal.Bronze => new(1f, 0.46f, 0.16f, 1.4f),
        _ => new(0.9f, 0.12f, 0.2f, 0.8f),
    };

    private static Vector4 GetZoneColor(int zoneIndex, int zoneCount)
    {
        if (zoneIndex < 0 || zoneCount <= 0)
        {
            return new(0.55f, 0.25f, 1f, 1f);
        }

        ReadOnlySpan<Vector4> palette =
        [
            new(0.6f, 0.7f, 1f, 1f),
            new(0.15f, 0.5f, 1f, 1f),
            new(0.15f, 1f, 0.45f, 1f),
            new(1f, 0.6f, 0.1f, 1f),
            new(1f, 0.12f, 0.3f, 1f),
        ];
        return palette[int.Clamp(zoneIndex * palette.Length / zoneCount, 0, palette.Length - 1)];
    }

    private readonly record struct Burst(double Distance, float StartTime, Vector4 Color, float Scale, float X, int Seed);
}
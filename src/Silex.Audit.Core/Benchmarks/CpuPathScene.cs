using System.Numerics;

namespace Silex.Audit.Core.Benchmarks;

// Independent implementation of the numerical scene specified in docs/cpu-workload-spec.md.
// Same auditor/implementer: no claim of strict clean-room separation.
internal sealed class CpuPathScene
{
    private enum Surface { Matte, Metal, Glass }
    private readonly record struct Ball(Vector3 Center, float Radius, Surface Surface, float Reflectance, float Blur = 0);
    private readonly record struct Ray(Vector3 Origin, Vector3 Direction);
    private readonly record struct Contact(float Distance, int Object, bool Light);

    private static readonly Ball[] Objects = CreateObjects();
    private static readonly Vector3 LightCenter = new(-4, 7, -3);
    private const float LightRadius = 2.2f;
    private const float LightRadiance = 9;
    private static readonly Vector3 Eye = new(0, 2.1f, -7.2f);
    private readonly Vector3 corner;
    private readonly Vector3 horizontal;
    private readonly Vector3 vertical;

    public CpuPathScene(int width, int height)
    {
        var backward = Vector3.Normalize(Eye - new Vector3(0, 0.75f, 0));
        var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, backward));
        var up = Vector3.Cross(backward, right);
        float fov = 38f * MathF.PI / 180;
        float halfHeight = MathF.Tan(fov / 2);
        float halfWidth = (float)width / height * halfHeight;
        corner = Eye - halfWidth * right + halfHeight * up - backward;
        horizontal = 2 * halfWidth * right;
        vertical = -2 * halfHeight * up;
    }

    public int RenderPixel(int x, int y, int width, int height, int samples, CancellationToken ct)
    {
        uint state = unchecked((uint)(y * width + x) * 9781u + 0x9E3779B9u);
        float accumulated = 0;
        for (int sample = 0; sample < samples; sample++)
        {
            // Added cancellation checks do not consume RNG state or alter completed pixels.
            if ((sample & 63) == 0) ct.ThrowIfCancellationRequested();
            float sx = (x + Next(ref state)) / width;
            float sy = (y + Next(ref state)) / height;
            var direction = Vector3.Normalize(corner + sx * horizontal + sy * vertical - Eye);
            accumulated += MathF.Min(Integrate(new Ray(Eye, direction), ref state), 4f);
        }
        return Encode(accumulated / samples);
    }

    private static Ball[] CreateObjects()
    {
        var objects = new List<Ball>
        {
            new(new Vector3(0, 1.1f, 0), 1.1f, Surface.Glass, 0.98f),
            new(new Vector3(-2.5f, 0.9f, 0.9f), 0.9f, Surface.Metal, 0.92f, 0.03f),
            new(new Vector3(2.4f, 0.8f, 0.7f), 0.8f, Surface.Matte, 0.85f),
            new(new Vector3(1, 0.45f, -1.9f), 0.45f, Surface.Metal, 0.85f, 0.25f),
            new(new Vector3(-1.1f, 0.35f, -2), 0.35f, Surface.Matte, 0.15f)
        };
        for (int index = 0; index < 14; index++)
        {
            float angle = index / 14f * MathF.Tau;
            var surface = index % 3 == 0 ? Surface.Metal : index % 3 == 1 ? Surface.Matte : Surface.Glass;
            objects.Add(new Ball(new Vector3(MathF.Cos(angle) * 4.2f, 0.28f,
                MathF.Sin(angle) * 4.2f + 1.5f), 0.28f, surface, index % 2 == 0 ? 0.9f : 0.3f, 0.1f));
        }
        return objects.ToArray();
    }

    private static float Integrate(Ray ray, ref uint random)
    {
        float energy = 1;
        float result = 0;
        bool seesLight = true;
        for (int bounce = 0; bounce < 6; bounce++)
        {
            if (!FindContact(ray, out var contact))
                return result + energy * (0.06f + 0.22f * MathF.Max(0, ray.Direction.Y));
            if (contact.Light)
                return seesLight ? result + energy * LightRadiance : result;

            var point = ray.Origin + ray.Direction * contact.Distance;
            Vector3 normal;
            Surface surface;
            float reflectance;
            float blur = 0;
            if (contact.Object == -1)
            {
                normal = Vector3.UnitY;
                surface = Surface.Matte;
                int parity = (int)MathF.Floor(point.X * 0.9f) + (int)MathF.Floor(point.Z * 0.9f);
                reflectance = (parity & 1) == 0 ? 0.62f : 0.38f;
            }
            else
            {
                var ball = Objects[contact.Object];
                normal = (point - ball.Center) / ball.Radius;
                surface = ball.Surface;
                reflectance = ball.Reflectance;
                blur = ball.Blur;
            }

            if (surface == Surface.Matte)
            {
                result += energy * reflectance * SampleLight(point, normal, ref random);
                energy *= reflectance;
                ray = new Ray(point + normal * 1e-3f, SampleHemisphere(normal, ref random));
                seesLight = false;
            }
            else if (surface == Surface.Metal)
            {
                var reflection = Vector3.Reflect(ray.Direction, normal) + blur * SampleBall(ref random);
                if (Vector3.Dot(reflection, normal) <= 0) return result;
                ray = new Ray(point + normal * 1e-3f, Vector3.Normalize(reflection));
                energy *= reflectance;
                seesLight = true;
            }
            else
            {
                bool entering = Vector3.Dot(ray.Direction, normal) < 0;
                var facing = entering ? normal : -normal;
                float ratio = entering ? 1 / 1.5f : 1.5f;
                float cosine = MathF.Min(-Vector3.Dot(ray.Direction, facing), 1);
                float sinSquared = ratio * ratio * (1 - cosine * cosine);
                float baseReflectance = (1 - 1.5f) / (1 + 1.5f);
                baseReflectance *= baseReflectance;
                float reflectionChance = baseReflectance + (1 - baseReflectance) * MathF.Pow(1 - cosine, 5);
                if (sinSquared > 1 || Next(ref random) < reflectionChance)
                    ray = new Ray(point + facing * 1e-3f, Vector3.Reflect(ray.Direction, facing));
                else
                    ray = new Ray(point - facing * 1e-3f, Vector3.Normalize(ratio * ray.Direction +
                        (ratio * cosine - MathF.Sqrt(1 - sinSquared)) * facing));
                energy *= reflectance;
                seesLight = true;
            }
            if (bounce >= 3)
            {
                float rejection = MathF.Max(0.05f, 1 - energy);
                if (Next(ref random) < rejection) return result;
                energy /= 1 - rejection;
            }
        }
        return result;
    }

    private static float SampleLight(Vector3 point, Vector3 normal, ref uint random)
    {
        var delta = LightCenter - point;
        float squaredDistance = delta.LengthSquared();
        float coneCosine = MathF.Sqrt(MathF.Max(0, 1 - LightRadius * LightRadius / squaredDistance));
        var axis = delta / MathF.Sqrt(squaredDistance);
        float first = Next(ref random);
        float second = Next(ref random);
        float cosine = 1 - first * (1 - coneCosine);
        float sine = MathF.Sqrt(1 - cosine * cosine);
        float azimuth = MathF.Tau * second;
        var (tangent, bitangent) = Tangents(axis);
        var direction = Vector3.Normalize(tangent * (MathF.Cos(azimuth) * sine) +
            bitangent * (MathF.Sin(azimuth) * sine) + axis * cosine);
        float incidence = Vector3.Dot(direction, normal);
        if (incidence <= 0) return 0;
        if (!FindContact(new Ray(point + normal * 1e-3f, direction), out var contact) || !contact.Light) return 0;
        return LightRadiance * incidence * 2 * (1 - coneCosine);
    }

    private static bool FindContact(Ray ray, out Contact contact)
    {
        float distance = float.MaxValue;
        int objectIndex = -2;
        bool light = false;
        if (ray.Direction.Y < -1e-6f)
        {
            float plane = -ray.Origin.Y / ray.Direction.Y;
            if (plane > 1e-4f && plane < 60) { distance = plane; objectIndex = -1; }
        }
        for (int index = 0; index < Objects.Length; index++)
            if (SphereDistance(Objects[index].Center, Objects[index].Radius, ray, distance, out var hit))
            { distance = hit; objectIndex = index; }
        if (SphereDistance(LightCenter, LightRadius, ray, distance, out var lightHit))
        { distance = lightHit; light = true; }
        contact = new Contact(distance, objectIndex, light);
        return objectIndex != -2 || light;
    }

    private static bool SphereDistance(Vector3 center, float radius, Ray ray, float maximum, out float distance)
    {
        var offset = ray.Origin - center;
        float projection = Vector3.Dot(offset, ray.Direction);
        float constant = offset.LengthSquared() - radius * radius;
        float discriminant = projection * projection - constant;
        distance = 0;
        if (discriminant < 0) return false;
        float root = MathF.Sqrt(discriminant);
        distance = -projection - root;
        if (distance < 1e-4f) distance = -projection + root;
        return distance > 1e-4f && distance < maximum;
    }

    private static float Next(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state >> 8) * (1f / 16777216f);
    }

    private static Vector3 SampleBall(ref uint state)
    {
        Vector3 candidate;
        do
        {
            candidate = new Vector3(Next(ref state) * 2 - 1, Next(ref state) * 2 - 1, Next(ref state) * 2 - 1);
        } while (candidate.LengthSquared() >= 1);
        return candidate;
    }

    private static (Vector3 Tangent, Vector3 Bitangent) Tangents(Vector3 axis)
    {
        var tangent = Vector3.Normalize(Vector3.Cross(MathF.Abs(axis.X) > 0.9f ? Vector3.UnitY : Vector3.UnitX, axis));
        return (tangent, Vector3.Cross(axis, tangent));
    }

    private static Vector3 SampleHemisphere(Vector3 axis, ref uint state)
    {
        float first = Next(ref state);
        float second = Next(ref state);
        float radius = MathF.Sqrt(first);
        float azimuth = MathF.Tau * second;
        var (tangent, bitangent) = Tangents(axis);
        return Vector3.Normalize(tangent * (radius * MathF.Cos(azimuth)) + bitangent * (radius * MathF.Sin(azimuth)) +
            axis * MathF.Sqrt(1 - first));
    }

    private static int Encode(float luminance)
    {
        float exposed = luminance * 1.1f;
        float mapped = exposed * (2.51f * exposed + 0.03f) / (exposed * (2.43f * exposed + 0.59f) + 0.14f);
        int gray = (int)(MathF.Pow(Math.Clamp(mapped, 0, 1), 1 / 2.2f) * 255 + 0.5f);
        return unchecked((int)0xFF000000) | gray << 16 | gray << 8 | gray;
    }
}

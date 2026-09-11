// Compile-check stub of the UnityEngine surface this package uses.
// NOT shipped, NOT committed. It exists so the renderer can be typechecked
// without an Editor install. It validates that this package's own code is
// internally consistent; it cannot prove the real Unity signatures match.
using System;

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; } = "";
        public static void Destroy(Object obj) { }
        public static void DestroyImmediate(Object obj) { }
        public static T Instantiate<T>(T original) where T : Object => original;
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public Vector3(float x, float y) { this.x = x; this.y = y; this.z = 0f; }
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a;
        public static float Distance(Vector3 a, Vector3 b) => 0f;
    }

    public struct Quaternion
    {
        public static Quaternion identity => default;
        public static Quaternion Euler(float x, float y, float z) => default;
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; this.a = 1f; }
        public static Color white => new Color(1, 1, 1, 1);
        public static Color black => new Color(0, 0, 0, 1);
        public static Color red => new Color(1, 0, 0, 1);
        public static Color green => new Color(0, 1, 0, 1);
        public static Color clear => new Color(0, 0, 0, 0);
        public static Color Lerp(Color a, Color b, float t) => a;
    }

    public static class Mathf
    {
        public const float PI = 3.14159265f;
        public static float Clamp(float v, float min, float max) => v;
        public static float Clamp01(float v) => v;
        public static float Lerp(float a, float b, float t) => a;
        public static float Sin(float f) => 0f;
        public static float Cos(float f) => 0f;
        public static float Abs(float f) => f;
        public static float Max(float a, float b) => a;
        public static float Min(float a, float b) => a;
        public static float Round(float f) => f;
        public static float Floor(float f) => f;
        public static int RoundToInt(float f) => 0;
        public static float MoveTowards(float current, float target, float maxDelta) => target;
    }

    public static class Time
    {
        public static float deltaTime => 0f;
        public static float unscaledDeltaTime => 0f;
        public static float time => 0f;
    }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
    }

    public class Transform : Component
    {
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public Vector3 localScale { get; set; } = Vector3.one;
        public Quaternion rotation { get; set; }
        public Vector3 eulerAngles { get; set; }
        public Transform? parent { get; set; }
        public int childCount => 0;
        public Transform GetChild(int index) => this;
        public void SetParent(Transform? p) { }
        public void SetParent(Transform? p, bool worldPositionStays) { }
    }

    public class Component : Object
    {
        public Transform transform { get; } = null!;
        public GameObject gameObject { get; } = null!;
        public T GetComponent<T>() where T : class => null!;
        public T GetComponentInChildren<T>() where T : class => null!;
    }

    public class GameObject : Object
    {
        public GameObject() { }
        public GameObject(string name) { this.name = name; }
        public Transform transform { get; } = null!;
        public bool activeSelf => true;
        public void SetActive(bool value) { }
        public T AddComponent<T>() where T : Component => null!;
        public T GetComponent<T>() where T : class => null!;
    }

    public class Behaviour : Component { public bool enabled { get; set; } = true; }
    public class MonoBehaviour : Behaviour { }

    public class Renderer : Component { public bool enabled { get; set; } = true; public int sortingOrder { get; set; } public string sortingLayerName { get; set; } = ""; }
    public class Sprite : Object { }
    public class SpriteRenderer : Renderer { public Sprite? sprite { get; set; } public Color color { get; set; } = Color.white; public bool flipX { get; set; } }

    public class Camera : Behaviour { public static Camera? main => null; }

    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public enum TextAlignment { Left, Center, Right }
    public class TextMesh : Component
    {
        public string text { get; set; } = "";
        public Color color { get; set; } = Color.white;
        public float characterSize { get; set; } = 1f;
        public int fontSize { get; set; }
        public TextAnchor anchor { get; set; }
        public TextAlignment alignment { get; set; }
    }

    public class AudioClip : Object { }
    public class AudioSource : Behaviour { public AudioClip? clip { get; set; } public void PlayOneShot(AudioClip clip) { } }

    public class TextAsset : Object { public string text => ""; }
    public static class Resources { public static T Load<T>(string path) where T : Object => null!; }

    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeFieldAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string header) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string tooltip) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class RangeAttribute : Attribute { public RangeAttribute(float min, float max) { } }
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)] public sealed class RequireComponentAttribute : Attribute { public RequireComponentAttribute(Type t) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class DisallowMultipleComponentAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public sealed class AddComponentMenuAttribute : Attribute { public AddComponentMenuAttribute(string menu) { } }
}

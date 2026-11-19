using UnityEngine;
using UnityEngine.UI;

/// Tiny code-only uGUI helpers: rounded sprites, iOS-style rows, pills, labels.
public static class UIKit
{
    public static Font Font => font ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    static Font font;
    static Sprite rounded, circle;

    public static Sprite Rounded => rounded ??= MakeRounded(64, 22);
    public static Sprite Circle => circle ??= MakeRounded(64, 32);

    static Sprite MakeRounded(int size, int radius)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float cx = Mathf.Clamp(x + 0.5f, radius, size - radius), cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
            byte a = (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255);
            px[y * size + x] = new Color32(255, 255, 255, a);
        }
        tex.SetPixels32(px); tex.Apply();
        float b = Mathf.Min(radius, size / 2 - 1);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
    }

    public static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    public static RectTransform Stretch(RectTransform r, float l = 0, float t = 0, float rr = 0, float b = 0)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(l, b); r.offsetMax = new Vector2(-rr, -t);
        return r;
    }

    public static RectTransform Place(RectTransform r, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        r.anchorMin = r.anchorMax = anchor; r.pivot = pivot; r.anchoredPosition = pos; r.sizeDelta = size;
        return r;
    }

    public static Image Box(Transform parent, Color c, Sprite s = null, string name = "Box")
    {
        var r = Rect(name, parent);
        var img = r.gameObject.AddComponent<Image>();
        img.sprite = s ?? Rounded; img.type = Image.Type.Sliced; img.color = c;
        return img;
    }

    public static Text Label(Transform parent, string text, int size, Color c, TextAnchor align = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
    {
        var r = Rect("Label", parent);
        var t = r.gameObject.AddComponent<Text>();
        t.font = Font; t.text = text; t.fontSize = size; t.color = c; t.alignment = align; t.fontStyle = style;
        t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    public static Button Button(Transform parent, Color bg, System.Action onClick)
    {
        var img = Box(parent, bg, null, "Button");
        var b = img.gameObject.AddComponent<Button>();
        var cb = b.colors; cb.pressedColor = new Color(0.8f, 0.8f, 0.8f); b.colors = cb;
        b.onClick.AddListener(() => onClick());
        return b;
    }

    /// iOS settings-style row: coloured rounded icon tile with a glyph, title, subtitle, chevron.
    public static Button Row(Transform parent, Color iconColor, string glyph, string title, string sub, System.Action onClick)
    {
        var b = Button(parent, new Color(1, 1, 1, 0.10f), onClick);
        var icon = Box(b.transform, iconColor);
        Place(icon.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(64, 64));
        var g = Label(icon.transform, glyph, 34, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        Stretch(g.rectTransform);
        var t = Label(b.transform, title, 34, Color.white, TextAnchor.LowerLeft, FontStyle.Bold);
        Stretch(t.rectTransform, 104, 10, 60, 52);
        var s = Label(b.transform, sub, 24, new Color(1, 1, 1, 0.6f), TextAnchor.UpperLeft);
        Stretch(s.rectTransform, 104, 54, 60, 6);
        var ch = Label(b.transform, "›", 44, new Color(1, 1, 1, 0.45f), TextAnchor.MiddleRight);
        Stretch(ch.rectTransform, 0, 0, 24, 0);
        return b;
    }

    public static Image Line(Transform parent, Color c, float width)
    {
        var img = Box(parent, c, Circle, "Line");
        img.rectTransform.pivot = new Vector2(0, 0.5f);
        img.rectTransform.anchorMin = img.rectTransform.anchorMax = Vector2.zero;
        img.rectTransform.sizeDelta = new Vector2(0, width);
        return img;
    }

    public static void SetLine(Image line, Vector2 a, Vector2 b)
    {
        var r = line.rectTransform;
        r.anchoredPosition = a;
        var d = b - a;
        r.sizeDelta = new Vector2(d.magnitude, r.sizeDelta.y);
        r.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
    }
}

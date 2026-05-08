#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

internal static class VLiveKitEditorUI
{
    private static GUIStyle titleStyle;
    private static GUIStyle subtitleStyle;
    private static GUIStyle panelStyle;
    private static GUIStyle noticeStyle;
    private static GUIStyle primaryButtonStyle;
    private static GUIStyle secondaryButtonStyle;
    private static GUIStyle tableHeaderStyle;

    public static GUIStyle TitleStyle
    {
        get
        {
            EnsureStyles();
            return titleStyle;
        }
    }

    public static GUIStyle SubtitleStyle
    {
        get
        {
            EnsureStyles();
            return subtitleStyle;
        }
    }

    public static GUIStyle PanelStyle
    {
        get
        {
            EnsureStyles();
            return panelStyle;
        }
    }

    public static GUIStyle NoticeStyle
    {
        get
        {
            EnsureStyles();
            return noticeStyle;
        }
    }

    public static GUIStyle PrimaryButtonStyle
    {
        get
        {
            EnsureStyles();
            return primaryButtonStyle;
        }
    }

    public static GUIStyle SecondaryButtonStyle
    {
        get
        {
            EnsureStyles();
            return secondaryButtonStyle;
        }
    }

    public static GUIStyle TableHeaderStyle
    {
        get
        {
            EnsureStyles();
            return tableHeaderStyle;
        }
    }

    public static Color AccentColor(float alpha)
    {
        return new Color(0.0f, 0.48f, 1f, alpha);
    }

    public static Color SurfaceColor()
    {
        return EditorGUIUtility.isProSkin ? new Color(0.118f, 0.118f, 0.118f) : new Color(0.965f, 0.965f, 0.965f);
    }

    public static Color PanelColor()
    {
        return EditorGUIUtility.isProSkin ? new Color(0.158f, 0.158f, 0.158f) : new Color(0.992f, 0.992f, 0.992f);
    }

    public static Color SubtlePanelColor()
    {
        return EditorGUIUtility.isProSkin ? new Color(0.185f, 0.185f, 0.185f) : new Color(0.935f, 0.935f, 0.935f);
    }

    public static Color SeparatorColor()
    {
        return EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.09f) : new Color(0f, 0f, 0f, 0.11f);
    }

    public static Color PrimaryTextColor()
    {
        return EditorGUIUtility.isProSkin ? new Color(0.92f, 0.92f, 0.92f) : new Color(0.12f, 0.12f, 0.12f);
    }

    public static Color SecondaryTextColor()
    {
        return EditorGUIUtility.isProSkin ? new Color(0.62f, 0.62f, 0.62f) : new Color(0.42f, 0.42f, 0.42f);
    }

    public static void DrawSeparator(float topPadding = 4f, float bottomPadding = 4f)
    {
        GUILayout.Space(topPadding);
        var rect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, SeparatorColor());
        GUILayout.Space(bottomPadding);
    }

    public static void DrawNotice(string message)
    {
        EnsureStyles();
        var rect = GUILayoutUtility.GetRect(0f, 34f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, SubtlePanelColor());
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), SeparatorColor());
        GUI.Label(rect, message, noticeStyle);
    }

    public static void DrawHeader(string title, string subtitle, float height = 72f)
    {
        EnsureStyles();
        var rect = GUILayoutUtility.GetRect(0f, height, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, SurfaceColor());
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), SeparatorColor());
        GUI.Label(new Rect(rect.x + 18f, rect.y + 13f, rect.width - 36f, 24f), title, titleStyle);
        GUI.Label(new Rect(rect.x + 18f, rect.y + 41f, rect.width - 36f, 18f), subtitle, subtitleStyle);
    }

    public static Texture2D MakeSolidTexture(Color color)
    {
        var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

    private static void EnsureStyles()
    {
        if (titleStyle != null &&
            subtitleStyle != null &&
            panelStyle != null &&
            noticeStyle != null &&
            primaryButtonStyle != null &&
            secondaryButtonStyle != null &&
            tableHeaderStyle != null)
        {
            return;
        }

        titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 20,
            normal = { textColor = PrimaryTextColor() }
        };
        subtitleStyle = new GUIStyle(EditorStyles.label)
        {
            wordWrap = false,
            normal = { textColor = SecondaryTextColor() }
        };
        panelStyle = new GUIStyle(EditorStyles.helpBox)
        {
            padding = new RectOffset(12, 12, 8, 8),
            margin = new RectOffset(8, 8, 4, 4),
            normal = { background = MakeSolidTexture(PanelColor()) }
        };
        noticeStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
        {
            padding = new RectOffset(12, 12, 7, 7),
            normal = { textColor = PrimaryTextColor() }
        };
        primaryButtonStyle = new GUIStyle(EditorStyles.miniButton)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            fixedHeight = 28,
            normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.88f, 0.94f, 1f) : new Color(0.05f, 0.24f, 0.52f) }
        };
        secondaryButtonStyle = new GUIStyle(EditorStyles.miniButton)
        {
            fixedHeight = 26,
            fontSize = 11
        };
        tableHeaderStyle = new GUIStyle(EditorStyles.miniBoldLabel)
        {
            normal = { textColor = SecondaryTextColor() }
        };
    }
}
#endif

using UnityEngine;

/// <summary>
/// 跨版本兼容工具：屏蔽 Unity 2022.3 与 2023+ 之间 API 差异，
/// 并提供各管线通用的内置字体获取。
/// </summary>
public static class UnityCompat
{
    /// <summary>查找场景中的第一个指定类型组件（兼容旧/新 Unity API）。</summary>
    public static T FindFirst<T>() where T : Object
    {
#if UNITY_2023_1_OR_NEWER
        return Object.FindFirstObjectByType<T>();
#else
        return Object.FindObjectOfType<T>();
#endif
    }

    /// <summary>获取内置字体，兼容 Unity 2022.2 前后 Arial 到 LegacyRuntime 的改名。</summary>
    public static Font GetDefaultFont()
    {
        Font f = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (f == null)
        {
            f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        return f;
    }
}

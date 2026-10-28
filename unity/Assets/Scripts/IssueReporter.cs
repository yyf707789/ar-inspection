using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// 负责上报问题：程序化创建填写表单（服务器地址/标题/描述/优先级/提交按钮），
/// 把标记位置与表单内容 POST 到 Go 后端，并给出成功/失败提示。
/// 所有 UI 在代码中生成，无需在场景里手动摆放 Canvas 或控件。
/// </summary>
public class IssueReporter : MonoBehaviour
{
    [Tooltip("Go 后端地址，例如 http://192.168.1.100:8080")]
    [SerializeField] private string serverUrl = "http://192.168.1.100:8080";

    private ARPlacementController placement;

    private InputField serverUrlInput;
    private InputField titleInput;
    private InputField descInput;
    private Text statusText;

    private int selectedPriority = 1; // 默认 medium
    private readonly Button[] priorityButtons = new Button[3];

    private const float RefWidth = 1080f; // CanvasScaler 参考分辨率宽度
    private static readonly string[] PriorityValues = { "low", "medium", "high" };
    private static readonly string[] PriorityLabels = { "低", "中", "高" };

    private void Awake()
    {
        placement = GetComponent<ARPlacementController>();
        if (placement == null)
        {
            placement = UnityCompat.FindFirst<ARPlacementController>();
        }

        EnsureEventSystem();
        BuildUI();
    }

    private void Start()
    {
        serverUrlInput.text = serverUrl;
        RefreshPriorityButtons();
    }

    // ---------------- UI 构建 ----------------

    private void BuildUI()
    {
        GameObject canvasGO = new GameObject("IssueUI");
        canvasGO.transform.SetParent(transform, false);
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(RefWidth, 1920f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // 底部半透明面板
        RectTransform panel = CreateRect("Panel", canvasGO.transform);
        SetStretch(panel, 0f, 0f, 520f);
        Image panelImg = panel.gameObject.AddComponent<Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0.72f);

        // 服务器地址输入框
        serverUrlInput = CreateInputField("ServerUrlInput", panel, "服务器地址，如 http://192.168.1.100:8080", false);
        SetStretch((RectTransform)serverUrlInput.transform, 30f, 460f, 52f);

        // 标题输入框（必填）
        titleInput = CreateInputField("TitleInput", panel, "标题（必填）", false);
        SetStretch((RectTransform)titleInput.transform, 30f, 396f, 52f);

        // 描述输入框（可选）
        descInput = CreateInputField("DescInput", panel, "描述（可选）", true);
        SetStretch((RectTransform)descInput.transform, 30f, 332f, 52f);

        // 优先级标签
        Text priorityLabel = CreateText("PriorityLabel", panel, "优先级：", 26, TextAnchor.MiddleLeft);
        priorityLabel.color = Color.white;
        SetRectLeft((RectTransform)priorityLabel.transform, 30f, 284f, 150f, 44f);

        // 三个优先级按钮
        float buttonWidth = (RefWidth - 60f - 24f) / 3f;
        for (int i = 0; i < 3; i++)
        {
            Button btn = CreateButton("PriorityBtn" + i, panel, PriorityLabels[i]);
            SetRectLeft((RectTransform)btn.transform, 30f + i * (buttonWidth + 12f), 222f, buttonWidth, 56f);
            int index = i;
            btn.onClick.AddListener(() => SelectPriority(index));
            priorityButtons[i] = btn;
        }

        // 提交按钮
        Button submit = CreateButton("SubmitButton", panel, "提交问题");
        SetStretch((RectTransform)submit.transform, 30f, 148f, 64f);
        submit.image.color = new Color(0.16f, 0.62f, 0.35f);
        Text submitText = submit.GetComponentInChildren<Text>();
        if (submitText != null)
        {
            submitText.color = Color.white;
        }
        submit.onClick.AddListener(OnSubmitClick);

        // 状态提示文本
        statusText = CreateText("StatusText", panel, "", 24, TextAnchor.MiddleCenter);
        statusText.color = Color.white;
        statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
        statusText.verticalOverflow = VerticalWrapMode.Overflow;
        SetStretch((RectTransform)statusText.transform, 30f, 36f, 100f);
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
        {
            return;
        }
        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }

    private void SelectPriority(int index)
    {
        selectedPriority = index;
        RefreshPriorityButtons();
    }

    private void RefreshPriorityButtons()
    {
        for (int i = 0; i < priorityButtons.Length; i++)
        {
            if (priorityButtons[i] == null)
            {
                continue;
            }
            bool selected = i == selectedPriority;
            priorityButtons[i].image.color = selected
                ? new Color(0.2f, 0.5f, 0.9f)
                : Color.white;
            Text t = priorityButtons[i].GetComponentInChildren<Text>();
            if (t != null)
            {
                t.color = selected ? Color.white : new Color(0.15f, 0.15f, 0.15f);
            }
        }
    }

    // ---------------- 提交 ----------------

    private void OnSubmitClick()
    {
        if (placement == null || !placement.HasMarker)
        {
            SetStatus("请先在 AR 场景中点击平面放置标记", false);
            return;
        }

        string title = titleInput != null ? titleInput.text.Trim() : "";
        if (string.IsNullOrEmpty(title))
        {
            SetStatus("标题不能为空", false);
            return;
        }

        StartCoroutine(SubmitIssue());
    }

    private IEnumerator SubmitIssue()
    {
        SetStatus("提交中…", true);

        string url = serverUrlInput != null ? serverUrlInput.text.Trim() : serverUrl;
        url = url.TrimEnd('/');
        if (string.IsNullOrEmpty(url))
        {
            SetStatus("服务器地址不能为空", false);
            yield break;
        }

        Vector3 pos = placement.MarkerPosition;
        IssuePayload payload = new IssuePayload
        {
            title = titleInput.text.Trim(),
            description = descInput != null ? descInput.text.Trim() : "",
            priority = PriorityValues[selectedPriority],
            position = new PositionPayload
            {
                x = Round(pos.x),
                y = Round(pos.y),
                z = Round(pos.z),
            },
        };

        string json = JsonUtility.ToJson(payload);
        byte[] body = Encoding.UTF8.GetBytes(json);

        using (UnityWebRequest req = new UnityWebRequest(url + "/api/issues", "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 10;

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                string id = ExtractId(req.downloadHandler.text);
                SetStatus("提交成功！问题 ID：" + id, true);
                titleInput.text = "";
                descInput.text = "";
            }
            else
            {
                string msg = req.error ?? "未知错误";
                if (!string.IsNullOrEmpty(req.downloadHandler.text))
                {
                    msg += " — " + req.downloadHandler.text;
                }
                SetStatus("提交失败：" + msg, false);
            }
        }
    }

    private static string ExtractId(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return "(未知)";
        }
        try
        {
            IssueResponse resp = JsonUtility.FromJson<IssueResponse>(json);
            return string.IsNullOrEmpty(resp.id) ? "(未知)" : resp.id;
        }
        catch
        {
            return "(未知)";
        }
    }

    private void SetStatus(string message, bool success)
    {
        if (statusText == null)
        {
            return;
        }
        statusText.text = message;
        statusText.color = success
            ? new Color(0.35f, 0.9f, 0.5f)
            : new Color(1f, 0.45f, 0.4f);
    }

    private static float Round(float value)
    {
        return Mathf.Round(value * 100f) / 100f;
    }

    // ---------------- 序列化载荷 ----------------

    [System.Serializable]
    private class IssuePayload
    {
        public string title;
        public string description;
        public string priority;
        public PositionPayload position;
    }

    [System.Serializable]
    private class PositionPayload
    {
        public float x;
        public float y;
        public float z;
    }

    [System.Serializable]
    private class IssueResponse
    {
        public string id;
    }

    // ---------------- UI 辅助方法 ----------------

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    /// <summary>水平铺满（左右留 margin），底部对齐到 y，高度 height。</summary>
    private static void SetStretch(RectTransform rt, float margin, float y, float height)
    {
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.offsetMin = new Vector2(margin, y);
        rt.offsetMax = new Vector2(-margin, y + height);
    }

    /// <summary>左下角锚点 + 固定尺寸。</summary>
    private static void SetRectLeft(RectTransform rt, float x, float y, float width, float height)
    {
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.sizeDelta = new Vector2(width, height);
        rt.anchoredPosition = new Vector2(x, y);
    }

    private static Text CreateText(string name, Transform parent, string content, int fontSize, TextAnchor align)
    {
        RectTransform rt = CreateRect(name, parent);
        Text text = rt.gameObject.AddComponent<Text>();
        text.font = UnityCompat.GetDefaultFont();
        text.fontSize = fontSize;
        text.alignment = align;
        text.text = content;
        text.supportRichText = false;
        text.color = new Color(0.15f, 0.15f, 0.15f);
        return text;
    }

    private static InputField CreateInputField(string name, Transform parent, string placeholder, bool multiLine)
    {
        RectTransform rt = CreateRect(name, parent);
        Image bg = rt.gameObject.AddComponent<Image>();
        bg.color = Color.white;

        InputField input = rt.gameObject.AddComponent<InputField>();
        input.targetGraphic = bg;
        input.lineType = multiLine ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;

        RectTransform textRt = CreateRect("Text", rt);
        Text text = textRt.gameObject.AddComponent<Text>();
        text.font = UnityCompat.GetDefaultFont();
        text.fontSize = 28;
        text.color = new Color(0.15f, 0.15f, 0.15f);
        text.alignment = TextAnchor.MiddleLeft;
        text.supportRichText = false;
        Stretch(textRt, 12f);
        input.textComponent = text;

        RectTransform phRt = CreateRect("Placeholder", rt);
        Text ph = phRt.gameObject.AddComponent<Text>();
        ph.font = UnityCompat.GetDefaultFont();
        ph.fontSize = 28;
        ph.fontStyle = FontStyle.Italic;
        ph.color = new Color(0.6f, 0.6f, 0.6f);
        ph.alignment = TextAnchor.MiddleLeft;
        ph.supportRichText = false;
        ph.text = placeholder;
        Stretch(phRt, 12f);
        input.placeholder = ph;

        return input;
    }

    private static Button CreateButton(string name, Transform parent, string label)
    {
        RectTransform rt = CreateRect(name, parent);
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = Color.white;
        Button btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;

        RectTransform textRt = CreateRect("Text", rt);
        Text text = textRt.gameObject.AddComponent<Text>();
        text.font = UnityCompat.GetDefaultFont();
        text.fontSize = 26;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color(0.15f, 0.15f, 0.15f);
        text.text = label;
        text.supportRichText = false;
        Stretch(textRt, 0f);

        return btn;
    }

    private static void Stretch(RectTransform rt, float padding)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padding, padding);
        rt.offsetMax = new Vector2(-padding, -padding);
    }
}

using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 负责 App 登录与问题上报。
/// 注意：此设备上 Unity 自带的 UnityWebRequest 会卡死，因此网络请求改用
/// 「原始 Socket + 手写 HTTP」在后台线程发送，结果通过 Update 轮询回主线程更新 UI。
/// </summary>
public class IssueReporter : MonoBehaviour
{
    [Tooltip("Go 后端地址，例如 http://192.168.1.100:8080")]
    [SerializeField] private string serverUrl = "http://192.168.1.100:8080";

    private ARPlacementController placement;

    // 登录相关
    private InputField usernameInput;
    private InputField passwordInput;
    private GameObject loginGroup;
    private GameObject issueGroup;
    private volatile string authToken = "";
    private bool loggedIn = false;

    // 问题表单
    private InputField titleInput;
    private InputField descInput;
    private Text statusText;

    private int selectedPriority = 1; // 默认 medium
    private readonly Button[] priorityButtons = new Button[3];

    // 后台线程网络结果（volatile 供主线程轮询）
    private volatile int _pendingKind;    // 0 无 / 1 登录 / 2 提交
    private volatile string _pendingBody; // 响应体
    private volatile bool _pendingOk;     // 是否 2xx
    private volatile string _pendingError;

    private const float RefWidth = 1080f;
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
        RefreshPriorityButtons();
        ShowLoginUI();
    }

    private void Update()
    {
        int kind = _pendingKind;
        if (kind == 0)
        {
            return;
        }
        string body = _pendingBody;
        bool ok = _pendingOk;
        string err = _pendingError;
        _pendingKind = 0;

        if (kind == 1)
        {
            HandleLoginResult(ok, body, err);
        }
        else if (kind == 2)
        {
            HandleSubmitResult(ok, body, err);
        }
        else if (kind == 3)
        {
            if (ok)
            {
                SetStatus("连接成功！后端可达 ✓", true);
            }
            else
            {
                SetStatus("连接失败：" + (err ?? "无响应"), false);
            }
        }
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

        RectTransform panel = CreateRect("Panel", canvasGO.transform);
        SetStretch(panel, 0f, 0f, 520f);
        Image panelImg = panel.gameObject.AddComponent<Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0.72f);

        // 共享状态文本
        statusText = CreateText("StatusText", panel, "", 24, TextAnchor.MiddleCenter);
        statusText.color = Color.white;
        statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
        statusText.verticalOverflow = VerticalWrapMode.Overflow;
        SetStretch((RectTransform)statusText.transform, 30f, 40f, 100f);

        // ---- 登录区 ----
        RectTransform loginRoot = CreateRect("LoginGroup", panel);
        Stretch(loginRoot, 0f);
        loginGroup = loginRoot.gameObject;

        Text userLabel = CreateText("UserLabel", loginRoot, "用户名", 22, TextAnchor.MiddleLeft);
        userLabel.color = new Color(0.85f, 0.85f, 0.85f);
        SetStretch((RectTransform)userLabel.transform, 30f, 440f, 26f);
        usernameInput = CreateInputField("UsernameInput", loginRoot, "请输入用户名", false);
        SetStretch((RectTransform)usernameInput.transform, 30f, 384f, 52f);

        Text passLabel = CreateText("PassLabel", loginRoot, "密码", 22, TextAnchor.MiddleLeft);
        passLabel.color = new Color(0.85f, 0.85f, 0.85f);
        SetStretch((RectTransform)passLabel.transform, 30f, 352f, 26f);
        passwordInput = CreateInputField("PasswordInput", loginRoot, "请输入密码", false);
        SetStretch((RectTransform)passwordInput.transform, 30f, 296f, 52f);
        // 密码框做遮罩显示（圆点），避免明文
        passwordInput.contentType = InputField.ContentType.Password;

        Button loginButton = CreateButton("LoginButton", loginRoot, "登录");
        SetStretch((RectTransform)loginButton.transform, 30f, 210f, 58f);
        loginButton.image.color = new Color(0.16f, 0.62f, 0.35f);
        Text loginText = loginButton.GetComponentInChildren<Text>();
        if (loginText != null) loginText.color = Color.white;
        loginButton.onClick.AddListener(OnLoginClick);

        Button testButton = CreateButton("TestConnButton", loginRoot, "测试连接");
        SetStretch((RectTransform)testButton.transform, 30f, 140f, 52f);
        testButton.image.color = new Color(0.35f, 0.4f, 0.48f);
        Text testText = testButton.GetComponentInChildren<Text>();
        if (testText != null) testText.color = Color.white;
        testButton.onClick.AddListener(OnTestConnection);

        // ---- 问题区 ----
        RectTransform issueRoot = CreateRect("IssueGroup", panel);
        Stretch(issueRoot, 0f);
        issueGroup = issueRoot.gameObject;

        Text titleLabel = CreateText("TitleLabel", issueRoot, "问题标题（必填）", 22, TextAnchor.MiddleLeft);
        titleLabel.color = new Color(0.85f, 0.85f, 0.85f);
        SetStretch((RectTransform)titleLabel.transform, 30f, 480f, 26f);
        titleInput = CreateInputField("TitleInput", issueRoot, "在这里输入问题标题", false);
        SetStretch((RectTransform)titleInput.transform, 30f, 418f, 52f);

        Text descLabel = CreateText("DescLabel", issueRoot, "问题描述（选填）", 22, TextAnchor.MiddleLeft);
        descLabel.color = new Color(0.85f, 0.85f, 0.85f);
        SetStretch((RectTransform)descLabel.transform, 30f, 384f, 26f);
        descInput = CreateInputField("DescInput", issueRoot, "在这里输入问题描述（可不填）", true);
        SetStretch((RectTransform)descInput.transform, 30f, 322f, 52f);

        Text priorityLabel = CreateText("PriorityLabel", issueRoot, "优先级：", 26, TextAnchor.MiddleLeft);
        priorityLabel.color = Color.white;
        SetRectLeft((RectTransform)priorityLabel.transform, 30f, 282f, 150f, 34f);

        float buttonWidth = (RefWidth - 60f - 24f) / 3f;
        for (int i = 0; i < 3; i++)
        {
            Button btn = CreateButton("PriorityBtn" + i, issueRoot, PriorityLabels[i]);
            SetRectLeft((RectTransform)btn.transform, 30f + i * (buttonWidth + 12f), 222f, buttonWidth, 52f);
            int index = i;
            btn.onClick.AddListener(() => SelectPriority(index));
            priorityButtons[i] = btn;
        }

        Button submit = CreateButton("SubmitButton", issueRoot, "提交问题");
        SetStretch((RectTransform)submit.transform, 30f, 150f, 58f);
        submit.image.color = new Color(0.16f, 0.62f, 0.35f);
        Text submitText = submit.GetComponentInChildren<Text>();
        if (submitText != null) submitText.color = Color.white;
        submit.onClick.AddListener(OnSubmitClick);

        Canvas.ForceUpdateCanvases();
    }

    private void ShowLoginUI()
    {
        if (loginGroup != null) loginGroup.SetActive(true);
        if (issueGroup != null) issueGroup.SetActive(false);
        SetStatus("请先登录后再上报问题", true);
    }

    private void ShowIssueUI()
    {
        if (loginGroup != null) loginGroup.SetActive(false);
        if (issueGroup != null) issueGroup.SetActive(true);
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

    // ---------------- 登录 ----------------

    private void OnLoginClick()
    {
        string username = usernameInput != null ? usernameInput.text.Trim() : "";
        string password = passwordInput != null ? passwordInput.text : "";
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            SetStatus("请输入用户名和密码", false);
            return;
        }
        SetStatus("登录中…", true);
        LoginPayload payload = new LoginPayload { username = username, password = password, client = "app" };
        string json = JsonUtility.ToJson(payload);
        StartRequest("/api/login", json, 1);
    }

    private void HandleLoginResult(bool ok, string body, string err)
    {
        if (ok)
        {
            LoginResponse resp = null;
            try { resp = JsonUtility.FromJson<LoginResponse>(body); } catch { }
            if (resp != null && !string.IsNullOrEmpty(resp.token))
            {
                authToken = resp.token;
                loggedIn = true;
                ShowIssueUI();
                string name = resp.user != null && !string.IsNullOrEmpty(resp.user.displayName) ? resp.user.displayName : "";
                SetStatus("登录成功！" + (string.IsNullOrEmpty(name) ? "" : "欢迎 " + name + "，") + "请点击平面放置标记", true);
                return;
            }
            SetStatus("登录失败：返回数据异常", false);
            return;
        }
        SetStatus("登录失败：" + (ExtractError(body) ?? err ?? "未知错误"), false);
    }

    // ---------------- 上报 ----------------

    private void OnSubmitClick()
    {
        if (!loggedIn)
        {
            SetStatus("请先登录后再上报问题", false);
            return;
        }
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

        SetStatus("提交中…", true);
        Vector3 pos = placement.MarkerPosition;
        IssuePayload payload = new IssuePayload
        {
            title = title,
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
        StartRequest("/api/issues", json, 2);
    }

    private void HandleSubmitResult(bool ok, string body, string err)
    {
        if (ok)
        {
            string id = "(未知)";
            try
            {
                IssueResponse r = JsonUtility.FromJson<IssueResponse>(body);
                if (r != null && !string.IsNullOrEmpty(r.id)) id = r.id;
            }
            catch { }
            SetStatus("提交成功！问题 ID：" + id, true);
            if (titleInput != null) titleInput.text = "";
            if (descInput != null) descInput.text = "";
            return;
        }
        SetStatus("提交失败：" + (ExtractError(body) ?? err ?? "未知错误"), false);
    }

    // ---------------- 连接测试 ----------------

    private void OnTestConnection()
    {
        SetStatus("正在测试连接…", true);
        System.Threading.Thread th = new System.Threading.Thread(DoGetHealth);
        th.IsBackground = true;
        th.Start();
    }

    private void DoGetHealth()
    {
        try
        {
            System.Uri uri = new System.Uri(serverUrl.TrimEnd('/') + "/health");
            using (System.Net.Sockets.Socket sock = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Stream,
                System.Net.Sockets.ProtocolType.Tcp))
            {
                System.IAsyncResult ar = sock.BeginConnect(uri.Host, uri.Port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(6000))
                {
                    try { sock.Close(); } catch { }
                    SetPending(3, false, null, "TCP 连接超时");
                    return;
                }
                sock.EndConnect(ar);

                string header = "GET /health HTTP/1.1\r\nHost: " + uri.Host + ":" + uri.Port + "\r\nConnection: close\r\n\r\n";
                byte[] hb = Encoding.ASCII.GetBytes(header);
                SendAll(sock, hb);

                System.IO.MemoryStream ms = new System.IO.MemoryStream();
                byte[] buf = new byte[4096];
                sock.ReceiveTimeout = 6000;
                try
                {
                    while (true)
                    {
                        int n = sock.Receive(buf);
                        if (n <= 0) break;
                        ms.Write(buf, 0, n);
                    }
                }
                catch { }
                string resp = Encoding.UTF8.GetString(ms.ToArray());
                SetPending(3, true, resp, null);
            }
        }
        catch (System.Exception e)
        {
            SetPending(3, false, null, e.Message);
        }
    }

    // ---------------- 原始 Socket 网络请求（后台线程） ----------------

    private void StartRequest(string path, string json, int kind)
    {
        int k = kind;
        System.Threading.Thread th = new System.Threading.Thread(() => DoPost(path, json, k));
        th.IsBackground = true;
        th.Start();
    }

    private void DoPost(string path, string json, int kind)
    {
        try
        {
            System.Uri uri = new System.Uri(serverUrl.TrimEnd('/') + path);
            byte[] body = Encoding.UTF8.GetBytes(json);
            string token = authToken;
            string header =
                "POST " + uri.AbsolutePath + " HTTP/1.1\r\n" +
                "Host: " + uri.Host + ":" + uri.Port + "\r\n" +
                "Content-Type: application/json\r\n" +
                (string.IsNullOrEmpty(token) ? "" : "Authorization: Bearer " + token + "\r\n") +
                "Content-Length: " + body.Length + "\r\n" +
                "Connection: close\r\n\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            byte[] all = new byte[headerBytes.Length + body.Length];
            System.Buffer.BlockCopy(headerBytes, 0, all, 0, headerBytes.Length);
            System.Buffer.BlockCopy(body, 0, all, headerBytes.Length, body.Length);

            using (System.Net.Sockets.Socket sock = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Stream,
                System.Net.Sockets.ProtocolType.Tcp))
            {
                System.IAsyncResult ar = sock.BeginConnect(uri.Host, uri.Port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(6000))
                {
                    try { sock.Close(); } catch { }
                    SetPending(kind, false, null, "连接后端超时");
                    return;
                }
                sock.EndConnect(ar);
                SendAll(sock, all);

                System.IO.MemoryStream ms = new System.IO.MemoryStream();
                byte[] buf = new byte[4096];
                sock.ReceiveTimeout = 6000;
                try
                {
                    while (true)
                    {
                        int n = sock.Receive(buf);
                        if (n <= 0) break;
                        ms.Write(buf, 0, n);
                    }
                }
                catch { }

                string resp = Encoding.UTF8.GetString(ms.ToArray());
                string respBody = ExtractBody(resp);
                bool ok = resp.StartsWith("HTTP/1.1 2") || resp.StartsWith("HTTP/1.0 2");
                SetPending(kind, ok, respBody, ok ? null : FirstLine(resp));
            }
        }
        catch (System.Exception e)
        {
            SetPending(kind, false, null, e.Message);
        }
    }

    private static void SendAll(System.Net.Sockets.Socket sock, byte[] data)
    {
        int total = 0;
        while (total < data.Length)
        {
            int n = sock.Send(data, total, data.Length - total, System.Net.Sockets.SocketFlags.None);
            if (n <= 0) break;
            total += n;
        }
    }

    // 从原始 HTTP 响应中取出响应体（处理 Content-Length 与 chunked 两种情况）
    private static string ExtractBody(string resp)
    {
        int idx = resp.IndexOf("\r\n\r\n");
        if (idx < 0) return null;
        string body = resp.Substring(idx + 4);
        if (body.StartsWith("{")) return body;
        return Dechunk(body);
    }

    private static string Dechunk(string body)
    {
        var sb = new StringBuilder();
        int i = 0;
        while (i < body.Length)
        {
            int eol = body.IndexOf("\r\n", i);
            if (eol < 0) break;
            string sizeStr = body.Substring(i, eol - i).Trim();
            int size;
            if (!int.TryParse(sizeStr, System.Globalization.NumberStyles.HexNumber, null, out size)) return body;
            if (size == 0) break;
            int start = eol + 2;
            if (start + size > body.Length) break;
            sb.Append(body, start, size);
            i = start + size + 2;
        }
        string result = sb.ToString();
        return result.StartsWith("{") ? result : body;
    }

    private static string ExtractError(string body)
    {
        if (string.IsNullOrEmpty(body) || !body.StartsWith("{")) return null;
        try
        {
            ServerError e = JsonUtility.FromJson<ServerError>(body);
            return e != null && !string.IsNullOrEmpty(e.error) ? e.error : null;
        }
        catch { return null; }
    }

    private static string FirstLine(string resp)
    {
        int nl = resp.IndexOf("\r\n");
        return nl >= 0 ? resp.Substring(0, nl) : resp;
    }

    private void SetPending(int kind, bool ok, string body, string err)
    {
        _pendingBody = body;
        _pendingOk = ok;
        _pendingError = err;
        _pendingKind = kind;
    }

    private static float Round(float value)
    {
        return Mathf.Round(value * 100f) / 100f;
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

    // ---------------- 序列化载荷 ----------------

    [System.Serializable]
    private class LoginPayload { public string username; public string password; public string client; }

    [System.Serializable]
    private class LoginUser { public string displayName; public string role; }

    [System.Serializable]
    private class LoginResponse { public string token; public LoginUser user; }

    [System.Serializable]
    private class ServerError { public string error; }

    [System.Serializable]
    private class IssuePayload
    {
        public string title;
        public string description;
        public string priority;
        public PositionPayload position;
    }

    [System.Serializable]
    private class PositionPayload { public float x; public float y; public float z; }

    [System.Serializable]
    private class IssueResponse { public string id; }

    // ---------------- UI 辅助方法 ----------------

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    private static void SetStretch(RectTransform rt, float margin, float y, float height)
    {
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.offsetMin = new Vector2(margin, y);
        rt.offsetMax = new Vector2(-margin, y + height);
    }

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
        input.caretWidth = 2;
        input.customCaretColor = true;
        input.caretColor = new Color(0.15f, 0.15f, 0.15f);
        input.selectionColor = new Color(0.2f, 0.5f, 0.9f, 0.3f);

        RectTransform textRt = CreateRect("Text", rt);
        Text text = textRt.gameObject.AddComponent<Text>();
        text.font = UnityCompat.GetDefaultFont();
        text.fontSize = 28;
        text.color = new Color(0.15f, 0.15f, 0.15f);
        text.alignment = TextAnchor.MiddleLeft;
        text.supportRichText = false;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        Stretch(textRt, 12f);
        input.textComponent = text;

        RectTransform phRt = CreateRect("Placeholder", rt);
        Text ph = phRt.gameObject.AddComponent<Text>();
        ph.font = UnityCompat.GetDefaultFont();
        ph.fontSize = 28;
        ph.fontStyle = FontStyle.Normal;
        ph.color = new Color(0.6f, 0.6f, 0.6f);
        ph.alignment = TextAnchor.MiddleLeft;
        ph.supportRichText = false;
        ph.raycastTarget = false;
        ph.text = placeholder;
        Stretch(phRt, 12f);
        input.placeholder = ph;

        input.ForceLabelUpdate();

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

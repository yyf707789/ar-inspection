using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// 一键自动搭建 AR 场景：
///   删除旧的 Main Camera → AR Session → XR Origin(Mobile AR，自带相机) →
///   AR Plane Manager + AR Raycast Manager → PlacementController(挂两个脚本并填后端地址) → 保存场景。
/// 使用：菜单 Tools → Auto-Build AR Scene
/// </summary>
public static class ARSceneSetup
{
    // 后端地址（手机与电脑同一局域网时，填电脑的局域网 IP）
    const string k_ServerUrl = "http://192.168.0.102:8080";

    [MenuItem("Tools/Auto-Build AR Scene")]
    public static void BuildScene()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("提示", "请先退出播放模式（Play）再执行。", "知道了");
            return;
        }

        // 0) 删除根级的旧 Main Camera，避免 MainCamera 标签冲突
        var oldCam = GameObject.Find("Main Camera");
        if (oldCam != null && oldCam.transform.parent == null)
            UnityEngine.Object.DestroyImmediate(oldCam);

        // 1) AR Session（用菜单命令保证 ARSession + ARInputManager 正确）
        if (GameObject.Find("AR Session") == null)
            EditorApplication.ExecuteMenuItem("GameObject/XR/AR Session");

        // 2) XR Origin (Mobile AR)：自动创建 Camera Offset + Main Camera + ARCameraManager / ARCameraBackground / TrackedPoseDriver
        if (GameObject.Find("XR Origin") == null)
            EditorApplication.ExecuteMenuItem("GameObject/XR/XR Origin (Mobile AR)");

        // 3) 给 XR Origin 挂 AR Plane Manager 和 AR Raycast Manager
        var origin = GameObject.Find("XR Origin");
        if (origin != null)
        {
            if (origin.GetComponent<ARPlaneManager>() == null)
                origin.AddComponent<ARPlaneManager>();
            if (origin.GetComponent<ARRaycastManager>() == null)
                origin.AddComponent<ARRaycastManager>();
        }
        else
        {
            Debug.LogWarning("[ARSetup] 未能找到 XR Origin。请手动执行菜单 GameObject → XR → XR Origin (Mobile AR)。");
        }

        // 4) PlacementController：挂 ARPlacementController + IssueReporter，并填 Server Url
        if (GameObject.Find("PlacementController") == null)
        {
            var pc = new GameObject("PlacementController");
            pc.AddComponent<ARPlacementController>();
            var reporter = pc.AddComponent<IssueReporter>();

            var so = new SerializedObject(reporter);
            var prop = so.FindProperty("serverUrl");
            if (prop != null)
            {
                prop.stringValue = k_ServerUrl;
                so.ApplyModifiedProperties();
            }
        }

        // 5) 保存场景
        var scene = SceneManager.GetActiveScene();
        if (scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, scene.path);
        }

        Debug.Log("[ARSetup] AR 场景搭建完成并已保存。Server Url = " + k_ServerUrl);
    }
}

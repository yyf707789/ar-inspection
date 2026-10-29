using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// 负责 AR 水平平面检测与标记放置：
/// 点击屏幕上的水平平面，在命中位置放置（或移动）一个标记。
/// 点击 UI（按钮/输入框）时不会触发放置。
/// </summary>
public class ARPlacementController : MonoBehaviour
{
    [Tooltip("要放置的标记预制体；留空则自动生成一个立方体")]
    [SerializeField] private GameObject markerPrefab;

    [Tooltip("可留空，脚本会自动查找")]
    [SerializeField] private ARRaycastManager raycastManager;

    [Tooltip("可留空，脚本会自动查找")]
    [SerializeField] private ARPlaneManager planeManager;

    private GameObject placedMarker;
    private static readonly List<ARRaycastHit> Hits = new List<ARRaycastHit>();

    /// <summary>当前是否已放置标记。</summary>
    public bool HasMarker => placedMarker != null;

    /// <summary>已放置标记的世界坐标（AR 会话坐标系，米）。</summary>
    public Vector3 MarkerPosition => placedMarker != null ? placedMarker.transform.position : Vector3.zero;

    private void Awake()
    {
        if (raycastManager == null)
        {
            raycastManager = UnityCompat.FindFirst<ARRaycastManager>();
        }
        if (planeManager == null)
        {
            planeManager = UnityCompat.FindFirst<ARPlaneManager>();
        }
        if (markerPrefab == null)
        {
            markerPrefab = CreateDefaultMarker();
            markerPrefab.SetActive(false);
        }
    }

    private void Update()
    {
        if (Input.touchCount == 0)
        {
            return;
        }

        Touch touch = Input.GetTouch(0);
        if (touch.phase != TouchPhase.Began)
        {
            return;
        }

        // 关键：点击 UI 控件（按钮/输入框）时不放置标记，避免误触。
        if (EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject(touch.fingerId))
        {
            return;
        }

        if (raycastManager == null)
        {
            return;
        }

        if (raycastManager.Raycast(touch.position, Hits, TrackableType.PlaneWithinPolygon))
        {
            Pose hitPose = Hits[0].pose;
            PlaceMarker(hitPose.position, hitPose.rotation);
        }
    }

    private void PlaceMarker(Vector3 position, Quaternion rotation)
    {
        if (placedMarker == null)
        {
            placedMarker = Instantiate(markerPrefab, position, rotation);
            placedMarker.SetActive(true);
        }
        else
        {
            placedMarker.transform.SetPositionAndRotation(position, rotation);
        }
    }

    private static GameObject CreateDefaultMarker()
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "IssueMarker";
        cube.transform.localScale = new Vector3(0.15f, 0.06f, 0.15f);
        Renderer renderer = cube.GetComponent<Renderer>();
        renderer.material.color = new Color(1f, 0.35f, 0.2f);
        return cube;
    }
}

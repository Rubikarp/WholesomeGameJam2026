using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JeffGrawAssets.FlexibleUI
{
public static class MenuItemCommon
{
    public static GameObject GetOrCreateCanvasGameObject()
    {
        GameObject selectedGo = Selection.activeGameObject;

        var canvas = selectedGo != null ? selectedGo.GetComponentInParent<Canvas>() : null;
        if (canvas != null && canvas.gameObject.activeInHierarchy)
            return canvas.gameObject;

        var canvasArray = StageUtility.GetCurrentStageHandle().FindComponentsOfType<Canvas>();
        foreach (var canvasElement in canvasArray)
            if (IsValidCanvas(canvasElement))
                return canvasElement.gameObject;

        return CreateNewUI();
    }

    public static bool IsValidCanvas(Canvas canvas)
    {
        if (canvas == null || !canvas.gameObject.activeInHierarchy)
            return false;

        if (EditorUtility.IsPersistent(canvas) || (canvas.hideFlags & HideFlags.HideInHierarchy) != 0)
            return false;

        return StageUtility.GetStageHandle(canvas.gameObject) == StageUtility.GetCurrentStageHandle();
    }

    public static GameObject CreateNewUI()
    {
        var root = new GameObject("Canvas") { layer = LayerMask.NameToLayer("UI") };
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        root.AddComponent<CanvasScaler>();
        root.AddComponent<GraphicRaycaster>();
        Undo.RegisterCreatedObjectUndo(root, "Create " + root.name);
        CreateEventSystem(false);
        return root;
    }

    public static void CreateEventSystem(bool select, GameObject parent = null)
    {
        var stage = parent == null ? StageUtility.GetCurrentStageHandle() : StageUtility.GetStageHandle(parent);
        var esys = stage.FindComponentOfType<EventSystem>();
        if (esys == null)
        {
            var eventSystem = new GameObject("EventSystem");
            GameObjectUtility.SetParentAndAlign(eventSystem, parent);
            esys = eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();

            Undo.RegisterCreatedObjectUndo(eventSystem, "Create " + eventSystem.name);
        }

        if (select && esys != null)
        {
            Selection.activeGameObject = esys.gameObject;
        }
    }

    public static void CopyImageProps(Image source, Image target)
    {
        target.color = source.color;
        target.sprite = source.sprite;
        target.raycastTarget = source.raycastTarget;
        target.raycastPadding = source.raycastPadding;
        target.type = source.type;
        target.useSpriteMesh = source.useSpriteMesh;
        target.preserveAspect = source.preserveAspect;
        target.fillCenter = source.fillCenter;
        target.fillMethod = source.fillMethod;
        target.fillOrigin = source.fillOrigin;
        target.fillAmount = source.fillAmount;
        target.fillClockwise = source.fillClockwise;
        target.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
    }
}
}
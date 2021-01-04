using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Illusion.Extensions;
using KKAPI.Studio;
using Studio;
using UnityEngine;

namespace StudioObjectManipulatorGizmo
{
    [BepInPlugin(GUID, Name, Version)]
    [BepInProcess("CharaStudio")]
    [BepInIncompatibility("shortcutsKoi.guideObjectPort")]
    [BepInDependency(KKAPI.KoikatuAPI.GUID, "1.10")]
    public class ObjectManipulatorGizmoPlugin : BaseUnityPlugin
    {
        public const string GUID = "ObjectManipulatorGizmo";
        public const string Name = "ObjectManipulatorGizmo";
        public const string Version = "1.0.0";

        private float _lastFov;


        private GameObject _gizmoRoot;

        private void OnDestroy()
        {
            Destroy(_gizmoRoot);
            _hi?.UnpatchAll(_hi.Id);
            _selectedObjects = null;
        }

        private static GameObject _moveObj, _rotObj, _scaleObj;

        internal static new ManualLogSource Logger;

        private Harmony _hi;
        private static HashSet<GuideObject> _selectedObjects;
        private static bool _lastAnySelected;
        private Camera _camera;

        private void Awake()
        {
            Logger = base.Logger;

            _hi = Harmony.CreateAndPatchAll(typeof(ObjectManipulatorGizmoPlugin), GUID);

            if (StudioAPI.StudioLoaded)
                Initialize();
            else
                StudioAPI.StudioLoadedChanged += (sender, args) => Initialize();
        }

        private void Initialize()
        {
            if (!StudioAPI.StudioLoaded) return;

            _camera = Camera.main;
            if (_camera == null) throw new ArgumentException("Camera.main not found");

            var origRoot = Traverse.Create(GuideObjectManager.Instance).Field<GameObject>("objectOriginal").Value;
            if (origRoot == null) throw new ArgumentException("origRoot not found");

            _gizmoRoot = Instantiate(origRoot, _camera.transform);
            _gizmoRoot.gameObject.name = "CustomManipulatorGizmo";

            // todo make a fix plugin that reuses single gizmo instead of creating copies fore ach object?
            var go = _gizmoRoot.GetComponent<GuideObject>();
            Destroy(go);

            AdjustScalingToFov();

            _gizmoRoot.transform.localEulerAngles = new Vector3(17f, 150f, 343f);

            foreach (Transform child in _gizmoRoot.transform)
            {
                switch (child.name)
                {
                    case "move":
                        _moveObj = child.gameObject;
                        foreach (var guideMove in _moveObj.GetComponentsInChildren<GuideMove>())
                        {
                            guideMove.moveCalc = GuideMove.MoveCalc.TYPE3;
                            Traverse.Create(guideMove).Field<Transform>("transformRoot").Value = _gizmoRoot.transform;
                            //guideMove.onDragAction = () => Cursor.lockState = CursorLockMode.Confined;
                            //guideMove.onEndDragAction = () => Cursor.lockState = CursorLockMode.None;
                        }

                        break;
                    case "rotation":
                        _rotObj = child.gameObject;
                        break;
                    case "scale":
                        _scaleObj = child.gameObject;
                        break;

                    default:
                        Destroy(child.gameObject);
                        break;
                }
            }

            if (_moveObj == null) throw new ArgumentException("_moveObj not found");
            if (_rotObj == null) throw new ArgumentException("_rotObj not found");
            if (_scaleObj == null) throw new ArgumentException("_scaleObj not found");

            // todo configurable? can work between 0.75 and 1.5
            _moveObj.transform.localScale = Vector3.one;
            _rotObj.transform.localScale = Vector3.one;
            _scaleObj.transform.localScale = Vector3.one;

            var layer = LayerMask.NameToLayer("Studio/Select");
            foreach (var child in _gizmoRoot.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = layer;

            SetVisibleGuide(GuideObjectManager.Instance.mode);

            // todo            guideEX.transform.localScale *= Camera.main.fieldOfView / scale;
            //copy.transform.localScale *= camera.fieldOfView / fov;


            _gizmoRoot.SetActive(true);


            _selectedObjects = Traverse.Create(Singleton<GuideObjectManager>.Instance)
                                   .Field<HashSet<GuideObject>>("hashSelectObject").Value ??
                               throw new ArgumentException("Couldn't get hashSelectObject");
        }

        private void Update()
        {
            if (_selectedObjects == null) return;

            var anySelected = _selectedObjects.Count > 0;
            if (_lastAnySelected != anySelected)
            {
                _lastAnySelected = anySelected;
                SetVisibleGuide();
            }

            if (_lastFov != _camera.fieldOfView)
                AdjustScalingToFov();
        }

        private void AdjustScalingToFov()
        {
            var screenPos = new Vector3(Screen.width * 0.9f, Screen.height * 0.14f, 6f);
            _gizmoRoot.transform.position = _camera.ScreenToWorldPoint(screenPos);
            var fov = _camera.fieldOfView;
            _gizmoRoot.transform.localScale = Vector3.one * (fov / 23f);
            _lastFov = fov;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GuideObjectManager), nameof(GuideObjectManager.mode), MethodType.Setter)]
        private static void SetModeHook(int value, int ___m_Mode)
        {
            if (value != ___m_Mode && _moveObj != null)
            {
                SetVisibleGuide(value);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GuideObjectManager), nameof(GuideObjectManager.SetVisibleTranslation))]
        private static void SetVisibleTranslationHook()
        {
            SetVisibleGuide();
        }

        private static void SetVisibleGuide()
        {
            SetVisibleGuide(GuideObjectManager.Instance.mode);
        }
        private static void SetVisibleGuide(int value)
        {
            //todo add setting
            if (!_lastAnySelected)
            {
                _moveObj.SetActiveIfDifferent(false);
                _rotObj.SetActiveIfDifferent(false);
                _scaleObj.SetActiveIfDifferent(false);
                return;
            }

            switch (value)
            {
                case 0:
                    // Some objects can't be moved
                    var moveIsVisible = Singleton<Studio.Studio>.Instance.workInfo?.visibleAxisTranslation ?? true;
                    _moveObj.SetActiveIfDifferent(moveIsVisible);
                    _rotObj.SetActiveIfDifferent(false);
                    _scaleObj.SetActiveIfDifferent(false);
                    break;
                case 1:
                    _moveObj.SetActiveIfDifferent(false);
                    _rotObj.SetActiveIfDifferent(true);
                    _scaleObj.SetActiveIfDifferent(false);
                    break;
                case 2:
                    // todo some objects can't be scaled
                    _moveObj.SetActiveIfDifferent(false);
                    _rotObj.SetActiveIfDifferent(false);
                    _scaleObj.SetActiveIfDifferent(true);
                    break;
                default:
                    Logger.LogWarning("Unknown GuideObject mode - " + value);
                    break;
            }
        }
    }
}
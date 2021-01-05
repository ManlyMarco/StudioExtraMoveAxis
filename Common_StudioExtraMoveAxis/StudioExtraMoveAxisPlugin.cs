using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Illusion.Extensions;
using KKAPI.Studio;
using KKAPI.Studio.UI;
using KKAPI.Utilities;
using Studio;
using UnityEngine;

namespace StudioExtraMoveAxis
{
    [BepInPlugin(GUID, Name, Version)]
    [BepInDependency(KKAPI.KoikatuAPI.GUID, "1.10")]
    public partial class StudioExtraMoveAxisPlugin : BaseUnityPlugin
    {
        public const string GUID = "StudioExtraMoveAxis";
        public const string Name = "Extra move axis in bottom right corner";
        public const string Version = "1.0.0";

        internal static new ManualLogSource Logger;

        private static ConfigEntry<bool> _showGizmo;
        private static ConfigEntry<bool> _referenceToSelectedObject;

        private static Harmony _hi;

        private static GameObject _gizmoRoot;
        private static GameObject _moveObj, _rotObj, _scaleObj;
        private static GuideMove[] _guideMoves;
        private static FieldInfo _fTransformRoot;

        private static HashSet<GuideObject> _selectedObjects;
        private static bool _lastAnySelected;

        private static Camera _camera;
        private static float _lastFov;

        private void Awake()
        {
            Logger = base.Logger;

            _showGizmo = Config.Bind("Extra gizmos", "Show extra move gizmo", false,
                "Show extra set of gizmos in the bottom right corner of the screen. An object must be selected for gizmo to be visible." +
                "You can use left toolbar to turn the gizmo on or off.");
            _referenceToSelectedObject = Config.Bind("Extra gizmos", "Use selected object as reference", true,
                "If true, using the extra XYZ move gizmo is the same as using the default gizmo on the currently selected object (so direction of the arrow may not be the same as direction of movement).\n" +
                "If false, current camera position is used as the reference frame, so using the extra XYZ gizmo will move the object to where the gizmo arrows are actually pointing.\n" +
                "Change currently selected object to apply the setting.");

            if (StudioAPI.StudioLoaded)
            {
                // for debug purposes, doesn't get called normally
                Initialize();
            }
            else
            {
                var texRes = ResourceUtils.GetEmbeddedResource("toolbar_icon.png", typeof(StudioExtraMoveAxisPlugin).Assembly) ?? throw new ArgumentException("icon resource not found");
                var buttonTex = texRes.LoadTexture(TextureFormat.DXT5, false) ?? throw new ArgumentException("failed to load icon texture");
                var tgl = CustomToolbarButtons.AddLeftToolbarToggle(buttonTex, _showGizmo.Value, b => _showGizmo.Value = b);
                _showGizmo.SettingChanged += (o, eventArgs) =>
                {
                    tgl.SetValue(_showGizmo.Value, false);
                    SetVisibility();
                };

                StudioAPI.StudioLoadedChanged += (sender, args) => Initialize();
            }
        }

        private void OnDestroy()
        {
            Destroy(_gizmoRoot);
            _hi?.UnpatchAll(_hi.Id);
            _selectedObjects = null;
        }

        private void Update()
        {
            if (_selectedObjects == null || !_showGizmo.Value) return;

            var anySelected = _selectedObjects.Count > 0;
            if (_lastAnySelected != anySelected)
            {
                _lastAnySelected = anySelected;
                SetVisibility();
            }

            if (_lastFov != _camera.fieldOfView)
                AdjustScaleToFov();
        }

        private static void Initialize()
        {
            if (!StudioAPI.StudioLoaded) return;

            _camera = Camera.main;
            if (_camera == null) throw new ArgumentException("Camera.main not found");

            var origRoot = Traverse.Create(GuideObjectManager.Instance).Field<GameObject>("objectOriginal").Value;
            if (origRoot == null) throw new ArgumentException("origRoot not found");

            _fTransformRoot = AccessTools.Field(typeof(GuideMove), "transformRoot") ??
                              throw new ArgumentException("Couldn't get transformRoot field");
            _selectedObjects = Traverse.Create(Singleton<GuideObjectManager>.Instance)
                                   .Field<HashSet<GuideObject>>("hashSelectObject").Value ??
                               throw new ArgumentException("Couldn't get hashSelectObject");

            _gizmoRoot = Instantiate(origRoot, _camera.transform);
            _gizmoRoot.gameObject.name = "CustomManipulatorGizmo";

            var go = _gizmoRoot.GetComponent<GuideObject>();
            Destroy(go);

            AdjustScaleToFov();

            _gizmoRoot.transform.localEulerAngles = new Vector3(17f, 150f, 343f);

            var visibleLayer = LayerMask.NameToLayer("Studio/Select");
            foreach (Transform rootChild in _gizmoRoot.transform)
            {
                switch (rootChild.name)
                {
                    case "move":
                        _moveObj = rootChild.gameObject;
                        _guideMoves = _moveObj.GetComponentsInChildren<GuideMove>();
                        break;

                    case "rotation":
                        _rotObj = rootChild.gameObject;
                        break;

                    case "scale":
                        _scaleObj = rootChild.gameObject;
                        break;

                    default:
                        Destroy(rootChild.gameObject);
                        break;
                }

                // todo configurable? can work between 0.75 and 1.5
                rootChild.localScale = Vector3.one;

                foreach (var subChild in rootChild.GetComponentsInChildren<Transform>(true))
                {
                    subChild.gameObject.layer = visibleLayer;
                    // Fix center point gizmos being disabled in some games
                    subChild.gameObject.SetActiveIfDifferent(true);
                }
            }

            if (_moveObj == null) throw new ArgumentException("_moveObj not found");
            if (_rotObj == null) throw new ArgumentException("_rotObj not found");
            if (_scaleObj == null) throw new ArgumentException("_scaleObj not found");

            SetVisibility(GuideObjectManager.Instance.mode);

            _gizmoRoot.SetActiveIfDifferent(true);

            _hi = Harmony.CreateAndPatchAll(typeof(Hooks), GUID);
        }

        private static void AdjustScaleToFov()
        {
            var screenPos = new Vector3(Screen.width * 0.9f, Screen.height * 0.14f, 6f);
            _gizmoRoot.transform.position = _camera.ScreenToWorldPoint(screenPos);
            var fov = _camera.fieldOfView;
            _gizmoRoot.transform.localScale = Vector3.one * (fov / 23f);
            _lastFov = fov;
        }

        private static void SetMoveRootTr(Transform rootTransform)
        {
            if (!_referenceToSelectedObject.Value)
                rootTransform = _gizmoRoot.transform;

            for (var i = 0; i < _guideMoves.Length; i++)
            {
                var guideMove = _guideMoves[i];
                guideMove.moveCalc = GuideMove.MoveCalc.TYPE3;
                _fTransformRoot.SetValue(guideMove, rootTransform);
                // not working because cursor doesnt delta move
                //guideMove.onDragAction = () => Cursor.lockState = CursorLockMode.Locked;
                //guideMove.onEndDragAction = () => Cursor.lockState = CursorLockMode.None;
            }
        }

        private static void SetVisibility()
        {
            SetVisibility(GuideObjectManager.Instance.mode);
        }

        private static void SetVisibility(int value)
        {
            //todo add setting
            if (!_lastAnySelected || !_showGizmo.Value)
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

        private static class Hooks
        {
            [HarmonyPrefix]
            [HarmonyPatch(typeof(GuideObjectManager), nameof(GuideObjectManager.mode), MethodType.Setter)]
            private static void SetModeHook(int value, int ___m_Mode)
            {
                if (value != ___m_Mode && _moveObj != null)
                {
                    SetVisibility(value);
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(GuideObjectManager), nameof(GuideObjectManager.SetVisibleTranslation))]
            private static void SetVisibleTranslationHook()
            {
                SetVisibility();
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(GuideObjectManager), "AddObject")]
            [HarmonyPatch(typeof(GuideObjectManager), "SetDeselectObject")]
            private static void AddObjectHook(GuideObject _object)
            {
                if (_object == null || _selectedObjects == null) return;

                var selectedObj = _selectedObjects.FirstOrDefault(x => x.isActive);
                if (selectedObj != null)
                    SetMoveRootTr(selectedObj.transformTarget);

                SetVisibility();
            }
        }

    }
}
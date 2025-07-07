using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;
using Object = UnityEngine.Object;

namespace AwAVR
{
    internal class Parameter
    {
        public string Name; // Name of the parameter (in the FX controller and expression parameters)
        public List<VRCExpressionsMenu> Menus; // Menus that the parameter is in

        public Parameter()
        {
            Name = String.Empty;
            Menus = new List<VRCExpressionsMenu>();
        }
    }

    public class ParameterRenamer : EditorWindow
    {
        #region Variables

        private static string _windowTitle = "Parameter Renamer";
        private List<VRCAvatarDescriptor> _avatars = null;
        private VRCAvatarDescriptor _avatar = null;
        private VRCExpressionParameters _vrcParams = null;
        private AnimatorController _fx = null;
        private VRCExpressionsMenu _vrcMainMenu = null;
        private List<VRCExpressionsMenu> _menuList = new List<VRCExpressionsMenu>();
        private int _parameterIndex = 0;
        private string _newParameterName = "";
        private Parameter _parameter = new Parameter();

        #endregion

        #region Window

        [MenuItem("Tools/AwA/Parameter Renamer", false, -100)]
        public static void ShowWindow()
        {
            var window = GetWindow<ParameterRenamer>(_windowTitle);
            window.titleContent = new GUIContent(
                image: EditorGUIUtility.IconContent("d_editicon.sml").image,
                text: _windowTitle,
                tooltip: "Rename a parameter everywhere"
            );
        }

        public void OnEnable()
        {
            _avatars = Core.GetAvatarsInScene();

            if (_avatars.Count == 0)
            {
                EditorGUILayout.HelpBox("Please place an avatar in the scene", MessageType.Error);
                _avatars = null;
                return;
            }

            if (_avatars.Count == 1)
            {
                _avatar = _avatars.First();
                _avatars.Clear();
                return;
            }
        }

        public void OnGUI()
        {
            Core.Title(_windowTitle);

            // Warning
            EditorGUILayout.HelpBox(
                "This tool renames parameters in the FX animator and all VRChat components on your currently selected avatar.",
                MessageType.Warning);
            EditorGUILayout.HelpBox(
                "This tool is in beta!\n\nPlease always have an up to date backup of your avatar before continuing.\nI am not responsible for broken avatars",
                MessageType.Error);

            // Avatar
            Core.GetAvatar(ref _avatar, ref _avatars);
            if (!_avatar)
            {
                EditorGUILayout.HelpBox("Please select an avatar.", MessageType.Error);
                return;
            }

            // Get avatar parameters
            if (_avatar.expressionParameters)
                _vrcParams = _avatar.expressionParameters;
            else
                return;

            // Get FX Animator
            _fx = Core.GetFXController(_avatar);

            if (!_fx || _fx.parameters.Length == 0)
            {
                EditorGUILayout.HelpBox("Can't find an FX animator on your avatar. Please add one.", MessageType.Error);
                return;
            }

            // Get main menu
            _vrcMainMenu = _avatar.expressionsMenu;
            if (!_vrcMainMenu)
            {
                EditorGUILayout.HelpBox("Can't find a main menu on your avatar. Please add one.", MessageType.Error);
                return;
            }

            // Get all menus
            _menuList.Clear();
            GetSubMenus(_vrcMainMenu);

            // Parameter renames stuffs
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // Parameter Select
                List<string> paramNames = new List<string>();
                foreach (var parameter in _vrcParams.parameters)
                {
                    paramNames.Add(parameter.name);
                }

                _parameterIndex = EditorGUILayout.Popup("Parameter to rename", _parameterIndex, paramNames.ToArray());
                _parameter.Name = _vrcParams.parameters[_parameterIndex].name;
                GetParameterMenus();

                // New parameter name
                _newParameterName = EditorGUILayout.TextField("New parameter name", _newParameterName);

                // Button
                using (new EditorGUILayout.HorizontalScope())
                {
                    // Menu button
                    if (GUILayout.Button("Show menu name(s)"))
                    {
                        if (_parameter.Menus.Count == 0)
                        {
                            EditorUtility.DisplayDialog("No menus found",
                                $"The parameter \"{_parameter.Name}\" was not found in any menus on the selected avatar.",
                                "OK");
                        }
                        else
                        {
                            string menuList = "\n";
                            foreach (var menu in _parameter.Menus)
                            {
                                menuList += $"- {menu.name}\n";
                            }

                            EditorUtility.DisplayDialog($"Parameter \"{_parameter.Name}\"",
                                $"Parameter \"{_parameter.Name}\" is found in {_parameter.Menus.Count} menu{(_parameter.Menus.Count > 1 ? "s" : "")}: {menuList}",
                                "OK");
                        }
                    }

                    // Rename button
                    if (GUILayout.Button("Rename"))
                    {
                        if (string.IsNullOrWhiteSpace(_newParameterName))
                        {
                            EditorUtility.DisplayDialog("Empty name", "New parameter name can't be empty.", "OK");
                        }
                        else
                        {
                            if (EditorUtility.DisplayDialog("Confirm",
                                    $"Are you sure you want to rename parameter \"{_parameter.Name}\" to \"{_newParameterName}\"?",
                                    "Yes", "No"))
                            {
                                Rename();
                            }
                        }
                    }
                }
            }
        }

        #endregion

        #region Methods

        private void GetSubMenus(VRCExpressionsMenu menu)
        {
            if (!menu)
                return;

            foreach (var control in menu.controls)
            {
                if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu)
                {
                    var submenu = (VRCExpressionsMenu)control.subMenu;
                    if (submenu != null)
                    {
                        _menuList.Add(submenu);
                        GetSubMenus(submenu);
                    }
                }
            }
        }

        private void GetParameterMenus()
        {
            _parameter.Menus.Clear();
            foreach (var menu in _menuList)
            {
                foreach (var control in menu.controls)
                {
                    // Check if in parameter
                    if (control.parameter.name == _parameter.Name && !_parameter.Menus.Contains(menu))
                    {
                        _parameter.Menus.Add(menu);
                        continue;
                    }

                    // Check in sub parameters
                    foreach (var subParameter in control.subParameters)
                    {
                        if (subParameter.name == _parameter.Name && !_parameter.Menus.Contains(menu))
                        {
                            _parameter.Menus.Add(menu);
                        }
                    }
                }
            }
        }

        #endregion

        #region Rename Methods

        private void Rename()
        {
            // Define all objects
            var objectList = new Object[] { _vrcParams, _fx }.Concat(_parameter.Menus.Cast<Object>()).ToArray();
            Undo.RecordObjects(objectList, "Rename parameter");

            // Do renaming
            RenameInVrcParametersList();
            RenameInMenus();
            // TODO: Check in all parameters, instead of just the FX one
            RenameInAnimator(_fx);

            // Clean all objects
            foreach (var o in objectList)
            {
                Core.Cleany(o);
            }
        }

        private void RenameInVrcParametersList()
        {
            foreach (var parameter in _vrcParams.parameters)
            {
                if (parameter.name == _parameter.Name)
                    parameter.name = _newParameterName;
            }
        }

        private void RenameInMenus()
        {
            foreach (var menu in _parameter.Menus)
            {
                foreach (var control in menu.controls)
                {
                    // Rename parameter
                    if (control.parameter.name == _parameter.Name)
                    {
                        control.parameter.name = _newParameterName;
                    }

                    // Rename all sub parameters
                    foreach (var subParam in control.subParameters)
                    {
                        if (subParam.name == _parameter.Name)
                        {
                            subParam.name = _newParameterName;
                        }
                    }
                }
            }
        }

        private void RenameInAnimator(AnimatorController animator)
        {
            RenameInAnimatorParameters(ref animator);

            // foreach (var layer in animator.layers)
            for (int i = 0; i < animator.layers.Length; i++)
            {
                var layer = animator.layers[i];
                RenameInLayer(ref layer);
            }
        }

        private void RenameInAnimatorParameters(ref AnimatorController animator)
        {
            var parameters = animator.parameters;
            for (var i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == _parameter.Name)
                {
                    // Create a new parameter with the new name and copy the properties
                    var newParam = new AnimatorControllerParameter
                    {
                        name = _newParameterName,
                        type = parameters[i].type,
                        defaultBool = parameters[i].defaultBool,
                        defaultFloat = parameters[i].defaultFloat,
                        defaultInt = parameters[i].defaultInt
                    };

                    // Replace the old parameter with the new one in the array
                    parameters[i] = newParam;
                }
            }

            animator.parameters = parameters;
        }

        private void RenameInLayer(ref AnimatorControllerLayer layer)
        {
            // foreach (var state in layer.stateMachine.states)
            for (int i = 0; i < layer.stateMachine.states.Length; i++)
            {
                var state = layer.stateMachine.states[i];

                RenameStateParameters(ref state);
                RenameInStateBehaviour(ref state);
                RenameInStateTransitions(ref state);
                if (state.state.motion is BlendTree blendTree)
                {
                    RenameInBlendTree(ref blendTree);
                }
            }
        }

        private void RenameInStateBehaviour(ref ChildAnimatorState state)
        {
            foreach (var behaviour in state.state.behaviours)
            {
                if (behaviour is VRC_AvatarParameterDriver driver)
                {
                    foreach (var param in driver.parameters)
                    {
                        if (param.name == _parameter.Name)
                        {
                            param.name = _newParameterName;
                        }
                    }
                }
            }
        }

        private void RenameStateParameters(ref ChildAnimatorState state)
        {
            var s = state.state;
            if (s.speedParameterActive)
                if (s.speedParameter == _parameter.Name)
                    s.speedParameter = _newParameterName;

            if (s.timeParameterActive)
                if (s.timeParameter == _parameter.Name)
                    s.timeParameter = _newParameterName;

            if (s.mirrorParameterActive)
                if (s.mirrorParameter == _parameter.Name)
                    s.mirrorParameter = _newParameterName;

            if (s.cycleOffsetParameterActive)
                if (s.cycleOffsetParameter == _parameter.Name)
                    s.cycleOffsetParameter = _newParameterName;
        }

        private void RenameInStateTransitions(ref ChildAnimatorState state)
        {
            foreach (var transition in state.state.transitions)
            {
                AnimatorCondition[] conditions = transition.conditions;

                // Loop through the array by index
                for (int i = 0; i < conditions.Length; i++)
                {
                    if (conditions[i].parameter == _parameter.Name)
                        conditions[i].parameter = _newParameterName;
                }

                transition.conditions = conditions;
            }
        }

        private void RenameInBlendTree(ref BlendTree blendTree)
        {
            if (blendTree.blendParameter == _parameter.Name)
                blendTree.blendParameter = _newParameterName;

            if (blendTree.blendParameterY == _parameter.Name)
                blendTree.blendParameterY = _newParameterName;

            foreach (var childMotion in blendTree.children)
            {
                if (childMotion.motion is BlendTree childBlendTree)
                {
                    RenameInBlendTree(ref childBlendTree);
                }
            }
        }

        #endregion
    }
}
using System;
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
        private bool _autoRename = false;

        #endregion

        #region Window

        [MenuItem("Tools/AwA/Parameter Renamer", false, -100)]
        static void ShowWindow()
        {
            var window = GetWindow<ParameterRenamer>(_windowTitle);
            window.titleContent = new GUIContent(
                image: EditorGUIUtility.IconContent("d_editicon.sml").image,
                text: _windowTitle,
                tooltip: "Rename a parameter everywhere"
            );
        }

        public static void Show(string parameterName = "", VRCAvatarDescriptor avatar = null,
            string newParameterName = "", bool autoRename = false)
        {
            ShowWindow();
            var window = GetWindow<ParameterRenamer>(_windowTitle);

            if (avatar)
                window._avatar = avatar;
            window.RefreshAvatarInfo();

            // Set parameter if given
            if (string.IsNullOrWhiteSpace(parameterName) || window._vrcParams == null) return;

            var index = window._vrcParams.parameters.ToList().FindIndex(p => p.name == parameterName);
            if (index >= 0)
                window._parameterIndex = index;

            // Set new parameter name
            window._newParameterName = newParameterName;

            // Set auto rename
            window._autoRename = autoRename;
        }

        // public static void

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

            RefreshAvatarInfo();
        }

        public void OnGUI()
        {
            Core.Title(_windowTitle);

            if (!RefreshAvatarInfo()) return;

            // Parameter renames stuffs
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // Parameter Select
                List<string> paramNames = new List<string>();
                foreach (var parameter in _vrcParams.parameters)
                {
                    paramNames.Add(_parameterIndex == paramNames.Count
                        ? parameter.name
                        : $"{parameter.name} [{parameter.valueType.ToString().ToLower()}]");
                }

                _parameterIndex = EditorGUILayout.Popup("Parameter to rename", _parameterIndex, paramNames.ToArray());
                _parameter.Name = _vrcParams.parameters[_parameterIndex].name;
                GetParameterMenus();

                // New parameter name + name checking
                var isAlreadyUsed = _vrcParams.parameters.Any(p => p.name == _newParameterName);
                _newParameterName = EditorGUILayout.TextField("New parameter name", _newParameterName);
                if (isAlreadyUsed)
                    EditorGUILayout.HelpBox("New parameter name is already used", MessageType.Error);

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
                    using (new EditorGUI.DisabledGroupScope(isAlreadyUsed))
                    {
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

                // Auto Rename
                if (_autoRename)
                {
                    Rename();
                    _autoRename = false;
                    this.Close();
                }
            }
        }

        bool RefreshAvatarInfo()
        {
            // Avatar
            Core.GetAvatar(ref _avatar, ref _avatars);
            if (!_avatar)
            {
                EditorGUILayout.HelpBox("Please select an avatar.", MessageType.Error);
                return false;
            }

            // Get avatar parameters
            if (_avatar.expressionParameters)
                _vrcParams = _avatar.expressionParameters;
            else
                return false;

            // Get FX Animator
            _fx = Core.GetAnimatorController(_avatar);

            if (!_fx || _fx.parameters.Length == 0)
            {
                EditorGUILayout.HelpBox("Can't find an FX animator on your avatar. Please add one.", MessageType.Error);
                return false;
            }

            // Get main menu
            _vrcMainMenu = _avatar.expressionsMenu;
            if (!_vrcMainMenu)
            {
                EditorGUILayout.HelpBox("Can't find a main menu on your avatar. Please add one.", MessageType.Error);
                return false;
            }

            // Get all menus
            _menuList.Clear();
            GetSubMenus(_vrcMainMenu);

            return true;
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
            // Define objects for undo
            var affectedObjects = new HashSet<Object>
            {
                _vrcParams,
                _fx
            };

            foreach (var menu in _parameter.Menus)
                affectedObjects.Add(menu);

            foreach (var type in Enum.GetValues(typeof(Core.ExpressionAnimatorType))
                         .Cast<Core.ExpressionAnimatorType>())
            {
                var animator = Core.GetAnimatorController(_avatar, type);
                if (animator)
                {
                    affectedObjects.Add(animator);
                    CollectAnimatorObjects(animator, ref affectedObjects);
                }
            }

            Undo.RecordObjects(affectedObjects.ToArray(),
                $"Rename parameter: {_parameter.Name} -> {_newParameterName}");

            // Do renaming
            RenameInVrcParametersList();
            RenameInMenus();
            // Rename all the animators
            foreach (var type in System.Enum.GetValues(typeof(Core.ExpressionAnimatorType)))
            {
                var animator = Core.GetAnimatorController(_avatar, (Core.ExpressionAnimatorType)type);
                if (animator)
                    RenameInAnimator(animator);
            }

            // Clean all objects
            Core.CleanObjects(affectedObjects.ToArray());
        }

        private void CollectAnimatorObjects(AnimatorController animator, ref HashSet<Object> affected)
        {
            affected.Add(animator);

            foreach (var layer in animator.layers)
            {
                var stateMachine = layer.stateMachine;
                affected.Add(stateMachine);

                // Collect states
                foreach (var childState in stateMachine.states)
                {
                    affected.Add(childState.state);

                    // Behaviours
                    foreach (var behaviour in childState.state.behaviours)
                    {
                        affected.Add(behaviour);
                    }

                    // Blendtrees
                    if (childState.state.motion is BlendTree blendTree)
                    {
                        CollectBlendTreesRecursive(blendTree, ref affected);
                    }

                    // Transitions
                    foreach (var transition in childState.state.transitions)
                    {
                        affected.Add(transition);
                    }
                }

                // Any State transitions
                foreach (var transition in stateMachine.anyStateTransitions)
                {
                    affected.Add(transition);
                }
            }
        }

        private void CollectBlendTreesRecursive(BlendTree blendTree, ref HashSet<Object> affected)
        {
            if (blendTree == null || affected.Contains(blendTree)) return;

            affected.Add(blendTree);

            foreach (var child in blendTree.children)
            {
                if (child.motion is BlendTree childTree)
                {
                    CollectBlendTreesRecursive(childTree, ref affected);
                }
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
            // States
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

            // Any State
            RenameInAnyState(ref layer);
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
                            param.name = _newParameterName;

                        if (param.source == _parameter.Name)
                            param.source = _newParameterName;
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

        private void RenameInAnyState(ref AnimatorControllerLayer layer)
        {
            for (var i = 0; i < layer.stateMachine.anyStateTransitions.Length; i++)
            {
                var transition = layer.stateMachine.anyStateTransitions[i];
                AnimatorCondition[] conditions = transition.conditions;

                for (var j = 0; j < conditions.Length; j++)
                {
                    if (conditions[j].parameter == _parameter.Name)
                        conditions[j].parameter = _newParameterName;
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
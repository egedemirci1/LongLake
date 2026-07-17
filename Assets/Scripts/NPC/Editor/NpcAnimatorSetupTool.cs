using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class NpcAnimatorSetupTool
{
    [MenuItem("Tools/Setup NPC Animator Controllers")]
    public static void SetupControllers()
    {
        string[] controllerPaths = new string[]
        {
            "Assets/Art/Animations/Feminine.controller",
            "Assets/Art/Animations/Masculine.controller"
        };

        AnimationClip talkingClip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Art/Animations/Talking.anim");
        if (talkingClip == null)
        {
            Debug.LogError("Talking.anim not found at 'Assets/Art/Animations/Talking.anim'");
            return;
        }

        foreach (var path in controllerPaths)
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
            {
                Debug.LogError($"Controller not found at path: {path}");
                continue;
            }

            // 1. Parameter ekle
            bool hasParam = false;
            foreach (var param in controller.parameters)
            {
                if (param.name == "IsTalking")
                {
                    hasParam = true;
                    break;
                }
            }

            if (!hasParam)
            {
                controller.AddParameter("IsTalking", AnimatorControllerParameterType.Bool);
            }

            // 2. State ve transition'ları ekle
            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            ChildAnimatorState talkingState = default;
            bool hasTalkingState = false;

            foreach (var childState in stateMachine.states)
            {
                if (childState.state.name == "Talking")
                {
                    talkingState = childState;
                    hasTalkingState = true;
                    break;
                }
            }

            AnimatorState tState = null;
            if (!hasTalkingState)
            {
                tState = stateMachine.AddState("Talking", new Vector3(250, 350, 0));
                tState.motion = talkingClip;
            }
            else
            {
                tState = talkingState.state;
                tState.motion = talkingClip;
            }

            // Blend Tree state'ini bul
            AnimatorState blendTreeState = null;
            foreach (var childState in stateMachine.states)
            {
                if (childState.state.name == "Blend Tree")
                {
                    blendTreeState = childState.state;
                    break;
                }
            }

            if (blendTreeState == null)
            {
                Debug.LogError($"Blend Tree state not found in controller: {path}");
                continue;
            }

            // Blend Tree -> Talking geçişini ekle
            bool hasToTalking = false;
            foreach (var transition in blendTreeState.transitions)
            {
                if (transition.destinationState == tState)
                {
                    hasToTalking = true;
                    break;
                }
            }

            if (!hasToTalking)
            {
                var transition = blendTreeState.AddTransition(tState);
                transition.hasExitTime = false;
                transition.duration = 0.2f;
                transition.AddCondition(AnimatorConditionMode.If, 0, "IsTalking");
            }

            // Talking -> Blend Tree geçişini ekle
            bool hasToBlendTree = false;
            foreach (var transition in tState.transitions)
            {
                if (transition.destinationState == blendTreeState)
                {
                    hasToBlendTree = true;
                    break;
                }
            }

            if (!hasToBlendTree)
            {
                var transition = tState.AddTransition(blendTreeState);
                transition.hasExitTime = false;
                transition.duration = 0.2f;
                transition.AddCondition(AnimatorConditionMode.IfNot, 0, "IsTalking");
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log($"Successfully setup talking animation for controller: {path}");
        }
    }
}

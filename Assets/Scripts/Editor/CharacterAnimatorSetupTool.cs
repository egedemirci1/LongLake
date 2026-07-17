using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class CharacterAnimatorSetupTool
{
    [MenuItem("Tools/Setup Character Animator Controllers")]
    public static void SetupControllers()
    {
        string[] controllerPaths = new string[]
        {
            "Assets/Art/Animations/Feminine.controller",
            "Assets/Art/Animations/Masculine.controller"
        };

        AnimationClip talkingClip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Art/Animations/Talking.anim");
        AnimationClip standingJumpClip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Art/Animations/StandingJump.anim");
        AnimationClip runningJumpClip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Art/Animations/RunningJump.anim");

        if (talkingClip == null || standingJumpClip == null || runningJumpClip == null)
        {
            Debug.LogError($"Animasyon klipleri yüklenemedi. Kontrol edin:\n" +
                           $"Talking: {talkingClip != null}\n" +
                           $"StandingJump: {standingJumpClip != null}\n" +
                           $"RunningJump: {runningJumpClip != null}");
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

            // 1. Parametreleri Ekle/Kontrol Et
            AddParameterIfNeeded(controller, "IsTalking", AnimatorControllerParameterType.Bool);
            AddParameterIfNeeded(controller, "StandingJump", AnimatorControllerParameterType.Trigger);
            AddParameterIfNeeded(controller, "RunningJump", AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;

            // Blend Tree State'ini Bul (Geri dönüş geçişleri için)
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

            // 2. Talking State Setup
            AnimatorState talkingState = GetOrCreateState(stateMachine, "Talking", talkingClip, new Vector3(250, 350, 0));
            AddTransitionIfNotExists(blendTreeState, talkingState, t =>
            {
                t.hasExitTime = false;
                t.duration = 0.2f;
                t.AddCondition(AnimatorConditionMode.If, 0, "IsTalking");
            });
            AddTransitionIfNotExists(talkingState, blendTreeState, t =>
            {
                t.hasExitTime = false;
                t.duration = 0.2f;
                t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsTalking");
            });

            // 3. Standing Jump State Setup
            AnimatorState standingJumpState = GetOrCreateState(stateMachine, "StandingJump", standingJumpClip, new Vector3(500, 200, 0));
            // Any State -> StandingJump
            AddAnyStateTransitionIfNotExists(stateMachine, standingJumpState, t =>
            {
                t.hasExitTime = false;
                t.duration = 0.1f;
                t.AddCondition(AnimatorConditionMode.If, 0, "StandingJump");
            });
            // StandingJump -> Blend Tree
            AddTransitionIfNotExists(standingJumpState, blendTreeState, t =>
            {
                t.hasExitTime = true;
                t.exitTime = 1.0f;
                t.duration = 0.25f;
            });

            // 4. Running Jump State Setup
            AnimatorState runningJumpState = GetOrCreateState(stateMachine, "RunningJump", runningJumpClip, new Vector3(500, 300, 0));
            // Any State -> RunningJump
            AddAnyStateTransitionIfNotExists(stateMachine, runningJumpState, t =>
            {
                t.hasExitTime = false;
                t.duration = 0.1f;
                t.AddCondition(AnimatorConditionMode.If, 0, "RunningJump");
            });
            // RunningJump -> Blend Tree
            AddTransitionIfNotExists(runningJumpState, blendTreeState, t =>
            {
                t.hasExitTime = true;
                t.exitTime = 1.0f;
                t.duration = 0.25f;
            });

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log($"Animator Controller başarıyla güncellendi: {path}");
        }
    }

    private static void AddParameterIfNeeded(AnimatorController controller, string name, AnimatorControllerParameterType type)
    {
        foreach (var param in controller.parameters)
        {
            if (param.name == name) return;
        }
        controller.AddParameter(name, type);
    }

    private static AnimatorState GetOrCreateState(AnimatorStateMachine stateMachine, string stateName, AnimationClip clip, Vector3 position)
    {
        foreach (var childState in stateMachine.states)
        {
            if (childState.state.name == stateName)
            {
                childState.state.motion = clip;
                return childState.state;
            }
        }
        AnimatorState newState = stateMachine.AddState(stateName, position);
        newState.motion = clip;
        return newState;
    }

    private static void AddTransitionIfNotExists(AnimatorState source, AnimatorState dest, System.Action<AnimatorStateTransition> configure)
    {
        foreach (var transition in source.transitions)
        {
            if (transition.destinationState == dest) return;
        }
        var newTransition = source.AddTransition(dest);
        configure(newTransition);
    }

    private static void AddAnyStateTransitionIfNotExists(AnimatorStateMachine stateMachine, AnimatorState dest, System.Action<AnimatorStateTransition> configure)
    {
        foreach (var transition in stateMachine.anyStateTransitions)
        {
            if (transition.destinationState == dest) return;
        }
        var newTransition = stateMachine.AddAnyStateTransition(dest);
        configure(newTransition);
    }
}

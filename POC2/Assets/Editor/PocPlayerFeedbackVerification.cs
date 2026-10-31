using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Actual play-mode CharacterController / physics checks; CLI without -quit.
[InitializeOnLoad]
public static class PocPlayerFeedbackVerification
{
    private const string RUNNING = "PocPlayerFeedbackVerification.running";
    private static int passes;
    private static int failures;
    private static InputActionAsset input;
    private static InputAction crouch;
    private static Keyboard keyboard;
    private static Gamepad pad;
    private static int inputStep;
    private static int inputFrame;
    private static InputSettings.BackgroundBehavior previousBackground;
    private static InputSettings.EditorInputBehaviorInPlayMode previousEditorInput;
    private static bool inputSettingsChanged;
    private static Action queuedInput;

    static PocPlayerFeedbackVerification()
    {
        EditorApplication.update += Tick;
    }

    public static void RunPlayChecks()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(RUNNING, true);
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (queuedInput != null)
        {
            Action queue = queuedInput;
            queuedInput = null;
            queue();
            inputFrame = Time.frameCount;
        }
        if (!SessionState.GetBool(RUNNING, false) || !EditorApplication.isPlaying)
        {
            return;
        }
        SessionState.SetBool(RUNNING, false);
        try
        {
            CheckPlayer();
            CheckDirectionAndSources();
            // Headless CLI has no focused Game View; route synthetic devices to play mode.
            previousBackground = InputSystem.settings.backgroundBehavior;
            previousEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            inputSettingsChanged = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            input = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions"));
            crouch = input.FindAction("Player/Crouch", true);
            keyboard = InputSystem.AddDevice<Keyboard>();
            pad = InputSystem.AddDevice<Gamepad>();
            crouch.Enable();
            InputSystem.onAfterUpdate += CheckInputs;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftCtrl));
            inputFrame = Time.frameCount;
        }
        catch (Exception exception)
        {
            failures++;
            Debug.LogException(exception);
            Finish();
        }
    }

    private static void CheckInputs()
    {
        if (InputState.currentUpdateType != InputUpdateType.Dynamic || queuedInput != null || Time.frameCount < inputFrame + 2)
        {
            return;
        }
        Debug.Log($"[PocPlayerFeedbackVerification] Input stage={inputStep} ctrl={keyboard.leftCtrlKey.ReadValue()} c={keyboard.cKey.ReadValue()} B={pad.buttonEast.ReadValue()} action={crouch.ReadValue<float>()} enabled={crouch.enabled} controls={crouch.controls.Count}");
        if (inputStep == 0)
        {
            Check(crouch.IsPressed(), "existing Player/Crouch Ctrl binding is held in real PlayerLoop");
            queuedInput = () => InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        }
        else if (inputStep == 1)
        {
            Check(!crouch.IsPressed(), "crouch releases with Ctrl");
            queuedInput = () => InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.East));
        }
        else
        {
            Check(crouch.IsPressed(), "existing gamepad B binding crouches in real PlayerLoop");
            InputSystem.onAfterUpdate -= CheckInputs;
            crouch.Disable();
            InputSystem.RemoveDevice(keyboard);
            InputSystem.RemoveDevice(pad);
            UnityEngine.Object.DestroyImmediate(input);
            Finish();
        }
        inputStep++;
        inputFrame = Time.frameCount;
    }

    private static void Finish()
    {
        if (inputSettingsChanged)
        {
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInput;
        }
        Debug.Log($"[PocPlayerFeedbackVerification] passes={passes}, failures={failures}");
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static void CheckPlayer()
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.position = new Vector3(0f, -0.5f, 0f);
        floor.transform.localScale = new Vector3(50f, 1f, 50f);
        GameObject player = new GameObject("Player");
        player.SetActive(false);
        CharacterController controller = player.AddComponent<CharacterController>();
        controller.height = 2f;
        controller.radius = 0.3f;
        controller.center = Vector3.up;
        GameObject camera = new GameObject("View");
        camera.transform.SetParent(player.transform, false);
        camera.transform.localPosition = new Vector3(0f, 1.8f, 0f);
        PlayerMovement movement = player.AddComponent<PlayerMovement>();
        Set(movement, "viewPivot", camera.transform);
        Set(movement, "moveSpeed", 6f);
        player.SetActive(true);
        movement.enabled = false;
        Physics.SyncTransforms();
        float feet = controller.bounds.min.y;
        Quaternion rotation = camera.transform.localRotation;
        movement.UpdateCrouch(true, 1f / 60f);
        Physics.SyncTransforms();
        Check(movement.IsCrouched && Mathf.Abs(controller.height - 1.1f) < 0.01f, "hold crouch resizes actual capsule");
        Check(Mathf.Abs(controller.bounds.min.y - feet) < 0.001f, "resize maintains feet");
        Check(Mathf.Abs(movement.CurrentMoveSpeed - 3f) < 0.01f, "crouch reduces movement speed");
        Check(camera.transform.position.y < controller.bounds.max.y && camera.transform.IsChildOf(player.transform), "view remains attached inside crouched capsule");
        Check(camera.transform.localRotation == rotation, "crouch never rotates camera");
        Check(!Physics.Raycast(new Vector3(0f, 1.6f, -2f), Vector3.forward, 4f), "head-height incoming ray really clears crouched collider");
        GameObject thinRoof = GameObject.CreatePrimitive(PrimitiveType.Cube);
        thinRoof.transform.position = new Vector3(0f, 1.98f, 0f);
        thinRoof.transform.localScale = new Vector3(2f, 0.02f, 2f);
        Physics.SyncTransforms();
        Check(!movement.CanStand(), "thin roof in standing head volume cannot be skipped by controller skin width");
        UnityEngine.Object.DestroyImmediate(thinRoof);
        GameObject ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ceiling.transform.position = new Vector3(0f, 1.55f, 0f);
        ceiling.transform.localScale = new Vector3(4f, 0.5f, 4f);
        Physics.SyncTransforms();
        Check(!movement.CanStand(), "overhead capsule clearance detects geometry");
        movement.UpdateCrouch(false, 0.1f);
        Check(movement.IsCrouched && controller.height < 1.2f, "release under ceiling refuses stand");
        controller.Move(Vector3.forward);
        Physics.SyncTransforms();
        Check(player.transform.position.z > 0.9f, "real crouched controller passes beneath low ceiling");
        controller.Move(Vector3.forward * 4f);
        Physics.SyncTransforms();
        Check(movement.CanStand(), "standing clearance returns after leaving low geometry");
        movement.UpdateCrouch(false, 0.01f);
        Check(!movement.IsCrouched && Mathf.Abs(controller.height - 2f) < 0.01f, "release in clear space restores standing capsule");
        float previous = camera.transform.localPosition.y;
        movement.UpdateCrouch(false, 0.01f);
        Check(camera.transform.localPosition.y > previous && camera.transform.localPosition.y < 1.8f, "view rises smoothly instead of snapping");
        player.transform.position = new Vector3(0f, 0f, -3f);
        Physics.SyncTransforms();
        controller.Move(Vector3.forward * 4f);
        Check(player.transform.position.z < -2.1f, "standing controller is blocked by same low opening");
        UnityEngine.Object.DestroyImmediate(ceiling);
        UnityEngine.Object.DestroyImmediate(player);
        UnityEngine.Object.DestroyImmediate(floor);
    }

    private static void CheckDirectionAndSources()
    {
        GameObject camera = new GameObject("CameraBearing");
        Check(Mathf.Abs(HudPresenter.DamageBearing(camera.transform, Vector3.forward * 10f)) < 0.01f, "front bearing");
        Check(Mathf.Abs(HudPresenter.DamageBearing(camera.transform, Vector3.right * 10f) - 90f) < 0.01f, "right bearing");
        Check(Mathf.Abs(HudPresenter.DamageBearing(camera.transform, Vector3.left * 10f) + 90f) < 0.01f, "left bearing");
        Check(Mathf.Abs(Mathf.Abs(HudPresenter.DamageBearing(camera.transform, Vector3.back * 10f)) - 180f) < 0.01f, "rear bearing");
        camera.transform.rotation = Quaternion.Euler(30f, 90f, 0f);
        Check(Mathf.Abs(HudPresenter.DamageBearing(camera.transform, Vector3.right * 10f)) < 0.01f, "current camera yaw/pitch changes source bearing");

        GameObject hud = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/HUD.prefab"));
        HudView view = hud.GetComponentInChildren<HudView>();
        HudPresenter presenter = hud.AddComponent<HudPresenter>();
        presenter.enabled = false;
        Set(presenter, "view", view);
        Set(presenter, "damageView", camera.transform);
        Set(presenter, "damageSource", Vector3.right * 10f);
        Set(presenter, "directionElapsed", 0f);
        presenter.UpdateDamageDirection(0f);
        CanvasGroup group = (CanvasGroup)Get(view, "damageDirectionGroup");
        RectTransform pivot = (RectTransform)Get(view, "damageDirectionPivot");
        Check(group != null && group.alpha == 1f && pivot != null, "serialized direction UI shown at damage");
        camera.transform.rotation = Quaternion.identity;
        presenter.UpdateDamageDirection(0.3f);
        Check(group.alpha > 0f && group.alpha < 1f && Mathf.Abs(Mathf.DeltaAngle(pivot.localEulerAngles.z, -90f)) < 0.01f, "active indicator tracks camera yaw and fades");
        presenter.UpdateDamageDirection(2f);
        Check(group.alpha == 0f, "indicator fully fades");
        Check(Get(view, "damageFlash") != null && Get(view, "hitMarkerGroup") != null, "existing flash and hit marker references preserved");
        typeof(HudPresenter).GetMethod("OnPickupPromptChanged", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(presenter, new object[] { "[F / X] Rifle pickup" });
        typeof(HudPresenter).GetMethod("OnInteractionPromptChanged", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(presenter, new object[] { "[F] Spin slot" });
        TMPro.TMP_Text prompt = (TMPro.TMP_Text)Get(view, "promptText");
        Check(prompt.text.Contains("[F / X]"), "pickup prompt wins regardless of same-frame slot event ordering");
        typeof(HudPresenter).GetMethod("OnPickupPromptChanged", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(presenter, new object[] { "" });
        Check(prompt.text.Contains("[F]"), "clearing pickup restores current slot prompt");
        typeof(HudPresenter).GetMethod("OnInteractionPromptChanged", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(presenter, new object[] { "" });
        Check(!prompt.enabled, "clearing both interaction channels hides prompt");

        GameObject target = new GameObject("DamageTarget");
        target.transform.position = Vector3.forward * 10f;
        target.AddComponent<BoxCollider>();
        PlayerHealth health = target.AddComponent<PlayerHealth>();
        health.Configure(100f);
        int events = 0;
        Vector3 source = Vector3.one * 999f;
        health.DamagedFrom += _position => { events++; source = _position; };
        health.Heal(1f);
        health.Configure(100f);
        health.ApplyDamage(0f, Vector3.zero);
        health.ApplyDamage(-1f, Vector3.zero);
        health.ApplyDamage(1f);
        Check(events == 0, "heal/configure/invalid/source-less damage do not invent direction");
        GameObject shooter = new GameObject("MovingShooter");
        Projectile projectile = new GameObject("Shot").AddComponent<Projectile>();
        projectile.Launch(new Projectile.Shot { team = Projectile.Team.Enemy, damage = 8f, speed = 100f, maxDistance = 20f, lifetime = 10f, radius = 0.02f }, Vector3.zero, Vector3.forward, ~0, shooter.transform);
        shooter.transform.position = Vector3.right * 50f;
        Physics.SyncTransforms();
        Check(events == 0 && health.CurrentHealth == 99f, "projectile launch creates no source event or damage");
        projectile.Step(0.11f);
        Check(events == 1 && health.CurrentHealth == 91f && source == Vector3.zero, "actual impact reports immutable original shot origin, not moving shooter");
        health.ApplyDamage(1000f, Vector3.left);
        health.ApplyDamage(1f, Vector3.right);
        Check(events == 2 && source == Vector3.left && !health.IsAlive, "lethal source damage fires once; dead target does not fire");
        Projectile.CancelAll();
        UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(shooter);
        UnityEngine.Object.DestroyImmediate(hud);
        UnityEngine.Object.DestroyImmediate(camera);
    }

    private static object Get(object _target, string _field)
    {
        return _target.GetType().GetField(_field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_target);
    }

    private static void Set(object _target, string _field, object _value)
    {
        _target.GetType().GetField(_field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_target, _value);
    }

    private static void Check(bool _condition, string _description)
    {
        if (_condition)
        {
            passes++;
            Debug.Log("[PocPlayerFeedbackVerification] PASS " + _description);
        }
        else
        {
            failures++;
            Debug.LogError("[PocPlayerFeedbackVerification] FAIL " + _description);
        }
    }
}

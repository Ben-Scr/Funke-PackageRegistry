using Funke;
using Funke.InputActions;

public sealed class PlayerInputExample : EntityScript
{
    private InputActionMap? m_Gameplay;
    private InputAction? m_Move;
    private InputAction? m_Jump;
    private RebindOperation? m_Rebind;
    private bool m_JumpQueued;

    public Vector2 Movement { get; private set; }
    public string JumpBindingLabel => m_Jump?.Bindings[0].DisplayName ?? string.Empty;

    public override void OnStart()
    {
        m_Gameplay = new InputActionMap("Gameplay");
        m_Move = m_Gameplay.AddVector2("Move").BindWASD().BindArrows().BindLeftStick();
        m_Jump = m_Gameplay.AddButton("Jump").Bind(KeyCode.Space).Bind(GamepadButton.A);
        m_Gameplay.LoadBindingOverrides();
        m_Gameplay.Enable();
    }

    public override void OnUpdate()
    {
        if (m_Gameplay == null || m_Move == null || m_Jump == null) return;
        m_Gameplay.Update();
        Movement = m_Move.ReadVector2();

        if (m_Rebind != null)
        {
            if (m_Rebind.IsFinished)
            {
                if (m_Rebind.Status == RebindStatus.Completed)
                    m_Gameplay.SaveBindingOverrides();
                m_Rebind = null;
            }
            return;
        }

        m_JumpQueued |= m_Jump.WasPressed;
    }

    public void BeginJumpRebind()
    {
        m_JumpQueued = false;
        Movement = Vector2.Zero;
        m_Rebind = m_Jump?.Rebind(0, options: new RebindOptions
        {
            Devices = RebindDevices.KeyboardMouse
        });
    }

    public void SetGameplayEnabled(bool enabled)
    {
        m_Rebind?.Cancel();
        m_Rebind = null;
        m_JumpQueued = false;
        Movement = Vector2.Zero;
        if (enabled) m_Gameplay?.Enable();
        else m_Gameplay?.Disable();
    }

    /// <summary>Call once from the motor's FixedUpdate to avoid repeating one input edge across physics ticks.</summary>
    public bool ConsumeJump()
    {
        bool queued = m_JumpQueued;
        m_JumpQueued = false;
        return queued;
    }

    public override void OnDestroy()
    {
        m_Gameplay?.Dispose();
        m_Gameplay = null;
        m_Move = m_Jump = null;
        m_Rebind = null;
    }
}

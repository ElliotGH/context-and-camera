using Godot;
using System;

public partial class Player : CharacterBody3D
{
    public float FacingAngle = 0.0f;

    public float MoveSpeed = 5.0f;
    public float MoveDeadzone = 0.18f;
    public float CrouchSpeed = 0.5f;
    public float TurnSpeed = 3.0f;
    public float Accel = 10.0f;

    public float AirTime = 0.0f;
    public float CoyoteTime = 0.5f;

    public float JumpAirTime = 1.5f;
    public Tween JumpTween = null;
    public bool IsJumping = false;
    public bool JumpQueued = false;

    private MeshInstance3D PlayerMesh;
    private CollisionShape3D SphereUpper; // upper part of the collision shape (ducking)
    private CollisionShape3D SphereLower; // lower part of the collision shape (ducking)

    private float MeshScaleY;
    private Vector3 UpperSphereStartPos;

    public enum MovementType
    {
        Immediate = 0,
        SlowAccelFastDecel = 1,
        EaseAccelConstDecel = 2
    }

    private MovementType CurrentMovement = MovementType.SlowAccelFastDecel;

    public override void _Ready()
    {
        PlayerMesh = GetNode<MeshInstance3D>("PlayerMesh");
        SphereUpper = GetNode<CollisionShape3D>("SphereUpper");
        SphereLower = GetNode<CollisionShape3D>("SphereLower");

        MeshScaleY = PlayerMesh.Scale.Y;
        UpperSphereStartPos = SphereUpper.Position;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        if (Input.IsActionJustPressed("move_switch"))
            CurrentMovement = (MovementType)(((int)CurrentMovement + 1) % 3);

        Vector2 movement = new(-Input.GetJoyAxis(0, JoyAxis.RightX), Input.GetJoyAxis(0, JoyAxis.RightY));

        float moveAmount = movement.Length();
        Vector2 moveInput = Vector2.Zero;

        if (moveAmount > MoveDeadzone)
        {
            float remapped = (moveAmount - MoveDeadzone) / (1.0f - MoveDeadzone);
            remapped = Mathf.Clamp(remapped, 0.0f, 1.0f);

            remapped *= remapped;

            moveInput = movement.Normalized() * remapped;
        }

        FacingAngle = Mathf.Wrap(FacingAngle, -Mathf.Pi, Mathf.Pi);

        bool isDucking = Input.IsActionPressed("move_duck");
        UpdateCrouch(isDucking);

        Vector3 forward = new(Mathf.Sin(FacingAngle), 0, -Mathf.Cos(FacingAngle));
        Vector3 right = new(forward.Z, 0, -forward.X);

        float forwardBackward = -moveInput.Y;
        float leftRight = moveInput.X;

        if (forwardBackward < 0.0f)
            forwardBackward *= 0.7f;

        float maxSpeed = isDucking ? (MoveSpeed * CrouchSpeed) : MoveSpeed;

        Vector3 target =
            (forward * forwardBackward + right * leftRight) * maxSpeed;

        if (target.LengthSquared() > maxSpeed * maxSpeed)
            target = target.Normalized() * maxSpeed;

        Vector3 v = Velocity;
        Vector3 gravity = GetGravity();
        bool onFloor = IsOnFloor();

        if (onFloor)
        {
            AirTime = 0.0f;
            IsJumping = false;
            if (v.Y > 0.0f)
                v.Y = 0.0f;
        }
        else
        {
            AirTime += dt;
            v += gravity * dt;
        }

        bool canJump = !IsJumping && (onFloor || AirTime < CoyoteTime);

        if (JumpQueued)
        {
            JumpQueued = false;
            if (!IsJumping)
                v.Y = -gravity.Y * (JumpAirTime * 0.5f);
        }
        else if (canJump && Input.IsActionJustPressed("move_jump"))
        {
            IsJumping = true;
            JumpTween?.Kill();

            JumpTween = CreateTween();
            JumpTween.SetParallel(false);
            JumpTween.TweenInterval(0.1f);
            JumpTween.TweenProperty(PlayerMesh, "scale:y", MeshScaleY * 0.7f, 0.0f);
            JumpTween.TweenInterval(0.3f);
            JumpTween.TweenCallback(Callable.From(() => JumpQueued = true));
            JumpTween.TweenProperty(PlayerMesh, "scale:y", MeshScaleY, 0.0f);
        }

        Vector2 v2 = new(v.X, v.Z);
        Vector2 target2 = new(target.X, target.Z);

        switch (CurrentMovement)
        {
            case MovementType.Immediate:
                v2 = target2;
                break;

            case MovementType.SlowAccelFastDecel:
                {
                    float accelRate = Accel;
                    float decelRate = Accel * 2.0f;

                    if (target2.LengthSquared() > v2.LengthSquared())
                        v2 = v2.MoveToward(target2, accelRate * dt);
                    else
                        v2 = v2.MoveToward(target2, decelRate * dt);
                }
                break;

            case MovementType.EaseAccelConstDecel:
                {
                    if (target2.Length() > 0.01f)
                    {
                        float speed = v2.Length();
                        float targetSpeed = target2.Length();

                        float t = 1.0f - Mathf.Exp(-6.0f * dt);
                        speed = Mathf.Lerp(speed, targetSpeed, t);

                        Vector2 desiredDir = target2.Normalized();

                        Vector2 currentDir = v2.Length() > 0.01f ? v2.Normalized() : desiredDir;
                        currentDir = currentDir.Lerp(desiredDir, 1.0f - Mathf.Exp(-10.0f * dt)).Normalized();

                        v2 = currentDir * speed;
                    }
                    else
                    {
                        v2 = v2.MoveToward(Vector2.Zero, Accel * 2.0f * dt);
                    }
                }
                break;
        }

        v.X = v2.X;
        v.Z = v2.Y;

        Velocity = v;
        MoveAndSlide();
    }

    private void UpdateCrouch(bool ducking)
    {
        // make sure its not interferring with jump anim
        if (JumpTween == null || !JumpTween.IsRunning())
        {
            float targetScale = ducking ? MeshScaleY * 0.5f : MeshScaleY;
            PlayerMesh.Scale = new Vector3(PlayerMesh.Scale.X, targetScale, PlayerMesh.Scale.Z);
        }

        SphereUpper.Position = ducking ? SphereLower.Position : UpperSphereStartPos;
    }

    public override void _Process(double delta)
    {
        // update visuals (gfx) in process instead of physicsprocess
        float dt = (float)delta;
        if (new Vector2(Velocity.X, Velocity.Z).Length() < 0.01f)
            return;

        float yaw = Mathf.Atan2(Velocity.X, Velocity.Z) + Mathf.Pi;
        float smoothYaw = Mathf.LerpAngle(PlayerMesh.Rotation.Y, yaw, dt * 10.0f);
        PlayerMesh.Rotation = new(PlayerMesh.Rotation.X, smoothYaw, PlayerMesh.Rotation.Z);
    }
}
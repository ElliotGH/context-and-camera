using Godot;
using System;

public partial class MainCamera : Camera3D
{
    public Vector3 Offset = new(5.0f, 5.0f, 5.0f);
    public float FollowSpeed = 6.0f;
    public float HintBlendSpeed = 3.0f;

    public float OrbitSensitivity = 2.0f;
    public float AutoAlignSpeed = 0.5f;

    public float MinPitchDeg = -20.0f;
    public float MaxPitchDeg = 60.0f;

    public uint AvoidanceMask = 2;

    public float CameraPadding = 0.25f;
    public float SideWhiskerOffset = 0.7f;
    public float SidePushStrength = 1.0f;
    public float InwardPushStrength = 0.7f;
    public float GroundWhiskerDrop = 1.0f;
    public float GroundPushUpStrength = 1.2f;
    public float GroundPushInwardStrength = 0.8f;

    private Player Player;
    private Area3D CameraHintArea;
    private Marker3D CameraHintMarker;

    private float Distance = 0.0f;
    private float Yaw = 0.0f;
    private float Pitch = 0.0f;

    private float CameraHintWeight = 0.0f;

    public override void _Ready()
    {
        Player = GetNode<Player>("../Player");

        CameraHintArea = GetNodeOrNull<Area3D>("../Level/CameraHintArea");
        if (CameraHintArea != null)
            CameraHintMarker = CameraHintArea.GetNodeOrNull<Marker3D>("Marker3D");

        Distance = Offset.Length();

        Vector3 dir = Offset.Normalized();
        Yaw = Mathf.Atan2(dir.X, dir.Z);
        Pitch = Mathf.Asin(dir.Y);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        Vector2 lookInput = Input.GetVector("camera_left",
                                            "camera_right",
                                            "camera_up",
                                            "camera_down");

        bool hasInput = lookInput.LengthSquared() > 0.01f;

        if (hasInput)
        {
            Yaw -= lookInput.X * OrbitSensitivity * dt;
            Pitch -= lookInput.Y * OrbitSensitivity * dt;
        }
        else
        {
            Vector3 behindPlayer = Player.GlobalTransform.Basis.Z.Normalized();
            float targetYaw = Mathf.Atan2(behindPlayer.X, behindPlayer.Z);
            Yaw = Mathf.LerpAngle(Yaw, targetYaw, AutoAlignSpeed * dt);
        }

        float minPitch = Mathf.DegToRad(MinPitchDeg);
        float maxPitch = Mathf.DegToRad(MaxPitchDeg);
        Pitch = Mathf.Clamp(Pitch, minPitch, maxPitch);

        Vector3 orbitOffset = new(Distance * Mathf.Sin(Yaw) * Mathf.Cos(Pitch),
                                  Distance * Mathf.Sin(Pitch),
                                  Distance * Mathf.Cos(Yaw) * Mathf.Cos(Pitch));

        Vector3 normalPosition = Player.GlobalPosition + orbitOffset;
        normalPosition = ApplyWhiskerAvoidance(Player.GlobalPosition, normalPosition);

        Transform3D normalTransform = CreateLookAtTransform(normalPosition, Player.GlobalPosition);

        bool isInCameraHint = IsPlayerInsideCameraHint();

        float targetHintWeight = isInCameraHint ? 1.0f : 0.0f;
        CameraHintWeight = Mathf.MoveToward(CameraHintWeight, targetHintWeight, HintBlendSpeed * dt);

        Transform3D finalTransform = normalTransform;

        if (CameraHintMarker != null && CameraHintWeight > 0.0f)
        {
            Transform3D hintTransform = CameraHintMarker.GlobalTransform;
            finalTransform = BlendTransforms(normalTransform, hintTransform, CameraHintWeight);
        }

        GlobalPosition = GlobalPosition.Lerp(finalTransform.Origin, FollowSpeed * dt);

        Quaternion currentRot = GlobalBasis.GetRotationQuaternion();
        Quaternion targetRot = finalTransform.Basis.GetRotationQuaternion();
        Quaternion newRot = currentRot.Slerp(targetRot, FollowSpeed * dt);
        GlobalBasis = new Basis(newRot);
    }

    private Transform3D CreateLookAtTransform(Vector3 position, Vector3 target)
    {
        Basis basis = Basis.LookingAt(target - position, Vector3.Up);
        return new Transform3D(basis, position);
    }

    private Transform3D BlendTransforms(Transform3D from, Transform3D to, float weight)
    {
        Vector3 pos = from.Origin.Lerp(to.Origin, weight);

        Quaternion fromRot = from.Basis.GetRotationQuaternion();
        Quaternion toRot = to.Basis.GetRotationQuaternion();
        Quaternion rot = fromRot.Slerp(toRot, weight);

        return new Transform3D(new Basis(rot), pos);
    }

    private bool IsPlayerInsideCameraHint()
    {
        if (CameraHintArea == null || CameraHintMarker == null)
            return false;

        var bodies = CameraHintArea.GetOverlappingBodies();

        foreach (Node body in bodies)
        {
            if (body == Player)
                return true;
        }

        return false;
    }

    private Vector3 ApplyWhiskerAvoidance(Vector3 from, Vector3 to)
    {
        Vector3 adjustedPosition = to;

        for (int i = 0; i < 2; i++)
        {
            Vector3 toCamera = adjustedPosition - from;
            float dist = toCamera.Length();

            if (dist <= 0.001f)
                return adjustedPosition;

            Vector3 dir = toCamera / dist;

            Vector3 right = dir.Cross(Vector3.Up);
            if (right.LengthSquared() < 0.001f)
                right = Vector3.Right;
            else
                right = right.Normalized();

            if (RayHit(from, adjustedPosition, out Vector3 centerHitPos, out Vector3 centerHitNormal))
            {
                float safeDist = Mathf.Max(from.DistanceTo(centerHitPos) - CameraPadding, 0.0f);
                adjustedPosition = from + dir * safeDist;
                adjustedPosition += centerHitNormal * 0.05f;
            }

            Vector3 leftTarget = adjustedPosition - right * SideWhiskerOffset;
            Vector3 rightTarget = adjustedPosition + right * SideWhiskerOffset;

            bool leftHit = RayHit(from, leftTarget, out _, out _);
            bool rightHit = RayHit(from, rightTarget, out _, out _);

            if (leftHit && !rightHit)
            {
                adjustedPosition += right * SidePushStrength;
            }
            else if (rightHit && !leftHit)
            {
                adjustedPosition -= right * SidePushStrength;
            }
            else if (leftHit && rightHit)
            {
                adjustedPosition = MoveInward(from, adjustedPosition, InwardPushStrength);
            }

            Vector3 downTarget = adjustedPosition - Vector3.Up * GroundWhiskerDrop;
            bool groundHit = RayHit(from, downTarget, out _, out Vector3 groundNormal);

            if (groundHit)
            {
                adjustedPosition += Vector3.Up * GroundPushUpStrength;
                adjustedPosition = MoveInward(from, adjustedPosition, GroundPushInwardStrength);

                if (groundNormal.Dot(Vector3.Up) < 0.5f)
                    adjustedPosition = MoveInward(from, adjustedPosition, GroundPushInwardStrength * 0.5f);
            }
        }

        return adjustedPosition;
    }

    private Vector3 MoveInward(Vector3 from, Vector3 to, float amount)
    {
        Vector3 dir = (to - from).Normalized();
        float dist = from.DistanceTo(to);
        dist = Mathf.Max(0.0f, dist - amount);
        return from + dir * dist;
    }

    private bool RayHit(Vector3 from, Vector3 to, out Vector3 hitPos, out Vector3 hitNormal)
    {
        hitPos = Vector3.Zero;
        hitNormal = Vector3.Zero;

        var spaceState = GetWorld3D().DirectSpaceState;

        var query = PhysicsRayQueryParameters3D.Create(from, to, AvoidanceMask);
        query.CollideWithBodies = true;
        query.CollideWithAreas = false;
        query.HitFromInside = false;

        var exclude = new Godot.Collections.Array<Rid>
        {
            GetCameraRid(),
            Player.GetRid()
        };
        query.Exclude = exclude;

        var result = spaceState.IntersectRay(query);

        if (result.Count == 0)
            return false;

        hitPos = (Vector3)result["position"];
        hitNormal = (Vector3)result["normal"];
        return true;
    }
}
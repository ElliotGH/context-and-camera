using Godot;
using System;

public partial class MainCamera : Camera3D
{
    public Vector3 Offset = new(4.0f, 4.0f, 4.0f);
    private Player Player;

    public override void _Ready()
    {
        Player = GetNode<Player>("../Player");
    }

    public override void _Process(double delta)
    {
        Position = Player.Position + Offset;
    }
}

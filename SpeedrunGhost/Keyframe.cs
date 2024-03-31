using UnityEngine;

namespace SpeedrunGhost;

public struct KeyframeData
{
    public string[] Paths;
    public Keyframe[] Keyframes;
}

public struct Keyframe
{
    public float Time;
    public Vector3 BodyPosition;
    public Quaternion BodyRotation;
    public PositionKeyframe[] Positions;
    public RotationKeyframe[] Rotations;
    public bool SyncFrame;
}

public struct PositionKeyframe
{
    public byte Index;
    public Vector3 Position;
}

public struct RotationKeyframe
{
    public byte Index;
    public Quaternion Rotation;
}
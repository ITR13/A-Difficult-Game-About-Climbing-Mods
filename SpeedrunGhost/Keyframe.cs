using System;
using UnityEngine;

namespace SpeedrunningTools;

[Serializable]
public struct KeyframeData
{
    public string Version;
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

[Serializable]
public struct PositionKeyframe
{
    public byte Index;
    public Vector3 Position;
}

[Serializable]
public struct RotationKeyframe
{
    public byte Index;
    public Quaternion Rotation;
}